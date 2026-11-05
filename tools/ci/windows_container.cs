// webForCore in a Windows container: webforcore-host and test/webForCore.Mvc.Sample in Microsoft's ASP.NET Core runtime
// image for Windows (mcr.microsoft.com/dotnet/aspnet:10.0-nanoserver-*), run with docker and probed from the container's
// host. CI's windows-container job:
//
//   dotnet run tools/ci/windows_container.cs -- <context> <base image> <port> <report.md>
//     <context> holds host/ (webforcore-host's build output) and app/ (the sample's folder: Web.config, Global.asax,
//     Views, bin_dotnet). Builds an image from <base image> with both (tools/ci/windows-container/Dockerfile), runs it
//     with the container's port 8080 published on <port>, and checks: the application serves its Razor views, JSON and
//     a POST's redirect; Web.config changed inside the container restarts the application in a new worker process of
//     webforcore-host (the old one is recycled and exits with 0) and the new worker serves the same responses. Appends
//     a Markdown report; the exit code is 1 if a check failed. The container is removed in every case.
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length != 4)
{
    Console.Error.WriteLine("usage: dotnet run tools/ci/windows_container.cs -- <context> <base image> <port> <report.md>");
    return 2;
}
string context = Path.GetFullPath(args[0]), baseImage = args[1], report = args[3];
int port = int.Parse(args[2]);
const string Image = "webforcore-windows-container:ci";
const string Container = "webforcore-windows-container";
var dockerfile = Path.Combine("tools", "ci", "windows-container", "Dockerfile"); // run from the repository's root
var results = new List<(string Check, bool Passed, string Detail)>();
void Check(string check, bool passed, string detail = "")
{
    results.Add((check, passed, detail));
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {check}{(detail.Length > 0 ? ": " + detail : "")}");
}

static (int Exit, string Output) Docker(params string[] arguments)
{
    var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = new StringBuilder();
    process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();
    lock (output) return (process.ExitCode, output.ToString());
}

string Logs() => Docker("logs", Container).Output;

var build = Docker("build", "--isolation=process", "--build-arg", $"BASE={baseImage}", "-f", dockerfile, "-t", Image, context);
Console.WriteLine(build.Output);
if (build.Exit != 0)
{
    Check($"docker build from {baseImage}", false, $"exit {build.Exit}");
    return Finish();
}
Docker("rm", "-f", Container);
var run = Docker("run", "-d", "--isolation=process", "--name", Container, "-p", $"{port}:8080", Image);
Check($"docker run ({baseImage})", run.Exit == 0, run.Output.Trim());
if (run.Exit != 0) return Finish();

var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
{
    BaseAddress = new Uri($"http://localhost:{port}"),
    Timeout = TimeSpan.FromSeconds(120),
};

try
{
    // The first request starts the application (the first worker, Application_Start, the views' compilation).
    var deadline = DateTime.UtcNow.AddSeconds(180);
    HttpResponseMessage? first = null;
    while (first == null && DateTime.UtcNow < deadline)
    {
        try { first = await client.GetAsync("/"); }
        catch (HttpRequestException) { await Task.Delay(1000); }
        catch (TaskCanceledException) { }
    }
    Check("the host answers from the container", first != null, first == null ? "no answer in 180 s" : $"GET / {(int)first.StatusCode}");
    if (first == null) return Finish();

    await Probe("before the restart");

    // A deployment's Web.config change: the application restarts in a new worker process, as IIS recycles it into a new
    // AppDomain. Changed inside the container, where the application's file change notifications are.
    int started = Regex.Matches(Logs(), @"Started worker \d+").Count;
    var touch = Docker("exec", "-u", "ContainerAdministrator", Container, "cmd", "/c", @"cd /d C:\app && copy /b Web.config +,,");
    Check("Web.config changed in the container", touch.Exit == 0, touch.Output.Trim());

    var recycling = new Regex(@"Recycling worker (\d+) \(process (\d+)\)");
    Match recycled = Match.Empty;
    for (int i = 0; i < 120; i++)
    {
        var logs = Logs();
        recycled = recycling.Match(logs);
        if (recycled.Success && logs.Contains($"(process {recycled.Groups[2].Value}) exited (0).")) break;
        await Task.Delay(500);
    }
    Check("the worker is recycled", recycled.Success, recycled.Success ? recycled.Value : "no \"Recycling worker\" in the host's log");
    if (recycled.Success)
        Check("the recycled worker exits with 0", Logs().Contains($"(process {recycled.Groups[2].Value}) exited (0)."));

    await Probe("after the restart");
    int startedAfter = Regex.Matches(Logs(), @"Started worker \d+").Count;
    Check("a new worker serves", startedAfter > started, $"workers started: {started} before the change, {startedAfter} after");
    var serving = Regex.Matches(Logs(), @"Worker (\d+) \(process (\d+)\) is serving");
    Check("the new worker is another process", serving.Count >= 2 && serving[^1].Groups[2].Value != serving[0].Groups[2].Value,
        string.Join(", ", serving.Select(m => m.Value)));
}
catch (Exception e)
{
    Check("the probes ran", false, e.ToString());
}
return Finish();

async Task Probe(string when)
{
    async Task Get(string path, string expected)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Check($"GET {path} {when}", response.StatusCode == HttpStatusCode.OK && body.Contains(expected),
            $"{(int)response.StatusCode}, {(body.Contains(expected) ? "has" : "lacks")} {expected}");
    }
    await Get("/Home/About", "Your application description page.");
    await Get("/Products/Details/2", "<li>classic</li>");
    await Get("/Products", "\"Name\":\"Yo-yo\"");

    var post = await client.PostAsync("/Products/Create", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["Name"] = "Widget",
        ["Price"] = "2.50",
    }));
    Check($"POST /Products/Create {when}", post.StatusCode == HttpStatusCode.Found && post.Headers.Location?.OriginalString == "/Products/Details/2",
        $"{(int)post.StatusCode} {post.Headers.Location}");
}

int Finish()
{
    var logs = Logs();
    Console.WriteLine("---- the container's log ----");
    Console.WriteLine(logs);
    Docker("rm", "-f", Container);
    var text = new StringBuilder();
    text.AppendLine($"Base image `{baseImage}`.").AppendLine();
    text.AppendLine("| Check | Result | Detail |").AppendLine("| --- | --- | --- |");
    foreach (var (check, passed, detail) in results)
        text.AppendLine($"| {check} | {(passed ? "pass" : "**fail**")} | {detail.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ")} |");
    File.AppendAllText(report, text.ToString());
    return results.Count > 0 && results.All(r => r.Passed) ? 0 : 1;
}
