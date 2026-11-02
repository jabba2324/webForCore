#nullable enable
using System.Diagnostics;

namespace WebForCoreGate
{
    // Starts a sample as applications run (#56): under webforcore-host, from this repository's build of it
    // (src/WebFormsForCore.Host, the tests' configuration), with the sample as its worker. The host's bindings are
    // "Kestrel" endpoints in its environment (Kestrel__Endpoints__<name>__Url, ...), as a deployment would give them in
    // appsettings.json. Linked into the gate suites.
    //
    // Each run gets a temp folder of its own (TMPDIR, TEMP, TMP), deleted when the run ends: compilation starts from an
    // empty Temporary ASP.NET Files every time, as on a site's first start after a deployment, rather than from whatever
    // an earlier run left (assemblies compiled in the run once behaved differently from cached ones: App_Code loaded twice).
    static class WebForCoreHost
    {
        public static ProcessStartInfo StartInfo(string repository, string sampleDirectory, string sample, IDictionary<string, string> settings,
            out string tempDirectory)
        {
            var output = new DirectoryInfo(AppContext.BaseDirectory); // test/<suite>/bin/<configuration>/<framework>/
            var host = Path.Combine(repository, "src", "WebFormsForCore.Host", "bin", output.Parent!.Name, output.Name, "webforcore-host.dll");
            if (!File.Exists(host)) throw new InvalidOperationException($"webforcore-host isn't built: {host}");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = sampleDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(host);
            start.ArgumentList.Add("--");
            start.ArgumentList.Add($"bin_dotnet/{sample}.dll");
            foreach (var setting in settings) start.Environment[setting.Key] = setting.Value;
            tempDirectory = Directory.CreateTempSubdirectory("webforcore-gate-").FullName;
            foreach (var variable in new[] { "TMPDIR", "TEMP", "TMP" }) start.Environment[variable] = tempDirectory;
            return start;
        }

        // After the host has exited.
        public static void DeleteTempDirectory(string tempDirectory)
        {
            try { Directory.Delete(tempDirectory, recursive: true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        // The repository's root, above the tests' output folder.
        public static string Repository(string marker)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, marker))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("repo root not found");
        }
    }
}
