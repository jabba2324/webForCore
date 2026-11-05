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
		// so fusion applied <configuration><runtime><assemblyBinding> from it. Fusion reads only elements in its namespace
		// and skips an <assemblyBinding> whose appliesTo names another runtime. From it webForCore applies
		// <probing privatePath> (#147; Orchard loads its modules from App_Data/Dependencies): folders, separated by ';',
		// below the application's folder, not rooted ones nor ones that lead out of it; and each <dependentAssembly>'s
		// <bindingRedirect> and <codeBase> (#158).
		internal static void ReadAssemblyBinding(string appPath)
		{
			var webConfig = FindWebConfig(appPath);
			privatePaths = ReadPrivatePath(appPath, webConfig);
			dependentAssemblies = ReadDependentAssemblies(webConfig);
			appRoot = appPath == null ? null : Path.GetFullPath(appPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
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

		// The children of the <assemblyBinding> elements that apply to this runtime, in document order; null for a
		// Web.config that can't be read, which is the configuration system's error to report.
		static List<XmlElement> ReadAssemblyBindingElements(string webConfig)
		{
			if (webConfig == null) return null;
			try
			{
				var document = new XmlDocument { XmlResolver = null };
				using (var reader = XmlReader.Create(webConfig, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
				{
					document.Load(reader);
				}
				var runtime = document.DocumentElement?.LocalName == "configuration" ?
					document.DocumentElement.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "runtime") : null;
				if (runtime == null) return null;
				return runtime.ChildNodes.OfType<XmlElement>()
					.Where(e => e.LocalName == "assemblyBinding" && e.NamespaceURI == AssemblyBindingNamespace)
					.Where(e => !e.HasAttribute("appliesTo") || e.GetAttribute("appliesTo").StartsWith("v4", StringComparison.OrdinalIgnoreCase))
					.SelectMany(e => e.ChildNodes.OfType<XmlElement>())
					.Where(e => e.NamespaceURI == AssemblyBindingNamespace)
					.ToList();
			}
			catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
			{
				return null;
			}
		}

		internal static string[] ReadPrivatePath(string appPath, string webConfig)
		{
			var privatePath = ReadAssemblyBindingElements(webConfig)
				?.FirstOrDefault(e => e.LocalName == "probing" && e.HasAttribute("privatePath"))
				?.GetAttribute("privatePath");
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

		// webForCore: a <dependentAssembly> of the application's Web.config (#158): the assembly it is for, its
		// <bindingRedirect>s and its <codeBase>s, in their order. A customer application carries about 60 redirects, many
		// to a version lower than or equal to the one its references were built against.
		sealed class DependentAssembly
		{
			public string Name;
			public byte[] PublicKeyToken; // empty: the assembly has no strong name
			public string Culture; // "" for neutral
			public readonly List<(Version Low, Version High, Version NewVersion)> Redirects = new List<(Version, Version, Version)>();
			public readonly List<(Version Version, string Href)> CodeBases = new List<(Version, string)>();

			public bool IsFor(AssemblyName name) =>
				string.Equals(Name, name.Name, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(Culture, name.CultureName ?? "", StringComparison.OrdinalIgnoreCase) &&
				PublicKeyToken.AsSpan().SequenceEqual(name.GetPublicKeyToken() ?? Array.Empty<byte>());

			// Fusion takes the first <bindingRedirect> whose oldVersion has the version.
			public Version Redirect(Version version) =>
				Redirects.Where(r => version >= r.Low && version <= r.High).Select(r => r.NewVersion).FirstOrDefault();
		}

		static DependentAssembly[] dependentAssemblies = Array.Empty<DependentAssembly>();
		static string appRoot = null;

		static DependentAssembly[] ReadDependentAssemblies(string webConfig)
		{
			var elements = ReadAssemblyBindingElements(webConfig);
			if (elements == null) return Array.Empty<DependentAssembly>();
			var result = new List<DependentAssembly>();
			foreach (var element in elements.Where(e => e.LocalName == "dependentAssembly"))
			{
				var children = element.ChildNodes.OfType<XmlElement>().Where(e => e.NamespaceURI == AssemblyBindingNamespace).ToList();
				var identity = children.FirstOrDefault(e => e.LocalName == "assemblyIdentity");
				var name = identity?.GetAttribute("name");
				if (string.IsNullOrEmpty(name)) continue;
				var token = identity.GetAttribute("publicKeyToken");
				byte[] tokenBytes;
				if (string.IsNullOrEmpty(token) || token.Equals("null", StringComparison.OrdinalIgnoreCase)) tokenBytes = Array.Empty<byte>();
				else
				{
					try { tokenBytes = Convert.FromHexString(token); }
					catch (FormatException) { continue; }
				}
				var culture = identity.GetAttribute("culture");
				if (culture.Equals("neutral", StringComparison.OrdinalIgnoreCase)) culture = "";
				var dependent = new DependentAssembly { Name = name, PublicKeyToken = tokenBytes, Culture = culture };
				foreach (var redirect in children.Where(e => e.LocalName == "bindingRedirect"))
				{
					var oldVersion = redirect.GetAttribute("oldVersion").Split('-');
					if (oldVersion.Length > 2 ||
						!Version.TryParse(oldVersion[0].Trim(), out var low) ||
						!Version.TryParse(oldVersion[oldVersion.Length - 1].Trim(), out var high) ||
						!Version.TryParse(redirect.GetAttribute("newVersion").Trim(), out var newVersion)) continue;
					dependent.Redirects.Add((low, high, newVersion));
				}
				foreach (var codeBase in children.Where(e => e.LocalName == "codeBase"))
				{
					var href = codeBase.GetAttribute("href");
					if (string.IsNullOrEmpty(href)) continue;
					// An assembly without a strong name has one code base, whatever its version attribute says.
					Version version = null;
					if (tokenBytes.Length > 0 && !Version.TryParse(codeBase.GetAttribute("version").Trim(), out version)) continue;
					dependent.CodeBases.Add((version, href));
				}
				result.Add(dependent);
			}
			return result.ToArray();
		}

		// webForCore: the file a <codeBase> href names, or null for one fusion would download (http:), which .NET
		// can't load from. A relative href is relative to the application's folder.
		static string CodeBasePath(string href)
		{
			if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
			{
				return uri.IsFile ? Path.GetFullPath(uri.LocalPath) : null;
			}
			if (appRoot == null) return null;
			var relative = href.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
			return Path.GetFullPath(Path.Combine(appRoot, relative));
		}

		// webForCore: the redirect being loaded on this thread: fusion applies a <bindingRedirect> once, so the
		// redirected name's own Resolving isn't redirected again.
		[ThreadStatic] static string redirecting;

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

		// webForCore: the assembly a <codeBase> names, if it is the one asked for (#158).
		static Assembly LoadCodeBase(AssemblyLoadContext context, AssemblyName name, string path)
		{
			if (path == null || !File.Exists(path)) return null;
			AssemblyName found;
			try
			{
				found = AssemblyName.GetAssemblyName(path);
			}
			catch (BadImageFormatException)
			{
				return null;
			}
			if (!string.Equals(found.Name, name.Name, StringComparison.OrdinalIgnoreCase) ||
				!(found.GetPublicKeyToken() ?? Array.Empty<byte>()).AsSpan().SequenceEqual(name.GetPublicKeyToken() ?? Array.Empty<byte>()) ||
				(IsStrongNamed(name) && name.Version != null && found.Version != name.Version))
			{
				return null;
			}
			return context.LoadFromAssemblyPath(path);
		}

		public static Assembly Resolve(AssemblyLoadContext context, AssemblyName name)
		{
            //Debugger.Break();
            Debug.WriteLine($"Resolving {name.Name} in {context.Name}");
			//Console.WriteLine($"Resolving {name.Name} in {context.Name}");

			// webForCore: the application's <dependentAssembly> for the assembly (#158); fusion takes the first. A
			// <bindingRedirect> applies to a strong-named reference whose version is in its oldVersion, and the new
			// version is loaded in its place, as any other reference is: from deps.json, its <codeBase>, or bin and the
			// probing folders. .NET binds a reference to a version at or below the one in deps.json without asking, so
			// only a redirect to a lower version (or of an assembly that isn't in deps.json) gets here.
			var dependent = dependentAssemblies.FirstOrDefault(d => d.IsFor(name));
			if (dependent != null && IsStrongNamed(name) && name.Version != null && redirecting != name.FullName)
			{
				var newVersion = dependent.Redirect(name.Version);
				if (newVersion != null && newVersion != name.Version)
				{
					var target = (AssemblyName)name.Clone();
					target.Version = newVersion;
					var outer = redirecting;
					redirecting = target.FullName;
					try
					{
						return context.LoadFromAssemblyName(target);
					}
					catch (FileNotFoundException)
					{
						return null;
					}
					finally
					{
						redirecting = outer;
					}
				}
			}

			// webForCore: a <codeBase> for the version (any version for an assembly without a strong name, and then only in
			// the application's folder) is the only place fusion looks: if the assembly isn't there, it doesn't probe (#158).
			if (dependent != null)
			{
				var strongNamed = IsStrongNamed(name);
				var codeBase = dependent.CodeBases
					.Where(c => !strongNamed || c.Version == name.Version)
					.Select(c => new { Path = CodeBasePath(c.Href), c.Href })
					.Where(c => strongNamed || (c.Path != null && appRoot != null &&
						c.Path.StartsWith(appRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
					.FirstOrDefault();
				if (codeBase != null) return LoadCodeBase(context, name, codeBase.Path);
			}

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