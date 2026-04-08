using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Flash.IDA;

namespace Flash
{
    /// <summary>
    /// MS1 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS1Parameters
    {
        public string Analyzer;
        public double FirstMass;
        public double LastMass;
        public int OrbitrapResolution;
        public int AGCTarget;
        public double MaxIT;
        public int Microscans;
        public string DataType;
        public double RFLens;
        public double SourceCID;
        // Should be zero
        public double SourceCIDScaling;
    }

    /// <summary>
    /// MS2 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS2Parameters
    {
        public string Analyzer;
        public string IsolationMode;
        public double FirstMass;
        public double LastMass;
        public int OrbitrapResolution;
        public int AGCTarget;
        public double MaxIT;
        public int Microscans;
        public string DataType;
        public string Activation;
        public double ReactionTime;
        public double ReagentMaxIT;
        public int ReagentAGCTarget;
        public int CollisionEnergy;
    }

    /// <summary>
    /// MS3 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS3Parameters
    {
        public string Analyzer;
        public string IsolationMode;
        public double FirstMass;
        public double LastMass;
        public int OrbitrapResolution;
        public int AGCTarget;
        public double MaxIT;
        public int Microscans;
        public string DataType;
        public string Activation;
        public double ReactionTime;
        public double ReagentMaxIT;
        public int ReagentAGCTarget;
        public int CollisionEnergy;
    }

    /// <summary>
    /// Complete set of aquisition parameters, includs MS1, MS2, MS3, FlashIDA, and some general ones
    /// </summary>
    public class MethodParameters
    {
        // === New XML structure (serialized) ===
        public GlobalParameters GlobalParameter;
        public PrecursorSelectionParameters PrecursorSelection;
        public AcquisitionModesConfig AcquisitionModes;
        public MSSettingsConfig MSSettings;
        public SelectionStrategyConfig SelectionStrategy;

        // === Backward-compatible accessors (not serialized) ===
        [XmlIgnore]
        public double Duration => GlobalParameter?.Duration ?? 90;

        [XmlIgnore]
        public MS1Parameters MS1 => MSSettings?.MS1 ?? new MS1Parameters();

        [XmlIgnore]
        public List<MS2Parameters> MS2 => MSSettings?.MS2 ?? new List<MS2Parameters>();

        [XmlIgnore]
        public List<MS3Parameters> MS3 => MSSettings?.MS3 ?? new List<MS3Parameters>();

        [XmlIgnore]
        public bool isobaricQuantification => IsActive(AcquisitionModes?.LabelingBasedQuantification?.Active);

        [XmlIgnore]
        public IDAParameters IDA { get; private set; }

