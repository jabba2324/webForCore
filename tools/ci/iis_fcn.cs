// <httpRuntime fcnMode> on IIS 10 (#167): which changes restart the application, compared with what
// test/webForCore.Host.Tests' FileChangeNotificationTests.AChangeInBinDotnetRestartsTheApplicationAndStartingDoesNot asserts of
// webForCore (bin_dotnet in place of bin). Both read the cases from tools/ci/iis-fcn-cases.json. CI's iis-compare job:
//
//   dotnet run tools/ci/iis_fcn.cs -- site <folder> <fcnMode>
//     writes a site: Web.config with the fcnMode, Default.aspx (the application's domain id), Global.asax (writes each
//     domain's shutdown reason to App_Data\end.txt), App_Code, a folder named "binaries", and bin with files in bin\cs,
//     bin\de and bin\runtimes\win-x64\native, as an application's satellite and native assemblies are.
//   dotnet run tools/ci/iis_fcn.cs -- check <folder> <URL> <fcnMode> <report.md> [fresh]
//     requests the site, waits, and checks whether it restarted; then makes each change, in order, and checks whether
//     the application restarted (its domain id changed) within 20 s: a new file in bin\cs, bin\cs's LastWriteTime set,
//     and a new file in binaries. Appends a Markdown report. The exit code is 1 if a case's webForCore decision isn't
//     IIS 10's, or if IIS decided otherwise than it does in every run. Where IIS varies from run to run (the case's
//     "varies"; #189: .github/workflows/iis-fcn-stability.yml measures it), a decision other than its usual one is
//     recorded, not a failure.
//
// The job writes the compared sites before IIS serves them: tools/ci/iis-site.ps1 sets their permissions, which opens
// every folder. A site written after (fresh: the start only, for the record) is what a deployment copies: Windows then
// reports each of bin's folders as modified the first time the application opens them (it writes their deferred
// LastWriteTime), during the first request, and in Single mode IIS 10 restarts the application for that (in every run
// since #189 measured it; in #167's investigation, in some runs and not in others). webForCore doesn't (#167).
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length >= 3 && args[0] == "site")
{
    var dir = args[1];
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "Web.config"), $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <system.web>
            <compilation debug="false" targetFramework="4.8" />
            <httpRuntime targetFramework="4.8" fcnMode="{args[2]}" />
          </system.web>
        </configuration>
        """);
    File.WriteAllText(Path.Combine(dir, "Default.aspx"), "<%@ Page Language=\"C#\" %>domain=<%= System.Web.HttpRuntime.AppDomainId %>;");
    File.WriteAllText(Path.Combine(dir, "Global.asax"), """
        <%@ Application Language="C#" %>
        <script runat="server">
        void Application_End() {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.Web.HttpRuntime.AppDomainAppPath, "App_Data", "end.txt"),
                System.DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + System.Web.HttpRuntime.AppDomainId + " "
                + System.Web.Hosting.HostingEnvironment.ShutdownReason + "\r\n");
        }
        </script>
        """);
    foreach (var folder in new[] { "App_Data", "App_Code", "binaries" }) Directory.CreateDirectory(Path.Combine(dir, folder));
    foreach (var folder in new[] { @"bin\cs", @"bin\de", @"bin\runtimes\win-x64\native" })
    {
        Directory.CreateDirectory(Path.Combine(dir, folder));
        File.WriteAllText(Path.Combine(dir, folder, "file.txt"), "file");
    }
    return 0;
}

if (args.Length < 5 || args[0] != "check" || (args.Length > 5 && args[5] != "fresh"))
{
    Console.Error.WriteLine("usage: iis_fcn.cs site <folder> <fcnMode> | check <folder> <URL> <fcnMode> <report.md> [fresh]");
    return 2;
}

var site = args[1];
var url = args[2].TrimEnd('/') + "/Default.aspx";
var mode = args[3];
var fresh = args.Length > 5;
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var report = new List<string> { $"### IIS 10, fcnMode=\"{mode}\"{(fresh ? ", a site copied while IIS serves its folder (for the record)" : "")}", "", "| change | IIS 10 | webForCore |", "| --- | --- | --- |" };
var failed = false;

// The cases (tools/ci/iis-fcn-cases.json, which Host.Tests reads too): webForCore's decision, and IIS 10's in every run
// or, when it varies, in most.
var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourcePath())!, "iis-fcn-cases.json")))
    .RootElement.GetProperty("cases").EnumerateArray()
    .Where(c => c.GetProperty("fcnMode").GetString() == mode)
    .ToDictionary(c => c.GetProperty("change").GetString()!, c => (
        WebForCore: c.GetProperty("webForCore").GetString() == "restart",
        Iis: c.GetProperty("iis").GetString() == "restart",
        Varies: c.TryGetProperty("varies", out var varies) ? varies.GetString() : null));

async Task<string> Domain()
{
    for (int attempt = 0; ; attempt++)
    {
        try
        {
            var body = await client.GetStringAsync(url);
            var match = Regex.Match(body, "domain=([^;]*);");
            if (match.Success) return match.Groups[1].Value;
            throw new InvalidOperationException(body);
        }
        catch (Exception) when (attempt < 30) { await Task.Delay(1000); }
    }
}

string Text(bool restart) => restart ? "restart" : "no restart";

// A fresh site's start is recorded only (webForCore's is the start case's). Otherwise webForCore must decide as IIS 10
// does (in every run, or in most when IIS varies), and IIS as it always has: if IIS decides otherwise in a case it
// varies in, that is recorded, not a failure.
void Record(string key, string change, bool restarted)
{
    if (!cases.TryGetValue(key, out var expected)) throw new InvalidOperationException($"iis-fcn-cases.json has no case {mode}/{key}");
    var note = "";
    if (fresh)
    {
        note = " (recorded)";
    }
    else if (expected.WebForCore != expected.Iis)
    {
        note = $" (fails: iis-fcn-cases.json has webForCore {Text(expected.WebForCore)}, IIS 10 {Text(expected.Iis)})";
        failed = true;
    }
    else if (restarted != expected.Iis)
    {
        if (expected.Varies != null) note = $" (recorded: IIS varies here; {expected.Varies})";
        else { note = " (fails: IIS 10 decided otherwise in every earlier run)"; failed = true; }
    }
    report.Add($"| {change} | {Text(restarted)}{note} | {Text(expected.WebForCore)} |");
    Console.WriteLine($"{change}: IIS {Text(restarted)}, webForCore {Text(expected.WebForCore)}{note}");
}

async Task Change(string key, string change, Action make)
{
    var before = await Domain();
    make();
    var after = before;
    for (var deadline = DateTime.UtcNow.AddSeconds(20); after == before && DateTime.UtcNow < deadline;)
    {
        await Task.Delay(1000);
        after = await Domain();
    }
    Record(key, change, after != before);
}

var first = await Domain();
await Task.Delay(8000);
Record("start", "start (the first request, then 8 s)", await Domain() != first);
if (fresh) goto End;
var cs = Path.Combine(site, "bin", "cs");
await Change("file", @"a new file in bin\cs", () => File.WriteAllText(Path.Combine(cs, "new.txt"), "new"));
await Change("folder time", @"bin\cs's LastWriteTime set", () => Directory.SetLastWriteTimeUtc(cs, DateTime.UtcNow));
await Change("binaries", @"a new file in binaries", () => File.WriteAllText(Path.Combine(site, "binaries", "x.txt"), "x"));

End:
var end = Path.Combine(site, "App_Data", "end.txt");
report.Add("");
report.Add("Shutdown reasons: " + (File.Exists(end) ? string.Join("; ", File.ReadAllLines(end).Where(l => l.Length > 0)) : "none"));
report.Add("");
File.AppendAllLines(args[4], report);
return failed ? 1 : 0;

static string SourcePath([CallerFilePath] string path = "") => path;
