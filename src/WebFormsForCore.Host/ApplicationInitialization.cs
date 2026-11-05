using System.Xml;
using System.Xml.Linq;

namespace WebFormsForCore.Host;

// IIS's Application Initialization (#193): <system.webServer><applicationInitialization> in the application's Web.config,
// with the host's settings (HostSettings) in place of what they set. IIS sends a new worker process the initialization
// pages, one after the other, and gives it traffic once they have been answered; in an overlapped recycle the old
// worker serves meanwhile. A page that fails doesn't stop the worker from serving.
//
//   <applicationInitialization doAppInitAfterRestart="true" remapManagedRequestsTo="Startup.htm" skipManagedModules="true">
//     <add initializationPage="/default.aspx" hostName="www.example.com" />
//   </applicationInitialization>
//
// skipManagedModules is read and has no effect: every module of a webForCore application is managed, and the requests
// run the whole pipeline (a page behind forms authentication answers its redirect, and the application has started).
sealed class ApplicationInitialization
{
	// IIS's user agent for the initialization requests.
	public const string UserAgent = "IIS Application Initialization Warmup";

	public IReadOnlyList<(string Page, string? HostName)> Pages { get; }
	public bool DoAppInitAfterRestart { get; }
	public bool PreloadEnabled { get; }
	// The file answered while a worker warms up, as a full path in the site; null when there is none.
	public string? RemapFile { get; }

	ApplicationInitialization(IReadOnlyList<(string, string?)> pages, bool afterRestart, bool preload, string? remapFile)
	{
		Pages = pages;
		DoAppInitAfterRestart = afterRestart;
		PreloadEnabled = preload;
		RemapFile = remapFile;
	}

	// The requests a new worker is sent before it takes traffic: after the application restarted, only with
	// doAppInitAfterRestart. Without pages, preloadEnabled sends one for the application's root.
	public IReadOnlyList<(string Page, string? HostName)> PagesFor(bool afterApplicationRestart)
	{
		if (afterApplicationRestart && !DoAppInitAfterRestart) return [];
		if (Pages.Count > 0) return Pages;
		return PreloadEnabled ? [("/", null)] : [];
	}

	// The site's folder is the parent of the application's output folder (bin_dotnet), as the worker's PhysicalPath is.
	public static ApplicationInitialization Read(string siteDirectory, HostSettings settings, ILogger logger)
	{
		var pages = new List<(string, string?)>();
		bool afterRestart = false;
		string remap = "";
		var file = WebConfig(siteDirectory);
		if (file != null)
		{
			try
			{
				var configuration = XDocument.Load(file).Root;
				if (configuration != null)
				{
					// The section at the application's level: <system.webServer> and <location path="."> (or without a path), in
					// order, each one's attributes overriding the one before and its <add>, <remove> and <clear> changing the list.
					foreach (var section in Sections(configuration))
					{
						if (Bool(section.Attribute("doAppInitAfterRestart")) is bool value) afterRestart = value;
						if (section.Attribute("remapManagedRequestsTo") is { } attribute) remap = attribute.Value;
						foreach (var element in section.Elements())
						{
							var page = (string?)element.Attribute("initializationPage") ?? "";
							switch (element.Name.LocalName)
							{
								case "add":
									pages.RemoveAll(p => string.Equals(p.Item1, Normalize(page), StringComparison.OrdinalIgnoreCase));
									pages.Add((Normalize(page), NullIfEmpty((string?)element.Attribute("hostName"))));
									break;
								case "remove":
									pages.RemoveAll(p => string.Equals(p.Item1, Normalize(page), StringComparison.OrdinalIgnoreCase));
									break;
								case "clear":
									pages.Clear();
									break;
							}
						}
					}
				}
			}
			catch (Exception e) when (e is XmlException || e is IOException || e is UnauthorizedAccessException)
			{
				// The application shows a Web.config error on its pages, as IIS did.
				logger.LogWarning("Couldn't read <applicationInitialization> from {File}: {Message}", file, e.Message);
			}
		}
		if (settings.InitializationPages != null)
			pages = settings.InitializationPages.Where(p => p.InitializationPage.Length != 0)
				.Select(p => (Normalize(p.InitializationPage), NullIfEmpty(p.HostName))).ToList();
		if (settings.DoAppInitAfterRestart is bool configured) afterRestart = configured;
		if (settings.RemapManagedRequestsTo != null) remap = settings.RemapManagedRequestsTo;
		string? remapFile = null;
		if (remap.Length != 0)
		{
			var path = Path.GetFullPath(Path.Combine(siteDirectory, remap.TrimStart('/', '~').Replace('/', Path.DirectorySeparatorChar)));
			if (path.StartsWith(Path.GetFullPath(siteDirectory), StringComparison.Ordinal)) remapFile = path;
		}
		return new ApplicationInitialization(pages, afterRestart, settings.PreloadEnabled, remapFile);
	}

	static IEnumerable<XElement> Sections(XElement configuration)
	{
		foreach (var element in configuration.Elements())
		{
			if (element.Name.LocalName == "system.webServer")
				foreach (var section in element.Elements("applicationInitialization")) yield return section;
			else if (element.Name.LocalName == "location" && ((string?)element.Attribute("path") ?? "") is "" or ".")
				foreach (var webServer in element.Elements("system.webServer"))
					foreach (var section in webServer.Elements("applicationInitialization")) yield return section;
		}
	}

	// Web.config, whatever the case of its name (off Windows the file system tells them apart; IIS's didn't).
	static string? WebConfig(string directory)
	{
		if (!Directory.Exists(directory)) return null;
		return Directory.EnumerateFiles(directory).FirstOrDefault(f => Path.GetFileName(f).Equals("web.config", StringComparison.OrdinalIgnoreCase));
	}

	// A page relative to the application's root, as IIS takes "default.aspx" for "/default.aspx".
	static string Normalize(string page) => page.StartsWith('/') ? page : "/" + page.TrimStart('~', '/');

	static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

	static bool? Bool(XAttribute? attribute) => attribute == null ? null : bool.TryParse(attribute.Value, out var value) ? value : null;
}
