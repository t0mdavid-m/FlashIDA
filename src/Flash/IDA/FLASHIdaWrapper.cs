using System;
using System.Collections.Generic;
using System.Linq;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using System.Runtime.InteropServices;
using Thermo.Interfaces.SpectrumFormat_V1;
using Flash.DataObjects;
using System.IO;
using log4net;
using log4net.Core;
using System.Xml.Linq;
using Thermo.TNG.Client.API.MsScanContainer;
using System.Data.Common;
using System.Web.UI;
using System.Security.Cryptography;
using System.Collections;
using System.Threading;

namespace Flash.IDA
{
    /// <summary>
    /// Wrapper for FLASHIda C++ engine
    /// </summary>
    public class FLASHIdaWrapper : IDisposable
    {
        //loggers
        private static ILog log = LogManager.GetLogger("General");
        private static ILog IDAlog = LogManager.GetLogger("IDA");

        //binding for FlashIda engine
        const string dllName = "OpenMS.dll";
        [DllImport(dllName)]
        static private extern IntPtr CreateFLASHIda(string arg);

        [DllImport(dllName)]
        static private extern void DisposeFLASHIda(IntPtr pTestClassObject);

        [DllImport(dllName)]
        static private extern int GetPeakGroupSize(IntPtr pTestClassObjectdouble, double[] mzs, double[] ints, int length, double rt, int msLevel, string name, string cv);

        [DllImport(dllName)]
        static private extern bool IsDifferentiallyAbundant(IntPtr pTestClassObjectdouble, double[] mzs, double[] ints, int length, double rt, int msLevel, string name, double reporter_mz_tol, double fold_change_threshold, bool only_one_condition);

        [DllImport(dllName)]
        static private extern int GetAllPeakGroupSize(IntPtr pTestClassObjectdouble);

        [DllImport(dllName)]
        static private extern double GetRepresentativeMass(IntPtr pTestClassObjectdouble);

        [DllImport(dllName)]
        static private extern void GetAllMonoisotopicMasses(IntPtr pTestClassObjectdouble, double[] monoMasses, int length);


        [DllImport(dllName)]
        static private extern void GetIsolationWindows(IntPtr pTestClassObjectdouble, double[] wstart, double[] wend, 
            double[] qScores, int[] charges, int[] min_charges, int[] max_charges, double[] monoMasses, double[] chargeCos, double[] chargeSnrs,
                           double[] isoCos,
                           double[] snrs, double[] chargeScores,
                           double[] ppmErrors, double[] precursorIntensities, double[] peakgroupIntensities, int[] hcds, int[] ids
            );

        [DllImport(dllName)]
        static private extern void RemoveFromExclusionList(IntPtr pTestClassObjectdouble, int id);

        [DllImport(dllName)]
        static private extern void TestCode(IntPtr pTestClassObjectdouble, int[] arg, int length);

        [DllImport(dllName)]
        static private extern bool ProcessMS2ForTagBasedTargeting(
            IntPtr pTestClassObject,
            double precursor_mass);

        [DllImport(dllName)]
        static private extern int DeconvolveMS2(
            IntPtr pTestClassObject,
            double[] mzs, double[] ints, int length,
            double rt_min, double precursor_mass);

        [DllImport(dllName)]
        static private extern int GetBestMS2Masses(
            IntPtr pTestClassObject,
            int n,
            double[] masses, double[] qscores, int[] charges,
            double[] window_starts, double[] window_ends);

        [DllImport(dllName)]
        static private extern bool HasMS2Deconvolution(IntPtr pTestClassObject);

        [DllImport(dllName)]
        static private extern int GetMS2PeakGroupCount(IntPtr pTestClassObject);

        [DllImport(dllName)]
        static private extern void ClearMS2Deconvolution(IntPtr pTestClassObject);

        [DllImport(dllName)]
        static private extern int GetTopFragmentMatches(
            IntPtr pTestClassObject,
            string proteinSequence,
            int n,
            double[] masses, double[] qscores, int[] charges,
            double[] window_starts, double[] window_ends);

        [DllImport(dllName)]
        static private extern int GetAmbiguityEnclosingIons(
            IntPtr pTestClassObject,
            string proteinSequence,
            int n,
            double[] masses, double[] qscores, int[] charges,
            double[] window_starts, double[] window_ends);

        [DllImport(dllName)]
        static private extern int GetTerminalFragmentIons(
            IntPtr pTestClassObject,
            string proteinSequence,
            int n,
            double[] masses, double[] qscores, int[] charges,
            double[] window_starts, double[] window_ends,
            bool[] is_b_ions);

        private IntPtr m_pNativeObject;

