using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Flash.IDA;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;
using Thermo.TNG.Client.API.Control.Scans;

namespace Flash
{
    /// <summary>
    /// All available scan parameters from the API
    /// </summary>
    /// <remarks>
    /// Names for the parameters are matching API representation and should not be changed
    /// Underscores are exchanged for spaces
    /// </remarks>
    public struct ScanParameters
    {
        public string Analyzer;
        public double[] FirstMass;
        public double[] LastMass;
        public int? OrbitrapResolution;
        /// <summary>
        /// One pre-formatted ';'-group per cascade stage, each a ','-joined list of that stage's
        /// windows (the stage itself, then its co-isolation notches). string[] rather than double[]
        /// because the element is a GROUP, not a number — see PrecursorMass.
        /// </summary>
        public string[] IsolationWidth;
        public string IsolationMode;
        public string[] ActivationType;
        public int? AGCTarget;
        public int? MSXTargets;
        public double? MaxIT;
        /// <summary>
        /// The isolation targets, carrying BOTH wire axes: ';' descends an MSn cascade stage, ','
        /// widens one into parallel co-isolation notches. Element i is stage i's group, already
        /// ','-joined, so FillParameters' String.Join(";", ...) yields e.g. "524.3;104,271,453" — an
        /// MS3 whose MS3 stage isolates three windows simultaneously.
        /// </summary>
        public string[] PrecursorMass;
        public int[] CollisionEnergy;
        public string ScanType;
        public double? SourceCIDEnergy;
        public double? SourceCIDScalingFactor;
        public int? Microscans;
        public string DataType;
        /// <summary>Same two-axis grouping as <see cref="PrecursorMass"/>: one group per stage.</summary>
        public string[] ChargeStates;
        public string ScanRate;
        public string Polarity;
        public double[] SrcRFLens;
        public string IonisationMode;
        public double[] ActivationQ;
        public double[] ReactionTime;
        public double[] ReagentMaxIT;
        public int[] ReagentAGCTarget;
        public double? FAIMS_CV;
        public string FAIMS_Voltages;
        public string ScanDescription;
        public string ScanRangeMode;
    }

    /// <summary>
    /// Helper-class to create scan requests for the instrument
    /// </summary>
    public class ScanFactory
    {
        private IFusionScans controler;

        /// <summary>
        /// Create an instance using provided <see cref="IFusionScans"/> for scan initialization by API
        /// </summary>
        /// <param name="scanControler">Scan controller</param>
        public ScanFactory(IFusionScans scanControler)
        {
            controler = scanControler;
        }

        /// <summary>
        /// Create a single custom scan request <seealso cref="ICustomScan"/>
        /// </summary>
        /// <param name="parameters">Scan parameters, such as analyzer, resolution, etc, <seealso cref="ScanParameters"/></param>
        /// <param name="id">Identifier for later refernce, it will be preserved in the scan returned by the instrument</param>
        /// <param name="delay">Processing delay - the time for the instrument to wait for any further custom scans requests after
        /// executing this request</param>
        /// <returns></returns>
        public ICustomScan CreateCustomScan(ScanParameters parameters, int id = 0, double delay = 0)
        {
            ICustomScan newScan = controler.CreateCustomScan();
            FillParameters(newScan, parameters);
            newScan.RunningNumber = id;
            newScan.SingleProcessingDelay = delay;

            return newScan;
        }

        /// <summary>
        /// Create a single custom scan request of tribrid format
        /// </summary>
        /// <remarks>
        /// This is extended version of <see cref="CreateCustomScan(ScanParameters, int, double)"/>
        /// </remarks>
        /// <param name="parameters">Scan parameters, such as analyzer, resolution, etc, <seealso cref="ScanParameters"/></param>
        /// <param name="id">Identifier for later refernce, it will be preserved in the scan returned by the instrument</param>
        /// <param name="delay">Processing delay - the time for the instrument to wait for any further custom scans requests after
        /// executing this request</param>
        /// <param name="IsAGC">Boolean indicator if this scan is an AGC scan, i.e. used for estimating current ion flux</param>
        /// <param name="AGCgroup">Identifier of the AGC group, AGC scan (i.e. the one with <paramref name="IsAGC"/> = true) will 
        /// be used for AGC of all the scans in the same group</param>
        /// <returns></returns>
        public virtual IFusionCustomScan CreateFusionCustomScan(ScanParameters parameters, int id = 0, double delay = 0, bool IsAGC = false, int AGCgroup = 1)
        {
            IFusionCustomScan newScan = new FusionCustomScan();
            FillParameters(newScan, parameters);
            newScan.RunningNumber = id;
            newScan.SingleProcessingDelay = delay;
            newScan.IsPAGCScan = IsAGC;
            newScan.PAGCGroupIndex = AGCgroup;
            return newScan;
        }

