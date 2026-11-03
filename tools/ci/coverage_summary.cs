// Summarises a Cobertura file by assembly: line coverage of webForCore's production code. Prints Markdown. Test
// assemblies, test helpers, samples and third-party packages are left out; assemblies are grouped by where their
// source comes from. Run from the repository root (tools/ci/coverage.sh does).
// Usage: dotnet run tools/ci/coverage_summary.cs -- merged.cobertura.xml
#:property NuGetAudit=false
using System.Globalization;
using System.Xml.Linq;

var root = Environment.CurrentDirectory;
var stats = new List<(string Package, string Project, Dictionary<(string?, string?), bool> Lines)>();
foreach (var package in XDocument.Load(args[0]).Root!.Descendants("package"))
    foreach (var cls in package.Descendants("class"))
    {
        var file = (string?)cls.Attribute("filename");
        var project = Origin(file);
        if (project is null || project.Contains("TestApp") || project.EndsWith("Testing")) continue;
        var name = (string?)package.Attribute("name") ?? "";
        var entry = stats.FirstOrDefault(s => s.Package == name && s.Project == project);
        if (entry.Lines is null) stats.Add(entry = (name, project, new()));
        foreach (var line in cls.Descendants("line"))
        {
            var key = (file, (string?)line.Attribute("number"));
            entry.Lines[key] = entry.Lines.GetValueOrDefault(key) || long.Parse((string?)line.Attribute("hits") ?? "0") > 0;
        }
    }

var inv = CultureInfo.InvariantCulture;
long totalLines = 0, totalCovered = 0;
Console.WriteLine("| Assembly | Source | Coverable lines | Covered | Line coverage |");
Console.WriteLine("| --- | --- | ---: | ---: | ---: |");
foreach (var (name, project, lines) in stats.OrderBy(s => s.Project.ToLowerInvariant(), StringComparer.Ordinal))
{
    int count = lines.Count, covered = lines.Values.Count(v => v);
    if (count == 0) continue;
    totalLines += count; totalCovered += covered;
    var kind = project.StartsWith("webForCore.") ? "lifted" : "port";
    Console.WriteLine($"| {name} | {kind} | {count.ToString("N0", inv)} | {covered.ToString("N0", inv)} | {(100.0 * covered / count).ToString("F1", inv)}% |");
}
Console.WriteLine($"| **Total** | | **{totalLines.ToString("N0", inv)}** | **{totalCovered.ToString("N0", inv)}** | **{(100.0 * totalCovered / totalLines).ToString("F1", inv)}%** |");

// The project a source file belongs to: the folder under src/, or null for anything else.
string? Origin(string? filename)
{
    var rel = filename is not null && Path.IsPathRooted(filename) ? Path.GetRelativePath(root, filename) : filename ?? "";
    var parts = rel.Replace('\\', '/').Split('/');
    return parts.Length > 1 && parts[0] == "src" ? parts[1] : null;
}