        /// <summary>
        /// Construct wrapping object
        /// </summary>
        /// <param name="param">FLASHIda parameters</param>
        /// <param name="log">Path for additional logging from the C++ side (optional)</param>
        public FLASHIdaWrapper(IDAParameters param)
        {
            string arg = param.ToFLASHDeconvInput();
            Console.WriteLine(arg);
            m_pNativeObject = CreateFLASHIda(arg);
        }

        /// <summary>
        /// Destroy wrapping object
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Default destructor
        /// </summary>
        ~FLASHIdaWrapper()
        {
            Dispose(false);
        }

        /// <summary>
        /// Destroy wrapping object
        /// </summary>
        /// <param name="bDisposing">Do not call the finalizer</param>
        protected virtual void Dispose(bool bDisposing)
        {
            if (m_pNativeObject != IntPtr.Zero)
            {
                // Call the DLL Export to dispose this class
                DisposeFLASHIda(m_pNativeObject);
                m_pNativeObject = IntPtr.Zero;
            }

            if (bDisposing)
            {
                // No need to call the finalizer since we've now cleaned
                // up the unmanaged memory
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Obtain the the list of targets for fragmentation from the current spectrum
        /// The spectral data has to be converted to array format (see below)
        /// </summary>
        /// <remarks>
        /// Internal function <see cref="GetIsolationWindows(IMsScan)"/> provides higher level interface
        /// </remarks>
        /// <param name="mzs">Array of m/z values</param>
        /// <param name="ints">Array of intensity values</param>
        /// <param name="rt">Retention time</param>
        /// <param name="msLevel">MS level as integer, i.e. MS1 - 1, MS2 - 2, etc</param>
        /// <param name="name">Identifier of the spectrum</param>
        /// <returns></returns>
        protected List<PrecursorTarget> GetIsolationWindows(double[] mzs, double[] ints, double rt, int msLevel, string name, string cv = null)
        {
            int size = 0;
            try
            {
                size = GetPeakGroupSize(m_pNativeObject, mzs, ints, mzs.Length, rt, msLevel, name, cv);                
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.GetPeakGroupSize reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }

            double[] wstart = new double[size];
            double[] wend = new double[size];
            double[] tqScores = new double[size];
            int[] tCharges = new int[size];
            int[] tMinCharges = new int[size];
            int[] tMaxCharges = new int[size];
            double[] tmonoMasses = new double[size];
            double[] tchargeCos = new double[size];
            double[] tchargeSnrs = new double[size];
            double[] tisoCos = new double[size];
            double[] tsnrs = new double[size];
            double[] tchargeScores = new double[size];
            double[] tppmErrors = new double[size];
            double[] tprecursorIntensities = new double[size];
            double[] tpeakgroupIntensities = new double[size];
            int[] hcds = new int[size];
            int[] ids = new int[size];

            try
            {
                GetIsolationWindows(m_pNativeObject, wstart, wend, tqScores, tCharges, tMinCharges, tMaxCharges, tmonoMasses, tchargeCos,
                    tchargeSnrs, tisoCos, tsnrs, tchargeScores, tppmErrors,
                    tprecursorIntensities, tpeakgroupIntensities, hcds, ids);
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.GetIsolationWindows reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }

            List<PrecursorTarget> result = new List<PrecursorTarget>(size); //convert raw output into a list of PrecursorTarget objects

            for (int i = 0; i < size; i++)
            {
                result.Add(new PrecursorTarget(wstart[i], wend[i], tCharges[i], tMinCharges[i], tMaxCharges[i], tmonoMasses[i], tqScores[i], tprecursorIntensities[i],
                    tpeakgroupIntensities[i], tchargeCos[i], tchargeSnrs[i], tisoCos[i], tsnrs[i], tchargeScores[i], tppmErrors[i], hcds[i], ids[i]));  
            }

            return result;
        }

        protected bool IsDifferentiallyAbundant(double[] mzs, double[] ints, double rt, int msLevel, string name, double reporter_mz_tol = 0, double fold_change_threshold = 0, bool only_one_condition = false)
        {
            try
            {
                return IsDifferentiallyAbundant(m_pNativeObject, mzs, ints, mzs.Length, rt, msLevel, name, reporter_mz_tol, fold_change_threshold, only_one_condition);
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.IsDifferentiallyAbundant reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }
            return false;
        }

        public List<double> GetAllMonoisotopicMasses()
        {
            try
            {
                int size = GetAllPeakGroupSize(m_pNativeObject);
                double[] masses= new double[size];
                GetAllMonoisotopicMasses(m_pNativeObject, masses, size);
                return masses.ToList();
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.GetAllMonoisotopicMasses reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }
            return null;
        }

        /// <summary>
        /// Obtain the the number of of targets for fragmentation from the current spectrum.
        /// </summary>
        /// <returns></returns>
        public int GetAllPeakGroupSize()
        {
            try
            {
                return GetAllPeakGroupSize(m_pNativeObject);
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.GetAllPeakGroupSize reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }
            return -1;
        }

        /// <summary>
        /// Get the representative mass of the spectrum. 
        /// </summary>
        /// <returns></returns>
        public double GetRepresentativeMass()
        {
            try
            {
                return GetRepresentativeMass(m_pNativeObject);
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.GetRepresentativeMass reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }
            return 0;
        }


        /// <summary>
        /// Remove a precursor with a certain id from the exclusion list
        /// </summary>
        /// <returns></returns>
        public void RemoveFromExclusionList(int id)
        {
            try
            {
                RemoveFromExclusionList(m_pNativeObject, id);
            }
            catch (Exception idaException)
            {
                log.Error(String.Format("IDAWrapper.RemoveFromExclusionList reported: {0}\n{1}", idaException.Message, idaException.StackTrace));
            }
        }


        /// <summary>
        /// Obtain the the list of targets for fragmentation from the current spectrum.
        /// </summary>
        /// <param name="msScan">Mass spectrum object</param>
        /// <returns></returns>
        public List<PrecursorTarget> GetIsolationWindows(IMsScan msScan, String cv = null)
        {
            int msLevel = int.Parse(msScan.Header["MSOrder"]);
            double rt = double.Parse(msScan.Header["StartTime"]);
            string name = msScan.Header["Scan"];

            double[] mzs;
            double[] ints;

            //always send centroided scans
            mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            ints = msScan.Centroids.Select(c => c.Intensity).ToArray();
            
            return GetIsolationWindows(mzs, ints, rt, msLevel, name, cv);
        }

        public bool IsDifferentiallyAbundant(IMsScan msScan, double reporter_mz_tol = 0, double fold_change_threshold = 0, bool only_one_condition = false)
        {
            int msLevel = int.Parse(msScan.Header["MSOrder"]);
            double rt = double.Parse(msScan.Header["StartTime"]);
            string name = msScan.Header["Scan"];

            double[] mzs;
            double[] ints;

            //always send centroided scans
            mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            ints = msScan.Centroids.Select(c => c.Intensity).ToArray();

            return IsDifferentiallyAbundant(mzs, ints, rt, msLevel, name, reporter_mz_tol, fold_change_threshold, only_one_condition);
        }

        /// <summary>
        /// Process MS2 for tag-based targeting.
        /// REQUIRES DeconvolveMS2() to be called first!
        /// </summary>
        /// <param name="msScan">MS2 scan object (used to extract precursor mass)</param>
        /// <returns>True if protein family detected</returns>
        public bool ProcessMS2ForTagBasedTargeting(IMsScan msScan)
        {
            // Get precursor m/z and charge, then calculate actual mass
            double precursorMz = double.Parse(msScan.Header["PrecursorMass[0]"]);
            msScan.Trailer.TryGetValue("Charge State", out var chargeString);
            int charge = int.Parse(chargeString);
            double precursorMass = precursorMz * charge;

            try
            {
                return ProcessMS2ForTagBasedTargeting(m_pNativeObject, precursorMass);
            }
            catch (Exception ex)
            {
                log.Error(String.Format("ProcessMS2ForTagBasedTargeting error: {0}\n{1}",
                    ex.Message, ex.StackTrace));
                return false;
            }
        }

        /// <summary>
        /// Perform MS2 deconvolution on a scan object.
        /// MUST be called before ProcessMS2ForTagBasedTargeting.
        /// </summary>
        /// <param name="msScan">MS2 scan object</param>
        /// <param name="precursorMass">Monoisotopic precursor mass from MS1 (0.0 if unknown)</param>
        /// <returns>Number of peak groups found</returns>
        public int DeconvolveMS2(IMsScan msScan, double precursorMass)
        {
            double rt = double.Parse(msScan.Header["StartTime"]);
            double[] mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            double[] ints = msScan.Centroids.Select(c => c.Intensity).ToArray();

            try
            {
                return DeconvolveMS2(m_pNativeObject, mzs, ints, mzs.Length, rt, precursorMass);
            }
            catch (Exception ex)
            {
                log.Error(String.Format("DeconvolveMS2 error: {0}\n{1}", ex.Message, ex.StackTrace));
                return 0;
            }
        }

        /// <summary>
        /// Get count of MS2 peak groups from most recent deconvolution.
        /// </summary>
        public int GetMS2PeakGroupCount()
        {
            try
            {
                return GetMS2PeakGroupCount(m_pNativeObject);
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetMS2PeakGroupCount error: {0}", ex.Message));
                return 0;
            }
        }

        /// <summary>
        /// Check if MS2 deconvolution state exists.
        /// </summary>
        public bool HasMS2Deconvolution()
        {
            try
            {
                return HasMS2Deconvolution(m_pNativeObject);
            }
            catch (Exception ex)
            {
                log.Warn(String.Format("HasMS2Deconvolution check failed: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// Clear MS2 deconvolution state. Call after processing is complete.
        /// </summary>
        public void ClearMS2DeconvolutionState()
        {
            try
            {
                ClearMS2Deconvolution(m_pNativeObject);
            }
            catch (Exception ex)
            {
                log.Warn(String.Format("ClearMS2Deconvolution warning: {0}", ex.Message));
            }
        }

        /// <summary>
        /// Target information for MS3 scans from MS2 deconvolution
        /// </summary>
        public class MS3Target
        {
            public double Mass { get; set; }
            public double QScore { get; set; }
            public int Charge { get; set; }
            public double WindowStart { get; set; }
            public double WindowEnd { get; set; }
            public bool? IsBIon { get; set; }  // null for modes 0-2, true/false for mode 3

            public double IsolationMz => (WindowStart + WindowEnd) / 2;
            public double IsolationWidth => WindowEnd - WindowStart;
        }

        /// <summary>
        /// Get the best deconvolved masses from MS2 spectrum for MS3 targeting.
        /// REQUIRES DeconvolveMS2() to be called first!
        /// </summary>
        /// <param name="n">Maximum number of masses to return</param>
        /// <returns>List of MS3Target objects sorted by qscore (descending)</returns>
        public List<MS3Target> GetBestMS2Masses(int n)
        {
            var result = new List<MS3Target>();

            try
            {
                int peakGroupCount = GetMS2PeakGroupCount();
                if (peakGroupCount == 0)
                    return result;

                int requestCount = Math.Min(n, peakGroupCount);

                double[] masses = new double[requestCount];
                double[] qscores = new double[requestCount];
                int[] charges = new int[requestCount];
                double[] windowStarts = new double[requestCount];
                double[] windowEnds = new double[requestCount];

                int actualCount = GetBestMS2Masses(
                    m_pNativeObject, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i]
                    });
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetBestMS2Masses error: {0}\n{1}", ex.Message, ex.StackTrace));
            }

            return result;
        }

        /// <summary>
        /// Get the best deconvolved masses matching protein sequence fragments.
        /// REQUIRES DeconvolveMS2() to be called first!
        /// </summary>
        /// <param name="sequence">Protein amino acid sequence</param>
        /// <param name="n">Maximum number of matches to return</param>
        /// <returns>List of MS3Target objects sorted by qscore (descending)</returns>
        public List<MS3Target> GetTopFragmentMatches(string sequence, int n)
        {
            var result = new List<MS3Target>();

            try
            {
                int peakGroupCount = GetMS2PeakGroupCount();
                if (peakGroupCount == 0 || string.IsNullOrEmpty(sequence))
                    return result;

                int requestCount = Math.Min(n, peakGroupCount);

                double[] masses = new double[requestCount];
                double[] qscores = new double[requestCount];
                int[] charges = new int[requestCount];
                double[] windowStarts = new double[requestCount];
                double[] windowEnds = new double[requestCount];

                int actualCount = GetTopFragmentMatches(
                    m_pNativeObject, sequence, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i]
                    });
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetTopFragmentMatches error: {0}\n{1}", ex.Message, ex.StackTrace));
            }

            return result;
        }

        /// <summary>
        /// Get fragment ions that enclose PTM ambiguity regions.
        /// REQUIRES DeconvolveMS2() to be called first!
        /// </summary>
        /// <param name="sequence">Protein amino acid sequence</param>
        /// <param name="n">Maximum number of ions to return</param>
        /// <returns>List of MS3Target objects sorted by qscore (descending)</returns>
        public List<MS3Target> GetAmbiguityEnclosingIons(string sequence, int n)
        {
            var result = new List<MS3Target>();

            try
            {
                int peakGroupCount = GetMS2PeakGroupCount();
                if (peakGroupCount == 0 || string.IsNullOrEmpty(sequence))
                    return result;

                int requestCount = Math.Min(n, peakGroupCount);

                double[] masses = new double[requestCount];
                double[] qscores = new double[requestCount];
                int[] charges = new int[requestCount];
                double[] windowStarts = new double[requestCount];
                double[] windowEnds = new double[requestCount];

                int actualCount = GetAmbiguityEnclosingIons(
                    m_pNativeObject, sequence, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i]
                    });
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetAmbiguityEnclosingIons error: {0}\n{1}", ex.Message, ex.StackTrace));
            }

            return result;
        }

