// Lists the libraries whose runtime or runtimeTargets assets in a deps.json include a file of the given name (#195):
//   dotnet run tools/ci/deps_assets.cs -- <app.deps.json> <file name>
// prints "<library>/<version> <asset path>" per asset.
#:property NuGetAudit=false
using System.Text.Json.Nodes;

var deps = JsonNode.Parse(File.ReadAllText(args[0]))!;
foreach (var (_, target) in deps["targets"]!.AsObject())
    foreach (var (library, entry) in target!.AsObject())
        foreach (var kind in new[] { "runtime", "runtimeTargets" })
            if (entry?[kind] is JsonObject assets)
                foreach (var (path, _) in assets)
                    if (Path.GetFileName(path).Equals(args[1], StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"{library} {path}");
