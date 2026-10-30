// WebFormsForCore's additions to aspnet_compiler (MIT license, Copyright (c) Simon Jakob Egli; webForCore): multiple
// bin folders and target frameworks (-b, -t), precompilation in its own load context, and error events for the MSBuild
// task. The rest of Precompiler is Microsoft's aspnet_compiler.cs (ReferenceSource/aspnet_compiler, MIT), lifted by
// tools/lift/lift_aspnet_compiler.py.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Web.Hosting;

#nullable disable
namespace System.Web.Compilation;

internal class Start
{
    public static int Main(string[] args)
    {
        return RunMain(args);
    }

    static int RunMain(string[] args)
    {
        return Precompiler.Main(args);
    }
}

// Flags that drive the behavior of precompilation
[Flags]
public enum PrecompileFlags
{

    Default = 0x00000000,

    // determines whether the deployed app will be updatable
    Updatable = 0x00000001,

    // determines whether the target directory can be overwritten
    OverwriteTarget = 0x00000002,

    // determines whether the compiler will emit debug information
    ForceDebug = 0x00000004,

    // determines whether the application is built clean
    Clean = 0x00000008,

    // determines whether the /define:CodeAnalysis flag needs to be added
    // as compilation symbol
    CodeAnalysis = 0x00000010,

    // determines whether to generate APTCA attribute.
    AllowPartiallyTrustedCallers = 0x00000020,

    // determines whether to delaySign the generate assemblies.
    DelaySign = 0x00000040,

    // determines whether to use fixed assembly names
    FixedNames = 0x00000080,

    // determines whether to skip BadImageFormatException
    IgnoreBadImageFormatException = 0x00000100,
}

public partial class Precompiler
{
    private static string _binFolder;
    private static string _targetFramework;

    // Suppresses console output (the MSBuild task reports through OnError and OnException instead).
    public static bool Silent = false;

    public static Action<Exception> OnException { get; set; } = null;
    public static Action<string, int, bool, string, string> OnError { get; set; } = null;
    public static bool HasErrors { get; set; } = false;

    // Exceptions come back from the precompilation's own load context, where System.Web's types are a different copy,
    // so they are recognised by name.
    static bool IsException(Exception e, string typeName) => e.GetType().Name == typeName;