        /// <summary>
        /// Get terminal fragment ions - innermost b-ions (rightmost) and y-ions (leftmost).
        /// Results interleaved: [b, y, b, y, ...] for complementary coverage.
        /// REQUIRES DeconvolveMS2() to be called first!
        /// </summary>
        /// <param name="sequence">Protein amino acid sequence</param>
        /// <param name="n">Maximum number of ions to return</param>
        /// <returns>List of MS3Target objects with IsBIon set</returns>
        public List<MS3Target> GetTerminalFragmentIons(string sequence, int n)
        {
            var result = new List<MS3Target>();

            try
            {
                int peakGroupCount = GetMS2PeakGroupCount();
                if (peakGroupCount == 0 || string.IsNullOrEmpty(sequence))
                    return result;

                int requestCount = Math.Min(n, peakGroupCount);

                double[] masses = new double[requestCount];
                double[] qscores = new double[requestCount];
                int[] charges = new int[requestCount];
                double[] windowStarts = new double[requestCount];
                double[] windowEnds = new double[requestCount];
                bool[] isBIons = new bool[requestCount];

                int actualCount = GetTerminalFragmentIons(
                    m_pNativeObject, sequence, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds, isBIons);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i],
                        IsBIon = isBIons[i]
                    });
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetTerminalFragmentIons error: {0}\n{1}", ex.Message, ex.StackTrace));
            }

            return result;
        }

        /// <summary>
        /// Calculate value G(<paramref name="x"/>) of Gaussian function (G) with height = <paramref name="intensity"/>, center = <paramref name="x0"/>,
        /// and standard deviation = <paramref name="sigma"/>
        /// </summary>
        /// <param name="x">Argument of Gaussian function</param>
        /// <param name="x0">Center of Gaussian function</param>
        /// <param name="intensity">Height of Gaussian function</param>
        /// <param name="sigma">Standard deviation of Gaussian function</param>
        /// <returns></returns>
        private static double Gauss(double x, double x0, double intensity, double sigma)
        {
            return intensity * Math.Exp(-1 * Math.Pow(x - x0, 2)/(2 * Math.Pow(sigma,2)));
        }

        /// <summary>
        /// Return index of the first element in the <paramref name="sequence"/> larger or equal than
        /// <paramref name="value"/>
        /// </summary>
        /// <remarks>
        /// returned index is equal to the size of the sequence if all sequence elements are smaller than the value
        /// </remarks>
        /// <param name="sequence">Sequence of numbers</param>
        /// <param name="value">Value to search for</param>
        /// <returns></returns>
        private static int FirstIndexAfter(IList<double> sequence, double value)
        {
            for (int index = sequence.Count - 1; index >= 0; index--)
            {
                if(sequence[index] < value) return index + 1;
            }

            return 0;
        }

        /// <summary>
        /// Converts centroid representation of the spectrum to profile one, using Gaussian shapes for the peaks
        /// The resulting profile spectrum is written to an array of <see cref="MassIntensityPair"/> objects.
        /// </summary>
        /// <param name="centroids">Array of mass centroids</param>
        /// <param name="peakPoints">Number of points to use for each Gaussian shape</param>
        /// <param name="points">Returning array</param>
        /// <returns>Boolean indicator weither the conversion was successful</returns>
        public static bool ToProfile(IEnumerable<ICentroid> centroids, int peakPoints, out MassIntensityPair[] points)
        {
            //create lists with largest possible capacity from the start (performance optimization - no need for resizing)
            List<double> fullGrid = new List<double>(centroids.Count() * peakPoints);
            List<double> fullIntensity = new List<double>(fullGrid.Capacity);
            bool success = true; //indicate if errors happened

            foreach (ICentroid centroid in centroids)
            {
                if (centroid.Resolution == null)
                {
                    log.Warn(String.Format("Centroid {0} has no resolution - ignoring", centroid.Mz));
                    success = false;
                }
                else
                {
                    double mz = centroid.Mz;
                    double intensity = centroid.Intensity;
                    double resolution = centroid.Resolution.Value;

                    double width = 2 * mz / resolution; //2 FWHM on each side ~ 5 sigma of gauss
                    double sigma = width / (4 * Math.Sqrt(2 * Math.Log(2))); // FWHM = sqrt(2 * ln(2)) * sigma_gauss

                    int start = FirstIndexAfter(fullGrid, mz - width); //start index
                    int end = start + peakPoints; //stop index

                    for (int gridIndex = start; gridIndex < end; gridIndex++) //filling data
                    {
                        if (gridIndex < fullGrid.Count) //reuse grid point
                        {
                            fullIntensity[gridIndex] += Gauss(fullGrid[gridIndex], mz, intensity, sigma);
                        }
                        else //create new point
                        {
                            //1.0 is forcing devision result to double, peakPoints - 1 is the number of intervals
                            //end - 1 is due to zero based indices
                            fullGrid.Add(mz + width * (1 - (2 * (end - gridIndex - 1) / (peakPoints - 1.0))));
                            fullIntensity.Add(Gauss(fullGrid[gridIndex], mz, intensity, sigma));
                        }
                    }
                }
            }

            points = new MassIntensityPair[fullGrid.Count];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new MassIntensityPair(fullGrid[i], fullIntensity[i]);
            }

            return success;

        }

        /// <summary>
        /// Load a single spectrum from a text file
        /// </summary>
        /// <param name="filePath">Path to spectrum file</param>
        /// <returns>Tuple of (m/z array, intensity array, retention time in minutes)</returns>
        private static (double[] mzs, double[] ints, double rt) LoadSpectrum(string filePath)
        {
            var mzs = new List<double>();
            var ints = new List<double>();
            double rt = 0;
            bool started = false;

            foreach (var line in File.ReadAllLines(filePath))
            {
                var token = line.Split('\t');
                if (line.StartsWith("Spec"))
                {
                    rt = double.Parse(token[1]) / 60.0; // Convert seconds to minutes
                    started = true;
                }
                else if (started && token.Length >= 2)
                {
                    mzs.Add(double.Parse(token[0]));
                    ints.Add(double.Parse(token[1]));
                }
            }

            return (mzs.ToArray(), ints.ToArray(), rt);
        }

        /// <summary>
        /// Extra execution entry point
        /// </summary>
        /// <remarks>
        /// Used internally for testing
        /// </remarks>
        /// <param name="args">Command line arguments</param>
        static public void Main(string[] args)
        {
            Console.WriteLine("Start");

            string line;

            StreamReader file;
            StreamWriter wfile;

            //parse command args
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: input_file output_file method.xml [ms2_spectrum_file]");
                Environment.Exit(1);
            }

            try
            {
                file = new StreamReader(args[0]);
            }
            catch
            {
                Console.WriteLine("Cannot open input file: {0}", args[0]);
                Environment.Exit(1);
                return;
            }

            try
            {
                wfile = new StreamWriter(args[1]);
            }
            catch
            {
                Console.WriteLine("Cannot open output file: {0}", args[1]);
                Environment.Exit(1);
                return;
            }
            Console.WriteLine('a');

            //create Wrapper
            var tolerances = new double[] { 10, 10 };
            IDAParameters param = new IDAParameters();
            Console.WriteLine('b');

            try
            {
                MethodParameters methodParams = MethodParameters.Load(args[2]);
                param = methodParams.IDA;
                //Console.WriteLine(methodParams.IDA.TargetLog);
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Format("Error loading method file: {0}\n{1}", ex.Message, ex.StackTrace));
                Environment.Exit(1);
            }

            // Optional MS2 spectrum file for tag-based targeting
            double[] ms2Mzs = null;
            double[] ms2Ints = null;
            if (args.Length > 3 && !String.IsNullOrEmpty(args[3]))
            {
                try
                {
                    var (loadedMzs, loadedInts, loadedRt) = LoadSpectrum(args[3]);
                    ms2Mzs = loadedMzs;
                    ms2Ints = loadedInts;
                    Console.WriteLine("Loaded MS2 spectrum: {0} peaks", ms2Mzs.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(String.Format("Cannot load MS2 file: {0}. Error: {1}", args[3], ex.Message));
                    Environment.Exit(1);
                }
            }

            FLASHIdaWrapper w;
            Console.WriteLine('1');
            w = new FLASHIdaWrapper(param);
            Console.WriteLine('2');
            // Read the file and display it line by line.  
            var mzs = new List<double>();
            var ints = new List<double>();
            var rt = .0;
            var msLevel = 1;
            var totalScore = .0;
            bool start = false;
            wfile.WriteLine("rt\tmz1\tmz2\tqScore\tcharges\tmonoMasses\tccos\tcsnr\tcos\tsnr\tcScore\tppm\tprecursorIntensity\tmassIntensity\thcd");

            Console.WriteLine("aa");
            while ((line = file.ReadLine()) != null)
            {
                var token = line.Split('\t');


                if (line.StartsWith(@"Spec") || (start && line.StartsWith(@"Running FLASHDeconv ... ")))
                {
                    start = true;
                    if (mzs.Count > 0)
                    {
                        var l = w.GetIsolationWindows(mzs.ToArray(), ints.ToArray(), rt, msLevel, line);
                        //var l = w.IsDifferentiallyAbundant(mzs.ToArray(), ints.ToArray(), rt, 2, line, 0.002, 1.5, true);
                        Console.WriteLine(l);
                        

                        List<double> monoMasses = w.GetAllMonoisotopicMasses();

                        Console.WriteLine(rt);
                        if (l.Count > 0) Console.WriteLine(String.Join<PrecursorTarget>("\n", l.ToArray()));

                        if (monoMasses.Count > 0)
                        {
                            Console.WriteLine(String.Format("AllMass={0}", String.Join<double>(" ", monoMasses.ToArray()))); ;
                        }

                        mzs.Clear();
                        ints.Clear();

                        foreach (var item in l)
                        {
                            wfile.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}\t{12}\t{13}\t{14}",
                                rt, item.Window.Start, item.Window.End, item.Score, item.Charge, item.MonoMass, item.ChargeCos, item.ChargeSnr, item.IsoCos,
                                item.Snr, item.ChargeScore, item.PpmError,
                                item.PrecursorIntensity, item.PrecursorPeakGroupIntensity, item.Hcd);
                            //   Console.WriteLine(item);
                            totalScore += item.Score;
                        }

                        // Send MS2 spectrum for tag-based targeting if MS1 had targets
                        Console.WriteLine(ms2Mzs != null);
                        Console.WriteLine(l.Count > 0);
                        if (ms2Mzs != null && l.Count > 0)
                        {
                            Console.WriteLine("aaaa");
                            // Use first target's mass as simulated precursor
                            double simulatedPrecursorMass = l[0].MonoMass;

                            try
                            {
                                // Explicit MS2 deconvolution workflow
                                int ms2PeakGroups = DeconvolveMS2(w.m_pNativeObject, ms2Mzs, ms2Ints, ms2Mzs.Length, rt, 14009.91);
                                Console.WriteLine("MS2 Deconvolution: {0} peak groups found", ms2PeakGroups);

                                if (ms2PeakGroups > 0)
                                {
                                    // New simplified signature - only needs precursor mass
                                    bool detected = ProcessMS2ForTagBasedTargeting(w.m_pNativeObject, simulatedPrecursorMass);

                                    if (detected)
                                    {
                                        Console.WriteLine("RT {0:f02} - Protein family detected (precursor {1:f02} Da), inclusion list expanded", rt, simulatedPrecursorMass);
                                    }

                                    // Test GetBestMS2Masses for MS3 targeting
                                    int maxMs3 = 100;  // Request top 4 masses
                                    double[] masses = new double[maxMs3];
                                    double[] qscores = new double[maxMs3];
                                    int[] charges = new int[maxMs3];
                                    double[] windowStarts = new double[maxMs3];
                                    double[] windowEnds = new double[maxMs3];

                                    int ms3Count = GetBestMS2Masses(w.m_pNativeObject, maxMs3,
                                        masses, qscores, charges, windowStarts, windowEnds);

                                    Console.WriteLine("\nMS3 Targets from GetBestMS2Masses ({0} found):", ms3Count);
                                    for (int i = 0; i < ms3Count; i++)
                                    {
                                        double isoMz = (windowStarts[i] + windowEnds[i]) / 2;
                                        double isoWidth = windowEnds[i] - windowStarts[i];
                                        //Console.WriteLine("  [{0}] Mass={1:f02} Da, QScore={2:f04}, Charge={3}+, IsoMz={4:f04}, IsoWidth={5:f02}",
                                        //    i + 1, masses[i], qscores[i], charges[i], isoMz, isoWidth);
                                        Console.WriteLine(masses[i]);
                                    }

                                    // Test GetTopFragmentMatches for MS3 mode 1 (fragment matching)
                                    string testSequence = "VTAMDVVYALKRQGRTLYGFGG";
                                    testSequence = "SGRGKQGGKARAKAKTRSSRAGLQFPVGRVHRLLRKGNYSERVGAGAPVYLAAVLEYLTAEILELAGNAARDNKKTRIIPRHLQLAIRNDEELNKLLGKVTIAQGGVLPNIQAVLLPKKTESHHKAKGK";
                                    double[] fragMasses = new double[maxMs3];
                                    double[] fragQscores = new double[maxMs3];
                                    int[] fragCharges = new int[maxMs3];
                                    double[] fragWindowStarts = new double[maxMs3];
                                    double[] fragWindowEnds = new double[maxMs3];

                                    int fragCount = GetTopFragmentMatches(w.m_pNativeObject, testSequence, maxMs3,
                                        fragMasses, fragQscores, fragCharges, fragWindowStarts, fragWindowEnds);

                                    Console.WriteLine("\nMS3 Targets from GetTopFragmentMatches ({0} found, seq length {1}):",
                                        fragCount, testSequence.Length);
                                    for (int i = 0; i < fragCount; i++)
                                    {
                                        double fragIsoMz = (fragWindowStarts[i] + fragWindowEnds[i]) / 2;
                                        double fragIsoWidth = fragWindowEnds[i] - fragWindowStarts[i];
                                        Console.WriteLine("  [{0}] Mass={1:f02} Da, QScore={2:f04}, Charge={3}+, IsoMz={4:f04}, IsoWidth={5:f02}",
                                            i + 1, fragMasses[i], fragQscores[i], fragCharges[i], fragIsoMz, fragIsoWidth);
                                    }

                                    // Test GetAmbiguityEnclosingIons for MS3 mode 2 (PTM ambiguity)
                                    double[] ambigMasses = new double[maxMs3];
                                    double[] ambigQscores = new double[maxMs3];
                                    int[] ambigCharges = new int[maxMs3];
                                    double[] ambigWindowStarts = new double[maxMs3];
                                    double[] ambigWindowEnds = new double[maxMs3];

                                    int ambigCount = GetAmbiguityEnclosingIons(w.m_pNativeObject, testSequence, maxMs3,
                                        ambigMasses, ambigQscores, ambigCharges, ambigWindowStarts, ambigWindowEnds);

                                    Console.WriteLine("\nMS3 Targets from GetAmbiguityEnclosingIons ({0} found):", ambigCount);
                                    for (int i = 0; i < ambigCount; i++)
                                    {
                                        double ambigIsoMz = (ambigWindowStarts[i] + ambigWindowEnds[i]) / 2;
                                        double ambigIsoWidth = ambigWindowEnds[i] - ambigWindowStarts[i];
                                        Console.WriteLine("  [{0}] Mass={1:f02} Da, QScore={2:f04}, Charge={3}+, IsoMz={4:f04}, IsoWidth={5:f02}",
                                            i + 1, ambigMasses[i], ambigQscores[i], ambigCharges[i], ambigIsoMz, ambigIsoWidth);
                                    }

                                    // Test GetTerminalFragmentIons for MS3 mode 3 (terminal b/y-ions)
                                    double[] termMasses = new double[maxMs3];
                                    double[] termQscores = new double[maxMs3];
                                    int[] termCharges = new int[maxMs3];
                                    double[] termWindowStarts = new double[maxMs3];
                                    double[] termWindowEnds = new double[maxMs3];
                                    bool[] termIsBIons = new bool[maxMs3];

                                    int termCount = GetTerminalFragmentIons(w.m_pNativeObject, testSequence, maxMs3,
                                        termMasses, termQscores, termCharges, termWindowStarts, termWindowEnds, termIsBIons);

                                    Console.WriteLine("\nMS3 Targets from GetTerminalFragmentIons ({0} found):", termCount);
                                    for (int i = 0; i < termCount; i++)
                                    {
                                        double termIsoMz = (termWindowStarts[i] + termWindowEnds[i]) / 2;
                                        double termIsoWidth = termWindowEnds[i] - termWindowStarts[i];
                                        string ionType = termIsBIons[i] ? "b" : "y";
                                        Console.WriteLine("  [{0}] {1}-ion: Mass={2:f02} Da, QScore={3:f04}, Charge={4}+, IsoMz={5:f04}, IsoWidth={6:f02}",
                                            i + 1, ionType, termMasses[i], termQscores[i], termCharges[i], termIsoMz, termIsoWidth);
                                    }
                                }

                                // Clear MS2 deconvolution state
                                ClearMS2Deconvolution(w.m_pNativeObject);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("MS2 tag processing failed: {0}", ex.Message);
                            }
                        }
                    }

                    rt = double.Parse(token[1]) / 60.0;
                    //rt = double.Parse(token[1]);

                    if (start && line.StartsWith(@"Running FLASHDeconv ... "))
                    {
                        break;
                    }
                }

                else if (start)
                {
                    mzs.Add(double.Parse(token[0]));
                    ints.Add(double.Parse(token[1]));
                }
            }
            Console.WriteLine(@"Total QScore (i.e., expected number of PrSM identification): {0}", totalScore);

            wfile.Close();
            file.Close();
            w.Dispose();
        }

    }

    class CustomComparer : IComparer<double>
    {
        private readonly Dictionary<double, int> dictionary;

        public CustomComparer(Dictionary<double, int> dict)
        {
            dictionary = dict;
        }

        public int Compare(double x, double y)
        {
            int valueX = dictionary[x];
            int valueY = dictionary[y];

            return valueY.CompareTo(valueX);
        }
    }
}
