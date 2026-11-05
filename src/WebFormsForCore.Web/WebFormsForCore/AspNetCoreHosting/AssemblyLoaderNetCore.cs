#if !NETFRAMEWORK
using Microsoft.AspNetCore.Routing.Constraints;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Xml;

namespace System.Web.Hosting
{
	public class AssemblyLoaderNetCore
	{
		static int Initialized = 0;
		public static void Init(string binpath = null, AssemblyLoadContext context = null)
		{
			var initialized = Interlocked.Exchange(ref Initialized, 1);
			if (initialized == 0)
			{
				exepath = binpath ?? exepath;
                var alc = context ?? AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
                alc.Resolving += Resolve;
                // webForCore: an unloading context keeps its handlers until it is collected, and this one (in the
                // context's own assembly) would keep it from being collected.
                if (alc.IsCollectible) alc.Unloading += unloading => unloading.Resolving -= Resolve;
            }
        }

		public static void Dispose()
		{
            var alc = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
            alc.Resolving -= Resolve;
        }

        static string probingPaths = "";
		public static string ProbingPaths {
			get => probingPaths;
			set { probingPaths = value; paths = null; }
		}
		
		static string exepath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
		public static List<string> AdditionalPaths = new List<string>();
		static string tempPath = null;
		public static string TempPath {
			get => tempPath;
			set
			{
				tempPath = value;
 				paths = null;
			}
		}

		// webForCore: the folders of the application's <probing privatePath> (#147), full paths, in their order.
		static string[] privatePaths = Array.Empty<string>();

		// webForCore: fusion's order, as .NET Framework probed an ASP.NET application: the codegen folder (the
		// AppDomain's dynamic directory), bin (here the build output), then <probing privatePath>'s folders (#147).
		static string[] paths = null;
		static string[] Paths => paths != null ? paths : paths =
			ProbingPaths
				.Replace('\\', Path.DirectorySeparatorChar)
				.Split(';')
				.Concat(new[] { tempPath, exepath })
				.Concat(privatePaths)
				.ToArray();
		public static bool UseNetFXGAC = false;

		const string AssemblyBindingNamespace = "urn:schemas-microsoft-com:asm.v1";

		// webForCore: on .NET Framework, an ASP.NET application's AppDomain had its Web.config as its configuration file,
		// so fusion probed the folders of <configuration><runtime><assemblyBinding><probing privatePath> after bin
		// (#147; Orchard loads its modules from App_Data/Dependencies). Fusion reads only elements in its namespace,
		// skips an <assemblyBinding> whose appliesTo names another runtime, and takes folders, separated by ';', below
		// the application's folder: not rooted ones, nor ones that lead out of it.
		internal static void ReadPrivatePath(string appPath)
		{
			privatePaths = ReadPrivatePath(appPath, FindWebConfig(appPath));
			paths = null;
		}

		static string FindWebConfig(string appPath)
		{
			if (!Directory.Exists(appPath)) return null;
			var path = Path.Combine(appPath, HttpConfigurationSystem.WebConfigFileName);
			if (File.Exists(path)) return path;
			return Directory.EnumerateFiles(appPath, HttpConfigurationSystem.WebConfigFileName,
				new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = false })
				.FirstOrDefault();
		}

