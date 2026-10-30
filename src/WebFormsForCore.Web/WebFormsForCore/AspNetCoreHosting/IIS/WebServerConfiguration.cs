#if NETCOREAPP
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using System.Web.Hosting;

namespace WebFormsForCore.Hosting.IIS
{
	// webForCore: IIS's <system.webServer> configuration for an application on Kestrel. IIS reads the section from
	// applicationHost.config and the application's web.config files; System.Web ignores it (machine.config declares it
	// an IgnoreSection). Here the effective section for a URL is built as IIS builds it: the applicationHost.config
	// defaults below, then the application's web.config, its <location> elements, and the web.config files of the
	// subdirectories on the way to the URL, each level's attributes overriding its parent's and its collections changed
	// by <add>, <remove> and <clear>. Locked sections, applicationHost.config's own <location> elements (sites, other
	// applications) and configuration encryption are not supported. Files are read again when they change.
	public sealed class WebServerConfiguration : IDisposable
	{
		// Collections below <system.webServer>: their path, the element that adds an item and the attributes that key it.
		static readonly Dictionary<string, (string Add, string[] Key)> Collections = new Dictionary<string, (string, string[])>(StringComparer.OrdinalIgnoreCase)
		{
			["modules"] = ("add", new[] { "name" }),
			["handlers"] = ("add", new[] { "name" }),
			["defaultDocument/files"] = ("add", new[] { "value" }),
			["staticContent"] = ("mimeMap", new[] { "fileExtension" }),
			["httpProtocol/customHeaders"] = ("add", new[] { "name" }),
			["httpProtocol/redirectHeaders"] = ("add", new[] { "name" }),
			["httpErrors"] = ("error", new[] { "statusCode", "subStatusCode" }),
			["httpRedirect"] = ("add", new[] { "wildcard" }),
			["httpCompression/staticTypes"] = ("add", new[] { "mimeType" }),
			["httpCompression/dynamicTypes"] = ("add", new[] { "mimeType" }),
			["security/authorization"] = ("add", new[] { "accessType", "users", "roles", "verbs" }),
			["security/requestFiltering/fileExtensions"] = ("add", new[] { "fileExtension" }),
			["security/requestFiltering/hiddenSegments"] = ("add", new[] { "segment" }),
			["security/requestFiltering/verbs"] = ("add", new[] { "verb" }),
			["security/requestFiltering/denyUrlSequences"] = ("add", new[] { "sequence" }),
			["security/requestFiltering/denyQueryStringSequences"] = ("add", new[] { "sequence" }),
			["security/requestFiltering/alwaysAllowedUrls"] = ("add", new[] { "url" }),
			["security/requestFiltering/alwaysAllowedQueryStrings"] = ("add", new[] { "queryString" }),
			["security/requestFiltering/requestLimits/headerLimits"] = ("add", new[] { "header" }),
			["rewrite/rules"] = ("rule", new[] { "name" }),
			["rewrite/outboundRules"] = ("rule", new[] { "name" }),
			["rewrite/outboundRules/preConditions"] = ("preCondition", new[] { "name" }),
			["rewrite/outboundRules/customTags"] = ("tags", new[] { "name" }),
			["rewrite/rewriteMaps"] = ("rewriteMap", new[] { "name" }),
			["security/ipSecurity"] = ("add", new[] { "ipAddress", "subnetMask", "domainName" }),
		};

