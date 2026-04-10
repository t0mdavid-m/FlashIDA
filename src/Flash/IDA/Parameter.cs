using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;

namespace Flash.IDA
{
    /// <summary>
    /// Parameters for FLASHIda
    /// </summary>
    public class IDAParameters
    {        
        [Description("Targeting mode (0=normal, 1=inclusion, 2=deep, 3=exclusion)")]
        public int TargetMode { set; get; }
        [Description("Quality score threshold for precursor selection")]
        public double QScoreThreshold { set; get; }
        [Description("Tie-breaking threshold for precursor ranking")]
        public double TieThreshold { set; get; }
        public double TQScoreThreshold { set; get; }
        public double quantReporterMZTol { set; get; }
        public double quantFoldChangeThreshold { set; get; }
        public bool quantOnlyOneCondition { set; get; }

        [Description("Minimum precursor charge state")]
        public int MinCharge { set; get; }

        [Description("Maximum precursor charge state")]
        public int MaxCharge { set; get; }

        [Description("Minimum precursor mass in Da")]
        public double MinMass { set; get; }

        [Description("Maximum precursor mass in Da")]
        public double MaxMass { set; get; }

        public List<string> TargetLogs { set; get; }

        [XmlArray()]
        public double[] Tolerances { set; get; }

        [XmlArray()]
        public double[] CVValues { set; get; }
        
        public double RTWindow { set; get; }

        public int MaxCVSkip { set; get; }
        public int MassThreshold { set; get; }

        public bool UseIDScore { set; get; }
        public bool ConsiderAllChargeStates { set; get; }
        [Description("HCD collision energy")]
        public int HCDEnergy { set; get; }

        public bool StrictInclusion { set; get; }
        public string InclusionList { set; get; }
        public string PtmList { set; get; }

        public bool MS2Tagging { set; get; }
        public string FastaFile { set; get; }
        public int MaxPtmCount { set; get; }
        public int MinTagLength { set; get; }
        public int MaxTagLength { set; get; }
        public double MaxFlankingMassDiff { set; get; }
        public bool ConditionalMS2 { set; get; }

        // MS3 mode parameters
        public bool EnableMS3 { set; get; }
        public int MS3Mode { set; get; }
        public int MaxMs3PerMs2 { set; get; }
        public string MS3ProteinSequence { set; get; }
        public bool MS3AllCharges { set; get; }

        /// <summary>
        /// Complete constructor
        /// </summary>
        /// <param name="tolerances">Two member array for mass tolerances (down, up)</param>
        /// <param name="qScoreThreshold">Threshold for quality score</param>
        /// <param name="rtWindow">Retention time tolerance window</param>
        /// <param name="minCharge">Minimal precursor charge</param>
        /// <param name="maxCharge">Maximal precursor charge</param>
        /// <param name="minMass">Minimal precursor mass</param>
        /// <param name="maxMass">Maximal precursor mass</param> 
        /// <param name="targetLogs">log files containing target or excluded masses</param> 
        /// <param name="targetMode">If set to 1, inclusive targeted mode if 2, exclusive targeted mode. If 0, normal exclusion list mode</param> 
        /// <param name="cvvalues">contains the cvvalues to be scanned</param>
        public IDAParameters(double[] tolerances = null, double qScoreThreshold = -1, double tieThreshold = 0.1, double rtWindow = 5, int minCharge = 1, int maxCharge = 100,
                             double minMass = 50, double maxMass = 100000, List<string> targetLogs = null, int targetMode = 0, double[] cvvalues = null,
                             int MaxCVSkip_ = 0, int MassThreshold_ = 15, double tqScoreThreshold = 0.9, double quantReporterMZTol_ = 0, double quantFoldChangeThreshold_ = 0, bool quantOnlyOneCondition_ = false,
                             bool UseIDScore_ = false, bool ConsiderAllChargeStates_ = false, int HCDEnergy_ = 29,
                             bool strictInclusion = false, string inclusionList = null, string ptmList = null,
                             bool ms2Tagging = false, string fastaFile = null, int maxPtmCount = 3, int minTagLength = 3, int maxTagLength = 8, double maxFlankingMassDiff = 50000.0,
                             bool conditionalMS2 = false,
                             bool enableMS3 = false, int ms3Mode = 0, int maxMs3PerMs2 = 4, string ms3ProteinSequence = null, bool ms3AllCharges = false)
        {
            Tolerances = tolerances ?? new double[] { 10, 10 };
            CVValues = cvvalues ?? new double[] { 0.0, -40.0, -50.0, -60.0 };
            RTWindow = rtWindow;
            MinCharge = minCharge;
            MaxCharge = maxCharge;
            MinMass = minMass;
            MaxMass = maxMass;
            QScoreThreshold = qScoreThreshold;
            TieThreshold = tieThreshold;
            TQScoreThreshold = tqScoreThreshold;
            TargetLogs = targetLogs;
            TargetMode = targetMode;
            MaxCVSkip = MaxCVSkip_;
            MassThreshold = MassThreshold_;
            quantReporterMZTol = quantReporterMZTol_;
            quantFoldChangeThreshold = quantFoldChangeThreshold_;
            quantOnlyOneCondition = quantOnlyOneCondition_;
            UseIDScore = UseIDScore_;
            ConsiderAllChargeStates = ConsiderAllChargeStates_;
            HCDEnergy = HCDEnergy_;
            StrictInclusion = strictInclusion;
            InclusionList = inclusionList;
            PtmList = ptmList;
            MS2Tagging = ms2Tagging;
            FastaFile = fastaFile;
            MaxPtmCount = maxPtmCount;
            MinTagLength = minTagLength;
            MaxTagLength = maxTagLength;
            MaxFlankingMassDiff = maxFlankingMassDiff;
            ConditionalMS2 = conditionalMS2;
            EnableMS3 = enableMS3;
            MS3Mode = ms3Mode;
            MaxMs3PerMs2 = maxMs3PerMs2;
            MS3ProteinSequence = ms3ProteinSequence;
            MS3AllCharges = ms3AllCharges;
        }

        /// <summary>
        /// Parameterless constructor used only for serialization
        /// </summary>
        public IDAParameters()
        {
            Tolerances = new double[] { 0, 0 };
        }
        
    }
}
