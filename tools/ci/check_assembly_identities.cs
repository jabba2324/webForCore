// Checks that every assembly webForCore's packages ship has the identity of the Microsoft assembly it replaces: name,
// version, culture and public key token (#141). A library built against Microsoft's assembly only compiles (CS0012,
// CS1705) and binds against webForCore's if they are the same: BundleTransformer, built against
// System.Web.Optimization 1.1.0.0 (31bf3856ad364e35), failed against webForCore's 11.0.0.0 (b03f5f7f11d50a3a) in
// SmartStoreNET.
//
// tools/ci/assembly-identities.json says, for each assembly name, where Microsoft's assembly is: a NuGet package, a
// version and the path in it (the .NET Framework's own assemblies are in Microsoft.NETFramework.ReferenceAssemblies).
// This reads Microsoft's identity from that assembly, from the NuGet cache or nuget.org, so no identity is typed in by
// hand. An entry can instead be webForCore's own assembly (no Microsoft counterpart) or a NuGet dependency shipped
// unchanged; and an entry whose version must be higher than Microsoft's says why ("higherVersion").
//
// Usage: dotnet run tools/ci/check_assembly_identities.cs -- <folder with the .nupkg files>
// Prints each assembly with webForCore's and Microsoft's identity, and exits with 1 on any difference, on an assembly
// in a package's lib or runtimes folder that the list doesn't have, and on an entry no package has.
#:property NuGetAudit=false
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: dotnet run tools/ci/check_assembly_identities.cs -- <folder with the .nupkg files>");
    return 2;
}

var listPath = Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "tools/ci", "assembly-identities.json");
using var listJson = JsonDocument.Parse(File.ReadAllText(listPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
var list = listJson.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => new Entry(
    Get(p.Value, "package"), Get(p.Value, "version"), Get(p.Value, "path"), Get(p.Value, "higherVersion"), Get(p.Value, "own"), Get(p.Value, "dependency")));

var nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
using var http = new HttpClient();
var downloaded = new Dictionary<string, ZipArchive>(StringComparer.OrdinalIgnoreCase);

int failures = 0;
var seen = new HashSet<string>();
foreach (var nupkg in Directory.GetFiles(args[0], "*.nupkg").Where(p => !p.EndsWith(".symbols.nupkg")).Order())
{
    using var zip = ZipFile.OpenRead(nupkg);
    foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName))
    {
        var parts = e.FullName.Split('/');
        // lib/<tfm>/X.dll and runtimes/<rid>/lib/<tfm>/X.dll are what applications compile against and load. A tool
        // package's folder (tools/) also has its NuGet dependencies; the assemblies of this repository's other packages
        // in it are checked like the packages' own.
        bool library = parts[0] == "lib" || (parts[0] == "runtimes" && parts.Length > 2 && parts[2] == "lib");
        var identity = ReadIdentity(Read(e));
        if (identity is null) continue; // native
        var name = identity.Name!;
        bool satellite = !string.IsNullOrEmpty(identity.CultureName);
        var key = satellite && name.EndsWith(".resources") ? name[..^".resources".Length] : name;
        if (!list.TryGetValue(key, out var entry))
        {
            if (library)
            {
                failures++;
                Console.WriteLine($"FAIL {Path.GetFileName(nupkg)} {e.FullName}: {identity.FullName} isn't in {Path.GetFileName(listPath)}");
            }
            continue;
        }
        seen.Add(key);
        var where = $"{Path.GetFileName(nupkg)} {e.FullName}";
        if (entry.Own is not null || entry.Dependency is not null)
        {
            if (library && satellite) continue;
            Console.WriteLine($"ok   {where}: {identity.FullName} ({(entry.Own is not null ? "webForCore's own" : "dependency")})");
            continue;
        }
        if (satellite) continue;
        var microsoft = Microsoft(entry);
        var problems = Compare(identity, microsoft, entry);
        if (problems.Count == 0)
            Console.WriteLine($"ok   {where}: {identity.FullName}" + (entry.HigherVersion is not null ? $" (Microsoft's {microsoft.Version}: {entry.HigherVersion})" : ""));
        else
        {
            failures++;
            Console.WriteLine($"FAIL {where}: {identity.FullName}");
            Console.WriteLine($"       Microsoft's ({entry.Package} {entry.Version}): {microsoft.FullName}");
            foreach (var p in problems) Console.WriteLine($"       {p}");
        }
    }
}
foreach (var name in list.Keys.Except(seen).Order())
{
    failures++;
    Console.WriteLine($"FAIL {name} is in {Path.GetFileName(listPath)} but in no package");
}
Console.WriteLine(failures == 0 ? "Every assembly has Microsoft's identity." : $"{failures} assemblies differ from Microsoft's.");
return failures == 0 ? 0 : 1;

