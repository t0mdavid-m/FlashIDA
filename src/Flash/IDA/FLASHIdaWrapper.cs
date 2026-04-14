using System;
using System.Collections.Generic;
using System.Linq;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using System.Runtime.InteropServices;
using Thermo.Interfaces.SpectrumFormat_V1;
using Flash.DataObjects;
using System.IO;
using log4net;

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
    /// Blittable struct matching C++ ScanCommand (2048 bytes).
    /// Layout: 1248 (existing) + 8 (microscans+pad3) + 24 (rf_lens+source_cid+source_cid_scaling)
    ///       + 64 (data_type+scan_rate) + 704 (reserved) = 2048.
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
        public ulong EnqueueTimestampMs;

        // Precursor scoring data (populated by C++ buildMS2Command_ for diagnostic output)
        public double Qscore;
        public double MonoMass;
        public double ChargeCos;
        public double ChargeSnr;
        public double IsoCos;
        public double Snr;
        public double ChargeScore;
        public double PpmError;
        public double PrecursorIntensity;
        public double PeakgroupIntensity;
        public int HcdEnergy;
        public int Pad2;
        public double FaimsCv;
        public int Microscans;
        public int Pad3;
        public double RfLens;
        public double SourceCid;
        public double SourceCidScaling;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DataType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ScanRate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 704)]
        public byte[] Reserved;
    }

    /// <summary>
    /// Wrapper for FLASHIda C++ engine
    /// </summary>
    public class FLASHIdaWrapper : IDisposable
    {
        //loggers
        private static ILog log = LogManager.GetLogger("General");
        private static ILog IDAlog = LogManager.GetLogger("IDA");

        //binding for FlashIda engine — exactly 5 bridge functions (Phase 8)
        const string dllName = "OpenMS.dll";
        [DllImport(dllName)]
        static private extern IntPtr CreateFLASHIda(string arg);

        [DllImport(dllName)]
        static private extern void DisposeFLASHIda(IntPtr pTestClassObject);

        [DllImport(dllName, CharSet = CharSet.Ansi)]
        static private extern int ProcessScan(
            IntPtr pObject, double[] mzs, double[] ints, int length,
            double rt_min, int ms_level, string scan_description,
            double faims_cv);

        [DllImport(dllName)]
        static private extern int GetNextScanCommand(IntPtr pObject, ref ScanCommand output);

        [DllImport(dllName)]
        static private extern int GetNextTrackingId(IntPtr pObject);

        private IntPtr m_pNativeObject;

        static FLASHIdaWrapper()
        {
            string sharePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "share", "OpenMS");
            Environment.SetEnvironmentVariable("OPENMS_DATA_PATH", sharePath);
        }

        /// <summary>
        /// Construct wrapping object using JSON configuration
        /// </summary>
        /// <param name="mp">Full method parameters — serialized to JSON for C++ engine</param>
        public FLASHIdaWrapper(MethodParameters mp)
        {
            string arg = mp.ToCppJson();
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
        /// Process an incoming scan: deconvolve (MS1) or resolve tracking (MS2), enqueue commands.
        /// </summary>
        public int ProcessScan(double[] mzs, double[] ints, double rt, int msLevel, string scanDesc, double faimsCv = 0.0)
        {
            try
            {
                return ProcessScan(m_pNativeObject, mzs, ints, mzs.Length, rt, msLevel, scanDesc ?? "", faimsCv);
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
        /// Process a single scan via the unified bridge (ProcessScan + GetNextScanCommand).
        /// Writes the same 15-column TSV format using scoring fields from ScanCommand.
        /// </summary>
        static double ProcessScanUnified(
            FLASHIdaWrapper w, List<double> mzs, List<double> ints,
            double rt, int msLevel, string scanName,
            StreamWriter wfile,
            double[] ms2Mzs, double[] ms2Ints,
            MethodParameters methodParams)
        {
            if (mzs.Count == 0) return 0.0;

            w.ProcessScan(mzs.ToArray(), ints.ToArray(), rt, msLevel, scanName);

            double scoreSum = 0.0;
            var cmd = new ScanCommand();
            while (w.GetNextScanCommand(ref cmd) == 1 && cmd.IsAgc == 0)
            {
                if (cmd.MsnLevel == 2 && cmd.NumStages > 0)
                {
                    double mz1 = cmd.Stages[0].PrecursorMz - cmd.Stages[0].IsolationWidth / 2;
                    double mz2 = cmd.Stages[0].PrecursorMz + cmd.Stages[0].IsolationWidth / 2;
                    wfile.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}\t{12}\t{13}\t{14}",
                        rt, mz1, mz2, cmd.Qscore, cmd.Stages[0].ChargeState,
                        cmd.MonoMass, cmd.ChargeCos, cmd.ChargeSnr, cmd.IsoCos,
                        cmd.Snr, cmd.ChargeScore, cmd.PpmError,
                        cmd.PrecursorIntensity, cmd.PeakgroupIntensity, cmd.HcdEnergy);
                    scoreSum += cmd.Qscore;

                    // MS2 return path: feed MS2 spectrum back through unified bridge
                    if (ms2Mzs != null)
                    {
                        w.ProcessScan(ms2Mzs, ms2Ints, rt, 2, cmd.ScanDescription);
                        var followup = new ScanCommand();
                        while (w.GetNextScanCommand(ref followup) == 1 && followup.IsAgc == 0) { followup = new ScanCommand(); }
                    }
                }
                cmd = new ScanCommand();
            }
            return scoreSum;
        }

        private static bool IsActive(string val) =>
            !String.IsNullOrEmpty(val) && val.Equals("True", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Test harness entry point for offline deconvolution.
        /// </summary>
        /// <remarks>
        /// Usage: Flash.exe input_file output_file method.json [ms2_spectrum_file]
        /// </remarks>
        /// <param name="args">Command line arguments</param>
        static public void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: input_file output_file method.json [ms2_spectrum_file]");
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
                    {
                        totalScore += ProcessScanUnified(w, mzs, ints, rt, msLevel, scanName, wfile, ms2Mzs, ms2Ints, methodParams);
                    }
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
            {
                totalScore += ProcessScanUnified(w, mzs, ints, rt, msLevel, scanName, wfile, ms2Mzs, ms2Ints, methodParams);
            }

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