		// applicationHost.config's defaults for the sections the hosting layer implements (IIS 10).
		const string ApplicationHostDefaults = @"<system.webServer>
  <defaultDocument enabled=""true"">
    <files>
      <add value=""Default.htm"" /><add value=""Default.asp"" /><add value=""index.htm"" /><add value=""index.html"" />
      <add value=""iisstart.htm"" /><add value=""default.aspx"" />
    </files>
  </defaultDocument>
  <directoryBrowse enabled=""false"" showFlags=""Date, Time, Size, Extension"" />
  <httpErrors errorMode=""DetailedLocalOnly"" existingResponse=""Auto"" defaultResponseMode=""File"" />
  <httpProtocol allowKeepAlive=""true"">
    <customHeaders><add name=""X-Powered-By"" value=""ASP.NET"" /></customHeaders>
    <redirectHeaders />
  </httpProtocol>
  <httpRedirect enabled=""false"" />
  <staticContent><clientCache cacheControlMode=""NoControl"" /></staticContent>
  <urlCompression doStaticCompression=""true"" doDynamicCompression=""false"" />
  <httpCompression minFileSizeForComp=""2700"">
    <staticTypes>
      <add mimeType=""text/*"" enabled=""true"" /><add mimeType=""message/*"" enabled=""true"" />
      <add mimeType=""application/javascript"" enabled=""true"" /><add mimeType=""application/atom+xml"" enabled=""true"" />
      <add mimeType=""application/xaml+xml"" enabled=""true"" /><add mimeType=""image/svg+xml"" enabled=""true"" />
      <add mimeType=""*/*"" enabled=""false"" />
    </staticTypes>
    <dynamicTypes>
      <add mimeType=""text/*"" enabled=""true"" /><add mimeType=""message/*"" enabled=""true"" />
      <add mimeType=""application/x-javascript"" enabled=""true"" /><add mimeType=""application/javascript"" enabled=""true"" />
      <add mimeType=""*/*"" enabled=""false"" />
    </dynamicTypes>
  </httpCompression>
  <security>
    <authorization><add accessType=""Allow"" users=""*"" /></authorization>
    <ipSecurity allowUnlisted=""true"" denyAction=""Forbidden"" enableProxyMode=""false"" enableReverseDns=""false"" />
    <dynamicIpSecurity denyAction=""Forbidden"" enableProxyMode=""false"" enableLoggingOnlyMode=""false"">
      <denyByConcurrentRequests enabled=""false"" maxConcurrentRequests=""20"" />
      <denyByRequestRate enabled=""false"" maxRequests=""20"" requestIntervalInMilliseconds=""200"" />
    </dynamicIpSecurity>
    <requestFiltering allowDoubleEscaping=""false"" allowHighBitCharacters=""true"" removeServerHeader=""false"">
      <fileExtensions allowUnlisted=""true"">
        <add fileExtension="".asa"" allowed=""false"" /><add fileExtension="".asax"" allowed=""false"" />
        <add fileExtension="".ascx"" allowed=""false"" /><add fileExtension="".master"" allowed=""false"" />
        <add fileExtension="".skin"" allowed=""false"" /><add fileExtension="".browser"" allowed=""false"" />
        <add fileExtension="".sitemap"" allowed=""false"" /><add fileExtension="".config"" allowed=""false"" />
        <add fileExtension="".cs"" allowed=""false"" /><add fileExtension="".csproj"" allowed=""false"" />
        <add fileExtension="".vb"" allowed=""false"" /><add fileExtension="".vbproj"" allowed=""false"" />
        <add fileExtension="".webinfo"" allowed=""false"" /><add fileExtension="".licx"" allowed=""false"" />
        <add fileExtension="".resx"" allowed=""false"" /><add fileExtension="".resources"" allowed=""false"" />
        <add fileExtension="".mdb"" allowed=""false"" /><add fileExtension="".vjsproj"" allowed=""false"" />
        <add fileExtension="".java"" allowed=""false"" /><add fileExtension="".jsl"" allowed=""false"" />
        <add fileExtension="".ldb"" allowed=""false"" /><add fileExtension="".dsdgm"" allowed=""false"" />
        <add fileExtension="".ssdgm"" allowed=""false"" /><add fileExtension="".lsad"" allowed=""false"" />
        <add fileExtension="".ssmap"" allowed=""false"" /><add fileExtension="".cd"" allowed=""false"" />
        <add fileExtension="".dsprototype"" allowed=""false"" /><add fileExtension="".lsaprototype"" allowed=""false"" />
        <add fileExtension="".sdm"" allowed=""false"" /><add fileExtension="".sdmDocument"" allowed=""false"" />
        <add fileExtension="".mdf"" allowed=""false"" /><add fileExtension="".ldf"" allowed=""false"" />
        <add fileExtension="".ad"" allowed=""false"" /><add fileExtension="".dd"" allowed=""false"" />
        <add fileExtension="".ldd"" allowed=""false"" /><add fileExtension="".sd"" allowed=""false"" />
        <add fileExtension="".adprototype"" allowed=""false"" /><add fileExtension="".lddprototype"" allowed=""false"" />
        <add fileExtension="".exclude"" allowed=""false"" /><add fileExtension="".refresh"" allowed=""false"" />
        <add fileExtension="".compiled"" allowed=""false"" /><add fileExtension="".msgx"" allowed=""false"" />
        <add fileExtension="".vsdisco"" allowed=""false"" /><add fileExtension="".rules"" allowed=""false"" />
      </fileExtensions>
      <verbs allowUnlisted=""true"" />
      <hiddenSegments>
        <add segment=""web.config"" /><add segment=""bin"" /><add segment=""App_code"" />
        <add segment=""App_GlobalResources"" /><add segment=""App_LocalResources"" /><add segment=""App_WebReferences"" />
        <add segment=""App_Data"" /><add segment=""App_Browsers"" />
      </hiddenSegments>
      <requestLimits maxAllowedContentLength=""30000000"" maxUrl=""4096"" maxQueryString=""2048"" />
    </requestFiltering>
  </security>
</system.webServer>";

