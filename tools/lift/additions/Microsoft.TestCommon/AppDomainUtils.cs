// webForCore: replaces Microsoft.TestCommon's AppDomainUtils.cs (built for .NET Framework only) on .NET.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Web.Compilation;
using System.Web.Hosting;

namespace System.Web.WebPages.TestUtils
{
    // .NET has no AppDomains. RunInSeparateAppDomain runs the test code in a collectible AssemblyLoadContext instead:
    // the test assembly, Microsoft.TestCommon, System.Web and the Web Stack assemblies are loaded again from the test's
    // directory, so their static state (HttpRuntime, HostingEnvironment, BuildManager, caches) is fresh, as in a new
    // AppDomain, and the context is unloaded afterwards. Only xunit's runner assemblies are shared. The delegate is
    // recreated there: its target's fields are copied when they are strings, primitives or enums (the lambdas these
    // tests pass capture nothing else). AppDomain data (SetData) is process-wide here, so it is restored afterwards.
    public static class AppDomainUtils
    {
        private static readonly string[] AppDomainDataKeys = { ".appDomain", ".appId", ".appPath", ".appVPath", ".domainId" };

        public static void RunInSeparateAppDomain(Action action)
        {
            MethodInfo method = action.Method;
            object target = action.Target;
            string directory = Path.GetDirectoryName(typeof(AppDomainUtils).Assembly.Location);
            var saved = AppDomainDataKeys.ToDictionary(k => k, k => AppDomain.CurrentDomain.GetData(k));
            var context = new IsolatedLoadContext(directory);
            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(method.DeclaringType.Assembly.Location);
                Type type = assembly.GetType(method.DeclaringType.FullName, throwOnError: true);
                MethodInfo isolated = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Single(m => m.Name == method.Name && m.GetParameters().Length == 0);
                object isolatedTarget = null;
                if (!isolated.IsStatic)
                {
                    isolatedTarget = RuntimeHelpers.GetUninitializedObject(type);
                    if (target != null)
                    {
                        CopyFields(target, isolatedTarget);
                    }
                }
                try
                {
                    isolated.Invoke(isolatedTarget, null);
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                }
            }
            finally
            {
                foreach (var pair in saved)
                {
                    AppDomain.CurrentDomain.SetData(pair.Key, pair.Value);
                }
                context.Unload();
            }
        }

        private static void CopyFields(object from, object to)
        {
            const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (FieldInfo field in from.GetType().GetFields(Fields))
            {
                FieldInfo other = to.GetType().GetField(field.Name, Fields);
                object value = field.GetValue(from);
                if (other == null || value == null)
                {
                    continue;
                }
                if (value is string || value.GetType().IsPrimitive || value is decimal)
                {
                    other.SetValue(to, value);
                }
                else if (value.GetType().IsEnum && other.FieldType.IsEnum)
                {
                    other.SetValue(to, Enum.ToObject(other.FieldType, Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()))));
                }
                else
                {
                    throw new NotSupportedException("webForCore: RunInSeparateAppDomain cannot copy captured field " + field.Name + " of type " + value.GetType());
                }
            }
        }

        public static void SetPreAppStartStage()
        {
            var stage = typeof(BuildManager).GetProperty("PreStartInitStage", BindingFlags.Static | BindingFlags.NonPublic);
            var value = ((FieldInfo)typeof(BuildManager).Assembly.GetType("System.Web.Compilation.PreStartInitStage").GetMember("DuringPreStartInit")[0]).GetValue(null);
            stage.SetValue(null, value, new object[] { });
            SetAppData();
            var env = new HostingEnvironment();
        }

        public static void SetAppData()
        {
            var appdomain = AppDomain.CurrentDomain;
            // Set some dummy values to make the appdomain seem more like a ASP.NET hosted one
            appdomain.SetData(".appDomain", "*");
            appdomain.SetData(".appId", "appId");
            appdomain.SetData(".appPath", "appPath");
            appdomain.SetData(".appVPath", "/WebSite1");
            appdomain.SetData(".domainId", "1");
            // The fork's System.Web reads these per load context (ApplicationManager.GetLoadContextData: on .NET a
            // load context stands in for the AppDomain); set them for the context this code runs in.
            foreach (var key in AppDomainDataKeys)
            {
                ApplicationManager.SetLoadContextData(key, appdomain.GetData(key), AssemblyLoadContext.GetLoadContext(typeof(AppDomainUtils).Assembly));
            }
        }

        private sealed class IsolatedLoadContext : AssemblyLoadContext
        {
            private readonly string _directory;

            public IsolatedLoadContext(string directory)
                : base("RunInSeparateAppDomain", isCollectible: true)
            {
                _directory = directory;
            }

            protected override Assembly Load(AssemblyName name)
            {
                if (name.Name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                    || name.Name.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase)
                    || name.Name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                string path = Path.Combine(_directory, name.Name + ".dll");
                return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
            }
        }
    }
}
