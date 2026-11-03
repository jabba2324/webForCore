// Lists the projects tools/ci/pack.sh packs: the solution's projects under src/ that it builds (not
// <Build Project="false" />), without the build tools in src/WebFormsForCore.Build/. One path per line.
// Usage: dotnet run tools/ci/packable_projects.cs -- WebFormsForCore.slnx
#:property NuGetAudit=false
using System.Xml.Linq;

foreach (var p in XDocument.Load(args[0]).Descendants("Project"))
{
    var path = (string?)p.Attribute("Path") ?? "";
    var built = p.Elements("Build").All(b => (string?)b.Attribute("Project") != "false");
    if (path.StartsWith("src/") && built && !path.StartsWith("src/WebFormsForCore.Build/"))
        Console.Out.Write(path + "\n");
}
