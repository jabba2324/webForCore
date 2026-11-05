// <httpRuntime fcnMode> on IIS 10 (#167): which changes restart the application, compared with what
// test/webForCore.Host.Tests' FileChangeNotificationTests.AChangeInBinDotnetRestartsTheApplicationAndStartingDoesNot asserts of
// webForCore (bin_dotnet in place of bin). CI's iis-compare job:
//
//   dotnet run tools/ci/iis_fcn.cs -- site <folder> <fcnMode>
//     writes a site: Web.config with the fcnMode, Default.aspx (the application's domain id), Global.asax (writes each
//     domain's shutdown reason to App_Data\end.txt), App_Code, a folder named "binaries", and bin with files in bin\cs,
//     bin\de and bin\runtimes\win-x64\native, as an application's satellite and native assemblies are.
//   dotnet run tools/ci/iis_fcn.cs -- check <folder> <URL> <fcnMode> <report.md> [fresh]
//     requests the site, waits, and checks that it didn't restart; then makes each change, in order, and checks whether
//     the application restarted (its domain id changed) within 20 s: a new file in bin\cs, bin\cs's LastWriteTime set,
//     and a new file in binaries. Appends a Markdown report; the exit code is 1 if IIS decided otherwise than webForCore.
//
// The job writes the compared sites before IIS serves them: tools/ci/iis-site.ps1 sets their permissions, which opens
// every folder. A site written after (fresh: the start only, for the record) is what a deployment copies: Windows then
// reports each of bin's folders as modified the first time the application opens them (it writes their deferred
// LastWriteTime), during the first request, and in Single mode IIS 10 sometimes restarts the application for that and
// sometimes not (the reports of different runs differ). webForCore doesn't (#167).
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
var single = mode == "Single";
var fresh = args.Length > 5;
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var report = new List<string> { $"### IIS 10, fcnMode=\"{mode}\"{(fresh ? ", a site copied while IIS serves its folder (for the record)" : "")}", "", "| change | IIS 10 | webForCore |", "| --- | --- | --- |" };
var failed = false;

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

void Record(string change, bool restarted, bool expected)
{
    string Text(bool restart) => restart ? "restart" : "no restart";
    report.Add($"| {change} | {Text(restarted)} | {Text(expected)} |");
    Console.WriteLine($"{change}: IIS {Text(restarted)}, webForCore {Text(expected)}");
    if (restarted != expected && !fresh) failed = true;
}

async Task Change(string change, Action make, bool expected)
{
    var before = await Domain();
    make();
    var after = before;
    for (var deadline = DateTime.UtcNow.AddSeconds(20); after == before && DateTime.UtcNow < deadline;)
    {
        await Task.Delay(1000);
        after = await Domain();
    }
    Record(change, after != before, expected);
}

var first = await Domain();
await Task.Delay(8000);
Record("start (the first request, then 8 s)", await Domain() != first, false);
if (fresh) goto End;
var cs = Path.Combine(site, "bin", "cs");
await Change(@"a new file in bin\cs", () => File.WriteAllText(Path.Combine(cs, "new.txt"), "new"), true);
await Change(@"bin\cs's LastWriteTime set", () => Directory.SetLastWriteTimeUtc(cs, DateTime.UtcNow), single);
await Change(@"a new file in binaries", () => File.WriteAllText(Path.Combine(site, "binaries", "x.txt"), "x"), single);

End:
var end = Path.Combine(site, "App_Data", "end.txt");
report.Add("");
report.Add("Shutdown reasons: " + (File.Exists(end) ? string.Join("; ", File.ReadAllLines(end).Where(l => l.Length > 0)) : "none"));
report.Add("");
File.AppendAllLines(args[4], report);
return failed ? 1 : 0;