        /// <summary>
        /// Create a repeating scan request <seealso cref="IRepeatingScan"/>
        /// </summary>
        /// <param name="parameters">Scan parameters, such as analyzer, resolution, etc, <seealso cref="ScanParameters"/></param>
        /// <param name="id">Identifier for later refernce, it will be preserved in all repeated scans returned by the instrument</param>
        /// <returns></returns>
        public IRepeatingScan CreateRepeatingScan(ScanParameters parameters, int id = 0)
        {
            IRepeatingScan newScan = controler.CreateRepeatingScan();
            FillParameters(newScan, parameters);
            newScan.RunningNumber = id;

            return newScan;
        }

        /// <summary>
        /// Formats one scan-parameter value for the wire, always with InvariantCulture.
        /// </summary>
        /// <remarks>
        /// A plain <c>ToString()</c> uses the CURRENT culture, so on a comma-decimal locale
        /// (de-DE, fr-FR, ...) a precursor m/z of 1000.5 is rendered "1000,5". That is not merely an
        /// odd-looking number: in the iAPI scan-parameter grammar a ',' separates parallel
        /// co-isolation windows within one MS stage (see docs/kb/scan-pipeline/multi-notch-wire-grammar.md),
        /// so "1000,5" is a well-formed request for TWO isolation windows — at m/z 1000 and m/z 5.
        /// The instrument has no way to tell that apart from a deliberate multiplexed isolation.
        ///
        /// Only values with a fractional part are exposed, which is why this stayed hidden:
        /// PrecursorMass and IsolationWidth are the live victims, while integral values
        /// (AGCTarget, FirstMass: 500, SrcRFLens: 30) render identically in either culture.
        /// GitHub's windows-2022 runners are en-US, so CI structurally cannot observe it —
        /// ScanFactoryCultureTests imposes the culture rather than inheriting it.
        /// </remarks>
        // public, not internal: these three are half of a cross-language ABI contract, and the layout
        // tests in Flash.Tests assert against them. There is no InternalsVisibleTo in this assembly.

        /// <summary>Mirrors C++ <c>MAX_ISOLATION_STAGES</c>: the instrument's ';'-axis limit, i.e. the
        /// deepest MSⁿ cascade one command can request.</summary>
        public const int MaxIsolationStages = 10;

        /// <summary>Mirrors C++ <c>MAX_NOTCHES_PER_STAGE</c>: co-isolation windows per fragmentation
        /// stage is capped at 10 (MSXTargets' own limit), and one of those is the anchor.</summary>
        public const int MaxNotchesPerStage = 9;

        /// <summary>Mirrors C++ <c>MAX_NOTCHES</c>: a per-stage block for each of the two stages that
        /// can carry notches.</summary>
        public const int MaxNotches = 2 * MaxNotchesPerStage;

        /// <summary>
        /// The co-isolation notches belonging to cascade stage <paramref name="k"/> of a command.
        /// </summary>
        /// <remarks>
        /// C# mirror of C++ <c>notchesForStage</c> (ScanCommand.h) — keep the two in lockstep. Stage
        /// <paramref name="k"/> owns the FIXED block <c>[k * MaxNotchesPerStage, +MaxNotchesPerStage)</c>
        /// of <c>Notches</c>, so either stage can carry a full 10-plex and neither can consume the
        /// other's slots. They used to be packed into the unused tail of <c>Stages[]</c> with stage-1's
        /// offset depending on stage-0's count, which capped an MS3 at 8 windows across both stages.
        /// </remarks>
        private static IEnumerable<Notch> NotchesForStage(ScanCommand cmd, int k)
        {
            if (k < 0 || k > 1) yield break;
            int count = (k == 0) ? cmd.Stage0NotchCount : cmd.Stage1NotchCount;
            if (count <= 0) yield break;
            int begin = k * MaxNotchesPerStage;
            if (count > MaxNotchesPerStage || begin + count > MaxNotches) yield break;
            for (int i = 0; i < count; i++) yield return cmd.Notches[begin + i];
        }

