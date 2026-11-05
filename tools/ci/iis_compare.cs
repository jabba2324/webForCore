// Compares the Web API gate's responses on webForCore with the same requests on IIS 10 and .NET Framework 4.8 (#78).
// WebApi.Gate.Tests writes every request it sends to test/webForCore.WebApi.Sample on Kestrel, and the response, to
// gate-responses.json. CI's iis-compare job serves the same sample from IIS (test/webForCore.WebApi.Sample.Iis), then:
//
//   dotnet run tools/ci/iis_compare.cs -- replay <gate-responses.json> <IIS base URL> <iis-responses.json>
//     sends each recorded request to IIS and writes the responses in the same form;
//   dotnet run tools/ci/iis_compare.cs -- diff <gate-responses.json> <iis-responses.json> [<known differences>]
//     compares status, headers and body of each pair and prints a Markdown report. The known differences
//     (tools/ci/iis-known-differences.json by default) are the ones docs/IIS-COMPATIBILITY.md records; the exit code is
//     1 if there is any other difference, or a known one that no longer occurs, so the list and the documentation stay
//     true.
//
// --title=<text> names the IIS side in the report, --gate=<text> the requests (default "Web API gate"); --informational
// prints the report and exits with 0. The request handling sample (test/webForCore.Requests.Sample, RequestHandlingTests
// writes requests-responses.json) is compared the same way, with tools/ci/iis-known-differences-requests.json.
//
// Both sides are normalised before the comparison: each side's own base address (http://127.0.0.1:<port>) becomes
// {base} wherever it appears (Location headers, bodies); stack traces are removed from Web API's JSON errors, as
// GateTests does for its oracle, and from ASP.NET's error pages (the comment at the end), since they name source files
// and line numbers; and Content-Length is compared as present or absent (the bodies are compared themselves).
#:property NuGetAudit=false
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var json = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine() };

if (args.Length >= 4 && args[0] == "replay") return await Replay(args[1], new Uri(args[2]), args[3]);
var options = args.Where(a => a.StartsWith("--")).ToList();
var positional = args.Where(a => !a.StartsWith("--")).ToArray();
if (positional.Length >= 3 && positional[0] == "diff")
{
    string title = options.FirstOrDefault(o => o.StartsWith("--title="))?["--title=".Length..] ?? "IIS 10";
    string gate = options.FirstOrDefault(o => o.StartsWith("--gate="))?["--gate=".Length..] ?? "Web API gate";
    int result = Diff(positional[1], positional[2], positional.Length > 3 ? positional[3] : Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "tools/ci", "iis-known-differences.json"), title, gate);
    // --informational: the report only, exit code 0.
    return options.Contains("--informational") ? 0 : result;
}
Console.Error.WriteLine("Usage: iis_compare.cs replay <gate-responses.json> <base URL> <out.json>\n       iis_compare.cs diff <gate-responses.json> <iis-responses.json> [<known differences.json>] [--title=<IIS side>] [--gate=<requests>] [--informational]");
return 2;

