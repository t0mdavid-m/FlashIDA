using System;
using System.IO;
using System.Runtime.InteropServices;
using Flash;
using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 0 bridge smoke tests: verify that CreateFLASHIda and DisposeFLASHIda
    /// do not crash. No assertion about return values beyond non-null.
    /// </summary>
    [TestFixture]
    public class BridgeSmokeTests
    {
        // The DLL name matches FLASHIdaWrapper.cs line 31.
        private const string DllName = "OpenMS.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateFLASHIda(string config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DisposeFLASHIda(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int GetConfigInt(IntPtr pObject, string key);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern double GetConfigDouble(IntPtr pObject, string key);

        // P0-I01: CreateFLASHIda() returns a non-null pointer and does not crash.
        [Test]
        [Category("Tier2")]
        public void P0_I01_CreateFLASHIda_DoesNotCrash()
        {
            string legacyConfig = BuildLegacyConfigString();

            IntPtr ptr = IntPtr.Zero;
            Assert.DoesNotThrow(() =>
            {
                ptr = CreateFLASHIda(legacyConfig);
            }, "CreateFLASHIda threw an exception.");

            Assert.AreNotEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda returned a null pointer.");

            if (ptr != IntPtr.Zero)
                DisposeFLASHIda(ptr);
        }

        // P0-I02: DisposeFLASHIda() completes without exception after CreateFLASHIda().
        [Test]
        [Category("Tier2")]
        public void P0_I02_DisposeFLASHIda_DoesNotCrash()
        {
            string legacyConfig = BuildLegacyConfigString();
            IntPtr ptr = CreateFLASHIda(legacyConfig);

            Assume.That(ptr, Is.Not.EqualTo(IntPtr.Zero),
                "Skipping dispose test: CreateFLASHIda returned null.");

            Assert.DoesNotThrow(() =>
            {
                DisposeFLASHIda(ptr);
            }, "DisposeFLASHIda threw an exception.");
        }

        /// <summary>
        /// Builds the legacy space-delimited config string that CreateFLASHIda
        /// expects. This replicates the output of Parameter.ToFLASHDeconvInput()
        /// with method_default.xml values (TargetLogs cleared).
        ///
        /// Values derived from method_default.xml via MethodParameters.InitializeIDA()
        /// → IDAParameters.ToFLASHDeconvInput():
        ///   MaxMs2CountPerMs1=1, QScoreThreshold=0, MinCharge=4, MaxCharge=50,
        ///   MinMass=500, MaxMass=50000, RTWindow=180, Tolerances=[10,10],
        ///   TQScoreThreshold=0.9, TargetMode=0, UseIDScore=false,
        ///   ConsiderAllChargeStates=false, HCDEnergy=29, StrictInclusion=false,
        ///   TieThreshold=0.1, MS3AllCharges=true,
        ///   MinTagLength=3, MaxTagLength=8, MaxPtmCount=3, MaxFlankingMassDiff=50000
        /// </summary>
        // P1-I01: CreateFLASHIda(jsonString) returns non-null handle
        [Test]
        [Category("Tier2")]
        public void P1_I01_CreateFLASHIda_JsonConfig_DoesNotCrash()
        {
            string jsonConfig = BuildJsonConfigString();
            Assert.IsTrue(jsonConfig.StartsWith("{"), "JSON config must start with '{'");

            IntPtr ptr = IntPtr.Zero;
            Assert.DoesNotThrow(() =>
            {
                ptr = CreateFLASHIda(jsonConfig);
            }, "CreateFLASHIda threw an exception with JSON input.");

            Assert.AreNotEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda returned null for JSON config. JSON was: " + jsonConfig);

            if (ptr != IntPtr.Zero)
                DisposeFLASHIda(ptr);
        }

        // P1-I02: CreateFLASHIda(legacyString) still works (auto-detect fallback)
        [Test]
        [Category("Tier2")]
        public void P1_I02_CreateFLASHIda_LegacyConfig_StillWorks()
        {
            string legacyConfig = BuildLegacyConfigString();

            IntPtr ptr = IntPtr.Zero;
            Assert.DoesNotThrow(() =>
            {
                ptr = CreateFLASHIda(legacyConfig);
            }, "CreateFLASHIda threw an exception with legacy input.");

            Assert.AreNotEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda returned null for legacy config.");

            if (ptr != IntPtr.Zero)
                DisposeFLASHIda(ptr);
        }

        // P1-I03: CreateFLASHIda with JSON from method_json_roundtrip.xml (non-default values)
        [Test]
        [Category("Tier2")]
        public void P1_I03_CreateFLASHIda_RoundtripJson_DoesNotCrash()
        {
            string configsDir = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "test-data", "configs");
            string roundtripPath = Path.Combine(configsDir, "method_json_roundtrip.xml");

            if (!File.Exists(roundtripPath))
            {
                Assert.Ignore("method_json_roundtrip.xml not yet created");
                return;
            }

            var mp = MethodParameters.Load(roundtripPath);
            string jsonConfig = mp.IDA.ToJSON(mp);
            Assert.IsTrue(jsonConfig.StartsWith("{"), "JSON config must start with '{'");

            IntPtr ptr = IntPtr.Zero;
            Assert.DoesNotThrow(() =>
            {
                ptr = CreateFLASHIda(jsonConfig);
            }, "CreateFLASHIda threw with roundtrip JSON.");

            Assert.AreNotEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda returned null for roundtrip JSON. JSON was: " + jsonConfig);

            if (ptr != IntPtr.Zero)
            {
                try
                {
                    int targetingMode = GetConfigInt(ptr, "targeting_mode");
                    double rtWindow = GetConfigDouble(ptr, "rt_window");
                    int hcdEnergy = GetConfigInt(ptr, "hcd_energy");

                    Assert.AreEqual(mp.IDA.TargetMode, targetingMode, "targeting_mode mismatch");
                    Assert.AreEqual(mp.IDA.RTWindow, rtWindow, 0.001, "RT_window mismatch");
                    Assert.AreEqual(mp.IDA.HCDEnergy, hcdEnergy, "HCDEnergy mismatch");
                }
                catch (EntryPointNotFoundException)
                {
                    Assert.Ignore("GetConfigInt/GetConfigDouble not exported — config assertions skipped");
                }
                finally
                {
                    DisposeFLASHIda(ptr);
                }
            }
        }

        private static string BuildLegacyConfigString()
        {
            return "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                   "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                   "tqscore_threshold 0.9 target_mode 0 IDScore 0 AllCharges 0 " +
                   "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 " +
                   "min_tag_length 3 max_tag_length 8 max_ptm_count 3 max_flanking_mass_diff 50000 ";
        }

        /// <summary>
        /// Build a JSON config string matching method_default.xml values.
        /// Used for P1-I01 to test JSON path without needing to load XML.
        /// </summary>
        private static string BuildJsonConfigString()
        {
            var mp = new MethodParameters();
            mp.PrecursorSelection = new PrecursorSelectionParameters
            {
                QScoreThreshold = 0,
                TQScoreThreshold = 0.9,
                MinCharge = 4,
                MaxCharge = 50,
                MinMass = 500,
                MaxMass = 50000,
                RTWindow = 180,
                HCDEnergy = 29,
                Tolerances = new double[] { 10, 10 }
            };
            mp.MSSettings = new MSSettingsConfig
            {
                MaxMs2CountPerMs1 = 1,
                FAIMS = new FAIMSSettings { CVValues = new double[] { -50 } },
                MS1 = new MS1Parameters { Analyzer = "Orbitrap", FirstMass = 500, LastMass = 2000, OrbitrapResolution = 120000, AGCTarget = 800000, MaxIT = 246 },
                MS2 = new System.Collections.Generic.List<MS2Parameters>
                {
                    new MS2Parameters { Analyzer = "Orbitrap", Activation = "ETD", OrbitrapResolution = 120000, CollisionEnergy = 0 }
                }
            };
            mp.SelectionStrategy = new SelectionStrategyConfig
            {
                MS1 = new MS1SelectionConfig { Selection = "qscore", MaxPrecursors = 1 },
                MS2 = new MS2SelectionConfig { Selection = "intensity" },
                MS3 = new MS3SelectionConfig { Selection = "none" }
            };
            mp.InitializeIDA();
            return mp.IDA.ToJSON(mp);
        }
    }
}