        private static string Fmt(object value)
        {
            IFormattable formattable = value as IFormattable;
            return formattable != null ? formattable.ToString(null, CultureInfo.InvariantCulture)
                                       : value.ToString();
        }

        /// <summary>
        /// Updates parameters of a scan request template according to the provided <see cref="ScanParameters"/>
        /// </summary>
        /// <param name="scan">Scan template, as received from API</param>
        /// <param name="parameters">Scan parameters</param>
        /// <remarks>
        /// <c>protected</c>, not <c>private</c>, deliberately: <c>MockScanFactory</c> used to carry a
        /// hand-copied reimplementation of this method because it could not reach it, which meant every
        /// test asserting on the <c>Values</c> dictionary was validating the copy rather than this
        /// method — and the two had already drifted on number formatting. One definition, one behaviour.
        /// </remarks>
        protected void FillParameters(IScanDefinition scan, ScanParameters parameters)
        {
            foreach (FieldInfo field in typeof(ScanParameters).GetFields())
            {
                if (field.GetValue(parameters) != null)
                    if (field.FieldType.IsArray) //arrays has to be provided as "elemnt1;element2;element3..."
                         scan.Values.Add(field.Name.Replace("_", " "),
                             //This casts `object` to `object[]` and joins it into string
                             String.Join(";", (field.GetValue(parameters) as IEnumerable).Cast<object>()
                                                  .Select(Fmt).ToArray()));
                    else
                        scan.Values.Add(field.Name.Replace("_", " "), Fmt(field.GetValue(parameters)));
            }
        }