List<string> Compare(AssemblyName ours, AssemblyName theirs, Entry entry)
{
    var problems = new List<string>();
    if (!string.Equals(ours.Name, theirs.Name, StringComparison.Ordinal)) problems.Add($"name {ours.Name}, Microsoft's {theirs.Name}");
    if ((ours.CultureName ?? "") != (theirs.CultureName ?? "")) problems.Add($"culture '{ours.CultureName}', Microsoft's '{theirs.CultureName}'");
    if (Token(ours) != Token(theirs)) problems.Add($"public key token {Token(ours)}, Microsoft's {Token(theirs)}");
    if (entry.HigherVersion is null ? ours.Version != theirs.Version : ours.Version <= theirs.Version)
        problems.Add(entry.HigherVersion is null ? $"version {ours.Version}, Microsoft's {theirs.Version}"
            : $"version {ours.Version} isn't higher than Microsoft's {theirs.Version} ({entry.HigherVersion})");
    return problems;
}

static string? Get(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

static string Token(AssemblyName n) => n.GetPublicKeyToken() is { Length: > 0 } t ? Convert.ToHexString(t).ToLowerInvariant() : "null";

AssemblyName Microsoft(Entry entry)
{
    var id = entry.Package ?? throw new InvalidDataException("an entry needs a package, \"own\" or \"dependency\"");
    var local = Path.Combine(nugetRoot, id.ToLowerInvariant(), entry.Version!.ToLowerInvariant(), entry.Path!);
    if (File.Exists(local)) return ReadIdentity(File.ReadAllBytes(local)) ?? throw new InvalidDataException(local);
    if (!downloaded.TryGetValue(id + "/" + entry.Version, out var package))
    {
        var lower = id.ToLowerInvariant();
        var v = entry.Version!.ToLowerInvariant();
        var bytes = http.GetByteArrayAsync($"https://api.nuget.org/v3-flatcontainer/{lower}/{v}/{lower}.{v}.nupkg").GetAwaiter().GetResult();
        downloaded[id + "/" + entry.Version] = package = new ZipArchive(new MemoryStream(bytes));
    }
    var file = package.GetEntry(entry.Path!) ?? throw new InvalidDataException($"{id} {entry.Version} has no {entry.Path}");
    return ReadIdentity(Read(file)) ?? throw new InvalidDataException($"{id} {entry.Version} {entry.Path} isn't an assembly");
}

static byte[] Read(ZipArchiveEntry e)
{
    using var s = e.Open();
    var m = new MemoryStream();
    s.CopyTo(m);
    return m.ToArray();
}

static AssemblyName? ReadIdentity(byte[] image)
{
    using var pe = new PEReader(new MemoryStream(image));
    if (!pe.HasMetadata) return null;
    var md = pe.GetMetadataReader();
    return md.IsAssembly ? md.GetAssemblyDefinition().GetAssemblyName() : null;
}

record Entry(string? Package, string? Version, string? Path, string? HigherVersion, string? Own, string? Dependency);
