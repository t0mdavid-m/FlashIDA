using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Flash;
using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Bridge smoke tests: verify that CreateFLASHIda and DisposeFLASHIda
    /// work correctly with JSON config and reject legacy format.
    /// </summary>
    [TestFixture]
    public class BridgeSmokeTests
    {
        private const string DllName = "OpenMS.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateFLASHIda(string config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DisposeFLASHIda(IntPtr ptr);

        // P0-I01: CreateFLASHIda() returns a non-null pointer and does not crash.
        [Test]
        [Category("Tier2")]
        public void P0_I01_CreateFLASHIda_DoesNotCrash()
        {
            string jsonConfig = BuildJsonConfigString();

            IntPtr ptr = IntPtr.Zero;
            Assert.DoesNotThrow(() =>
            {
                ptr = CreateFLASHIda(jsonConfig);
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
            string jsonConfig = BuildJsonConfigString();
            IntPtr ptr = CreateFLASHIda(jsonConfig);

            Assert.AreNotEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda returned null; cannot test Dispose.");

            Assert.DoesNotThrow(() =>
            {
                DisposeFLASHIda(ptr);
            }, "DisposeFLASHIda threw an exception.");
        }

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

        // P1-I02: Legacy config is rejected (Phase 8 — parseLegacy_ removed)
        [Test]
        [Category("Tier2")]
        public void P1_I02_CreateFLASHIda_LegacyConfig_IsRejected()
        {
            string legacyConfig = "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                   "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                   "tqscore_threshold 0.9 target_mode 0 AllCharges 0 " +
                   "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 ";

            // C++ CreateFLASHIda catches std::invalid_argument and returns nullptr
            IntPtr ptr = CreateFLASHIda(legacyConfig);
            Assert.AreEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda should return null for legacy config after Phase 8.");
        }

        // P1-I03: CreateFLASHIda with JSON from method_json_roundtrip.json (non-default values)
        [Test]
        [Category("Tier2")]
        public void P1_I03_CreateFLASHIda_RoundtripJson_DoesNotCrash()
        {
            string configsDir = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "test-data", "configs");
            string roundtripPath = Path.Combine(configsDir, "method_json_roundtrip.json");

            Assert.IsTrue(File.Exists(roundtripPath),
                "method_json_roundtrip.json not found at " + roundtripPath);

            var mp = MethodParameters.Load(roundtripPath);
            string jsonConfig = mp.ToCppJson();
            Assert.IsTrue(jsonConfig.StartsWith("{"), "JSON config must start with '{'");

            IntPtr ptr = IntPtr.Zero;
            Assert.DoesNotThrow(() =>
            {
                ptr = CreateFLASHIda(jsonConfig);
            }, "CreateFLASHIda threw with roundtrip JSON.");

            Assert.AreNotEqual(IntPtr.Zero, ptr,
                "CreateFLASHIda returned null for roundtrip JSON. JSON was: " + jsonConfig);

            if (ptr != IntPtr.Zero)
                DisposeFLASHIda(ptr);
        }

        /// <summary>
        /// Build a JSON config string matching method_default.json values.
        /// </summary>
        private static string BuildJsonConfigString()
        {
            var mp = new MethodParameters();
            mp.Config = new MethodConfig
            {
                Deconvolution = new DeconvolutionConfig
                {
                    ScoreThreshold = 0, TQScoreThreshold = 0.9,
                    MinCharge = 4, MaxCharge = 50,
                    MinMass = 500, MaxMass = 50000,
                    Tolerances = new double[] { 10, 10, 10 }
                },
                PrecursorSelection = new PrecursorSelectionConfig
                {
                    RTWindow = 180, HCDEnergy = 29
                },
                Faims = new FaimsConfig { CVValues = new double[] { -50 } },
                MsSettings = new MsSettingsConfig
                {
                    MS1 = new MS1Parameters { Analyzer = "Orbitrap", FirstMass = 500, LastMass = 2000, OrbitrapResolution = 120000, AGCTarget = 800000, MaxIT = 246 },
                    MS2 = new List<MS2Parameters>
                    {
                        // ETD requires its activation-coupled reaction time (ADR-0009); without it
                        // Config::validate() rejects the config and CreateFLASHIda returns null.
                        new MS2Parameters { Analyzer = "Orbitrap", Activation = "ETD", OrbitrapResolution = 120000, CollisionEnergy = 0, ReactionTime = 10.0 }
                    }
                },
                SelectionStrategy = new SelectionStrategyConfig
                {
                    MS1 = new MS1SelectionConfig { Selection = "qscore", MaxTargets = 1 },
                    MS2 = new MS2SelectionConfig { Selection = "none" },
                    MS3 = new MS3SelectionConfig { Selection = "none" }
                }
            };
            return mp.ToCppJson();
        }
    }
}
