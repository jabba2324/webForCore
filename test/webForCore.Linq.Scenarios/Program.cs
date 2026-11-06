using System.Text.Json.Nodes;
using LinqScenarios;

// Usage: webForCore.Linq.Scenarios <base URL of the Requests sample> <out.json>
// Writes the responses up to a failing step too, and exits with 1 then.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: webForCore.Linq.Scenarios <base URL> <out.json>");
    return 2;
}
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(args[0]) };
var results = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
int exitCode = 0;
try
{
    await Scenarios.RunAsync(client, results);
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    exitCode = 1;
}
File.WriteAllText(args[1], Scenarios.ToJson(results));
foreach (var (key, entry) in results) Console.WriteLine($"{entry["status"]} {key}");
return exitCode;