    public static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite = true)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        DirectoryInfo[] dirs = dir.GetDirectories();

        // Create destination if it doesn't exist
        Directory.CreateDirectory(destinationDir);

        // Copy files
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite);
        }

        // Copy subdirectories recursively
        foreach (DirectoryInfo subDir in dirs)
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir, overwrite);
        }
    }

    static int pass = 0;
    static int reentrant = 0;
    public static int Precompile(string sourceVirtualDir, string sourcePhysicalDir, string targetDir, PrecompileParameter parameter, bool forceCleanBuild = false)
    {
        if (Interlocked.Increment(ref reentrant) == 0) HasErrors = false;
        var binFolders = parameter.BinFolder?.Split(new char[] { ',', ';' }, StringSplitOptions.TrimEntries) ?? new string[] { null };
        var targetFrameworks = parameter.TargetFramework?.Split(new char[] { ',', ';' }, StringSplitOptions.TrimEntries) ?? new string[] { null };
        if (binFolders.Length <= 1 && targetFrameworks.Length <= 1)
        {
            var self = Assembly.GetExecutingAssembly().Location;
            var alc = new AssemblyLoadContext($"PrecompileContext.{++pass}", true);
            var selfpath = Path.GetDirectoryName(self);
            var selfwebdll = Path.Combine(selfpath, "System.Web.dll");
            var selfconfigdll = Path.Combine(selfpath, "System.Configuration.ConfigurationManager.dll");
            var path = Path.Combine(sourcePhysicalDir, binFolders[0] ?? "bin");
            //AssemblyLoaderNetCore.Init(path, alc);

            //var webdll = Path.Combine(path, "System.Web.dll");
            //var configdll = Path.Combine(path, "System.Configuration.ConfigurationManager.dll");
            //AssemblyLoaderNetCore.UseNetFXGAC = !File.Exists(webdll) ||
            //    Regex.IsMatch(targetFrameworks[0], "^(net)?[234][0-9.]+$", RegexOptions.Singleline);
            alc.LoadFromAssemblyPath(selfconfigdll);
            alc.LoadFromAssemblyPath(selfwebdll);
            // Load the other System.Web.* assemblies into the same context as System.Web.dll. Otherwise they are
            // resolved in the default context, which loads a second copy of System.Web.dll, and types like
            // System.Web.UI.Control get a different identity (breaking e.g. ExtenderControl overrides).
            foreach (var dll in Directory.GetFiles(selfpath, "System.Web.*.dll"))
            {
                try { alc.LoadFromAssemblyPath(dll); }
                catch (Exception) { }
            }
            var selfAssembly = alc.LoadFromAssemblyPath(self);

            var precompileType = selfAssembly.GetType("System.Web.Compilation.Precompiler");
            var precompileMethod = precompileType.GetMethod("PrecompileInternal");
            CopyEventStatic(typeof(Precompiler), precompileType, "OnError");
            CopyEventStatic(typeof(Precompiler), precompileType, "OnException");

            try
            {
                precompileMethod.Invoke(null, new object[] { sourceVirtualDir, sourcePhysicalDir, targetDir,
                    parameter.StrongNameKeyFile, parameter.StrongNameKeyContainer,
                    parameter.PrecompilationFlags, parameter.ExcludedVirtualPaths,
                    parameter.BinFolder, parameter.TargetFramework,
                    forceCleanBuild });
                return HasErrors ? -1 : 0;
            }
            catch (Exception ex)
            {
                DumpErrors(ex);
                return -1;
            }
            finally
            {
                alc.Unload();
                Interlocked.Decrement(ref reentrant);
            }
        }
        else
        {
            if (binFolders.Length != targetFrameworks.Length)
            {
                Console.WriteLine(CompilerResources.unequalBinAndFramework);
                Interlocked.Decrement(ref reentrant);
                return -401;
            }

            int i;
            for (i = 0; i < binFolders.Length; i++)
            {
                var par = new PrecompileParameter()
                {
                    PrecompilationFlags = parameter.PrecompilationFlags,
                    StrongNameKeyContainer = parameter.StrongNameKeyContainer,
                    StrongNameKeyFile = parameter.StrongNameKeyFile,
                    TargetFramework = targetFrameworks[i],
                    BinFolder = binFolders[i]
                };
                par.ExcludedVirtualPaths.AddRange(parameter.ExcludedVirtualPaths);

                var tempTargetDir = targetDir;
                if (i >= 1) tempTargetDir = Path.Combine(targetDir, binFolders[i], "_AspNetCompiler");
                if (!string.IsNullOrEmpty(tempTargetDir)) Directory.CreateDirectory(tempTargetDir);

                Precompile(sourceVirtualDir, sourcePhysicalDir, tempTargetDir, par, i >= 1);

                if (i >= 1)
                {
                    var targetBin = Path.Combine(targetDir, binFolders[i]);
                    var tempTargetBin = Path.Combine(tempTargetDir, binFolders[i]);
                    if (Directory.Exists(tempTargetBin)) CopyDirectory(tempTargetBin, targetBin, true);
                    if (Directory.Exists(tempTargetDir)) Directory.Delete(tempTargetDir, true);
                }
            }
        }

        Interlocked.Decrement(ref reentrant);
        return HasErrors ? -1 : 0;
    }

    public static int PrecompileInternal(string sourceVirtualDir, string sourcePhysicalDir, string targetDir,
        string strongNameKeyFile,
        string strongNameKeyContainer,
        PrecompileFlags precompilationFlags,
        List<string> excludedVirtualPaths,
        string binFolder,
        string targetFramework,
        bool forceCleanBuild = false)
    {
        var par = new ClientBuildManagerParameter()
        {
            TargetFramework = targetFramework,
            BinFolder = binFolder,
            PrecompilationFlags = (System.Web.Compilation.PrecompilationFlags)precompilationFlags,
            StrongNameKeyFile = strongNameKeyFile,
            StrongNameKeyContainer = strongNameKeyContainer
        };
        par.ExcludedVirtualPaths.AddRange(excludedVirtualPaths);
        var path = Path.Combine(sourcePhysicalDir, binFolder ?? "bin");
        var webdll = Path.Combine(path, "System.Web.dll");
        var configdll = Path.Combine(path, "System.Configuration.ConfigurationManager.dll");
        //AssemblyLoaderNetCore.UseNetFXGAC = !File.Exists(webdll) ||
        //  Regex.IsMatch(targetFramework, "^(net)?[234][0-9.]+$", RegexOptions.Singleline);

        using var manager = new ClientBuildManager(sourceVirtualDir, sourcePhysicalDir, targetDir, par);
        AssemblyLoaderNetCore.AdditionalPaths.Add(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

        manager.PrecompileApplication(new CBMCallback(), forceCleanBuild);

        AssemblyLoaderNetCore.Dispose();
        return HasErrors ? -1 : 0;
    }

    static void CopyEventStatic(Type source, Type dest, string eventName)
    {
        var field = source.GetProperty(eventName, BindingFlags.Static | BindingFlags.Public);
        var destField = dest.GetProperty(eventName, BindingFlags.Static | BindingFlags.Public);

        if (field is null)
            throw new InvalidOperationException(
                $"No backing field found for event '{eventName}'.");

        if (!typeof(Delegate).IsAssignableFrom(field.PropertyType))
            throw new InvalidOperationException(
                $"'{eventName}' is not backed by a delegate field.");

        if (destField is null)
            throw new InvalidOperationException(
                $"No backing field found for event '{eventName}'.");

        if (!typeof(Delegate).IsAssignableFrom(destField.PropertyType))
            throw new InvalidOperationException(
                $"'{eventName}' is not backed by a delegate field.");

        // var existing = (Delegate?)destField.GetValue(null);
        var handlers = (Delegate?)field.GetValue(null);
        destField.SetValue(null, handlers); // Delegate.Combine(existing, handlers));
    }
}

