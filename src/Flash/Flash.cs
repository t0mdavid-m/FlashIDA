using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using Thermo.TNG.Factory;
using Thermo.Interfaces.FusionAccess_V1;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Acquisition;
using Thermo.Interfaces.InstrumentAccess_V1;
using Thermo.Interfaces.FusionAccess_V1.MsScanContainer;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using System.IO;
using Flash.IDA;
using System.Timers;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.FusionAccess_V1.Control;
using log4net;
using log4net.Config;
using Mono.Options;

namespace Flash
{
    class Flash
    {
        //acquisition controller
        static IAcquisition acquisition;

        //instrument controller
        static IFusionControl control;

        //instrument scan control
        static IFusionScans scanControl;

        //stores if FAIMS is enabled
        static bool useFAIMS;

        //scans that are ariving from the instrument
        static IFusionMsScanContainer  msscans;

        //switch indicating that we received custom scan control
        static bool inCustom = false;

        //Commands submitted that the instrument has not yet executed - the acquisition's DEPTH
        //(CONTEXT.md, "outstanding command"). Starts at 1: the handshake is submitted before any
        //scan can arrive. Thermo defines depth > 1 as UNDEFINED behaviour that fails silently
        //(dependencies/API-2.0.xml, IScans.SetCustomScan), so depth is maintained deliberately and
        //can never be inferred from the absence of complaint.
        //DERIVED, not accumulated: incremented only after a submission returns, so a command that
        //was never sent self-heals on the next arrival instead of being baked in. See docs/adr/0032.
        //NOTE: a second contact-closure event would send a second handshake and leave this one low.
        //That double-send is pre-existing; it is neither introduced nor fixed here.
        static int outstanding = 1;

        //Instrument job number stamped on the handshake scan; the latch in ProcessSpectrum keys on
        //its echo in Trailer["Access ID"]. NOT an engine identity - see docs/adr/0008.
        private const int HandshakeJobNumber = 41;

        //switch indicating that we need to stop.
        //volatile: written from the DataPipe pool thread (abort path) and read by the
        //empty-bodied spin loop in Main, which the JIT would otherwise be free to hoist.
        static volatile bool stopRequest = false;

        //one-shot guard so a systemic failure cannot fire the abort once per buffered scan
        private static int stopRequested = 0;

        //helper class to create scan objects
        static ScanFactory scanFactory;

        //flashIDA
        static IScanProcessor flashIDAProcessor;

        //FLASHIda wrapper (unified bridge)
        static FLASHIdaWrapper wrapper;

        //DataPipe
        static DataPipe dataPipe;

        //loggers
        static ILog log;

        //Method parameters
        static MethodParameters methodParams;

        //Run clock. Armed before the handshake is sent, restarted when it echoes - docs/adr/0043.
        static Timer duration;

        //Monotonic base for the "armed but not charged" figure the restart logs. A wall-clock
        //DateTime can step under NTP or DST; this cannot.
        static System.Diagnostics.Stopwatch sinceArmed;

        //Spectrum running number
        static int currentNumber;

        //assembly data
        static private string selfFileName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
        static private string selfName = Assembly.GetExecutingAssembly().GetName().Name;
        static private string selfVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        static private string selfLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        //Command line option structue
        static CmdOptions cliArgs;
        
        /// <summary>
        /// Parsing command line prameters into <see cref="CmdOptions"/>
        /// </summary>
        /// <param name="cliArgs">Command line as received from OS</param>
        /// <returns></returns>
        static CmdOptions ParseCLI(string[] cliArgs)
        {
            bool showHelp = false;
            bool showVersion = false;
            CmdOptions args = new CmdOptions();

            OptionSet options = new OptionSet
            {
                { String.Format("Usage\n{0} [option arguments]\n" +
                                "All arguments are optional\nOptions:", selfFileName) },
                { "h|help", "Usage information", _ =>  showHelp = true },
                { "v|version", "Show version information", _ => showVersion = true },
                { "o|nocc", "Ignore contact closure. Default: false",  _ => args.OverrideCC = true },
                { "t|test", "Run in test mode without connection to the instrument. Default: false", _ => args.TestMode = true },
                { "m|method=", "Location of method file. Default: method.json in the program folder", v => args.MethodPath = v },
                { "r|rawname=", "Name or path of the raw file. Used to prefix the timestamped run folder that holds every log file. If not specified the folder is named by the timestamp alone", v => args.Rename = v }
            };

            List<string> positionArgs = new List<string>();

            try
            {
                positionArgs = options.Parse(cliArgs);
            }
            catch (OptionException e)
            {
                Console.Error.WriteLine(String.Format("Error parsing command line:\n{0}", e.Message));
                options.WriteOptionDescriptions(Console.Out);
                Environment.Exit(1);
            }

            if (showHelp)
            {
                options.WriteOptionDescriptions(Console.Out);
                Environment.Exit(0);
            }

            if (showVersion)
            {
                Console.WriteLine("{0} Version {1}", selfName, selfVersion);
                Environment.Exit(0);
            }

            if (args.TestMode)
            {
                FLASHIdaWrapper.Main(positionArgs.ToArray());
                Environment.Exit(0);
            }

            if (args.MethodPath == null) //no method file provided
            {
                args.MethodPath = Path.Combine(selfLocation, "method.json");
            }

            if (!File.Exists(args.MethodPath))
            {
                if (File.Exists(Path.Combine(selfLocation + args.MethodPath))) //in case user provided relative path to method file
                {
                    args.MethodPath = Path.Combine(selfLocation, args.MethodPath);
                }
                else
                {
                    Console.Error.WriteLine(String.Format("Cannot find method file {0}", args.MethodPath));
                    Environment.Exit(1);
                } 
            }
            
            return args;
        }