        private static bool IsActive(string val) =>
            val != null && val.Equals("True", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Default constructor
        /// </summary>
        public MethodParameters()
        {
            GlobalParameter = new GlobalParameters();
            PrecursorSelection = new PrecursorSelectionParameters();
            AcquisitionModes = new AcquisitionModesConfig();
            MSSettings = new MSSettingsConfig();
        }

        /// <summary>
        /// Assemble IDAParameters from the new XML structure sections
        /// </summary>
        public void InitializeIDA()
        {
            IDA = new IDAParameters();

            // From PrecursorSelection
            IDA.QScoreThreshold = PrecursorSelection.QScoreThreshold;
            IDA.TQScoreThreshold = PrecursorSelection.TQScoreThreshold;
            IDA.MinCharge = PrecursorSelection.MinCharge;
            IDA.MaxCharge = PrecursorSelection.MaxCharge;
            IDA.MinMass = PrecursorSelection.MinMass;
            IDA.MaxMass = PrecursorSelection.MaxMass;
            IDA.RTWindow = PrecursorSelection.RTWindow;
            IDA.Tolerances = PrecursorSelection.Tolerances;

            // From AcquisitionModes - compute TargetMode from TargetingMode string
            switch (AcquisitionModes.TargetingMode?.ToLower())
            {
                case "deep": IDA.TargetMode = 3; break;
                case "exclusion": IDA.TargetMode = 2; break;
                case "inclusion": IDA.TargetMode = 1; break;
                default: IDA.TargetMode = 0; break;
            }

            IDA.TargetLogs = AcquisitionModes.TargetLogs ?? new List<string>();

            // From TargetedInclusion
            var incl = AcquisitionModes.TargetedInclusion;
            if (incl != null)
            {
                IDA.StrictInclusion = incl.StrictInclusion;
                IDA.TieThreshold = incl.TieThreshold;
                IDA.InclusionList = incl.InclusionList;

                var tag = incl.MS2Tagging;
                if (tag != null)
                {
                    IDA.MS2Tagging = IsActive(tag.Active);
                    IDA.ConditionalMS2 = tag.ConditionalMS2;
                    IDA.FastaFile = tag.FastaFile;
                    IDA.PtmList = tag.PtmList;
                    IDA.MaxPtmCount = tag.MaxPtmCount;
                    IDA.MinTagLength = tag.MinTagLength;
                    IDA.MaxTagLength = tag.MaxTagLength;
                    IDA.MaxFlankingMassDiff = tag.MaxFlankingMassDiff;
                }
            }

            // From LabelingBasedQuantification
            var quant = AcquisitionModes.LabelingBasedQuantification;
            if (quant != null)
            {
                IDA.quantReporterMZTol = quant.ReporterMZTol;
                IDA.quantFoldChangeThreshold = quant.FoldChangeThreshold;
                IDA.quantOnlyOneCondition = quant.OnlyOneCondition;
            }

            // From MS3Characterization
            var ms3 = AcquisitionModes.MS3Characterization;
            if (ms3 != null)
            {
                IDA.EnableMS3 = IsActive(ms3.Active);
                IDA.MS3Mode = ms3.MS3Mode;
                IDA.MaxMs3PerMs2 = ms3.MaxMs3PerMs2;
                IDA.MS3AllCharges = ms3.MS3AllCharges;
                IDA.MS3ProteinSequence = ms3.MS3ProteinSequence;
            }

            // From Developer
            var dev = AcquisitionModes.Developer;
            if (dev != null)
            {
                var devPS = dev.PrecursorSelection;
                if (devPS != null)
                {
                    IDA.UseIDScore = devPS.UseIDScore;
                    IDA.ConsiderAllChargeStates = devPS.ConsiderAllChargeStates;
                    IDA.HCDEnergy = devPS.HCDEnergy;
                }

                var devFaims = dev.FAIMS;
                if (devFaims != null)
                {
                    IDA.MaxCVSkip = devFaims.MaxCVSkip;
                    IDA.MassThreshold = devFaims.MassThreshold;
                }
            }

            // From MSSettings.FAIMS
            IDA.CVValues = MSSettings?.FAIMS?.CVValues ?? new double[] { -50.0 };
        }

        /// <summary>
        /// Returns a concise multi-line summary of all method parameters for logging
        /// </summary>
        public string ToLogString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- Method Parameters ---");

            // Global
            sb.AppendFormat("Global: Duration={0}min\n", Duration);

            // Precursor selection
            var ida = IDA;
            sb.AppendFormat("Precursor: QScore>={0}, TQScore>={1}, Charge=[{2},{3}], Mass=[{4},{5}], RTWindow={6}s, Tol=[{7}]\n",
                ida.QScoreThreshold, ida.TQScoreThreshold, ida.MinCharge, ida.MaxCharge,
                ida.MinMass, ida.MaxMass, ida.RTWindow,
                String.Join(",", ida.Tolerances));

            // Targeting mode
            var targetMode = AcquisitionModes?.TargetingMode ?? "None";
            sb.AppendFormat("Targeting: {0}\n", targetMode);

            // Inclusion
            sb.AppendFormat("Inclusion: Strict={0}, TieThreshold={1}\n", ida.StrictInclusion, ida.TieThreshold);

            // MS2 Tagging
            if (ida.MS2Tagging)
                sb.AppendFormat("MS2Tagging: ConditionalMS2={0}, Fasta={1}, Tags=[{2},{3}], MaxPtm={4}\n",
                    ida.ConditionalMS2, ida.FastaFile ?? "", ida.MinTagLength, ida.MaxTagLength, ida.MaxPtmCount);
            else
                sb.AppendLine("MS2Tagging: Off");

            // Quant
            if (isobaricQuantification)
                sb.AppendFormat("Quant: MZTol={0}, FoldChange={1}, OneCondition={2}\n",
                    ida.quantReporterMZTol, ida.quantFoldChangeThreshold, ida.quantOnlyOneCondition);
            else
                sb.AppendLine("Quant: Off");

            // MS3
            if (ida.EnableMS3)
                sb.AppendFormat("MS3: Mode={0}, MaxPerMS2={1}, AllCharges={2}, Seq={3}\n",
                    ida.MS3Mode, ida.MaxMs3PerMs2, ida.MS3AllCharges, ida.MS3ProteinSequence ?? "");
            else
                sb.AppendLine("MS3: Off");

            // Developer
            sb.AppendFormat("Developer: IDScore={0}, AllCharges={1}, HCDEnergy={2}, MaxCVSkip={3}, MassThreshold={4}\n",
                ida.UseIDScore, ida.ConsiderAllChargeStates, ida.HCDEnergy, ida.MaxCVSkip, ida.MassThreshold);

            // MS settings
            sb.AppendFormat("MS: CV=[{0}]\n",
                String.Join(",", ida.CVValues));

            // MS1
            var ms1 = MS1;
            sb.AppendFormat("MS1: {0} {1}k, mz=[{2},{3}], AGC={4}, MaxIT={5}ms, uScans={6}, {7}, RF={8}, sCID={9}\n",
                ms1.Analyzer, ms1.OrbitrapResolution / 1000, ms1.FirstMass, ms1.LastMass,
                ms1.AGCTarget, ms1.MaxIT, ms1.Microscans, ms1.DataType, ms1.RFLens, ms1.SourceCID);

            // MS2 entries
            for (int i = 0; i < MS2.Count; i++)
            {
                var m = MS2[i];
                var activation = m.Activation ?? "";
                if (activation.Equals("ETD", StringComparison.OrdinalIgnoreCase))
                    sb.AppendFormat("MS2[{0}]: {1} {2}k, mz=[{3},{4}], AGC={5}, MaxIT={6}ms, uScans={7}, {8}, {9} RT={10}ms\n",
                        i, m.Analyzer, m.OrbitrapResolution / 1000, m.FirstMass, m.LastMass,
                        m.AGCTarget, m.MaxIT, m.Microscans, m.DataType, activation, m.ReactionTime);
                else
                    sb.AppendFormat("MS2[{0}]: {1} {2}k, mz=[{3},{4}], AGC={5}, MaxIT={6}ms, uScans={7}, {8}, {9} CE={10}\n",
                        i, m.Analyzer, m.OrbitrapResolution / 1000, m.FirstMass, m.LastMass,
                        m.AGCTarget, m.MaxIT, m.Microscans, m.DataType, activation, m.CollisionEnergy);
            }

            // MS3 entries
            for (int i = 0; i < MS3.Count; i++)
            {
                var m = MS3[i];
                var activation = m.Activation ?? "";
                if (activation.Equals("ETD", StringComparison.OrdinalIgnoreCase))
                    sb.AppendFormat("MS3[{0}]: {1} {2}k, mz=[{3},{4}], AGC={5}, MaxIT={6}ms, uScans={7}, {8}, {9} RT={10}ms\n",
                        i, m.Analyzer, m.OrbitrapResolution / 1000, m.FirstMass, m.LastMass,
                        m.AGCTarget, m.MaxIT, m.Microscans, m.DataType, activation, m.ReactionTime);
                else
                    sb.AppendFormat("MS3[{0}]: {1} {2}k, mz=[{3},{4}], AGC={5}, MaxIT={6}ms, uScans={7}, {8}, {9} CE={10}\n",
                        i, m.Analyzer, m.OrbitrapResolution / 1000, m.FirstMass, m.LastMass,
                        m.AGCTarget, m.MaxIT, m.Microscans, m.DataType, activation, m.CollisionEnergy);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Serialize <see cref="MethodParameters"/> to an XML file on disk
        /// </summary>
        /// <param name="path">Path to write the result</param>
        public void Save(string path)
        {
            using (StreamWriter output = new StreamWriter(path))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(MethodParameters));
                serializer.Serialize(output, this);
            }
        }

        /// <summary>
        /// Deserialize <see cref="MethodParameters"/> from an XML file on disk
        /// </summary>
        /// <param name="path">Path to read from</param>
        /// <returns></returns>
        public static MethodParameters Load(string path)
        {
            using (StreamReader input = new StreamReader(path))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(MethodParameters));
                var mp = (MethodParameters)serializer.Deserialize(input);
                mp.InitializeIDA();
                return mp;
            }
        }
    }
}
