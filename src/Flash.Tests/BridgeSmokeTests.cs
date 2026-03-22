using System;
using System.IO;
using System.Runtime.InteropServices;
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
        private static string BuildLegacyConfigString()
        {
            return "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                   "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                   "tqscore_threshold 0.9 target_mode 0 IDScore 0 AllCharges 0 " +
                   "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 " +
                   "min_tag_length 3 max_tag_length 8 max_ptm_count 3 max_flanking_mass_diff 50000 ";
        }
    }
}