        static void Main(string[] args)
        {
            cliArgs = ParseCLI(args);

            // The method file is loaded HERE, not in InstrumentConnected where it used to live.
            // runtime.log_dir has to reach the log4net appenders below, and XmlConfigurator opens
            // their files immediately -- roughly one async event and a hundred lines before the old
            // load site ran. ParseCLI has already resolved and existence-checked MethodPath, and
            // MethodParameters.Load is a pure static with no instrument dependency, so this is a
            // reorder rather than a redesign.
            try
            {
                methodParams = MethodParameters.Load(cliArgs.MethodPath);
            }
            catch (Exception ex)
            {
                // Console.Error, NOT log.Error: `log` is not assigned until after XmlConfigurator
                // runs, a few lines below. ParseCLI reports its own failures the same way.
                Console.Error.WriteLine(String.Format("Error loading method file: {0}\n{1}", ex.Message, ex.StackTrace));
                Environment.Exit(1);
            }

            // One folder and one timestamp for this run, minted once and shared by all seven files.
            // A verbatim copy of the method file joins them further down, once `log` exists.
            // -r/--rawname (Xcalibur's %R) prefixes it, so a sequence's logs sit beside the .raw
            // they describe instead of being appended into each other.
            string runFolder = LogPathResolver.Compose(
                methodParams.Config.Runtime.LogDir, cliArgs.Rename, DateTime.Now);
            try
            {
                Directory.CreateDirectory(runFolder);
            }
            catch (Exception ex)
            {
                // Fail fast, before the instrument container exists, so nothing expensive is lost.
                // The alternatives both end with an operator who believes they have logs and does
                // not: the engine's streams fail to open SILENTLY (no header, no rows, no error),
                // and a warning has nowhere to go because the log file lives in the folder that
                // just failed. A bad method file is already fatal here; an uncreatable log folder
                // is the same class of configuration error.
                Console.Error.WriteLine(String.Format(
                    "Cannot create log folder {0}: {1}", runFolder, ex.Message));
                Environment.Exit(1);
            }
            // What crosses the bridge is the RESOLVED folder. C++ joins its five fixed basenames
            // onto it and treats empty as "open nothing", so this assignment is also what switches
            // the engine's logging on.
            methodParams.Config.Runtime.LogDir = runFolder;

            XmlDocument appConfig = new XmlDocument();
            appConfig.Load(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile); //logger configuration is stored in the {App}.config

            // Unconditional now -- both files always land in the run folder, whether or not -r was
            // given. Absolute, because log4net resolves a relative <file value> against
            // AppDomain.BaseDirectory (bin\) rather than the process CWD, which would put these two
            // files in a different directory from the engine's five.
            var generalFileNode = appConfig.SelectSingleNode("//log4net/appender[@name='GeneralFile']/file");
            var idaFileNode = appConfig.SelectSingleNode("//log4net/appender[@name='IDAFile']/file");
            if (generalFileNode == null || idaFileNode == null)
            {
                Console.Error.WriteLine("App.config is missing the GeneralFile or IDAFile appender <file> node.");
                Environment.Exit(1);
            }
            generalFileNode.Attributes.GetNamedItem("value").Value = Path.Combine(runFolder, "FlashLog.log");
            idaFileNode.Attributes.GetNamedItem("value").Value = Path.Combine(runFolder, "IDALog.log");

            XmlConfigurator.Configure((XmlElement) appConfig.GetElementsByTagName("log4net").Item(0));
            log = LogManager.GetLogger("General");

            // First point at which `log` exists; these two lines used to sit next to the load.
            log.Info(String.Format("Logging to {0}", runFolder));

            // Earliest possible stop trigger: registered as soon as `log` exists, so a Ctrl+C during
            // instrument connection is still an orderly stop rather than a kill.
            // ONE-SHOT via RequestStop's own latch (docs/adr/0041) - the first press returns true
            // and we keep the process alive for teardown; the second returns false, e.Cancel stays
            // false, and the runtime terminates us. Always-true would make a blocked teardown
            // swallow every further Ctrl+C.
            // CancelKeyPress ONLY. ProcessExit would add console-close coverage under a short,
            // version-dependent budget, and would give teardown a second entry point that can
            // interleave with Main's own. Closing the console window, taskkill /F and an unhandled
            // exception therefore still leave the queue behind - an accepted, stated gap.
            Console.CancelKeyPress += (s, e) => e.Cancel = RequestStop("Ctrl+C");

            // The copy lands here rather than beside Directory.CreateDirectory above because `log`
            // does not exist until XmlConfigurator has run, and a warning needs somewhere to go.
            if (!LogPathResolver.TryCopyMethodFile(cliArgs.MethodPath, runFolder, out string copyError))
            {
                log.Warn(String.Format("Could not copy the method file into the run folder: {0}", copyError));
            }

            log.Info("Read method");
            log.Info(methodParams.ToLogString());

            try
            {
                //Create Access Container
                IFusionInstrumentAccessContainer instrumentContainer = Factory<IFusionInstrumentAccessContainer>.Create();

                //Connect to the instrument
                instrumentContainer.StartOnlineAccess();

                //hook up to the connection signal
                instrumentContainer.ServiceConnectionChanged += InstrumentConnected;
            }
            catch (Exception ex)
            {
                log.Error(String.Format("Cannot create Instrument Container. Do you have Tune installed? Do you run it on the Instrument Computer?\n{0}\n{1}",
                    ex.Message, ex.StackTrace));
                Environment.Exit(1);
            }

            //infinite loop - waiting for other signals - should have been done better
            while (!stopRequest)
            {
                
            }

            //TEARDOWN (docs/adr/0041). HERE, on the main thread - not inside RequestStop, because
            //stopRequest is the same flag the loop above exits on, so teardown in RequestStop would
            //race Main's own unwind, across four different calling threads. RequestStop latches;
            //Main tears down. One thread, ordered by construction, and the Ctrl+C path inherits it
            //for free. RequestStop publishes its latch LAST, so the stop reason is already written
            //by the time we get here.
            //
            //Each step null-guarded and separately caught: scanControl/msscans are null if the
            //instrument never connected - a Ctrl+C during connection falls straight through the
            //loop above, and so does -t test mode - and an exception on an iAPI call "does not
            //crash the software the usual way, but lead[s] to weird behavior" (see the
            //InstrumentConnected remarks).

            //Redundant against the latch on the normal path, and kept for the case that is not
            //normal: if an iAPI FOREGROUND thread keeps this process alive after Main returns, this
            //is the only thing that stops ProcessSpectrum ingesting - and the engine appending to
            //five log files - indefinitely, with no run and nobody watching.
            //Does not un-dispatch a callback already in flight; it is not a barrier.
            try { if (msscans != null) msscans.MsScanArrived -= ProcessSpectrum; }
            catch (Exception ex) { log.Error(String.Format("Unsubscribe failed: {0}", ex.Message)); }

            //The CANCEL half. Vendor semantics at depth 2 are undefined in exactly the way
            //SetCustomScan's are (docs/adr/0033): CancelCustomScan is documented against a
            //one-outstanding-command model, so it may clear one command or both and will not say
            //which. The latch is what bounds the damage either way.
            try { if (scanControl != null) scanControl.CancelCustomScan(); }
            catch (Exception ex) { log.Error(String.Format("CancelCustomScan failed: {0}", ex.Message)); }

            //Depth at stop, free forensics - there was no record of it anywhere.
            log.Info(String.Format("Exiting (depth {0})", outstanding));
        }

