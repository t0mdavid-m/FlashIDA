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

        //Holds the next instrument request already drained and already built, so ProcessSpectrum only
        //has to hand it over. See docs/adr/0024 for why the drain must not happen on this thread.
        static NextScanSource source;

        //loggers
        static ILog log;

        //Method parameters
        static MethodParameters methodParams;

        //Duration timer
        static Timer duration;

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

            log.Info("Exiting");
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

                //Armed-command buffer. The refill goes to a DEDICATED background thread, not the
                //thread pool: a filler blocked on the engine's analysis_mutex_ would otherwise occupy
                //a pool slot that the DataPipe ActionBlock needs to run and release that very mutex,
                //and the pool injects replacement threads at only ~1-2/sec.
                //System.Threading is fully qualified on purpose - a using would make the Timer in
                //'static Timer duration' ambiguous against System.Timers.Timer (CS0104).
                source = new NextScanSource(
                    TryDrainNextCommand,
                    cmd => scanFactory.BuildFromCommand(cmd),
                    BuildFillerScan,
                    work => new System.Threading.Thread(new System.Threading.ThreadStart(work))
                            { IsBackground = true }.Start());
                log.Info("Created NextScanSource");
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

                //start method
                duration = new Timer(methodParams.Config.Global.Duration * 60000); //Timer acepts milliseconds, but the duration is in minutes
                duration.Elapsed += StopExecution; //run StopExecution when the time is up
                duration.AutoReset = false;
                duration.Start();
                log.Info("Method started");

                //send the first custom scan (the handshake one)
                try
                {
                    scanControl.SetFusionCustomScan(BuildHandshakeScan());
                    log.Info("Sent the first magic scan");

                    //Arm the buffer while the handshake is in flight. Synchronous on purpose: the
                    //handshake echo can return in tens of milliseconds, and nothing is acquiring yet,
                    //so however long the first drain takes costs nothing. MUST be on both startup
                    //paths or they drift - the defect ADR-0008 exists to prevent.
                    source.Arm();
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

            //start method
            duration = new Timer(methodParams.Config.Global.Duration * 60000);
            duration.Elapsed += StopExecution;
            duration.AutoReset = false;
            duration.Start();
            log.Info("Method started");
            
            //send the first custom scan (the handshake one).
            //MUST be the same handshake scan the OverrideCC branch sends: it carries the
            //HandshakeJobNumber the latch in ProcessSpectrum keys on. Building it from
            //GetNextScanCommand instead stamps the engine's first tracking id (0, an
            //iAPI-reserved value), the latch never fires, and the run acquires nothing.
            try
            {
                scanControl.SetFusionCustomScan(BuildHandshakeScan());
                log.Info("Sent the first magic scan");

                //Arm the buffer while the handshake is in flight - see the OverrideCC branch. Both
                //startup paths must do this, or the first arriving scan finds an empty buffer and is
                //answered with a filler instead of the engine's first real command.
                source.Arm();
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
        /// echoed back immediately. <c>delay: 3</c> is load-bearing - SingleProcessingDelay is the time
        /// the instrument waits for further custom scan requests after executing this one, i.e. the
        /// grace window that keeps custom control open until the first real command is drained.
        ///
        /// Must be handed to <c>scanControl.SetFusionCustomScan</c> DIRECTLY, never via
        /// <see cref="SendCustomScan"/>, which would overwrite RunningNumber with ++currentNumber.
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
                }, id: HandshakeJobNumber, IsAGC: true, delay: 3);
        }

        /// <summary>
        /// Drain one command out of the engine. Matches <see cref="TryDrainCommand"/>.
        /// </summary>
        /// <remarks>
        /// A <c>false</c> means the wrapper CAUGHT something, never "queue empty" - the engine returns
        /// 1 on every path and fabricates an idle AGC when its queues drain.
        /// </remarks>
        private static bool TryDrainNextCommand(out ScanCommand cmd)
        {
            cmd = new ScanCommand();
            return wrapper.GetNextScanCommand(ref cmd) == 1;
        }

        /// <summary>
        /// Build the cheap scan submitted when no armed command is ready.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT <see cref="BuildHandshakeScan"/>, though the parameters are identical.
        /// That one stamps <c>id: HandshakeJobNumber</c>, and its echo hits the latch in
        /// <see cref="ProcessSpectrum"/>, which reassigns <c>currentNumber = HandshakeJobNumber</c> -
        /// re-issuing instrument job numbers already used earlier in the run. The engine never reads
        /// them, so it is not fatal, but it destroys log correlation exactly when someone is trying to
        /// diagnose timing.
        ///
        /// Sent through <see cref="SendCustomScan"/> like any other command, so it takes the next
        /// running number. It carries no ScanDescription, so the engine rejects its echo before
        /// deconvolution (gate 1), same as the handshake's: a control signal, not data. That also means
        /// filler scans appear in the raw file and in no engine log - <c>NextScanSource.DryRuns</c> and
        /// its warnings are the only trace they leave.
        ///
        /// Built fresh per call rather than cached and re-stamped: <see cref="SendCustomScan"/> assigns
        /// RunningNumber immediately before submitting, and mutating a scan the iAPI may still hold
        /// from the previous submission would be a race for no gain.
        /// </remarks>
        private static IFusionCustomScan BuildFillerScan()
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
                }, id: 0, IsAGC: true, delay: 0);
        }

        /// <summary>
        /// Method to send custom scan request to instrument
        /// </summary>
        /// <param name="scan">Scan request</param>
        private static void SendCustomScan(IFusionCustomScan scan)
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

                scanControl.SetFusionCustomScan(scan);
            }
            else
            {
                log.Debug("Sending NULL - Nothing to do");
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
        /// Processing routine for each scans received from the instrument
        /// Scan is contained in event arhs <paramref name="e"/>
        /// </summary>
        private static void ProcessSpectrum(object sender, MsScanEventArgs e)
        {
            IMsScan msScan = e.GetScan();

            //parse out API scan identifier
            msScan.Trailer.TryGetValue("Access ID", out var scanId);

            if (msScan.Header["MSOrder"] == "1")
            {
                log.Debug(String.Format("RECD {0} MS1 Scan #{1}; ID: {2}",
                    msScan.Header["MassAnalyzer"], msScan.Header["Scan"], scanId));
            }
            else if (msScan.Header["MSOrder"] == "2")
            {
                log.Debug(String.Format("RECD {0} MS2 Scan #{1}; ID: {2}; Precursor: {3:f04}",
                    msScan.Header["MassAnalyzer"], msScan.Header["Scan"], scanId, msScan.Header["PrecursorMass[0]"]));
            }

            //when handshake scan received switch to custom control mode
            if (scanId == HandshakeJobNumber.ToString())
            {
                if (!inCustom) inCustom = true;
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
                //1. Top up the armed-command buffer if it is running low. Non-blocking - the drain and
                //   the build happen on a background thread. First, so the refill overlaps the rest.
                source.OnScanArrived();

                //2. Hand the arriving scan to the analysis pipeline.
                //   A rejected Post means the scan is DROPPED, not deferred - never silent.
                if (!dataPipe.Push(msScan))
                {
                    log.Error(String.Format("Scan {0} was rejected by the pipeline and will NOT be processed", scanId));
                }

                //3. Submit. Next() is pure handover: no P/Invoke, no BuildFromCommand, no disk flush.
                //   The drain used to happen right here, between the scan arriving and the command
                //   going out, while the instrument waited (ADR-0024).
                //   Next() hands back a cheap filler rather than nothing when the buffer has run dry,
                //   and it never throws - an exception escaping onto the instrument event thread does
                //   not crash the process the usual way but leaves the API in a weird state (see the
                //   InstrumentConnected remarks). The refusal of a malformed command is caught inside
                //   NextScanSource.FillOnce now, on the filler thread, off this path entirely.
                SendCustomScan(source.Next());
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
        private static void RequestStop(string reason)
        {
            //fully qualified: 'using System.Threading' would make the Timer in 'static Timer duration'
            //ambiguous against System.Timers.Timer (CS0104).
            if (System.Threading.Interlocked.Exchange(ref stopRequested, 1) != 0) return;

            //set the flag FIRST: a throw in the logging or timer teardown below must not be able to
            //strand the run in the Main spin loop.
            stopRequest = true;
            log.Info(reason);
            if (duration != null) duration.Close();
        }

        // CheckLogPath is DELETED. It appended a timestamp only ON COLLISION, and by concatenation
        // onto the already-suffixed name, so a third collision produced name_ts1_ts2_ts3. It also
        // probed File.Exists on RELATIVE names -- resolved against the process CWD -- while log4net
        // writes relative paths under AppDomain.BaseDirectory, so the guard may never have been
        // looking at the directory it was protecting. LogPathResolver.Compose now stamps every run
        // unconditionally and disambiguates with a _2/_3 suffix, so collisions cannot merge runs.
    }
}
