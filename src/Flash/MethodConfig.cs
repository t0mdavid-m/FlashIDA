using System.Collections.Generic;
using System.Xml.Serialization;

namespace Flash
{
    public class GlobalParameters
    {
        public string MethodName;
        public string MethodDescription;
        public double Duration = 90;
    }

    public class PrecursorSelectionParameters
    {
        public double QScoreThreshold = -1;
        public double TQScoreThreshold = 0.9;
        public int MinCharge = 4;
        public int MaxCharge = 50;
        public double MinMass = 500;
        public double MaxMass = 50000;
        public double RTWindow = 180;
        public int HCDEnergy = 29;
        [XmlArray] public double[] Tolerances = new double[] { 10, 10 };
    }

    public class MS2TaggingConfig
    {
        public string Active = "False";
        public bool ConditionalMS2;
        public string FastaFile;
        public string PtmList;
        public int MaxPtmCount = 3;
        public int MinTagLength = 3;
        public int MaxTagLength = 8;
        public double MaxFlankingMassDiff = 50000;
    }

    public class TargetedInclusionConfig
    {
        public bool StrictInclusion;
        public double TieThreshold = 0.1;
        public string InclusionList;
        public MS2TaggingConfig MS2Tagging;
    }

    public class TargetedExclusionConfig { }

    public class DeepModeConfig { }

    public class LabelingQuantConfig
    {
        public string Active = "False";
        public double ReporterMZTol;
        public double FoldChangeThreshold;
        public bool OnlyOneCondition;
    }

    public class MS3CharacterizationConfig
    {
        public string Active = "False";
        public int MS3Mode;
        public int MaxMs3PerMs2 = 4;
        public bool MS3AllCharges;
        public string MS3ProteinSequence;
    }

    public class DeveloperFAIMSConfig
    {
        public int MaxCVSkip;
        public int MassThreshold = 15;
    }

    public class DeveloperPrecursorSelectionConfig
    {
        public bool UseIDScore;
        public bool ConsiderAllChargeStates;
    }

    public class DeveloperConfig
    {
        public DeveloperPrecursorSelectionConfig PrecursorSelection;
        public DeveloperFAIMSConfig FAIMS;
    }

    public class AcquisitionModesConfig
    {
        public string TargetingMode = "None";
        public List<string> TargetLogs;
        public TargetedInclusionConfig TargetedInclusion;
        public TargetedExclusionConfig TargetedExclusion;
        public DeepModeConfig DeepMode;
        public LabelingQuantConfig LabelingBasedQuantification;
        public MS3CharacterizationConfig MS3Characterization;
        public DeveloperConfig Developer;
    }

    public class FAIMSSettings
    {
        [XmlArray] public double[] CVValues;
    }

    public class MSSettingsConfig
    {
        public int MaxMs2CountPerMs1 = 4;
        public FAIMSSettings FAIMS;
        public MS1Parameters MS1;
        public List<MS2Parameters> MS2;
        public List<MS3Parameters> MS3;
    }
}