		readonly string root;
		readonly FileSystemWatcher watcher;
		readonly ConcurrentDictionary<string, XElement> files = new ConcurrentDictionary<string, XElement>(StringComparer.Ordinal);
		readonly ConcurrentDictionary<string, XElement> effective = new ConcurrentDictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
		int version;

		// Directories (relative, '/'-separated) that are not part of the site: the application's output folder.
		public ICollection<string> ExcludedDirectories { get; } = new List<string>();

		public WebServerConfiguration(string physicalRoot)
		{
			root = Path.GetFullPath(physicalRoot).TrimEnd(Path.DirectorySeparatorChar);
			try
			{
				watcher = new FileSystemWatcher(root, "*.config") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
				FileSystemEventHandler changed = (_, e) => { if (IsWebConfig(e.Name)) Invalidate(); };
				watcher.Changed += changed;
				watcher.Created += changed;
				watcher.Deleted += changed;
				watcher.Renamed += (_, e) => Invalidate();
				using (ExecutionContext.SuppressFlow()) watcher.EnableRaisingEvents = true;
			}
			catch (Exception e) when (e is IOException || e is ArgumentException || e is PlatformNotSupportedException)
			{
				watcher = null; // read once
			}
			// In a PseudoAppDomain the watcher would keep the unloaded domain alive.
			PseudoAppDomain.OnUnload(typeof(WebServerConfiguration).Assembly, Dispose);
		}

		static bool IsWebConfig(string name) => name != null && Path.GetFileName(name).Equals("web.config", StringComparison.OrdinalIgnoreCase);

		public string PhysicalRoot => root;

		// Incremented when a web.config changes.
		public int Version => version;
		public event Action Changed;

		void Invalidate()
		{
			files.Clear();
			effective.Clear();
			Interlocked.Increment(ref version);
			Changed?.Invoke();
		}

		public void Dispose() => watcher?.Dispose();

		// The effective <system.webServer> section for an application-relative URL path ("/", "/dir/", "/dir/page.aspx").
		public XElement GetSection(string path)
		{
			path = string.IsNullOrEmpty(path) ? "/" : path;
			if (effective.Count > 10000) effective.Clear();
			return effective.GetOrAdd(path, Build);
		}

		XElement Build(string path)
		{
			var result = XElement.Parse(ApplicationHostDefaults);
			var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			bool isDirectory = path.EndsWith("/");
			// Each directory from the root to the URL's directory: its web.config, then the locations in it that apply.
			int directories = isDirectory ? segments.Length : Math.Max(segments.Length - 1, 0);
			var directory = root;
			for (int level = 0; level <= directories; level++)
			{
				if (level > 0)
				{
					var next = FindEntryIgnoreCase(directory, segments[level - 1], directoryOnly: true);
					if (next == null) break;
					directory = next;
				}
				var config = ReadConfig(directory);
				if (config == null) continue;
				var levelPath = "/" + string.Join("/", segments.Take(level)) + (level > 0 ? "/" : "");
				foreach (var section in config.Elements("system.webServer")) Merge(result, section, "", levelPath);
				// <location path="..."> relative to this directory; the shorter paths first, as IIS applies them.
				var rest = string.Join("/", segments.Skip(level));
				foreach (var location in config.Elements("location")
					.Select(l => (Element: l, Path: ((string)l.Attribute("path") ?? "").Trim('/')))
					.Where(l => l.Path == "" || l.Path == "." || rest.Equals(l.Path, StringComparison.OrdinalIgnoreCase)
						|| rest.StartsWith(l.Path + "/", StringComparison.OrdinalIgnoreCase))
					.OrderBy(l => l.Path.Length))
				{
					var locationPath = levelPath + (location.Path == "." ? "" : location.Path);
					foreach (var section in location.Element.Elements("system.webServer")) Merge(result, section, "", locationPath);
				}
			}
			return result;
		}

