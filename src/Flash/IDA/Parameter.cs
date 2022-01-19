﻿using System;
using System.Xml.Serialization;

namespace Flash.IDA
{
    /// <summary>
    /// Parameters for FLASHIda
    /// </summary>
    public class IDAParameters
    {
        public int MaxMs2CountPerMs1 { set; get; }

        public double QScoreThreshold { set; get; }

        public int MinCharge { set; get; }
        
        public int MaxCharge { set; get; }
        
        public double MinMass { set; get; }

        public double MaxMass { set; get; }

        public string TargetLog { set; get; }

        [XmlArray()]
        public double[] Tolerances { set; get; }
        
        public double RTWindow { set; get; }
        
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
        public IDAParameters(double[] tolerances = null, int maxMs2CountPerMs1 = 5, double qScoreThreshold = -1,
            double rtWindow = 5, int minCharge = 1, int maxCharge = 100, double minMass = 50, double maxMass = 100000, string targetLog = "NONE")
        {
            Tolerances = tolerances ?? new double[] { 10, 10 };
            RTWindow = rtWindow;
            MaxMs2CountPerMs1 = maxMs2CountPerMs1;
            MinCharge = minCharge;
            MaxCharge = maxCharge;
            MinMass = minMass;
            MaxMass = maxMass;
            QScoreThreshold = qScoreThreshold;
            TargetLog = targetLog;
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
            return String.Format("max_mass_count {0} score_threshold {1} min_charge {2} max_charge {3} min_mass {4} max_mass {5} RT_window {6} tol {7} {8}",
                MaxMs2CountPerMs1, QScoreThreshold, MinCharge, MaxCharge, MinMass, MaxMass, RTWindow, String.Join(" ", Tolerances), TargetLog);
        }

    }
}
