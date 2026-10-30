// Checks lifted assemblies: identity (name, version, PublicKeyToken) and that every strongly typed
// resource class can load its resources and read a string, exactly as the code does at runtime.
// Usage: dotnet run tools/lift/check_assembly.cs -- <assembly.dll> [...]
// Exit code 1 if any resource class fails.
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;

int failures = 0;
foreach (var path in args)
{
    var full = Path.GetFullPath(path);
    var dir = Path.GetDirectoryName(full)!;
    var alc = new AssemblyLoadContext(Path.GetFileName(full), isCollectible: true);
    alc.Resolving += (ctx, name) =>
    {
        var candidate = Path.Combine(dir, name.Name + ".dll");
        return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
    };
    var asm = alc.LoadFromAssemblyPath(full);
    var an = asm.GetName();
    var token = Convert.ToHexString(an.GetPublicKeyToken() ?? []).ToLowerInvariant();
    Console.WriteLine($"{an.Name}, Version={an.Version}, PublicKeyToken={(token == "" ? "null" : token)}");
    foreach (var r in asm.GetManifestResourceNames().Order())
        Console.WriteLine($"  resource {r}");

    Type[] types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }

    foreach (var t in types.OrderBy(t => t.FullName))
    {
        var prop = t.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.PropertyType != typeof(ResourceManager)) continue;
        try
        {
            var rm = (ResourceManager)prop.GetValue(null)!;
            var set = rm.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
            var first = set.Cast<System.Collections.DictionaryEntry>().OrderBy(e => (string)e.Key).First();
            var value = rm.GetString((string)first.Key, CultureInfo.InvariantCulture);
            var count = set.Cast<object>().Count();
            Console.WriteLine($"  OK   {t.FullName}: base name {rm.BaseName}, {count} strings, {first.Key} = \"{Trim(value)}\"");
        }
        catch (Exception e)
        {
            failures++;
            var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
            Console.WriteLine($"  FAIL {t.FullName}: {inner.GetType().Name}: {Trim(inner.Message)}");
        }
    }
    alc.Unload();
}
return failures == 0 ? 0 : 1;

static string Trim(string? s) => s is null ? "" : (s.Length > 70 ? s[..70] + "..." : s).ReplaceLineEndings(" ");