		internal static string[] ReadPrivatePath(string appPath, string webConfig)
		{
			if (webConfig == null) return Array.Empty<string>();
			string privatePath = null;
			try
			{
				var document = new XmlDocument { XmlResolver = null };
				using (var reader = XmlReader.Create(webConfig, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
				{
					document.Load(reader);
				}
				var runtime = document.DocumentElement?.LocalName == "configuration" ?
					document.DocumentElement.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "runtime") : null;
				privatePath = runtime?.ChildNodes.OfType<XmlElement>()
					.Where(e => e.LocalName == "assemblyBinding" && e.NamespaceURI == AssemblyBindingNamespace)
					.Where(e => !e.HasAttribute("appliesTo") || e.GetAttribute("appliesTo").StartsWith("v4", StringComparison.OrdinalIgnoreCase))
					.SelectMany(e => e.ChildNodes.OfType<XmlElement>())
					.FirstOrDefault(e => e.LocalName == "probing" && e.NamespaceURI == AssemblyBindingNamespace && e.HasAttribute("privatePath"))
					?.GetAttribute("privatePath");
			}
			catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
			{
				// A Web.config that can't be read is the configuration system's error to report.
				return Array.Empty<string>();
			}
			if (string.IsNullOrEmpty(privatePath)) return Array.Empty<string>();

			var root = Path.GetFullPath(appPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			var folders = new List<string>();
			foreach (var entry in privatePath.Split(';'))
			{
				var folder = entry.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
				if (folder.Length == 0 || Path.IsPathRooted(folder) || folder.Contains(':')) continue;
				var full = Path.GetFullPath(Path.Combine(root, folder)).TrimEnd(Path.DirectorySeparatorChar);
				if (full.Length < root.Length || !full.StartsWith(root, comparison)) continue;
				if (!folders.Contains(full, StringComparer.FromComparison(comparison))) folders.Add(full);
			}
			return folders.ToArray();
		}

		// webForCore: the assembly resolver System.Web passes to Type.GetType(typeName, assemblyResolver, typeResolver,
		// throwOnError, ...) where .NET Framework's code called Type.GetType(typeName, throwOnError, ...). Type.GetType lets
		// a resolver's exceptions through whatever throwOnError says, and a resolver returns null for an assembly it
		// can't find. As .NET Framework's Type.GetType did, an assembly that isn't there is null unless throwOnError, and
		// one that is found but can't be loaded (FileLoadException, BadImageFormatException) throws (#137).
		internal static Func<AssemblyName, Assembly> TypeAssemblyResolver(AssemblyLoadContext context, bool throwOnError) =>
			assemblyName =>
			{
				try
				{
					return context.LoadFromAssemblyName(assemblyName);
				}
				catch (FileNotFoundException) when (!throwOnError)
				{
					return null;
				}
			};

		// webForCore: the files fusion tried in each folder, in its order: name.dll and name/name.dll (under the
		// culture's subfolder for a satellite assembly) in every folder, then the same with .exe.
		static IEnumerable<string> Candidates(IEnumerable<string> folders, AssemblyName name)
		{
			var culture = name.CultureName;
			foreach (var extension in new[] { ".dll", ".exe" })
			{
				foreach (var folder in folders)
				{
					var baseFolder = string.IsNullOrEmpty(culture) ? folder : Path.Combine(folder, culture);
					yield return Path.Combine(baseFolder, name.Name + extension);
					yield return Path.Combine(baseFolder, name.Name, name.Name + extension);
				}
			}
		}

		static bool IsAssembly(string path)
		{
			try
			{
				AssemblyName.GetAssemblyName(path);
				return true;
			}
			catch (BadImageFormatException)
			{
				return false;
			}
		}

		static bool IsStrongNamed(AssemblyName name)
		{
			var token = name.GetPublicKeyToken();
			return token != null && token.Length > 0;
		}

		public static Assembly Resolve(AssemblyLoadContext context, AssemblyName name)
		{
            //Debugger.Break();
            Debug.WriteLine($"Resolving {name.Name} in {context.Name}");
			//Console.WriteLine($"Resolving {name.Name} in {context.Name}");

			var paths = Paths
				.Concat(AdditionalPaths)
				.Where(p => !string.IsNullOrEmpty(p));

            if (UseNetFXGAC)
			{
				var winAssemblyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
					"Microsoft.NET", "assembly");
                byte[] tokenBytes = name.GetPublicKeyToken();
                string token = BitConverter
                    .ToString(tokenBytes)
                    .Replace("-", "")
                    .ToLowerInvariant();

				var subdir = Path.Combine(name.Name, $"v4.0_{name.Version}__{token}");
				var gacNative = OSInfo.Is64 ? "GAC_64" : "GAC_32";
				paths = paths
					.Concat(new[] { Path.Combine(winAssemblyDir, "GAC_MSIL", subdir),
						Path.Combine(winAssemblyDir, gacNative, subdir) });
			}
			var files = Candidates(paths, name)
				.Select(relativename =>
				{
					return new
					{
						FullName = Path.IsPathRooted(relativename) ? relativename :
							new DirectoryInfo(Path.Combine(exepath, relativename)).FullName,
						Name = relativename
					};
				})
				// webForCore: a native .exe (an apphost next to its .dll) isn't an assembly fusion would have found.
				.Where(p => File.Exists(p.FullName) && (!p.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsAssembly(p.FullName)));
			var assembly = files
				.Select(p => context.LoadFromAssemblyPath(p.FullName))
				// webForCore: as fusion, the version is checked only for a strong-named reference; one without a public
				// key token binds to the assembly of that name found, whatever its version (#171).
				.Where(assembly => assembly != null &&
					(!IsStrongNamed(name) || name.Version == null || assembly.GetName().Version >= name.Version))
				.FirstOrDefault();
			if (assembly != null)
			{
                Debug.WriteLine($"Loaded assembly {name.Name} in {context.Name}");
                //Console.WriteLine($"Loaded assembly {name.Name} in {context.Name}");
            }
            else
			{
                Debug.WriteLine($"Failed to resolve assembly {name.Name} in {context.Name}");
                //Console.WriteLine($"Failed to resolve assembly {name.Name} in {context.Name}");
            }
            return assembly;
		}
	}
}
#endif