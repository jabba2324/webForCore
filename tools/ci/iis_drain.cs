// A request running when the application restarts, on IIS 10 (#181): whether it runs to its end in the old domain while
// new requests go to the new one, compared with what test/webForCore.IIS.Gate.Tests' RecyclingTests asserts of
// webForCore. CI's iis-compare job:
//
//   dotnet run tools/ci/iis_drain.cs -- site <folder>
//     writes a site: Web.config with <httpRuntime shutdownTimeout="10"> and <hostingEnvironment shutdownTimeout="5">,
//     Default.aspx (the application's domain id), Stream.aspx (the domain id, then a line flushed every second for
//     ?seconds=, then "done") and bin.
//   dotnet run tools/ci/iis_drain.cs -- check <folder> <URL> <pipeline> <report.md>
//     for each length, requests Stream.aspx, writes a new file in bin 2 s later (the application restarts), asks for
//     Default.aspx every half second while the stream runs (does a new domain serve?), and records how many lines
//     arrived and how the stream ended. Appends a Markdown report; the exit code is 1 if IIS decided otherwise than
//     webForCore.
using System.Diagnostics;
using System.Text.RegularExpressions;

if (args.Length >= 2 && args[0] == "site")
{
    var dir = args[1];
    Directory.CreateDirectory(Path.Combine(dir, "bin"));
    File.WriteAllText(Path.Combine(dir, "Web.config"), """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <system.web>
            <compilation debug="false" targetFramework="4.8" />
            <httpRuntime targetFramework="4.8" shutdownTimeout="10" />
            <hostingEnvironment shutdownTimeout="5" />
          </system.web>
        </configuration>
        """);
    File.WriteAllText(Path.Combine(dir, "Default.aspx"), "<%@ Page Language=\"C#\" %>domain=<%= System.Web.HttpRuntime.AppDomainId %>;");
    File.WriteAllText(Path.Combine(dir, "Stream.aspx"), """
        <%@ Page Language="C#" %>
        <%
            int seconds = int.Parse(Request.QueryString["seconds"]);
            Response.Write("domain=" + System.Web.HttpRuntime.AppDomainId + ";\n");
            Response.Flush();
            for (int i = 1; i <= seconds; i++)
            {
                System.Threading.Thread.Sleep(1000);
                Response.Write("line " + i + "\n");
                Response.Flush();
            }
            Response.Write("done\n");
        %>
        """);
    return 0;
}

if (args.Length != 5 || args[0] != "check")
{
    Console.Error.WriteLine("usage: iis_drain.cs site <folder> | check <folder> <URL> <pipeline> <report.md>");
    return 2;
}

var site = args[1];
var baseUrl = args[2].TrimEnd('/');
var pipeline = args[3];
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
var report = new List<string>
{
    $"### IIS 10, {pipeline} pipeline: a request running when bin changes (`<httpRuntime shutdownTimeout=\"10\">`, `<hostingEnvironment shutdownTimeout=\"5\">`)",
    "",
    "| request | lines received | how it ended | a new domain served during it | webForCore |",
    "| --- | --- | --- | --- | --- |",
};
var failed = false;

async Task<string> Domain()
{
    for (int attempt = 0; ; attempt++)
    {
        try
        {
            var body = await client.GetStringAsync(baseUrl + "/Default.aspx");
            var match = Regex.Match(body, "domain=([^;]*);");
            if (match.Success) return match.Groups[1].Value;
            throw new InvalidOperationException(body);
        }
        catch (Exception) when (attempt < 30) { await Task.Delay(1000); }
    }
}

// expected: whether webForCore runs the request to its end (null: recorded only).
async Task Check(int seconds, bool? expected)
{
    await Domain();
    await Task.Delay(3000);
    var clock = Stopwatch.StartNew();
    using var response = await client.GetAsync($"{baseUrl}/Stream.aspx?seconds={seconds}", HttpCompletionOption.ResponseHeadersRead);
    var lines = new List<string>();
    var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    string ending = "complete";
    var read = Task.Run(async () =>
    {
        try
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            while (await reader.ReadLineAsync() is string line)
            {
                first.TrySetResult(line);
                lines.Add(line);
            }
        }
        catch (Exception e) when (e is IOException || e is HttpRequestException)
        {
            ending = "cut (" + e.GetBaseException().Message + ")";
        }
        first.TrySetResult("");
    });
    await Task.Delay(2000);
    File.WriteAllText(Path.Combine(site, "bin", $"drain-{seconds}.txt"), DateTime.UtcNow.ToString("O"));
    var streamDomain = Regex.Match(await first.Task, "domain=([^;]*);").Groups[1].Value;
    var newDomain = false;
    while (!read.IsCompleted)
    {
        await Task.Delay(500);
        if (!newDomain && !read.IsCompleted)
        {
            var domain = await Domain();
            newDomain = domain != streamDomain && streamDomain.Length > 0 && !read.IsCompleted;
        }
    }
    await read;
    var elapsed = clock.Elapsed.TotalSeconds;
    var complete = lines.Count > 0 && lines[^1] == "done" && ending == "complete";
    if (!complete && ending == "complete") ending = "ended early";
    var lineCount = Math.Max(0, lines.Count(l => l.StartsWith("line ")));
    var request = $"{seconds} s";
    var webForCore = expected == null ? "(recorded only)" : expected.Value ? "runs to its end" : "cut";
    report.Add($"| {request} | {lineCount} of {seconds} | {ending}, after {elapsed:0.0} s | {(newDomain ? "yes" : "no")} | {webForCore} |");
    Console.WriteLine($"{pipeline} {request}: {lineCount} of {seconds} lines, {ending} after {elapsed:0.0} s, new domain during it: {newDomain}");
    if (expected != null && (complete != expected.Value || !newDomain)) failed = true;
}

// Longer than <hostingEnvironment shutdownTimeout>, shorter than <httpRuntime shutdownTimeout>: webForCore runs it to its end.
await Check(8, true);
// Longer than <httpRuntime shutdownTimeout> and the sixth of it HttpRuntime.Dispose waits again.
await Check(30, null);

report.Add("");
File.AppendAllLines(args[4], report);
return failed ? 1 : 0;