        /// <summary>
        /// Build a Fusion custom scan from a ScanCommand struct returned by the C++ engine.
        /// Maps blittable struct fields to ScanParameters and creates the scan via existing API.
        /// </summary>
        /// <param name="cmd">ScanCommand from GetNextScanCommand</param>
        /// <returns>IFusionCustomScan ready for submission</returns>
        public virtual IFusionCustomScan BuildFromCommand(ScanCommand cmd)
        {
            var p = new ScanParameters();

            // Analyzer
            if (!string.IsNullOrEmpty(cmd.Analyzer))
                p.Analyzer = cmd.Analyzer;

            // Mass range
            if (cmd.FirstMass > 0)
                p.FirstMass = new double[] { cmd.FirstMass };
            if (cmd.LastMass > 0)
                p.LastMass = new double[] { cmd.LastMass };
            if (cmd.FirstMass > 0 && cmd.LastMass > 0) {
                p.ScanRangeMode = "DefineMZRange";
            }
            else if (cmd.FirstMass > 0) {
                p.ScanRangeMode = "DefineFirstMass";
            }

            // Orbitrap resolution (nullable — leave null if 0 = not set)
            if (cmd.OrbitrapResolution > 0)
                p.OrbitrapResolution = cmd.OrbitrapResolution;

            // AGC target (nullable)
            if (cmd.AgcTarget > 0)
                p.AGCTarget = cmd.AgcTarget;

            // Max injection time (nullable)
            if (cmd.MaxIt > 0)
                p.MaxIT = cmd.MaxIt;

            // MSn scan type
            if (cmd.MsnLevel > 1)
            {
                p.ScanType = "MSn";
            }
            else
            {
                p.ScanType = "Full";
            } 

            // Isolation stages
            if (cmd.NumStages > 0 && cmd.Stages != null)
            {
                int n = Math.Min(cmd.NumStages, MaxIsolationStages);
                // Two axes in one string (docs/kb/scan-pipeline/multi-notch-wire-grammar.md): ';'
                // descends an MSn cascade stage, ',' widens one into parallel co-isolation notches.
                // The three notch-bearing keys therefore carry one ';'-separated GROUP per stage, each
                // group a ','-joined list. Groups are pre-formatted here so FillParameters' plain
                // String.Join(";", ...) produces the whole thing unchanged -- and so every number goes
                // through Fmt, keeping the ',' axis safe from a decimal comma.
                var precursorMasses = new List<string>();
                var isolationWidths = new List<string>();
                var chargeStates = new List<string>();
                // Per cascade stage only: all notches of a stage fire into the SAME fragmentation
                // event, and the wire has no per-notch slot for these (verified against Thermo's own
                // SPS example, which sends ActivationType "HCD;CID" against a three-notch MS3 stage).
                var collisionEnergies = new List<int>();
                var activationTypes = new List<string>();
                var reactionTimes = new List<double>();
                var reagentMaxIts = new List<double>();
                var reagentAgcTargets = new List<int>();

                // Array POSITION is the only thing binding a value to a stage: element i -> stage i.
                // A stage must therefore contribute an element to EVERY array it appears in, or each
                // later stage shifts one slot forward onto the wrong stage. These predicates used to
                // read `if (value > 0)`, inherited verbatim from the single-stage version of this
                // block where they could only mean "set the field or leave it null" -- see the note
                // in Config.cpp:508-510, which names this exact failure.
                //
                // STRUCTURAL (mz, width, CE, activation, charge) describe THAT a stage exists. Always
                // one element per stage; a zero is malformed rather than "unused", so a stage missing
                // them is refused outright instead of being zero-filled.
                // OPTIONAL (reaction_time, reagent_max_it, reagent_agc_target) are activation-coupled
                // and 0 is their documented "not used" sentinel (ScanCommand.h:53-55). Zero-filled
                // positionally, but the key is omitted entirely when NO stage uses one, so a wholly
                // unused parameter still defers to the instrument method default (ADR-0009).
                for (int i = 0; i < n; i++)
                {
                    var stage = cmd.Stages[i];
                    if (stage.PrecursorMz <= 0 || stage.IsolationWidth <= 0 || stage.ChargeState <= 0
                        || string.IsNullOrEmpty(stage.ActivationType))
                    {
                        throw new InvalidOperationException(String.Format(
                            "ScanCommand {0} stage {1} is missing isolation geometry " +
                            "(mz={2}, width={3}, z={4}, activation='{5}') - refusing to build a " +
                            "malformed MSn request.",
                            cmd.ScanId, i, stage.PrecursorMz, stage.IsolationWidth,
                            stage.ChargeState, stage.ActivationType));
                    }

                    // This stage's group starts with the stage itself (the anchor), then its notches.
                    var mzGroup = new List<string> { Fmt(stage.PrecursorMz) };
                    var widthGroup = new List<string> { Fmt(stage.IsolationWidth) };
                    var chargeGroup = new List<string> { Fmt(Math.Min(stage.ChargeState, 25)) };

                    foreach (var notch in NotchesForStage(cmd, i))
                    {
                        // A notch is refused on the same structural grounds as a stage: position binds
                        // a value to a window, so a notch missing geometry would shift every later
                        // notch onto the wrong one.
                        if (notch.PrecursorMz <= 0 || notch.IsolationWidth <= 0 || notch.ChargeState <= 0)
                        {
                            throw new InvalidOperationException(String.Format(
                                "ScanCommand {0} stage {1} has a co-isolation notch missing geometry " +
                                "(mz={2}, width={3}, z={4}) - refusing to build a malformed MSn request.",
                                cmd.ScanId, i, notch.PrecursorMz, notch.IsolationWidth, notch.ChargeState));
                        }
                        mzGroup.Add(Fmt(notch.PrecursorMz));
                        widthGroup.Add(Fmt(notch.IsolationWidth));
                        chargeGroup.Add(Fmt(Math.Min(notch.ChargeState, 25)));
                    }

                    precursorMasses.Add(String.Join(",", mzGroup.ToArray()));
                    isolationWidths.Add(String.Join(",", widthGroup.ToArray()));
                    chargeStates.Add(String.Join(",", chargeGroup.ToArray()));

                    collisionEnergies.Add((int)Math.Round(stage.CollisionEnergy));
                    activationTypes.Add(stage.ActivationType);
                    reactionTimes.Add(stage.ReactionTime);
                    reagentMaxIts.Add(stage.ReagentMaxIt);
                    reagentAgcTargets.Add(stage.ReagentAgcTarget);
                }

                p.PrecursorMass = precursorMasses.ToArray();
                p.IsolationWidth = isolationWidths.ToArray();
                p.CollisionEnergy = collisionEnergies.ToArray();
                p.ActivationType = activationTypes.ToArray();
                p.ChargeStates = chargeStates.ToArray();
                if (reactionTimes.Any(v => v > 0)) p.ReactionTime = reactionTimes.ToArray();
                if (reagentMaxIts.Any(v => v > 0)) p.ReagentMaxIT = reagentMaxIts.ToArray();
                if (reagentAgcTargets.Any(v => v > 0)) p.ReagentAGCTarget = reagentAgcTargets.ToArray();
            }

            // Scan description
            if (!string.IsNullOrEmpty(cmd.ScanDescription))
                p.ScanDescription = cmd.ScanDescription;

            // FAIMS from the C++ engine (ADR-0012). Branch on the explicit flag, never on the CV
            // magnitude: |cv| > 0.001 could not tell "FAIMS is not in use" from "the compensation
            // voltage is 0", and it had no else branch at all -- so FLASHIda could say "on" and
            // never "off". "off" is an instruction the instrument has to be given; without it a
            // non-FAIMS run inherits whatever FAIMS state the instrument method carried.
            if (cmd.FaimsEnabled != 0)
            {
                p.FAIMS_CV = cmd.FaimsCv;
                p.FAIMS_Voltages = "on";
            }
            else
            {
                p.FAIMS_Voltages = "off";
            }

            // New scan parameters from C++ engine
            if (cmd.Microscans > 0)
                p.Microscans = cmd.Microscans;

            // SOURCE-REGION GROUP (ADR-0011) -- emitted unconditionally, unlike every analyzer-side
            // scalar above, and travelling together as one unit.
            //
            // These three describe the ion source rather than this scan's analyzer, so 0 is a real
            // setting and not the "leave it to the method" sentinel that ScanCommand.h documents for
            // the analyzer-side scalars. source_cid_scaling makes the point: 0 is its DOCUMENTED
            // correct value (MethodParameters.cs, etc/method.json), so a `> 0` guard meant
            // SourceCIDScalingFactor was never sent on any scan at any level, and the instrument
            // silently applied whatever scaling its own method carried.
            //
            // Restores the pre-port behaviour of IDAScanProcessor.cs@cd0d086:116-118, which set all
            // three unguarded from MS1 -- in deliberate contrast to the guarded CollisionEnergy /
            // ReactionTime / Reagent* lines immediately above it there. The engine now supplies the
            // inherited value (MethodParameters.ToJsonScanConfig), so every command already carries
            // the survey's source region and there is nothing left to defer.
            //
            // This makes makeAGC's source region load-bearing: an AGC command that left these at 0
            // would now actively command RF lens 0 rather than omitting the key.
            p.SrcRFLens              = new double[] { cmd.RfLens };
            p.SourceCIDEnergy        = cmd.SourceCid;
            p.SourceCIDScalingFactor = cmd.SourceCidScaling;

            if (!string.IsNullOrEmpty(cmd.DataType))
                p.DataType = cmd.DataType;

            if (!string.IsNullOrEmpty(cmd.ScanRate))
                p.ScanRate = cmd.ScanRate;

            bool isAgc = cmd.IsAgc != 0;
            return CreateFusionCustomScan(p, cmd.ScanId, delay: 0.0, IsAGC: isAgc, AGCgroup: 1);
        }

        /// <summary>
        /// Text representation of a scan request object
        /// </summary>
        /// <param name="scan">Scan request</param>
        /// <returns></returns>
        public string ScanToString(ICustomScan scan)
        {
            string result = "";
            result += String.Join("\n", scan.Values.Select(e => String.Format("{0} = {1}", e.Key, e.Value)).ToArray());
            result += String.Format("\nID = {0}\nDelay = {1}", scan.RunningNumber, scan.SingleProcessingDelay);
            return result;
        }
    }
}
