// webForCore: WebActivatorEx 2.x, written from its documented public API and behaviour (MIT license). The assembly keeps
// WebActivatorEx's name, version and public key, so applications and packages built against the original (Unity.Mvc,
// Ninject.MVC, StructureMap.MVC, ...) bind to it.
//
// Why it isn't the original (#21): the original finds the activation attributes with Assembly.LoadFrom on every DLL in
// HttpRuntime.BinDirectory. On .NET, LoadFrom always loads into the default AssemblyLoadContext, so in a
// PseudoAppDomain it returned second copies of the application's assemblies, whose attributes are another
// WebActivatorEx's types, and no start method ever ran (ApplicantTracking: Unity never registered; Roadkill:
// StructureMap never started). This one loads them into its own context, which is the application's, as LoadFrom does
// in an AppDomain on .NET Framework.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Web;
using System.Web.Compilation;
using System.Web.Hosting;

[assembly: PreApplicationStartMethod(typeof(WebActivatorEx.ActivationManager), "Run")]

namespace WebActivatorEx
{
    /// <summary>The base class of the activation attributes: a static method of <see cref="Type"/> to call.</summary>
    public abstract class BaseActivationMethodAttribute : Attribute
    {
        public BaseActivationMethodAttribute(Type type, string methodName)
        {
            Type = type;
            MethodName = methodName;
        }

        public Type Type { get; }

        public string MethodName { get; }

        /// <summary>The order among the methods of the same kind; lower first.</summary>
        public int Order { get; set; }

        public void InvokeMethod()
        {
            var method = Type.GetMethod(MethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                throw new ArgumentException($"The type {Type.FullName} doesn't have a static method named {MethodName}");
            method.Invoke(null, null);
        }

        public virtual bool ShouldRunInDesignerMode() => false;
    }

    /// <summary>A method to call before the application starts (when System.Web runs pre-application-start methods).</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PreApplicationStartMethodAttribute : BaseActivationMethodAttribute
    {
        public PreApplicationStartMethodAttribute(Type type, string methodName) : base(type, methodName) { }

        public bool RunInDesigner { get; set; }

        public override bool ShouldRunInDesignerMode() => RunInDesigner;
    }

    /// <summary>A method to call after the application has started (when the first HttpApplication initializes its modules).</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class PostApplicationStartMethodAttribute : BaseActivationMethodAttribute
    {
        public PostApplicationStartMethodAttribute(Type type, string methodName) : base(type, methodName) { }
    }

    /// <summary>A method to call when the application shuts down (when the last HttpApplication is disposed).</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ApplicationShutdownMethodAttribute : BaseActivationMethodAttribute
    {
        public ApplicationShutdownMethodAttribute(Type type, string methodName) : base(type, methodName) { }
    }

    public class ActivationManager
    {
        private static bool _hasInited;
        private static List<Assembly> _assemblies;

        /// <summary>Runs the pre-start methods, and arranges for the post-start and shutdown methods.</summary>
        public static void Run()
        {
            if (_hasInited) return;
            RunPreStartMethods();
            // Under ASP.NET, the post-start methods run when the first module initializes; elsewhere, now.
            if (HostingEnvironment.IsHosted)
            {
                try { HttpApplication.RegisterModule(typeof(StartMethodCallingModule)); }
                catch (InvalidOperationException) { RunPostStartMethods(); } // too late to register: after start
            }
            else RunPostStartMethods();
            _hasInited = true;
        }

        public static void RunPreStartMethods(bool designerMode = false) =>
            RunActivationMethods<PreApplicationStartMethodAttribute>(designerMode);

        public static void RunPostStartMethods() => RunActivationMethods<PostApplicationStartMethodAttribute>();

        public static void RunShutdownMethods() => RunActivationMethods<ApplicationShutdownMethodAttribute>();

        /// <summary>Forgets the assemblies and the initialization (for tests).</summary>
        public static void Reset()
        {
            _hasInited = false;
            _assemblies = null;
        }

        private static void RunActivationMethods<T>(bool designerMode = false) where T : BaseActivationMethodAttribute
        {
            var attributes = Assemblies.Concat(AppCodeAssemblies)
                .Distinct()
                .SelectMany(GetActivationAttributes<T>)
                .OrderBy(attribute => attribute.Order);
            foreach (var attribute in attributes)
            {
                if (!designerMode || attribute.ShouldRunInDesignerMode()) attribute.InvokeMethod();
            }
        }

        private static IEnumerable<T> GetActivationAttributes<T>(Assembly assembly) where T : Attribute
        {
            try { return assembly.GetCustomAttributes(typeof(T), inherit: false).OfType<T>().ToList(); }
            catch (FileNotFoundException) { return Enumerable.Empty<T>(); } // an attribute type that can't load
            catch (TypeLoadException) { return Enumerable.Empty<T>(); }
        }

        private static IEnumerable<Assembly> AppCodeAssemblies
        {
            get
            {
                if (!HostingEnvironment.IsHosted) return Enumerable.Empty<Assembly>();
                try { return BuildManager.CodeAssemblies?.OfType<Assembly>().ToList() ?? Enumerable.Empty<Assembly>(); }
                catch (InvalidOperationException) { return Enumerable.Empty<Assembly>(); } // before App_Code is compiled
            }
        }

        // The assemblies in the application's bin directory (the build output's when hosted, otherwise this assembly's
        // folder) that can contain the attributes: the ones that reference WebActivatorEx. Each is loaded into this
        // assembly's load context, which returns the copy the application already has.
        private static IEnumerable<Assembly> Assemblies
        {
            get
            {
                if (_assemblies != null) return _assemblies;
                var assemblies = new List<Assembly>();
                var context = AssemblyLoadContext.GetLoadContext(typeof(ActivationManager).Assembly) ?? AssemblyLoadContext.Default;
                var directory = HostingEnvironment.IsHosted ? HttpRuntime.BinDirectory : Path.GetDirectoryName(typeof(ActivationManager).Assembly.Location);
                foreach (var file in Directory.Exists(directory) ? Directory.GetFiles(directory, "*.dll") : Array.Empty<string>())
                {
                    try
                    {
                        if (!ReferencesWebActivator(file)) continue;
                        assemblies.Add(context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(file)));
                    }
                    // Assemblies that can't be loaded (native, or for another platform) are skipped, as the original does.
                    catch (BadImageFormatException) { }
                    catch (FileNotFoundException) { }
                    catch (FileLoadException) { }
                    catch (ArgumentException) { }
                    catch (IOException) { }
                }
                return _assemblies = assemblies;
            }
        }

        private static bool ReferencesWebActivator(string file)
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return false;
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) return false;
            return reader.AssemblyReferences.Any(handle =>
                reader.StringComparer.Equals(reader.GetAssemblyReference(handle).Name, "WebActivatorEx", ignoreCase: true));
        }
    }

    // Runs the post-start methods when the first HttpApplication initializes its modules, and the shutdown methods when
    // the last one is disposed.
    internal sealed class StartMethodCallingModule : IHttpModule
    {
        private static readonly object Lock = new object();
        private static int _initializedModuleCount;

        public void Init(HttpApplication context)
        {
            lock (Lock)
            {
                if (_initializedModuleCount++ == 0) ActivationManager.RunPostStartMethods();
            }
        }

        public void Dispose()
        {
            lock (Lock)
            {
                if (--_initializedModuleCount == 0) ActivationManager.RunShutdownMethods();
            }
        }
    }
}
