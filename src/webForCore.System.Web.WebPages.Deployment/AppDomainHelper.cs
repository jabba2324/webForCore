// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;

namespace System.Web.WebPages.Deployment
{
    internal static class AppDomainHelper
    {
        public static IDictionary<string, IEnumerable<string>> GetBinAssemblyReferences(string appPath, string configPath)
        {
            string binDirectory = Path.Combine(appPath, "bin");
            if (!Directory.Exists(binDirectory))
            {
                return null;
            }

            // webForCore: .NET has no AppDomains (CreateDomain, AppDomainSetup.PrivateBinPath, Evidence). A collectible
            // AssemblyLoadContext does the same job: the bin assemblies load there, references resolve from the bin
            // directory and otherwise as in the app (the default context), and it is unloaded afterwards.
            var loadContext = new System.Runtime.Loader.AssemblyLoadContext(typeof(AppDomainHelper).Namespace, isCollectible: true);
            loadContext.Resolving += (context, name) =>
            {
                string candidate = Path.Combine(binDirectory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };
            try
            {
                return Directory.EnumerateFiles(binDirectory, "*.dll")
                    .ToDictionary(assemblyPath => assemblyPath,
                                  assemblyPath => GetReferences(loadContext, assemblyPath));
            }
            finally
            {
                loadContext.Unload();
            }
        }

        private static IEnumerable<string> GetReferences(System.Runtime.Loader.AssemblyLoadContext loadContext, string assemblyPath)
        {
            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            return assembly.GetReferencedAssemblies()
                .Select(asmName => loadContext.LoadFromAssemblyName(asmName).FullName)
                .Concat(new[] { assembly.FullName })
                .ToArray();
        }

        private sealed class RemoteAssemblyLoader : MarshalByRefObject
        {
            [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Method needs to be instance level for cross domain invocation"),
             SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods", MessageId = "System.Reflection.Assembly.LoadFrom",
                 Justification = "We want to load this specific assembly.")]
            public IEnumerable<string> GetReferences(string assemblyPath)
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                return assembly.GetReferencedAssemblies()
                    .Select(asmName => Assembly.Load(asmName.FullName).FullName)
                    .Concat(new[] { assembly.FullName })
                    .ToArray();
            }
        }
    }
}
