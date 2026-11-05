// A request running when the application restarts, on IIS 10 (#181): whether it runs to its end in the old domain while
// new requests go to the new one, compared with what test/webForCore.IIS.Gate.Tests' RecyclingTests asserts of
// webForCore. CI's iis-compare job:
//
//   dotnet run tools/ci/iis_drain.cs -- site <folder>
//     writes a site: Web.config with <httpRuntime shutdownTimeout="10"> and <hostingEnvironment shutdownTimeout="5">,
//     Default.aspx (the application's domain id), Echo.ashx (a WebSocket echo), Stream.aspx (the domain id, then a
//     line flushed every second for ?seconds=, then "done") and bin.
//   dotnet run tools/ci/iis_drain.cs -- check <folder> <URL> <pipeline> <report.md>
//     for each length, requests Stream.aspx, writes a new file in bin 2 s later (the application restarts), asks for
//     Default.aspx every half second while the stream runs (does a new domain serve?), and records how many lines
//     arrived and how the stream ended; in the integrated pipeline, how long a WebSocket open across the change is
//     served. Appends a Markdown report; the exit code is 1 if IIS decided otherwise than webForCore.
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
    File.WriteAllText(Path.Combine(dir, "Echo.ashx"), """
        <%@ WebHandler Language="C#" Class="EchoHandler" %>
        using System;
        using System.Net.WebSockets;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Web;
        using System.Web.WebSockets;

        public class EchoHandler : IHttpHandler
        {
            public bool IsReusable { get { return false; } }

            public void ProcessRequest(HttpContext context)
            {
                if (context.IsWebSocketRequest) context.AcceptWebSocketRequest(Echo);
                else context.Response.Write("not a websocket request");
            }

            static async Task Echo(AspNetWebSocketContext context)
            {
                var socket = context.WebSocket;
                var buffer = new byte[4096];
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        break;
                    }
                    await socket.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
        """);
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

// A WebSocket open when bin changes (the integrated pipeline only: the classic one has none): how long it is served.
async Task CheckWebSocket()
{
    await Domain();
    await Task.Delay(3000);
    using var socket = new System.Net.WebSockets.ClientWebSocket();
    var uri = new UriBuilder(baseUrl + "/Echo.ashx") { Scheme = "ws" }.Uri;
    try { await socket.ConnectAsync(uri, CancellationToken.None); }
    catch (Exception e)
    {
        report.Add($"| a WebSocket | | not opened ({e.GetBaseException().Message}) | | |");
        return;
    }
    await Task.Delay(2000);
    File.WriteAllText(Path.Combine(site, "bin", "drain-websocket.txt"), DateTime.UtcNow.ToString("O"));
    var changed = Stopwatch.StartNew();
    var buffer = new byte[256];
    string ending;
    while (true)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.SendAsync("ping"u8.ToArray(), System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                ending = $"closed ({result.CloseStatus})";
                break;
            }
        }
        catch (Exception e)
        {
            ending = $"aborted ({e.GetBaseException().Message})";
            break;
        }
        if (changed.Elapsed > TimeSpan.FromSeconds(60))
        {
            ending = "still open";
            break;
        }
        await Task.Delay(500);
    }
    report.Add($"| a WebSocket | | {ending}, {changed.Elapsed.TotalSeconds:0.0} s after the change | | (recorded only) |");
    Console.WriteLine($"{pipeline} WebSocket: {ending}, {changed.Elapsed.TotalSeconds:0.0} s after the change");
}

// Longer than <hostingEnvironment shutdownTimeout>, shorter than <httpRuntime shutdownTimeout>: webForCore runs it to its end.
await Check(8, true);
// Longer than <httpRuntime shutdownTimeout> and the sixth of it HttpRuntime.Dispose waits again: then the classic
// pipeline waits for the requests still running (on IIS in both pipelines), and webForCore runs it to its end too.
await Check(30, true);
if (pipeline == "Integrated") await CheckWebSocket();

report.Add("");
File.AppendAllLines(args[4], report);
return failed ? 1 : 0;
