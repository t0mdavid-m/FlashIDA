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
    /// Blittable struct matching C++ IsolationStage (80 bytes).
    /// Layout: 5 doubles (40) + 2 int32 (8) + char[32] (32) = 80.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct IsolationStage
    {
        public double PrecursorMz;
        public double IsolationWidth;
        public double CollisionEnergy;
        public double ReactionTime;
        public double ReagentMaxIt;
        public int ReagentAgcTarget;
        public int ChargeState;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ActivationType;
    }

    /// <summary>
    /// Blittable struct matching C++ ScanCommand (1144 bytes).
    /// Layout: 8 int32 (32) + 3 doubles (24) + char[32] + char[256] + IsolationStage[10] (800) = 1144.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
    public struct ScanCommand
    {
        public int ScanId;
        public int MsnLevel;
        public int Priority;
        public int IsAgc;
        public int NumStages;
        public int OrbitrapResolution;
        public int AgcTarget;
        public int Pad1;
        public double FirstMass;
        public double LastMass;
        public double MaxIt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Analyzer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ScanDescription;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public IsolationStage[] Stages;
    }

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
            double rt_min, double precursor_mass, int precursor_charge);

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
            double[] window_starts, double[] window_ends,
            byte[] ion_types, int[] fragment_indices,
            string fragmentation_method);

        [DllImport(dllName)]
        static private extern int GetAmbiguityEnclosingIons(
            IntPtr pTestClassObject,
            string proteinSequence,
            int n,
            double[] masses, double[] qscores, int[] charges,
            double[] window_starts, double[] window_ends,
            byte[] ion_types, int[] fragment_indices,
            string fragmentation_method);

        [DllImport(dllName)]
        static private extern int GetTerminalFragmentIons(
            IntPtr pTestClassObject,
            string proteinSequence,
            int n,
            double[] masses, double[] qscores, int[] charges,
            double[] window_starts, double[] window_ends,
            byte[] ion_types, int[] fragment_indices,
            string fragmentation_method);

        [DllImport(dllName, CharSet = CharSet.Ansi)]
        static private extern int ProcessScan(
            IntPtr pObject, double[] mzs, double[] ints, int length,
            double rt_min, int ms_level, string scan_description);

        [DllImport(dllName)]
        static private extern int GetNextScanCommand(IntPtr pObject, ref ScanCommand output);

        [DllImport(dllName)]
        static private extern int GetNextTrackingId(IntPtr pObject);

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
        /// Construct wrapping object using JSON configuration (Phase 1)
        /// </summary>
        /// <param name="mp">Full method parameters — serialized to JSON for C++ engine</param>
        public FLASHIdaWrapper(MethodParameters mp)
        {
            string arg = mp.IDA.ToJSON(mp);
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
        /// <param name="precursorCharge">Precursor charge state (0 if unknown)</param>
        /// <returns>Number of peak groups found</returns>
        public int DeconvolveMS2(IMsScan msScan, double precursorMass, int precursorCharge)
        {
            double rt = double.Parse(msScan.Header["StartTime"]);
            double[] mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            double[] ints = msScan.Centroids.Select(c => c.Intensity).ToArray();

            try
            {
                return DeconvolveMS2(m_pNativeObject, mzs, ints, mzs.Length, rt, precursorMass, precursorCharge);
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
            public char? IonType { get; set; }  // 'a','b','c','x','y','z' or null for mode 0
            public int? FragmentIndex { get; set; }  // 1-based position (b12 = 12, y5 = 5)

            public double IsolationMz => (WindowStart + WindowEnd) / 2;
            public double IsolationWidth => WindowEnd - WindowStart;

            /// <summary>
            /// Get ion name like "b12" or "c5". Returns null if ion info not available.
            /// </summary>
            public string IonName => IonType.HasValue && FragmentIndex.HasValue
                ? String.Format("{0}{1}", IonType.Value, FragmentIndex.Value)
                : null;
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
        /// <param name="fragmentationMethod">Fragmentation method: "HCD", "ETD", or "UVPD" (null defaults to HCD)</param>
        /// <returns>List of MS3Target objects sorted by qscore (descending)</returns>
        public List<MS3Target> GetTopFragmentMatches(string sequence, int n, string fragmentationMethod = null)
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
                byte[] ionTypes = new byte[requestCount];
                int[] fragmentIndices = new int[requestCount];

                int actualCount = GetTopFragmentMatches(
                    m_pNativeObject, sequence, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds,
                    ionTypes, fragmentIndices,
                    fragmentationMethod);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i],
                        IonType = (char)ionTypes[i],
                        FragmentIndex = fragmentIndices[i]
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
        /// <param name="fragmentationMethod">Fragmentation method: "HCD", "ETD", or "UVPD" (null defaults to HCD)</param>
        /// <returns>List of MS3Target objects sorted by qscore (descending)</returns>
        public List<MS3Target> GetAmbiguityEnclosingIons(string sequence, int n, string fragmentationMethod = null)
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
                byte[] ionTypes = new byte[requestCount];
                int[] fragmentIndices = new int[requestCount];

                int actualCount = GetAmbiguityEnclosingIons(
                    m_pNativeObject, sequence, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds,
                    ionTypes, fragmentIndices,
                    fragmentationMethod);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i],
                        IonType = (char)ionTypes[i],
                        FragmentIndex = fragmentIndices[i]
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
        /// Get terminal fragment ions - innermost prefix-ions (rightmost) and suffix-ions (leftmost).
        /// Results interleaved for complementary coverage.
        /// REQUIRES DeconvolveMS2() to be called first!
        /// </summary>
        /// <param name="sequence">Protein amino acid sequence</param>
        /// <param name="n">Maximum number of ions to return</param>
        /// <param name="fragmentationMethod">Fragmentation method: "HCD", "ETD", or "UVPD" (null defaults to HCD)</param>
        /// <returns>List of MS3Target objects with IonType set</returns>
        public List<MS3Target> GetTerminalFragmentIons(string sequence, int n, string fragmentationMethod = null)
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
                byte[] ionTypes = new byte[requestCount];
                int[] fragmentIndices = new int[requestCount];

                int actualCount = GetTerminalFragmentIons(
                    m_pNativeObject, sequence, requestCount,
                    masses, qscores, charges,
                    windowStarts, windowEnds, ionTypes, fragmentIndices,
                    fragmentationMethod);

                for (int i = 0; i < actualCount; i++)
                {
                    result.Add(new MS3Target
                    {
                        Mass = masses[i],
                        QScore = qscores[i],
                        Charge = charges[i],
                        WindowStart = windowStarts[i],
                        WindowEnd = windowEnds[i],
                        IonType = (char)ionTypes[i],
                        FragmentIndex = fragmentIndices[i]
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
        /// Process an incoming scan for shadow validation (Phase 3 stub).
        /// </summary>
        public int ProcessScan(double[] mzs, double[] ints, double rt, int msLevel, string scanDesc)
        {
            try
            {
                return ProcessScan(m_pNativeObject, mzs, ints, mzs.Length, rt, msLevel, scanDesc ?? "");
            }
            catch (Exception ex)
            {
                log.Error(String.Format("ProcessScan error: {0}\n{1}", ex.Message, ex.StackTrace));
                return -1;
            }
        }

        /// <summary>
        /// Dequeue the next scan command by priority.
        /// </summary>
        public int GetNextScanCommand(ref ScanCommand cmd)
        {
            try
            {
                return GetNextScanCommand(m_pNativeObject, ref cmd);
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetNextScanCommand error: {0}\n{1}", ex.Message, ex.StackTrace));
                return 0;
            }
        }

        /// <summary>
        /// Get the next monotonically increasing tracking ID.
        /// </summary>
        public int GetNextTrackingId()
        {
            try
            {
                return GetNextTrackingId(m_pNativeObject);
            }
            catch (Exception ex)
            {
                log.Error(String.Format("GetNextTrackingId error: {0}\n{1}", ex.Message, ex.StackTrace));
                return -1;
            }
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
        /// Process a single scan: deconvolve, write TSV rows, optionally run MS2 pipeline.
        /// </summary>
        /// <returns>Sum of qScores for targets found in this scan.</returns>
        static double ProcessScan(
            FLASHIdaWrapper w, List<double> mzs, List<double> ints,
            double rt, int msLevel, string scanName,
            StreamWriter wfile,
            double[] ms2Mzs, double[] ms2Ints,
            MethodParameters methodParams)
        {
            if (mzs.Count == 0) return 0.0;

            var targets = w.GetIsolationWindows(mzs.ToArray(), ints.ToArray(), rt, msLevel, scanName);
            double scoreSum = 0.0;

            foreach (var item in targets)
            {
                wfile.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}\t{12}\t{13}\t{14}",
                    rt, item.Window.Start, item.Window.End, item.Score, item.Charge, item.MonoMass, item.ChargeCos, item.ChargeSnr, item.IsoCos,
                    item.Snr, item.ChargeScore, item.PpmError,
                    item.PrecursorIntensity, item.PrecursorPeakGroupIntensity, item.Hcd);
                scoreSum += item.Score;
            }

            if (ms2Mzs != null && targets.Count > 0)
            {
                int ms2PeakGroups = DeconvolveMS2(w.m_pNativeObject, ms2Mzs, ms2Ints, ms2Mzs.Length, rt, targets[0].MonoMass, targets[0].Charge);
                if (ms2PeakGroups > 0)
                {
                    ProcessMS2ForTagBasedTargeting(w.m_pNativeObject, targets[0].MonoMass);

                    // Quant mode: test IsDifferentiallyAbundant on MS2 data
                    if (methodParams.isobaricQuantification)
                    {
                        var quant = methodParams.AcquisitionModes.LabelingBasedQuantification;
                        bool isDiff = IsDifferentiallyAbundant(w.m_pNativeObject,
                            ms2Mzs, ms2Ints, ms2Mzs.Length, rt, 2, scanName,
                            quant.ReporterMZTol, quant.FoldChangeThreshold, quant.OnlyOneCondition);
                        Console.WriteLine("[QUANT] rt={0:F4} precursor={1:F2} isDiff={2}", rt, targets[0].MonoMass, isDiff);
                    }

                    // MS3 mode: test GetBestMS2Masses for fragment targeting
                    var ms3Config = methodParams.AcquisitionModes?.MS3Characterization;
                    if (ms3Config != null && IsActive(ms3Config.Active))
                    {
                        int maxMs3 = ms3Config.MaxMs3PerMs2;
                        var ms3Targets = w.GetBestMS2Masses(maxMs3);
                        Console.WriteLine("[MS3] rt={0:F4} mode={1} ms2PeakGroups={2} ms3Targets={3}",
                            rt, ms3Config.MS3Mode, ms2PeakGroups, ms3Targets.Count);
                        foreach (var t in ms3Targets)
                        {
                            Console.WriteLine("[MS3-TARGET] mass={0:F4} charge={1} qscore={2:F4}",
                                t.Mass, t.Charge, t.QScore);
                        }
                    }
                }
                ClearMS2Deconvolution(w.m_pNativeObject);
            }

            return scoreSum;
        }

        private static bool IsActive(string val) =>
            !String.IsNullOrEmpty(val) && val.Equals("True", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Test harness entry point for offline deconvolution.
        /// </summary>
        /// <remarks>
        /// Usage: Flash.exe input_file output_file method.xml [ms2_spectrum_file]
        /// </remarks>
        /// <param name="args">Command line arguments</param>
        static public void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: input_file output_file method.xml [ms2_spectrum_file]");
                Environment.Exit(1);
            }

            StreamReader file;
            StreamWriter wfile;

            try { file = new StreamReader(args[0]); }
            catch { Console.WriteLine("Cannot open input file: {0}", args[0]); Environment.Exit(1); return; }

            try { wfile = new StreamWriter(args[1]); }
            catch { Console.WriteLine("Cannot open output file: {0}", args[1]); Environment.Exit(1); return; }

            MethodParameters methodParams = null;
            try
            {
                methodParams = MethodParameters.Load(args[2]);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading method file: {0}\n{1}", ex.Message, ex.StackTrace);
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cannot load MS2 file: {0}. Error: {1}", args[3], ex.Message);
                    Environment.Exit(1);
                }
            }

            var w = new FLASHIdaWrapper(methodParams);
            var mzs = new List<double>();
            var ints = new List<double>();
            var rt = 0.0;
            var msLevel = 1;
            var totalScore = 0.0;
            var scanName = "";
            bool started = false;

            wfile.WriteLine("rt\tmz1\tmz2\tqScore\tcharges\tmonoMasses\tccos\tcsnr\tcos\tsnr\tcScore\tppm\tprecursorIntensity\tmassIntensity\thcd");

            string line;
            while ((line = file.ReadLine()) != null)
            {
                var token = line.Split('\t');

                if (line.StartsWith("Spec"))
                {
                    if (started)
                        totalScore += ProcessScan(w, mzs, ints, rt, msLevel, scanName, wfile, ms2Mzs, ms2Ints, methodParams);
                    mzs.Clear();
                    ints.Clear();
                    rt = double.Parse(token[1]) / 60.0;
                    scanName = line;
                    started = true;
                }
                else if (started && token.Length >= 2)
                {
                    mzs.Add(double.Parse(token[0]));
                    ints.Add(double.Parse(token[1]));
                }
            }

            // Process the last scan (previously missed — no subsequent Spec header to trigger it)
            if (started)
                totalScore += ProcessScan(w, mzs, ints, rt, msLevel, scanName, wfile, ms2Mzs, ms2Ints, methodParams);

            Console.WriteLine("Total QScore (i.e., expected number of PrSM identification): {0}", totalScore);

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