[Serializable]
public class PrecompileParameter
{
    private string _strongNameKeyFile;
    private string _strongNameKeyContainer;
    private PrecompileFlags _precompilationFlags = PrecompileFlags.Default;
    private List<string> _excludedVirtualPaths;
    private string _binFolder;

    public string TargetFramework { get; set; }

    public List<string> ExcludedVirtualPaths
    {
        get
        {
            if (_excludedVirtualPaths == null)
            {
                _excludedVirtualPaths = new List<string>();
            }
            return _excludedVirtualPaths;
        }
    }

    // Determines the behavior of the precompilation
    public PrecompileFlags PrecompilationFlags
    {
        get { return _precompilationFlags; }
        set { _precompilationFlags = value; }
    }

    public string StrongNameKeyFile
    {
        get { return _strongNameKeyFile; }
        set { _strongNameKeyFile = value; }
    }

    public string StrongNameKeyContainer
    {
        get { return _strongNameKeyContainer; }
        set { _strongNameKeyContainer = value; }
    }

    public string BinFolder
    {
        get => _binFolder;
        set => _binFolder = value;
    }
}

// WebFormsForCore's strings for -b and -t (CompilerResources.resx); the rest are the Reference Source's.
internal partial class CompilerResources
{
    internal static string b_help => ResourceManager.GetString("b_help", resourceCulture);
    internal static string t_help => ResourceManager.GetString("t_help", resourceCulture);
    internal static string unequalBinAndFramework => ResourceManager.GetString("unequalBinAndFramework", resourceCulture);
}
