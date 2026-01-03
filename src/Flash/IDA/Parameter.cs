using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Flash.IDA
{
    /// <summary>
    /// Parameters for FLASHIda
    /// </summary>
    public class IDAParameters
    {        
        public int MaxMs2CountPerMs1 { set; get; }

        public int TargetMode { set; get; } 
        public double QScoreThreshold { set; get; }
        public double TQScoreThreshold { set; get; }
        public double quantReporterMZTol { set; get; }
        public double quantFoldChangeThreshold { set; get; }
        public bool quantOnlyOneCondition { set; get; }

        public int MinCharge { set; get; }
        
        public int MaxCharge { set; get; }
        
        public double MinMass { set; get; }

        public double MaxMass { set; get; }

        public List<string> TargetLogs { set; get; }

        [XmlArray()]
        public double[] Tolerances { set; get; }

        [XmlArray()]
        public double[] CVValues { set; get; }
        
        public double RTWindow { set; get; }

        public double CycleTime { set; get; }

        public bool UseCVQScore { set; get; }
        public int MaxCVSkip { set; get; }
        public int MassThreshold { set; get; }

        public bool UseIDScore { set; get; }
        public bool ConsiderAllChargeStates { set; get; }
        public int HCDEnergy { set; get; }

        public bool StrictInclusion { set; get; }
        public string InclusionList { set; get; }
        public string PtmList { set; get; }

        /// <summary>
        /// Complete constructor
        /// </summary>
        /// <param name="tolerances">Two member array for mass tolerances (down, up)</param>
        /// <param name="maxMs2CountPerMs1"></param>
        /// <param name="qScoreThreshold">Threshold for quality score</param>
        /// <param name="rtWindow">Retention time tolerance window</param>
        /// <param name="minCharge">Minimal precursor charge</param>
        /// <param name="maxCharge">Maximal precursor charge</param>
        /// <param name="minMass">Minimal precursor mass</param>
        /// <param name="maxMass">Maximal precursor mass</param> 
        /// <param name="targetLogs">log files containing target or excluded masses</param> 
        /// <param name="targetMode">If set to 1, inclusive targeted mode if 2, exclusive targeted mode. If 0, normal exclusion list mode</param> 
        /// <param name="cvvalues">contains the cvvalues to be scanned</param>
        public IDAParameters(double[] tolerances = null, int maxMs2CountPerMs1 = 5, double qScoreThreshold = -1, double rtWindow = 5, int minCharge = 1, int maxCharge = 100,
                             double minMass = 50, double maxMass = 100000, List<string> targetLogs = null, int targetMode = 0, double[] cvvalues = null, double cycletime = 180, bool usecvqscore = true,
                             int MaxCVSkip_ = 0, int MassThreshold_ = 15, double tqScoreThreshold = 0.9, double quantReporterMZTol_ = 0, double quantFoldChangeThreshold_ = 0, bool quantOnlyOneCondition_ = false,
                             bool UseIDScore_ = false, bool ConsiderAllChargeStates_ = false, int HCDEnergy_ = 29,
                             bool strictInclusion = false, string inclusionList = null, string ptmList = null)
        {
            Tolerances = tolerances ?? new double[] { 10, 10 };
            CVValues = cvvalues ?? new double[] { 0.0, -40.0, -50.0, -60.0 };
            RTWindow = rtWindow;
            MaxMs2CountPerMs1 = maxMs2CountPerMs1;
            MinCharge = minCharge;
            MaxCharge = maxCharge;
            MinMass = minMass;
            MaxMass = maxMass;
            QScoreThreshold = qScoreThreshold;
            TQScoreThreshold = tqScoreThreshold;
            TargetLogs = targetLogs;
            TargetMode = targetMode;
            CycleTime = cycletime;
            UseCVQScore = usecvqscore;
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
        }

        /// <summary>
        /// Parameterless constructor used only for serialization
        /// </summary>
        public IDAParameters()
        {
            Tolerances = new double[] { 0, 0 };
        }
        
        /// <summary>
        /// Convert <see cref="IDAParameters"/> instnace to string representation to transfer to C++ engine
        /// </summary>
        /// <returns></returns>
        public string ToFLASHDeconvInput()
        {
            var ret = String.Format("max_mass_count {0} score_threshold {1} min_charge {2} max_charge {3} min_mass {4} max_mass {5} RT_window {6} tol {7} tqscore_threshold {8} target_mode {9} IDScore {10} AllCharges {11} HCDEnergy {12} strict_inclusion {13} ",
                MaxMs2CountPerMs1, QScoreThreshold, MinCharge, MaxCharge, MinMass, MaxMass, RTWindow, String.Join(" ", Tolerances), TQScoreThreshold, TargetMode, UseIDScore ? 1 : 0, ConsiderAllChargeStates ? 1 : 0, HCDEnergy, StrictInclusion ? 1 : 0);

            foreach(var f in TargetLogs)
            {
                ret += f + " ";
            }

            // PTM list must come before inclusion list (file extension detection order)
            if (!String.IsNullOrEmpty(PtmList))
            {
                ret += PtmList + " ";
            }

            if (!String.IsNullOrEmpty(InclusionList))
            {
                ret += InclusionList + " ";
            }

            return ret;
        }

    }
}
