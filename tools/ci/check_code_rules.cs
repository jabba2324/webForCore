// Checks webForCore's own code against CONTRIBUTING.md rules 6 and 7: no reflection into non-public members of
// .NET or other libraries, no unsafe code or P/Invoke, nothing that steers the garbage collector.
// Usage: dotnet run tools/ci/check_code_rules.cs [-- <repository root>]
// Prints each line that breaks a rule and isn't in the list of known exceptions below, and exits with 1 if there is
// one. A known exception that no longer matches is reported too, so the list shrinks as #57 is worked through.
using System.Text.RegularExpressions;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");

// Code webForCore wrote itself. Microsoft's code (the submodules and the port of the reference source) keeps its own
// reflection and interop (principle 4), so the System.Web port is listed by the files webForCore changed for #57.
string[] ownDirectories =
[
    "src/WebFormsForCore.Web/WebFormsForCore",
    "src/WebFormsForCore.Host",
    "src/WebFormsForCore.Compilers",
    "src/WebFormsForCore.Web.Infrastructure",
    // LINQ to SQL's generated classes (#142); its Microsoft code is in external/referencesource.
    "src/webForCore.System.Data.Linq",
];
string[] ownFiles =
[
    "src/WebFormsForCore.Serialization.Formatters/Serialization/Formatters/Binary/FrameworkSurrogates.cs",
    // Ported files where webForCore had added code that broke the rules (#57).
    "src/WebFormsForCore.Serialization.Formatters/Serialization/Formatters/Binary/BinaryObjectReader.cs",
    "src/WebFormsForCore.Web/Compilation/CodeDirectoryCompiler.cs",
    "src/WebFormsForCore.Web/HttpApplication.cs",
    "src/WebFormsForCore.Web/HttpRuntime.cs",
    "src/WebFormsForCore.Web/Util/GCUtil.cs",
];
// Files that would be in scope but are Microsoft's code (CompilationUtil is from Microsoft's CodeDom providers).
string[] microsoftFiles =
[
    "src/WebFormsForCore.Web/WebFormsForCore/CodeDom/Util/CompilationUtil.cs",
];

(string Name, Regex Pattern)[] rules =
[
    ("non-public reflection", new(@"BindingFlags\.NonPublic|UnsafeAccessor|\.InvokeMember\(|Activator\.CreateInstance\([^;]*,\s*(true|nonPublic:\s*true)\s*\)")),
    ("unsafe code", new(@"\bunsafe\b|\bstackalloc\b|\bfixed\s*\(|\bUnsafe\.|\bMemoryMarshal\.")),
    ("P/Invoke", new(@"\[\s*(DllImport|LibraryImport)\b|\bextern\s+(static\s+)?\w+\s+\w+\s*\(")),
    ("GC steering", new(@"\bGC\.(Collect|WaitForPendingFinalizers|AddMemoryPressure)\b|\bGCHandle\b|\bWeakReference\b")),
];

// Known exceptions: (file, rule, issue). Each is listed in #57, or explained here.
(string File, string Rule, string Why)[] known =
[
    ("src/WebFormsForCore.Serialization.Formatters/Serialization/Formatters/Binary/FrameworkSurrogates.cs", "non-public reflection", "#57"),
    // Microsoft's own CreateNonPublicInstance, unchanged from the reference source.
    ("src/WebFormsForCore.Web/HttpRuntime.cs", "non-public reflection", "reference source"),
    // Microsoft's own GCHandle use, unchanged from the reference source (DisposableGCHandleRef, RootObject).
    ("src/WebFormsForCore.Web/Util/GCUtil.cs", "GC steering", "reference source"),
    // System.Web's DynamicValidationShim, which System.Web keeps for Microsoft.Web.Infrastructure as on .NET Framework.
    ("src/WebFormsForCore.Web.Infrastructure/DynamicValidationHelper/ValidationUtility.cs", "non-public reflection", "System.Web's shim"),
];

var files = ownDirectories
    .Select(d => Path.Combine(root, d))
    .Where(Directory.Exists)
    .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
    .Concat(ownFiles.Select(f => Path.Combine(root, f)).Where(File.Exists))
    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
    .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !microsoftFiles.Contains(f))
    .Distinct()
    .Order(StringComparer.Ordinal)
    .ToList();

var used = new HashSet<(string, string)>();
int violations = 0, allowed = 0;
foreach (var file in files)
{
    var lines = File.ReadAllLines(Path.Combine(root, file));
    for (int i = 0; i < lines.Length; i++)
    {
        var code = StripComment(lines[i]);
        foreach (var (name, pattern) in rules)
        {
            if (!pattern.IsMatch(code)) continue;
            var exception = known.FirstOrDefault(k => k.File == file && k.Rule == name);
            if (exception.File != null)
            {
                used.Add((file, name));
                allowed++;
                continue;
            }
            violations++;
            Console.WriteLine($"{file}:{i + 1}: {name}: {lines[i].Trim()}");
        }
    }
}

int stale = 0;
foreach (var k in known.Where(k => !used.Contains((k.File, k.Rule))))
{
    stale++;
    Console.WriteLine($"{k.File}: no longer has {k.Rule}; remove it from the known exceptions in tools/ci/check_code_rules.cs ({k.Why})");
}

Console.WriteLine($"{files.Count} files checked: {violations} new rule breaks, {allowed} lines in {used.Count} known exceptions, {stale} stale exceptions");
return violations + stale > 0 ? 1 : 0;

// Drops a // comment (not inside a string) so that prose about a rule doesn't count as breaking it.
static string StripComment(string line)
{
    bool inString = false;
    for (int i = 0; i < line.Length - 1; i++)
    {
        if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
        else if (!inString && line[i] == '/' && line[i + 1] == '/') return line[..i];
    }
    return line;
}