        /// <summary>
        /// "Instrument-is-connected"-event handler
        /// </summary>
        private static void InstrumentConnected(object sender, EventArgs e)
        {
            //sender is the instrument container
            IFusionInstrumentAccessContainer instrumentContainer = (IFusionInstrumentAccessContainer)sender;

            //Connect to the instrument accessor, IFAIK it should be always index 1
            IFusionInstrumentAccess instrumentAccess = instrumentContainer.Get(1);
            log.Info(String.Format("Instrument {0} ({1}) is connected", instrumentAccess.InstrumentName, instrumentAccess.InstrumentId));

            //fill controllers
            acquisition = instrumentAccess.Control.Acquisition;
            control = instrumentAccess.Control;

            //subscribe for Status Changes
            acquisition.StateChanged += OnStateChanged;

            //The instrument's acquisition ending is TERMINAL for this run - docs/adr/0041 and
            //CONTEXT.md "Acquisition stop". Plain EventHandler; Thermo's own sample wires it that
            //way (dependencies/API-2.0.xml:2318).
            acquisition.AcquisitionStreamClosing += OnAcquisitionStreamClosing;

            //switch the acquisition on if necessary
            if (acquisition.State.SystemMode == SystemMode.Off || acquisition.State.SystemMode == SystemMode.Standby)
            {
                log.Info("Switching instrument on...");
                acquisition.SetMode(acquisition.CreateOnMode());
            }

            // handler for acqusition error events
            instrumentAccess.AcquisitionErrorsArrived += HandleAcqError;

            int numberOfMS = instrumentAccess.CountMsDetectors;
            log.Info(String.Format("Number of MS: {0}", numberOfMS));

            //it is unlikely there will be less than one MS detector, but for sanity we are checking for it
            if (numberOfMS > 0) msscans = instrumentAccess.GetMsScanContainer(0);

            //interface to schedule and create scans ('false' means cooperative access)
            try
            {
                scanControl = control.GetScans(false) as IFusionScans;
                useFAIMS = false;
                foreach (var property in scanControl.PossibleParameters)
                {
                    if (property.Name == "FAIMS Voltages")
                    {
                        useFAIMS = true;
                        break;
                    }
                }
                if (useFAIMS) {
                    log.Info("FAIMS detected");
                }
                else
                {
                    log.Info("FAIMS not detected");
                }
                log.Info("ScanControl success");
            }
            //NOTE: it is extremly important to catch all possible exceptions in the "instrument part", unhandled exception does not crash the software the usual way, but lead to weird behavior
            catch (Exception ex)
            {
                log.Error(String.Format("ScanControl failed\n{0}\n{1}", ex.Message, ex.StackTrace));
            }

            //should fire when a custom scan is done (never fires as of current version of API), apparently fixed in API 3.5
            //scanControl.CanAcceptNextCustomScan += CustomScanListner;

            //helper to have easier interface for scan creation
            scanFactory = new ScanFactory(scanControl);

            // The method is loaded and its log folder resolved in Main, before log4net is
            // configured -- see there. `methodParams` is a static field and is already populated by
            // the time this event handler fires.

            // Phase 6: Default/AGC scans and per-CV FAIMS scans are no longer needed.
            // C++ engine provides all scan commands via GetNextScanCommand, including
            // MS1 fallback with correct FAIMS CV. ScanScheduler and FAIMSScanProcessor are deleted.

            //Initialize FLASHIDA Processor (Phase 6: C++ handles FAIMS CV cycling)
            try
            {
                wrapper = new FLASHIdaWrapper(methodParams);
                flashIDAProcessor = new UnifiedScanProcessor(wrapper);
                log.Info("Created FLASHIDA processor");
            }
            catch (Exception ex)
            {
                log.Error(String.Format("Processor creation failed: {0}\n{1}", ex.Message, ex.StackTrace));
                Environment.Exit(1);
            }

            //Initialize data processing pipeline
            try
            {
                dataPipe = new DataPipe(flashIDAProcessor,
                    ex => RequestStop(String.Format("Aborting run - scan processing failed: {0}", ex.Message)));
                log.Info("Created DataPipe");
            }
            catch (Exception ex)
            {
                log.Error(String.Format("DataPipe failed: {0}\n{1}", ex.Message, ex.StackTrace));
            }

            if (cliArgs.OverrideCC) //do not wait for contact closure event - start now
            {
                log.Info("Contact closure override");

                //subscribe for new scans from the instruments
                msscans.MsScanArrived += ProcessSpectrum;

                //Arm the run clock BEFORE the handshake goes out, so a handshake that never echoes
                //still bounds the run. The latch in ProcessSpectrum restarts it - docs/adr/0043.
                ArmRunClock();

                //send the first custom scan (the handshake one)
                try
                {
                    scanControl.SetFusionCustomScan(BuildHandshakeScan());
                    log.Info("Sent the first magic scan");
                }
                catch (Exception ex)
                {
                    log.Error(String.Format("First magic scan failed: {0}\n{1}", ex.Message, ex.StackTrace));
                }
            }
            else
            {
                //Subscribe for contact closure and wait with starting
                instrumentAccess.ContactClosureChanged += OnContactClosure;
                log.Info("Waiting for contact closure");
            }
        }

