// Prints each failed test from a .trx with its message and the source line of the failing frame in the test files.
// Usage: dotnet run tools/ci/trx_failures.cs -- results.trx <test-source-root> [name-filter ...]
#:property NuGetAudit=false
using System.Text.RegularExpressions;
using System.Xml.Linq;

XNamespace t = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
var (trx, root, filters) = (args[0], Path.GetFullPath(args[1]).TrimEnd('/'), args[2..]);
var doc = XDocument.Load(trx).Root!;
var classes = doc.Descendants(t + "UnitTest").ToDictionary(
    u => (string)u.Attribute("id")!, u => (string)u.Element(t + "TestMethod")!.Attribute("className")!);
foreach (var x in doc.Descendants(t + "UnitTestResult"))
{
    var className = classes.GetValueOrDefault((string)x.Attribute("testId")!, "");
    var name = className[(className.LastIndexOf('.') + 1)..] + "." + (string)x.Attribute("testName")!;
    if ((string?)x.Attribute("outcome") != "Failed" || (filters.Length > 0 && !filters.Any(name.Contains))) continue;
    var info = x.Descendants(t + "ErrorInfo").FirstOrDefault();
    var message = (info?.Element(t + "Message")?.Value ?? "").Trim().Replace("\r", "").Replace("\n", " ⏎ ");
    var stack = info?.Element(t + "StackTrace")?.Value ?? "";
    Console.WriteLine($"■ {name}\n    {Take(message, 400)}");
    var m = Regex.Match(stack, " in (" + Regex.Escape(root) + "/[^:]+):line (\\d+)");
    if (m.Success)
    {
        var path = m.Groups[1].Value;
        var source = File.ReadAllLines(path);
        var line = int.Parse(m.Groups[2].Value);
        Console.WriteLine($"    @ {Path.GetRelativePath(root, path)}:{line}: {Take(source[line - 1].Trim(), 160)}");
    }
}

static string Take(string s, int count) => string.Concat(s.EnumerateRunes().Take(count).Select(r => r.ToString()));