async Task<int> Replay(string recordedPath, Uri baseAddress, string outPath)
{
    var recorded = JsonNode.Parse(File.ReadAllText(recordedPath))!.AsObject();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    // The first request starts the application (Application_Start, OWIN startup), as SampleServer's does.
    for (int i = 0; ; i++)
    {
        try
        {
            using var warmup = await client.GetAsync(new Uri(baseAddress, "/api/values"));
            Console.WriteLine($"warm-up: {(int)warmup.StatusCode}");
            break;
        }
        catch (HttpRequestException) when (i < 60)
        {
            await Task.Delay(1000);
        }
    }

    var results = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
    foreach (var (key, entry) in recorded)
    {
        var request = entry!["request"]?.AsObject();
        if (request == null)
        {
            Console.Error.WriteLine($"{key}: no request recorded (gate-responses.json from before #78?)");
            return 1;
        }
        var url = new Uri((string)request["url"]!);
        // The recorded path and query as they were sent (a path that starts with "//" is a path here, not a host).
        var message = new HttpRequestMessage(new HttpMethod((string)request["method"]!), new Uri(baseAddress.GetLeftPart(UriPartial.Authority) + url.PathAndQuery));
        if ((string?)request["accept"] is string accept) message.Headers.TryAddWithoutValidation("Accept", accept);
        // The request's own headers, if it was recorded with any (RequestHandlingTests, #170).
        if (request["headers"] is JsonObject requestHeaders)
            foreach (var (name, value) in requestHeaders) message.Headers.TryAddWithoutValidation(name, (string?)value);
        if ((string?)request["body"] is string body)
        {
            message.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            if ((string?)request["contentType"] is string contentType) message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        using var response = await client.SendAsync(message);
        string responseBody = await response.Content.ReadAsStringAsync();
        var headers = new JsonObject();
        foreach (var h in response.Headers.Concat(response.Content.Headers).OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            if (h.Key != "Date") headers[h.Key] = string.Join(", ", h.Value);
        results[key] = new JsonObject
        {
            ["request"] = new JsonObject
            {
                ["method"] = message.Method.Method,
                ["url"] = message.RequestUri!.AbsoluteUri,
                ["accept"] = (string?)request["accept"],
                ["contentType"] = (string?)request["contentType"],
                ["body"] = (string?)request["body"],
                ["headers"] = request["headers"]?.DeepClone(),
            },
            ["status"] = (int)response.StatusCode,
            ["headers"] = headers,
            ["body"] = responseBody,
        };
        Console.WriteLine($"{(int)response.StatusCode} {key}");
    }
    var all = new JsonObject();
    foreach (var (key, value) in results) all[key] = value;
    File.WriteAllText(outPath, all.ToJsonString(json));
    return 0;
}

int Diff(string kestrelPath, string iisPath, string knownPath, string title, string gate)
{
    var kestrel = JsonNode.Parse(File.ReadAllText(kestrelPath))!.AsObject();
    var iis = JsonNode.Parse(File.ReadAllText(iisPath))!.AsObject();
    var known = JsonNode.Parse(File.ReadAllText(knownPath))!.AsArray()
        .Select(k => new Known(new Regex((string)k!["requests"]!), (string)k["field"]!, (string)k["reason"]!)).ToList();
    var used = new HashSet<Known>();

    var unexpected = new List<string>();
    var expected = new Dictionary<Known, (int Count, string Example)>();
    int same = 0;
    foreach (var (key, k) in kestrel)
    {
        if (!iis.TryGetPropertyValue(key, out var i) || i == null)
        {
            unexpected.Add($"| `{key}` | (all) | recorded on Kestrel only | |");
            continue;
        }
        var differences = Compare(k!.AsObject(), i.AsObject()).ToList();
        int unexpectedBefore = unexpected.Count;
        foreach (var (field, onKestrel, onIis) in differences)
        {
            var rule = known.FirstOrDefault(r => r.Requests.IsMatch(key) && FieldMatches(r.Field, field));
            string row = $"| `{key}` | {field} | {Cell(onKestrel)} | {Cell(onIis)} |";
            if (rule != null)
            {
                used.Add(rule);
                expected[rule] = expected.TryGetValue(rule, out var e) ? (e.Count + 1, e.Example) : (1, row);
            }
            else unexpected.Add(row + " |");
        }
        if (unexpected.Count == unexpectedBefore) same++;
    }
    foreach (var (key, _) in iis)
        if (!kestrel.ContainsKey(key)) unexpected.Add($"| `{key}` | (all) | | recorded on IIS only |");
    var stale = known.Where(r => !used.Contains(r)).ToList();

    var report = new StringBuilder();
    report.Append($"## {gate}: webForCore (Kestrel) and {title}\n\n{kestrel.Count} requests: {same} with the same response apart from known differences, " +
        $"{unexpected.Count} unexpected differences, {expected.Values.Sum(e => e.Count)} known differences, {stale.Count} known differences that no longer occur.\n\n");
    if (unexpected.Count > 0)
        report.Append("### Unexpected differences\n\n| Request | Field | webForCore | IIS 10 |\n| --- | --- | --- | --- |\n").AppendJoin("\n", unexpected).Append("\n\n");
    if (stale.Count > 0)
        report.Append("### Known differences that no longer occur\n\nRemove them from tools/ci/iis-known-differences.json and docs/IIS-COMPATIBILITY.md:\n\n")
            .AppendJoin("\n", stale.Select(r => $"- `{r.Requests}` {r.Field}: {r.Reason}")).Append("\n\n");
    if (expected.Count > 0)
        report.Append("### Known differences (docs/IIS-COMPATIBILITY.md)\n\n| Why | Occurrences | For example (request, field, webForCore, IIS 10) |\n| --- | ---: | --- |\n")
            .AppendJoin("\n", expected.Select(e => $"| {e.Key.Reason} | {e.Value.Count} | {e.Value.Example.Trim('|', ' ').Replace(" | ", ", ")} |")).Append("\n\n");
    Console.Write(report);
    return unexpected.Count == 0 && stale.Count == 0 ? 0 : 1;
}

IEnumerable<(string Field, string? Kestrel, string? Iis)> Compare(JsonObject k, JsonObject i)
{
    string kBase = BaseOf(k), iBase = BaseOf(i);
    int kStatus = (int)k["status"]!, iStatus = (int)i["status"]!;
    if (kStatus != iStatus) yield return ("status", kStatus.ToString(), iStatus.ToString());
    var kHeaders = Headers(k, kBase);
    var iHeaders = Headers(i, iBase);
    foreach (var name in kHeaders.Keys.Union(iHeaders.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
    {
        kHeaders.TryGetValue(name, out var kv);
        iHeaders.TryGetValue(name, out var iv);
        if (kv != iv) yield return ("header " + name, kv, iv);
    }
    string? kBody = Body(k, kBase), iBody = Body(i, iBase);
    if (kBody != iBody) yield return ("body", kBody, iBody);
}

string BaseOf(JsonObject entry)
{
    var url = new Uri((string)entry["request"]!["url"]!);
    return url.GetLeftPart(UriPartial.Authority);
}

Dictionary<string, string> Headers(JsonObject entry, string baseAddress) =>
    entry["headers"]!.AsObject().ToDictionary(h => h.Key,
        h => h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ? "(present)" : ((string)h.Value!).Replace(baseAddress, "{base}"),
        StringComparer.OrdinalIgnoreCase);

string? Body(JsonObject entry, string baseAddress)
{
    var body = ((string?)entry["body"])?.Replace(baseAddress, "{base}");
    if (body == null) return null;
    // ASP.NET's error page ends with the exception and its stack trace in a comment.
    body = Regex.Replace(body, @"<!--\s*\[[A-Za-z.]*Exception\]:[\s\S]*?-->\s*$", "<!-- (stack trace) -->");
    if (!body.TrimStart().StartsWith('{')) return body;
    try
    {
        var node = JsonNode.Parse(body);
        if (node is not JsonObject o || !o.ContainsKey("StackTrace")) return body;
        for (JsonObject? e = o; e != null; e = e["InnerException"] as JsonObject) e.Remove("StackTrace");
        return node.ToJsonString();
    }
    catch (JsonException)
    {
        return body;
    }
}

static bool FieldMatches(string rule, string field) =>
    rule == field || rule.EndsWith('*') && field.StartsWith(rule[..^1], StringComparison.OrdinalIgnoreCase)
    || string.Equals(rule, field, StringComparison.OrdinalIgnoreCase);

static string Cell(string? value)
{
    if (value == null) return "(none)";
    value = Regex.Replace(value, @"\s+", " ");
    if (value.Length > 160) value = value[..160] + "...";
    return "`" + value.Replace("|", "\\|").Replace("`", "'") + "`";
}

record Known(Regex Requests, string Field, string Reason);
