using System;
using System.IO;
using Flash;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// The only direct coverage of the run-folder feature.
    ///
    /// Both entry points that use it are unreachable from CI — Flash.csproj pins the offline
    /// harness as the StartupObject, and no CI job connects to an instrument — so composition
    /// logic left inline in either Main would ship untested by construction. LogPathResolver
    /// exists to be testable, and this is the test that makes that worth doing.
    /// </summary>
    [TestFixture]
    public class LogPathResolverTests
    {
        // Injected rather than DateTime.Now, so every expectation below is an exact string.
        private static readonly DateTime FixedNow = new DateTime(2026, 8, 9, 14, 33, 2);
        private const string Stamp = "2026-08-09-14-33-02";

        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "lpr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
            catch { /* best effort */ }
        }

        [Test, Category("Tier1")]
        public void Compose_EmptyLogDir_UsesCurrentDirectory()
        {
            string r = LogPathResolver.Compose("", null, FixedNow);
            Assert.AreEqual(Path.Combine(Directory.GetCurrentDirectory(), Stamp), r,
                "an empty log_dir means '.', the process working directory");
        }

        [Test, Category("Tier1")]
        public void Compose_NullLogDir_UsesCurrentDirectory()
        {
            Assert.AreEqual(Path.Combine(Directory.GetCurrentDirectory(), Stamp),
                LogPathResolver.Compose(null, null, FixedNow));
        }

        /// <summary>
        /// The guard against a run folder that is only half a run folder.
        ///
        /// log4net resolves a relative &lt;file value&gt; against AppDomain.BaseDirectory (bin\),
        /// while the engine's paths resolve against the process CWD. A relative result would put
        /// the five engine streams in one directory and FlashLog/IDALog in another — both existing,
        /// nothing failing, and the whole point of the feature lost.
        /// </summary>
        [Test, Category("Tier1")]
        public void Compose_AlwaysReturnsRootedPath()
        {
            foreach (string logDir in new[] { "", null, "logs", "./logs", "logs/nested" })
            {
                string r = LogPathResolver.Compose(logDir, "sample", FixedNow);
                Assert.IsTrue(Path.IsPathRooted(r),
                    "log_dir '" + (logDir ?? "<null>") + "' produced a relative run folder: " + r);
            }
        }

        [Test, Category("Tier1")]
        public void Compose_RawName_PrefixesTheStamp()
        {
            string r = LogPathResolver.Compose(tempRoot, "sample_042", FixedNow);
            Assert.AreEqual(Path.Combine(tempRoot, "sample_042_" + Stamp), r);
        }

        /// <summary>-r is documented as "the name or path to raw file", so it may be a full path.</summary>
        [Test, Category("Tier1")]
        public void Compose_RawName_StripsDirectoryAndExtension()
        {
            string r = LogPathResolver.Compose(tempRoot, @"D:\data\runs\sample_042.raw", FixedNow);
            Assert.AreEqual(Path.Combine(tempRoot, "sample_042_" + Stamp), r);
        }

        [Test, Category("Tier1")]
        public void Compose_RawName_SanitisesIllegalAndPatternCharacters()
        {
            string leaf = Path.GetFileName(LogPathResolver.Compose(tempRoot, "a:b*c?d%e", FixedNow));

            foreach (char c in Path.GetInvalidFileNameChars())
                Assert.IsFalse(leaf.IndexOf(c) >= 0,
                    "run folder leaf still contains the path-illegal character 0x" + ((int)c).ToString("X2"));

            // '%' is legal in a filename but is a conversion specifier to log4net's PatternString,
            // where an unknown one is dropped silently.
            Assert.IsFalse(leaf.Contains("%"), "'%' must not survive into a log4net <file> value");
            StringAssert.EndsWith("_" + Stamp, leaf);
        }

        [Test, Category("Tier1")]
        public void Compose_NoRawName_IsStampOnly()
        {
            Assert.AreEqual(Path.Combine(tempRoot, Stamp),
                LogPathResolver.Compose(tempRoot, "", FixedNow));
        }

        /// <summary>
        /// Two runs must never share a folder. The engine's streams open in append mode and
        /// re-emit their header on every open, so a shared folder puts a header row in the middle
        /// of a TSV — which every reader on both sides parses as a data row.
        /// </summary>
        [Test, Category("Tier1")]
        public void Compose_Collision_DisambiguatesWithCounter()
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, Stamp));
            Assert.AreEqual(Path.Combine(tempRoot, Stamp + "_2"),
                LogPathResolver.Compose(tempRoot, null, FixedNow));

            Directory.CreateDirectory(Path.Combine(tempRoot, Stamp + "_2"));
            Assert.AreEqual(Path.Combine(tempRoot, Stamp + "_3"),
                LogPathResolver.Compose(tempRoot, null, FixedNow));
        }

        /// <summary>
        /// The feature in one assertion: ONE timestamp, not one per file.
        ///
        /// App.config used to carry two independent %date{} PatternStrings, so a run straddling a
        /// second boundary produced FlashLog_…-05 and IDALog_…-06. Putting the stamp in the folder
        /// name makes disagreement structurally impossible — provided it appears exactly once.
        /// </summary>
        [Test, Category("Tier1")]
        public void Compose_StampAppearsExactlyOnce()
        {
            string r = LogPathResolver.Compose(tempRoot, "sample_042", FixedNow);

            int count = 0;
            for (int i = r.IndexOf(Stamp, StringComparison.Ordinal); i >= 0;
                     i = r.IndexOf(Stamp, i + 1, StringComparison.Ordinal))
                count++;

            Assert.AreEqual(1, count, "the run stamp must appear exactly once in " + r);
        }

        /// <summary>A raw name that is only illegal characters must still yield a usable folder.</summary>
        [Test, Category("Tier1")]
        public void Compose_RawNameOfOnlyIllegalCharacters_StillComposes()
        {
            string r = LogPathResolver.Compose(tempRoot, "??", FixedNow);
            Assert.IsTrue(Path.IsPathRooted(r));
            StringAssert.EndsWith("_" + Stamp, Path.GetFileName(r));
        }
    }
}