		XElement ReadConfig(string directory)
		{
			var file = FindEntryIgnoreCase(directory, "web.config", directoryOnly: false);
			if (file == null) return null;
			return files.GetOrAdd(file, f =>
			{
				try
				{
					using var stream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					return XElement.Load(stream);
				}
				catch (Exception e) when (e is IOException || e is System.Xml.XmlException || e is UnauthorizedAccessException)
				{
					// System.Web reports a malformed web.config; IIS answers 500.19. Treated as absent here.
					return new XElement("configuration");
				}
			});
		}

		// IIS paths are case-insensitive; Linux file systems are not.
		static string FindEntryIgnoreCase(string directory, string name, bool directoryOnly)
		{
			var exact = Path.Combine(directory, name);
			if (directoryOnly ? Directory.Exists(exact) : File.Exists(exact)) return exact;
			try
			{
				var entries = directoryOnly ? Directory.EnumerateDirectories(directory) : Directory.EnumerateFiles(directory);
				return entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
			}
			catch (IOException) { return null; }
			catch (UnauthorizedAccessException) { return null; }
		}

		// The URL path of the level that set an element's attributes (httpRedirect appends the rest of the URL to it).
		public static readonly XName LevelPathAttribute = XName.Get("levelPath", "urn:webForCore");

		// Merges one level's element into the effective one.
		static void Merge(XElement target, XElement source, string path, string levelPath)
		{
			foreach (var attribute in source.Attributes())
				target.SetAttributeValue(attribute.Name, attribute.Value);
			if (path == "httpRedirect" && source.Attribute("destination") != null)
				target.SetAttributeValue(LevelPathAttribute, levelPath);
			Collections.TryGetValue(path, out var collection);
			foreach (var child in source.Elements())
			{
				var name = child.Name.LocalName;
				if (collection.Add != null && (name == "clear" || name == "remove" || name == collection.Add))
				{
					// <remove> and <clear> stay in the result when they reach past it: <modules> and <handlers> continue
					// lists IIS and ASP.NET keep outside this configuration (WebServerModules applies them there).
					if (name == "clear")
					{
						target.Elements().Where(e => e.Name.LocalName == collection.Add || e.Name.LocalName == "remove").Remove();
						target.Add(new XElement(child));
					}
					else
					{
						var key = Key(child, collection.Key);
						var existing = target.Elements(collection.Add).FirstOrDefault(e => Key(e, collection.Key) == key);
						var copy = new XElement(child);
						// A rewrite rule runs against the URL relative to the level that defined it (distributed rules).
						if (path == "rewrite/rules" && name == collection.Add) copy.SetAttributeValue(LevelPathAttribute, levelPath);
						if (name == "remove")
						{
							if (existing != null) existing.Remove();
							else target.Add(copy);
						}
						else if (existing != null) existing.ReplaceWith(copy);
						else target.Add(copy);
					}
					continue;
				}
				var childPath = path == "" ? name : path + "/" + name;
				var targetChild = target.Element(child.Name);
				if (targetChild == null) target.Add(targetChild = new XElement(child.Name));
				Merge(targetChild, child, childPath, levelPath);
			}
		}

		static string Key(XElement element, string[] key) =>
			string.Join("\u0001", key.Select(k => ((string)element.Attribute(k) ?? DefaultKey(k)).ToLowerInvariant()));

		static string DefaultKey(string attribute) => attribute == "subStatusCode" ? "-1" : "";

		// Helpers for reading the effective section.
		public static XElement Element(XElement section, string path)
		{
			var e = section;
			foreach (var name in path.Split('/'))
			{
				e = e?.Element(name);
			}
			return e;
		}

		public static IEnumerable<XElement> Items(XElement section, string path)
		{
			var e = Element(section, path);
			if (e == null || !Collections.TryGetValue(path, out var collection)) return Enumerable.Empty<XElement>();
			return e.Elements(collection.Add);
		}

		public static string Attr(XElement element, string name, string defaultValue = null) =>
			(string)element?.Attribute(name) ?? defaultValue;

		public static bool Bool(XElement element, string name, bool defaultValue) =>
			bool.TryParse(Attr(element, name), out var value) ? value : defaultValue;

		public static long Long(XElement element, string name, long defaultValue) =>
			long.TryParse(Attr(element, name), out var value) ? value : defaultValue;
	}
}
#endif
