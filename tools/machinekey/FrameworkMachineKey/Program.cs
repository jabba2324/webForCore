// webForCore (#191): .NET Framework 4.8's side of the <machineKey> interop check. For each configuration of
// MachineKeyConfigs, a site with its Web.config is hosted in an application domain of its own
// (ApplicationHost.CreateApplicationHost, with the configuration's virtual path), where Interop.Produce writes .NET
// Framework's outputs (FormsAuthentication tickets, MachineKey.Protect and Encode, Katana cookies), and Interop.Consume
// reads:
//   - the committed .NET Framework outputs (test/webForCore.IIS.Gate.Tests/FrameworkData/MachineKey/framework.json),
//     which IIS.Gate.Tests' MachineKeyInteropTests has webForCore read; an output .NET Framework gives the same every
//     time (Deterministic) must be what is committed;
//   - webForCore's outputs (MachineKeyInteropTests writes them; build-and-test's machinekey-webforcore artifact), when
//     given: what webForCore protected must read on .NET Framework.
// framework.json (to commit when a configuration or case is added) and results.md go to the output directory.
//
// Usage (on Windows, .NET Framework 4.8):
//   FrameworkMachineKey.exe <output directory> [<committed framework.json> [<webForCore outputs directory>]]
// Exits with 1 on a difference that MachineKeyConfigs doesn't list as known.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Hosting;
using System.Web.Script.Serialization;
using MachineKeyInterop;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 3)
        {
            Console.Error.WriteLine("usage: FrameworkMachineKey <output directory> [<committed framework.json> [<webForCore outputs directory>]]");
            return 2;
        }
        string output = Path.GetFullPath(args[0]);
        string committedFile = args.Length > 1 ? args[1] : null;
        string webForCoreDirectory = args.Length > 2 ? args[2] : null;
        Directory.CreateDirectory(output);
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        string systemWeb = typeof(HttpRuntime).Assembly.Location;
        string version = ".NET Framework " + Environment.Version + ", System.Web " + FileVersionInfo.GetVersionInfo(systemWeb).FileVersion;
        Console.WriteLine(version);

        List<InteropEntry> committed = committedFile != null && File.Exists(committedFile)
            ? json.Deserialize<List<InteropEntry>>(File.ReadAllText(committedFile))
            : null;
        var webForCore = new List<InteropEntry>();
        if (webForCoreDirectory != null)
        {
            foreach (string file in Directory.GetFiles(webForCoreDirectory, "*.json"))
            {
                webForCore.AddRange(json.Deserialize<List<InteropEntry>>(File.ReadAllText(file)));
            }
        }

        // The sites: each configuration's Web.config, and this program's assemblies in bin. At the same path every run:
        // the application ID (IsolateByAppId's key) is a hash of the virtual and physical paths.
        string sites = Path.Combine(Path.GetPathRoot(Path.GetTempPath()), "webforcore-machinekey");
        if (Directory.Exists(sites)) Directory.Delete(sites, recursive: true);
        string programDirectory = AppDomain.CurrentDomain.BaseDirectory;

        var produced = new List<InteropEntry>();
        var report = new StringBuilder();
        report.AppendLine("## <machineKey> output: .NET Framework 4.8 and webForCore (#191)");
        report.AppendLine();
        report.AppendLine(version + ". Each output is read by the other runtime; one that is the same on every call (no random IV) must also be byte for byte the same.");
        report.AppendLine();
        report.AppendLine("| Configuration | Kind | Case | Deterministic | Committed .NET Framework output | webForCore's output on .NET Framework |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- |");
        int failures = 0;
        foreach (MachineKeyConfig config in MachineKeyConfigs.All)
        {
            string site = Path.Combine(sites, config.Name);
            Directory.CreateDirectory(Path.Combine(site, "bin"));
            File.WriteAllText(Path.Combine(site, "Web.config"), config.WebConfig());
            foreach (string file in Directory.GetFiles(programDirectory, "*.dll").Concat(Directory.GetFiles(programDirectory, "*.exe")))
            {
                File.Copy(file, Path.Combine(site, "bin", Path.GetFileName(file)));
            }
            var runner = (Runner)ApplicationHost.CreateApplicationHost(typeof(Runner), config.VirtualPath, site);

            List<InteropEntry> mine = json.Deserialize<List<InteropEntry>>(runner.Produce(config.Name));
            produced.AddRange(mine);

            Dictionary<string, string> committedResults = null;
            if (committed != null)
            {
                List<InteropEntry> theirs = committed.Where(e => e.Config == config.Name).ToList();
                committedResults = Results(json.Deserialize<List<InteropResult>>(runner.Consume(json.Serialize(theirs))));
                foreach (InteropEntry entry in theirs.Where(e => e.Deterministic))
                {
                    InteropEntry fresh = mine.FirstOrDefault(e => e.Kind == entry.Kind && e.Case == entry.Case);
                    string key = Key(entry);
                    if (fresh != null && fresh.Value != entry.Value && committedResults[key] == null)
                    {
                        committedResults[key] = "the committed output differs from what .NET Framework writes";
                    }
                }
            }
            Dictionary<string, string> webForCoreResults = null;
            if (webForCoreDirectory != null)
            {
                webForCoreResults = Results(json.Deserialize<List<InteropResult>>(
                    runner.Consume(json.Serialize(webForCore.Where(e => e.Config == config.Name).ToList()))));
            }

            foreach (InteropEntry entry in mine)
            {
                string key = Key(entry);
                string known = MachineKeyConfigs.KnownDifference(config.Name, entry.Kind);
                string committedProblem = committed == null ? null : committedResults.TryGetValue(key, out string c) ? c : "not committed";
                string webForCoreProblem = webForCoreDirectory == null ? null : webForCoreResults.TryGetValue(key, out string w) ? w : "missing from webForCore's outputs";
                bool failed = committedProblem != null || (webForCoreProblem != null && known == null) || (webForCoreProblem == null && known != null && webForCoreDirectory != null);
                if (failed) failures++;
                report.AppendLine("| " + config.Name + " | " + entry.Kind + " | " + entry.Case + " | " + (entry.Deterministic ? "yes" : "no") + " | " +
                    (committed == null ? "-" : committedProblem == null ? "reads" : "**" + Cell(committedProblem) + "**") + " | " +
                    (webForCoreDirectory == null ? "-" : webForCoreProblem == null ? (known == null ? "reads" : "**reads, but listed as a known difference**")
                        : known != null ? "known difference: " + Cell(known) : "**" + Cell(webForCoreProblem) + "**") + " |");
                Console.WriteLine((failed ? "FAILED  " : "ok      ") + key + (committedProblem != null ? "; committed: " + committedProblem : "") +
                    (webForCoreProblem != null ? "; webForCore's: " + webForCoreProblem + (known != null ? " (known)" : "") : ""));
            }
        }
        HostingEnvironment.InitiateShutdown();
        ApplicationManager.GetApplicationManager().ShutdownAll();

        File.WriteAllText(Path.Combine(output, "framework.json"), Indent(json.Serialize(produced)));
        report.AppendLine();
        report.AppendLine(failures == 0 ? "No differences." : failures + " failed.");
        File.WriteAllText(Path.Combine(output, "results.md"), report.ToString());
        try { Directory.Delete(sites, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Console.WriteLine(failures == 0 ? "All outputs ok." : failures + " failed.");
        return failures == 0 ? 0 : 1;
    }

    private static string Key(InteropEntry entry)
    {
        return entry.Config + " " + entry.Kind + " " + entry.Case;
    }

    private static Dictionary<string, string> Results(List<InteropResult> results)
    {
        return results.ToDictionary(r => r.Config + " " + r.Kind + " " + r.Case, r => r.Problem);
    }

    private static string Cell(string text)
    {
        return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    // One entry a line, so that the committed file diffs readably.
    private static string Indent(string array)
    {
        return array.Replace("},{", "},\n {").Replace("[{", "[\n {").Replace("}]", "}\n]") + "\n";
    }

    // Runs in the configuration's application domain (HttpRuntime set up for the site, its Web.config read).
    public sealed class Runner : MarshalByRefObject
    {
        public string Produce(string config)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(Interop.Produce(config, owin: true));
        }

        public string Consume(string entries)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return json.Serialize(Interop.Consume(json.Deserialize<List<InteropEntry>>(entries), owin: true));
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }
    }
}
