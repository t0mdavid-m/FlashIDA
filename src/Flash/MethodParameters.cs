using System;
using System.IO;
using System.Collections.Generic;
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

            // From MSSettings
            IDA.MaxMs2CountPerMs1 = MSSettings.MaxMs2CountPerMs1;

            // From PrecursorSelection
            IDA.QScoreThreshold = PrecursorSelection.QScoreThreshold;
            IDA.TQScoreThreshold = PrecursorSelection.TQScoreThreshold;
            IDA.MinCharge = PrecursorSelection.MinCharge;
            IDA.MaxCharge = PrecursorSelection.MaxCharge;
            IDA.MinMass = PrecursorSelection.MinMass;
            IDA.MaxMass = PrecursorSelection.MaxMass;
            IDA.RTWindow = PrecursorSelection.RTWindow;
            IDA.HCDEnergy = PrecursorSelection.HCDEnergy;
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
