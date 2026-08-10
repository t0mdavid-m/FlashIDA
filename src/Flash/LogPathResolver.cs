using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Flash
{
    /// <summary>
    /// Composes the per-run log folder that receives ALL of FLASHIda's output — the five engine
    /// streams written by C++ IdaLogger plus the two log4net files — and copies the authored
    /// method file in beside them, so the folder also records the config that produced it.
    ///
    /// This exists as a separate, pure, injectable-clock unit for one reason: neither entry point
    /// can be executed by CI. Flash.csproj pins the offline harness as the StartupObject, and
    /// XmlConfigurator.Configure has exactly one call site inside the instrument Main, which no CI
    /// job reaches. Logic left inline in either Main would ship untested by construction; here it
    /// is covered by LogPathResolverTests.
    ///
    /// It is also the ONLY place that resolves a log path. MethodParameters.ToCppJson is a pure
    /// passthrough on purpose — it is the body of GenerateReferenceConfigJson, so a clock- or
    /// CWD-derived value reaching it would make config_schema_reference.json differ on every run.
    /// </summary>
    public static class LogPathResolver
    {
        /// <summary>Shared by the folder name and, historically, by the log4net appenders.</summary>
        public const string StampFormat = "yyyy-MM-dd-HH-mm-ss";

        /// <summary>
        /// Absolute path of this run's log folder.
        ///
        /// <paramref name="logDir"/> is the authored runtime.log_dir: empty means "." — the process
        /// working directory, which is the behaviour Installation.md has always documented.
        /// <paramref name="rawName"/> is the -r/--rawname value (a raw file NAME or PATH); null or
        /// empty yields a folder named by the timestamp alone.
        /// <paramref name="now"/> is a parameter rather than DateTime.Now so a test can assert the
        /// exact folder string, and so the stamp is minted once per process rather than once per
        /// caller — two independent evaluations is precisely the App.config bug this replaces.
        ///
        /// The result is always rooted and never an existing directory.
        /// </summary>
        public static string Compose(string logDir, string rawName, DateTime now)
        {
            // GetFullPath/GetCurrentDirectory rather than leaving anything relative: log4net
            // resolves a relative <file value> against AppDomain.BaseDirectory (bin\), NOT the
            // process CWD. A relative folder would therefore split the run in half -- five TSVs
            // under CWD and two logs under bin\ -- with both folders existing and nothing failing.
            string baseDir = string.IsNullOrEmpty(logDir)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(logDir);

            string stamp = now.ToString(StampFormat, CultureInfo.InvariantCulture);

            // -r is documented as "the name or path to raw file", so it may arrive as a full path;
            // strip it to a bare name exactly as the deleted CheckLogPath did.
            string leaf = string.IsNullOrEmpty(rawName)
                ? stamp
                : Sanitize(Path.GetFileNameWithoutExtension(rawName)) + "_" + stamp;

            // Never merge two runs into one folder. The engine's streams open in append mode and
            // re-emit their header, so a shared folder produces a header row in the middle of a
            // TSV -- which every reader on both sides parses as a data row. Collision needs two
            // runs with the same -r value in the same second, so this is a guard, not a hot path.
            string candidate = Path.Combine(baseDir, leaf);
            for (int n = 2; Directory.Exists(candidate); n++)
            {
                candidate = Path.Combine(baseDir, leaf + "_" + n.ToString(CultureInfo.InvariantCulture));
            }
            return candidate;
        }

        /// <summary>Name the copy always takes, whatever the source file was called.</summary>
        public const string MethodFileName = "method.json";

        /// <summary>
        /// Copy the authored method file verbatim into this run's folder, so the folder records the
        /// exact input that produced it. The copy is the file as authored, not the emitted bridge
        /// config, so it can be handed straight back to Flash.exe unchanged.
        ///
        /// Non-fatal by contract, unlike both of its neighbours at the call sites: an unloadable
        /// method file and an uncreatable run folder each invalidate the run, but by the time this
        /// runs the config has already parsed and the folder already exists. A failure here (source
        /// locked, deleted between load and copy, AV or permissions) says nothing about the validity
        /// of the run, and losing instrument time over a provenance artifact is the worse trade.
        /// Returns false with a message; the caller reports it however it can -- log4net on the
        /// instrument path, Console in the offline harness.
        /// </summary>
        public static bool TryCopyMethodFile(string sourcePath, string runFolder, out string error)
        {
            error = null;
            try
            {
                // overwrite:false -- Compose never returns an existing directory, so a destination
                // that already exists is a surprise worth reporting rather than silently clobbering.
                File.Copy(sourcePath, Path.Combine(runFolder, MethodFileName), false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Replace characters that cannot appear in a path segment.</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                // '%' is not path-illegal, but the log4net <file> node it is injected into is
                // declared as a PatternString, where '%' starts a conversion specifier and an
                // unknown one is dropped SILENTLY. The type attribute is removed in App.config so
                // this is belt-and-braces; keeping it here means a future re-add cannot corrupt a
                // path built from an operator-supplied raw file name.
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == '%' ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