        /// <summary>
        /// Contact closure event handler
        /// </summary>
        private static void OnContactClosure(object sender, ContactClosureEventArgs e)
        {
            log.Info("Contact closure received");

            //unsubscribe from any future contact closure events
            IFusionInstrumentAccess instrumentAccess = (IFusionInstrumentAccess)sender;
            instrumentAccess.ContactClosureChanged -= OnContactClosure;
            
            //subscribe for new scans from the instruments
            msscans.MsScanArrived += ProcessSpectrum;

            //Arm the run clock BEFORE the handshake goes out, so a handshake that never echoes
            //still bounds the run. The latch in ProcessSpectrum restarts it - docs/adr/0043.
            ArmRunClock();

            //send the first custom scan (the handshake one).
            //MUST be the same handshake scan the OverrideCC branch sends: it carries the
            //HandshakeJobNumber the latch in ProcessSpectrum keys on. Building it from
            //GetNextScanCommand instead stamps the engine's first tracking id (0, an
            //iAPI-reserved value), the latch never fires, and the run acquires nothing.
            try
            {
                scanControl.SetFusionCustomScan(BuildHandshakeScan());
                log.Info("Sent the first magic scan");
            }
            catch (Exception ex)
            {
                log.Error(String.Format("First magic scan failed: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }

        /// <summary>
        /// Build the handshake ("magic") scan that switches the instrument into custom control.
        /// </summary>
        /// <remarks>
        /// Both startup paths (contact closure and -o/--nocc) MUST send this exact scan - it is the
        /// single definition of the handshake, so the two paths cannot drift apart again.
        ///
        /// Deliberately a cheap, fast IonTrap scan: it is a control signal, not data, and we want it
        /// echoed back immediately.
        ///
        /// <c>delay: 3</c> is INERT, and this used to claim the opposite. SingleProcessingDelay is
        /// documented as the grace window the instrument keeps custom control open for after
        /// executing a scan (dependencies/API-2.0.xml, ICustomScan) - but per Thermo it is not
        /// functional on this instrument, and adjusting it was tried on the hardware with no
        /// observable effect. The handshake works for other reasons. Every other command we send
        /// carries <c>delay: 0.0</c> (ScanFactory.BuildFromCommand), so the parameter is doing
        /// nothing anywhere in this codebase.
        ///
        /// Do not reason about instrument pacing as if the host could widen that window, and do not
        /// propose changing this value to fix a latency problem - that has been tried.
        ///
        /// Must be handed to <c>scanControl.SetFusionCustomScan</c> DIRECTLY, never via
        /// <see cref="SendCustomScan"/>, which would overwrite RunningNumber with ++currentNumber.
        ///
        /// <c>AGCgroup: 2</c> is load-bearing. With the default group this control scan landed in
        /// PAGC group 1 - the group every real acquisition uses, since
        /// <see cref="ScanFactory.BuildFromCommand"/> hardcodes 1 - while commanding neither the
        /// configured source region nor a FAIMS CV. It is bit-identical to the engine's real
        /// prescan (<c>ScanCommandQueue::makeAGC</c>) in everything that makes the instrument treat
        /// it as one - IonTrap/Turbo, AGC 30000, MaxIT 1, one microscan - and different in every
        /// parameter that decides WHICH IONS ARRIVE. So it measured flux through the instrument
        /// method's source region and FAIMS state, and that estimate gain-corrected the first real
        /// scans of every run, which are acquired through ours.
        ///
        /// Group 2 has no other members, which is the point: nothing consumes this measurement.
        ///
        /// <c>IsAGC</c> stays <c>true</c> deliberately. This scan is submitted BEFORE custom control
        /// exists, and a handshake that fails to latch is a run that acquires nothing - ADR-0008
        /// records that happening once already. Moving the group is the smaller change with the
        /// known blast radius. See docs/adr/0032.
        /// </remarks>
        private static IFusionCustomScan BuildHandshakeScan()
        {
            return scanFactory.CreateFusionCustomScan(
                new ScanParameters
                {
                    Analyzer = "IonTrap",
                    FirstMass = new double[] { methodParams.Config.MsSettings.MS1.FirstMass },
                    LastMass = new double[] { methodParams.Config.MsSettings.MS1.LastMass },
                    ScanRate = "Turbo",
                    AGCTarget = 30000,
                    MaxIT = 1,
                    Microscans = 1,
                    DataType = "Profile",
                    ScanType = "Full",
                }, id: HandshakeJobNumber, IsAGC: true, AGCgroup: 2, delay: 3);
        }

        /// <summary>
        /// Method to send custom scan request to instrument
        /// </summary>
        /// <param name="scan">Scan request</param>
        /// <returns>
        /// Whether the INSTRUMENT accepted the command. SetFusionCustomScan is documented as
        /// "true if the command has been sent to the instrument, false otherwise"
        /// (dependencies/API-2.0.xml:1747-1768). This return used to be discarded, which is what
        /// let a declined command be counted as outstanding forever - see docs/adr/0041.
        /// </returns>
        private static bool SendCustomScan(IFusionCustomScan scan)
        {
            if (scan != null)
            {
                scan.RunningNumber = ++currentNumber;
                if (scan.Values["ScanType"] == "Full")
                {
                    log.Debug(String.Format("Sending Full {0} scan [{1} - {2}]; ID: {3}",
                        scan.Values["Analyzer"], scan.Values["FirstMass"], scan.Values["LastMass"], currentNumber));
                }
                //make sure not to ask for non-existing keys from scan.Values, the procedure will fail silently
                else if (scan.Values["ScanType"] == "MSn") //PrecursorMass and ChargeStates exist only for MSn scans, 
                {
                    log.Debug(String.Format("Sending MSn scan MZ = {0} Z = {1}; ID: {2}",
                        scan.Values["PrecursorMass"], scan.Values["ChargeStates"], currentNumber));
                }

                bool accepted = scanControl.SetFusionCustomScan(scan);
                if (!accepted)
                    log.Error(String.Format("Instrument did not accept scan ID {0}", currentNumber));
                return accepted;
            }
            else
            {
                log.Debug("Sending NULL - Nothing to do");
                return false;
            }
        }

        /// <summary>
        /// Handler of instrument status changes
        /// </summary>
        private static void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            log.Info(String.Format("Instrument Status: {0}", acquisition.State.SystemMode.ToString()));
        }

        /// <summary>
        /// The instrument's acquisition ended. Terminal for this run - docs/adr/0041.
        /// </summary>
        /// <remarks>
        /// ARMED ON <c>inCustom</c>, and that guard is the whole safety of this handler. The event
        /// carries no identity, and FLASHIda never calls StartAcquisition, so it has no handle on
        /// "its own" acquisition to compare against. Unarmed, a PREVIOUS sample's stream closing
        /// while we sit waiting for contact closure stops a run that has not started - and the log
        /// line would be perfectly true, just about somebody else's acquisition.
        ///
        /// Fails in the safe direction, like every other predicate on this path: if the handshake
        /// never echoes we never honour a Closing, which is exactly the old behaviour.
        ///
        /// NOT a proof of quiet. The iAPI states that scans keep arriving after this fires, and
        /// that an open rawfile may still gather them (dependencies/API-2.0.xml:194-207). It is a
        /// signal to stop, nothing more.
        /// </remarks>
        private static void OnAcquisitionStreamClosing(object sender, EventArgs e)
        {
            if (!inCustom)
            {
                log.Info("Acquisition stream closed before custom control latched - ignoring");
                return;
            }

            RequestStop("Acquisition ended");
        }

        /// <summary>
        /// Processing routine for each scans received from the instrument
        /// Scan is contained in event arhs <paramref name="e"/>
        /// </summary>
        private static void ProcessSpectrum(object sender, MsScanEventArgs e)
        {
            IMsScan msScan = e.GetScan();

            //parse out API scan identifier
            msScan.Trailer.TryGetValue("Access ID", out var scanId);

            //The RECD lines below report `outstanding` BEFORE the decrement further down, so the
            //logged depth is depth AS THIS SCAN ARRIVED. That is the quantity worth reading: it is
            //what the instrument was holding when it acquired this scan. Deliberate, not a slip.

            if (msScan.Header["MSOrder"] == "1")
            {
                log.Debug(String.Format("RECD {0} MS1 Scan #{1}; ID: {2}; depth {3}",
                    msScan.Header["MassAnalyzer"], msScan.Header["Scan"], scanId, outstanding));
            }
            else if (msScan.Header["MSOrder"] == "2")
            {
                log.Debug(String.Format("RECD {0} MS2 Scan #{1}; ID: {2}; depth {3}; Precursor: {4:f04}",
                    msScan.Header["MassAnalyzer"], msScan.Header["Scan"], scanId, outstanding, msScan.Header["PrecursorMass[0]"]));
            }

            //when handshake scan received switch to custom control mode
            if (scanId == HandshakeJobNumber.ToString())
            {
                if (!inCustom)
                {
                    inCustom = true;

                    //The echo is the first moment the instrument is under our control, so
                    //global.duration is measured from HERE - docs/adr/0043. One-shot by
                    //construction: the !inCustom guard means a stray re-echo cannot extend the run.
                    ArmRunClock();
                }
                currentNumber = HandshakeJobNumber;
            }

            //push current scan to the DataPipe.
            //NOTHING here disposes msScan. An IMsScan is a handle to framework-owned shared memory
            //that the iAPI releases itself once the next scan replaces it as the container's
            //LastScan; our only job is to stop reading it. Disposing it on this thread frees memory
            //the pool thread is still lazily enumerating (Centroids/Header/Trailer), and disposing it
            //on the pool thread instead - late, out of arrival order - killed the run outright.
            //One rule, no ownership protocol: we never dispose a scan.
            if (inCustom)
            {
                //An UNCOMMANDED scan is not ingested EITHER, and the reason is measured rather than
                //assumed. The engine does not reject these cheaply: its first gate is
                //`desc.size() < 3` (FLASHIda.cpp:88), and on the 2026-08-25 Eclipse run the
                //instrument method's own scans came back with a description of three BLANK
                //characters, which clears it. Each one therefore ran the full snapshot on this
                //thread, allocated two double[], crossed the P/Invoke bridge, took analysis_mutex_,
                //and printed "[TRACK-RESOLVE] id=<blank> status=not_found" with std::endl - a stdout
                //flush, under the lock, ~13.7 times a second, racing log4net's ConsoleAppender on
                //this thread for the same console. 45 709 of them in that run's remaining gradient.
                //
                //Same predicate and same FAIL-OPEN direction as the answer half below: every way of
                //misreading the trailer lands on "push it", which is merely the old behaviour.
                //
                //Placement is load-bearing. The snapshot stays BEFORE the drain so the IMsScan is
                //still live when ScanData.From reads it - moving it after would reintroduce the
                //use-after-release that has already cost two runs (FlashIDA/CLAUDE.md).
                //
                //a rejected Post means the scan is DROPPED, not deferred - never silent
                if (scanId != "0" && !dataPipe.Push(msScan))
                {
                    log.Error(String.Format("Scan {0} was rejected by the pipeline and will NOT be processed", scanId));
                }

                //An UNCOMMANDED scan (CONTEXT.md) is NOT answered. "0" is the iAPI's reserved job
                //number, so it is never one of ours; this is the half that used to buy it a command.
                //Every one we answered deepened the instrument's queue by one, permanently --
                //depth = 1 + uncommanded arrivals, with no path that decrements it. docs/adr/0032.
                //
                //Deliberately the LOOSEST predicate available: every way of misreading the trailer
                //(null, garbage, a moved key) lands on "answer it", which merely degrades to the old
                //behaviour. A range check or a tracking-id check would fail CLOSED instead, and
                //depth 0 is ABSORBING -- the instrument then acquires only its own scans, so every
                //further arrival is uncommanded and we would never send again.
                //NESTED, not folded into the condition as `scanId != "0" && outstanding > 0`.
                //That version sends a COMMANDED scan arriving at depth 0 into the else, where it
                //logs itself as uncommanded.
                //
                //The clamp is INERT today - nothing could drive the count negative while every send
                //incremented. It goes live with the `break` on a refusal below: a return value that
                //lies stops the increments while the commands still come back, and a count left at
                //-10 makes the loop send its whole allowance every arrival while one executes -- a
                //recovered instrument then behaving worse than a broken one. Bounded here to a
                //one-time overshoot of targetDepth+1, then steady. docs/adr/0041.
                if (scanId != "0")
                {
                    if (outstanding > 0) outstanding--;
                }
                else log.Warn(String.Format("Uncommanded scan #{0} - not answering it (depth {1})",
                                            msScan.Header["Scan"], outstanding));

                //`< targetDepth`, not `<= 0`. At depth 1 the instrument's queue is provably empty
                //between EVERY pair of scans, and a Tribrid does not sit idle waiting for us -- it
                //runs its own method. On the 2026-08-25 Eclipse run that gap was ~186 ms per scan
                //and the method's own ITMS 110-130 filled all of it: 144 scans against our 47,
                //53% of the duty cycle, and an operator watching a display of nothing but ion-trap
                //scans. Keeping one command queued BEHIND the one executing leaves no gap.
                //
                //This is the clause docs/adr/0033 amends in docs/adr/0032. What 0032 removed was a
                //monotonic RATCHET -- depth climbing 2, 4, 9, 11 with no path down - and that stays
                //removed: the count is still derived from arrivals and still incremented only
                //inside the success path, so it cannot grow past the target. Default 2;
                //scheduling.target_depth: 1 restores 0032's behaviour exactly, with no rebuild.
                //
                //A LOOP, and it has to be. One send per arrival can only ever oscillate depth
                //between 0 and 1 -- it can never REACH 2, so a single `if` here would read like
                //the fix and change nothing. Topping up to the target is what bootstraps it: the
                //first arrival sends twice, every arrival after that sends once and holds.
                //
                //Clamped at 1 so a config of 0 or a negative cannot make this body unreachable.
                //That failure is absorbing -- no send, no arrival, no next chance to send.
                int targetDepth = Math.Max(1, methodParams.Config.Scheduling.TargetDepth);

                //BOUNDED TWICE, and neither bound is redundant. `outstanding < targetDepth` is the
                //intent; `sent < targetDepth` is the guard, because outstanding is incremented only
                //inside the success path below -- so a throwing BuildFromCommand would spin this
                //loop forever on the instrument event thread. Note GetNextScanCommand never returns
                //0 for an empty queue (it mints an idle survey instead), so it is no bound at all.
                //
                //!stopRequest is the LATCH half of latch-then-cancel (docs/adr/0041). Without it
                //Main's CancelCustomScan buys nothing: the very next arrival tops the queue straight
                //back up to targetDepth, and the iAPI guarantees arrivals continue after an
                //acquisition closes.
                //
                //Gates the SEND only, deliberately not the whole handler. Ingestion stays on because
                //teardown disposes nothing - every engine log stream flushes per row and
                //~FLASHIda is `= default` - so there is no use-after-free to outrun and no tail
                //worth waiting for.
                for (int sent = 0; !stopRequest && outstanding < targetDepth && sent < targetDepth; sent++)
                {
                    var cmd = new ScanCommand();
                    if (wrapper.GetNextScanCommand(ref cmd) != 1) break;   //0 means the wrapper caught an exception

                    //BuildFromCommand refuses a command whose stage geometry is incomplete rather
                    //than emitting a request the instrument would bind to the wrong stage. Caught
                    //HERE so it cannot escape onto the instrument event thread, where an unhandled
                    //exception does not crash the process the usual way but leaves the API in a
                    //weird state (see the InstrumentConnected remarks).
                    try
                    {
                        //BREAK, not continue - and it mirrors the InvalidOperationException path
                        //below deliberately. One attempt per arrival means one command sent per
                        //arrival and one arrival per command, so a return value that LIES ("false"
                        //for a command that did go) parks the real queue at depth 1 rather than
                        //ratcheting it upward. Retrying inside the same arrival is what would
                        //ratchet. docs/adr/0041.
                        if (!SendCustomScan(scanFactory.BuildFromCommand(cmd))) break;
                        outstanding++;   //ONLY on success -- which now means "the instrument took
                                         //it", not merely "nothing threw". A declined command used
                                         //to be counted here and nothing ever arrived to discharge
                                         //it: two of those and this loop never fires again.
                                         //A throw also leaves depth low so the next arrival
                                         //re-sends, instead of stranding the run at 0.
                    }
                    catch (InvalidOperationException ex)
                    {
                        log.Fatal(String.Format("Refused to send scan {0}: {1}", cmd.ScanId, ex.Message));
                        break;   //one report per arrival; retrying the same bad command targetDepth
                                 //times would just multiply the log line. The next arrival retries.
                    }
                }
            }
        }

        /// <summary>
        /// Handler for acquisition errors
        /// </summary>
        /// <remarks>
        /// Most of these errors are purely technical, such as spray instability
        /// </remarks>
        private static void HandleAcqError(object sender, AcquisitionErrorsArrivedEventArgs e)
        {
            log.Error(String.Format("Aquisition Error: {0}", String.Join("; ", e.Errors.Select(err => err.Content)).Trim()));
        }

        /// <summary>
        /// Arm the run clock, or restart it if it is already armed.
        /// </summary>
        /// <remarks>
        /// THREE call sites, TWO meanings, told apart by <c>duration == null</c>:
        ///
        ///   * both startup paths call it BEFORE the handshake is sent, which ARMS it. That is what
        ///     bounds a run whose handshake never echoes - the send is wrapped in a catch that logs
        ///     and carries on, and OnAcquisitionStreamClosing is armed on inCustom, so if the latch
        ///     never fires this timer is the ONLY stop trigger left besides Ctrl+C.
        ///
        ///   * the handshake latch in ProcessSpectrum calls it again, which RESTARTS it.
        ///     global.duration is measured from the ECHO - the first moment the instrument is under
        ///     our control (CONTEXT.md "Custom control mode") - so the wait for control is not
        ///     charged against the run. Worst-case process lifetime is duration + (send -> echo).
        ///
        /// NOT keyed to IAcquisition.AcquisitionStreamOpening, which is the event actually NAMED for
        /// "the acquisition started". A scan executes and echoes with no acquisition open at all -
        /// "Scans may be created without an explicite acquisition if the instrument is 'just' set to
        /// running" (dependencies/API-2.0.xml:179-192) - and InstrumentConnected commands exactly
        /// that state ~90 lines above, via SetMode(CreateOnMode()). See docs/adr/0043.
        ///
        /// Guards, in order:
        ///   * stopRequest - a stop already latched must never be extended by a late echo. The iAPI
        ///     goes on delivering scans after a stop (docs/adr/0041), and Main does not unsubscribe
        ///     ProcessSpectrum until after the spin loop releases, so a late echo is reachable.
        ///   * ObjectDisposedException - that check cannot be made atomic against RequestStop's
        ///     duration.Close(), and this runs on the arrival thread, where an unhandled exception
        ///     "does not crash the software the usual way, but lead[s] to weird behavior".
        /// </remarks>
        private static void ArmRunClock()
        {
            if (stopRequest) return;

            try
            {
                if (duration == null)
                {
                    //Timer accepts milliseconds, but the duration is in minutes
                    duration = new Timer(methodParams.Config.Global.Duration * 60000);
                    duration.Elapsed += StopExecution; //run StopExecution when the time is up
                    duration.AutoReset = false;
                    duration.Start();
                    sinceArmed = System.Diagnostics.Stopwatch.StartNew();
                    log.Info(String.Format("Run clock armed ({0} min)", methodParams.Config.Global.Duration));
                    return;
                }

                duration.Stop();
                duration.Start();
                log.Info(String.Format("Run clock restarted at the custom control latch - {0:f1} s armed but not charged",
                                       sinceArmed.Elapsed.TotalSeconds));
            }
            catch (ObjectDisposedException)
            {
                log.Debug("Run clock already closed - a stop was requested during the handshake");
            }
        }

        /// <summary>
        /// Stop the acqusition
        /// </summary>
        private static void StopExecution(object sender, ElapsedEventArgs args)
        {
            RequestStop("Time is over");
        }

        /// <summary>
        /// Request an orderly end of the run. One-shot: a systemic scan-processing failure would
        /// otherwise call this once per buffered scan.
        /// </summary>
        /// <param name="reason">Why the run is stopping - logged verbatim, so never pass a fixed
        /// string on an error path (an abort logging "Time is over" is actively misleading).</param>
        /// <returns>
        /// <c>true</c> if THIS call latched the stop, <c>false</c> if one was already requested.
        /// The Ctrl+C handler keys on it: the first press keeps the process alive to run teardown,
        /// the second lets the runtime kill us. Without that, a teardown that blocked would swallow
        /// every subsequent Ctrl+C and leave no way out short of Task Manager.
        /// </returns>
        private static bool RequestStop(string reason)
        {
            //fully qualified: 'using System.Threading' would make the Timer in 'static Timer duration'
            //ambiguous against System.Timers.Timer (CS0104).
            if (System.Threading.Interlocked.Exchange(ref stopRequested, 1) != 0) return false;

            //REASON FIRST, LATCH IN THE FINALLY -- and this inverts the order the comment here used
            //to argue for, because what that comment was written against changed. Setting the flag
            //no longer only ends the spin loop: it RELEASES Main into teardown, which unsubscribes,
            //cancels and RETURNS. So anything after the flag races a process on its way out, and
            //the statement that loses that race is the one recording WHY the run stopped -- the
            //only line the operator gets on the Ctrl+C path.
            //
            //The finally keeps exactly what the old ordering was protecting: a throwing logger or a
            //throwing Close() still latches, so a systemic logging failure cannot strand the run in
            //Main's spin loop. Nothing is traded away.
            //
            //Cost: the latch is delayed by one log call, during which ProcessSpectrum may send once
            //more. That is precisely the old behaviour, so the failure direction is "no worse".
            try
            {
                log.Info(reason);
                if (duration != null) duration.Close();
            }
            finally
            {
                stopRequest = true;
            }

            return true;
        }

        // CheckLogPath is DELETED. It appended a timestamp only ON COLLISION, and by concatenation
        // onto the already-suffixed name, so a third collision produced name_ts1_ts2_ts3. It also
        // probed File.Exists on RELATIVE names -- resolved against the process CWD -- while log4net
        // writes relative paths under AppDomain.BaseDirectory, so the guard may never have been
        // looking at the directory it was protecting. LogPathResolver.Compose now stamps every run
        // unconditionally and disambiguates with a _2/_3 suffix, so collisions cannot merge runs.
    }
}
