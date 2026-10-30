#if NETCOREAPP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Configuration;
using System.Xml.Linq;
using static WebFormsForCore.Hosting.IIS.WebServerConfiguration;

namespace WebFormsForCore.Hosting.IIS
{
	// webForCore: <system.webServer>'s managed modules, handlers and URL authorization in System.Web's classic pipeline.
	// On IIS an integrated-mode application lists its managed modules and handlers there (the template web.configs do);
	// here the pipeline is classic (system.web/httpModules and httpHandlers), and these are merged in, as IIS merged
	// ASP.NET's modules into the one integrated list:
	//   <modules>: entries with a managed type are added after system.web's; <remove> and <clear> apply to the merged list.
	//     Native modules (no type) are IIS's and are ignored.
	//   <handlers>: the first entry with a managed type that matches the request's file name (or path) and verb is used
	//     before system.web/httpHandlers. TransferRequestHandler, IIS's integrated-pipeline-only handler
	//     (ExtensionlessUrlHandler-Integrated-4.0), is skipped: routing remaps extensionless URLs before handler mapping.
	//   <security><authorization>: IIS URL Authorization, at AuthorizeRequest (IisUrlAuthorizationModule).
	// In use only when the hosting layer runs IIS's modules (Configuration is set by AspNetCoreHost.CreateForMiddleware).
	public static class WebServerModules
	{
		public static WebServerConfiguration Configuration { get; set; }

		static string AppRelative(string virtualPath)
		{
			var app = HttpRuntime.AppDomainAppVirtualPath ?? "/";
			if (app.Length > 1 && virtualPath.StartsWith(app, StringComparison.OrdinalIgnoreCase))
				virtualPath = virtualPath.Substring(app.TrimEnd('/').Length);
			return virtualPath.Length == 0 ? "/" : virtualPath;
		}

		internal static void Apply(HttpModuleCollection modules)
		{
			var config = Configuration;
			if (config == null) return;
			var section = Element(config.GetSection("/"), "modules");
			if (section != null)
			{
				foreach (var item in section.Elements())
				{
					var name = Attr(item, "name");
					switch (item.Name.LocalName)
					{
						case "clear":
							modules.ClearModules();
							break;
						case "remove":
							if (name != null) modules.RemoveModule(name);
							break;
						case "add":
							var type = Attr(item, "type");
							if (name == null || type == null || modules.Get(name) != null) break;
							modules.AddModule(name, new HttpModuleAction(name, type).Entry.Create());
							break;
					}
				}
			}
			modules.AddModule("webForCore.IisUrlAuthorization", new IisUrlAuthorizationModule());
		}

		internal static HttpHandlerAction FindHandler(string verb, string virtualPath)
		{
			var config = Configuration;
			if (config == null) return null;
			var path = AppRelative(virtualPath);
			var fileName = path.Substring(path.LastIndexOf('/') + 1);
			foreach (var handler in Items(config.GetSection(path), "handlers"))
			{
				var pattern = Attr(handler, "path");
				if (pattern == null || !VerbMatches(Attr(handler, "verb", "*"), verb)) continue;
				if (!PathMatches(pattern, pattern.Contains('/') ? path.TrimStart('/') : fileName)) continue;
				var type = Attr(handler, "type");
				// The first match decides; a native handler (StaticFile, ISAPI) leaves it to system.web's mappings.
				if (type == null || type.StartsWith("System.Web.Handlers.TransferRequestHandler", StringComparison.Ordinal)) return null;
				return new HttpHandlerAction(pattern, type, Attr(handler, "verb", "*"), false);
			}
			return null;
		}

		static bool VerbMatches(string verbs, string verb) =>
			verbs.Trim() == "*" || verbs.Split(',').Any(v => string.Equals(v.Trim(), verb, StringComparison.OrdinalIgnoreCase));

		// IIS handler paths: '*' and '?' wildcards; "*." is a name without an extension.
		static bool PathMatches(string pattern, string value)
		{
			if (pattern == "*.") return !value.Contains('.');
			return Regex.IsMatch(value, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
		}

		// IIS URL Authorization (<system.webServer><security><authorization>): a matching Deny rule denies, otherwise a
		// matching Allow rule allows, otherwise access is denied; applicationHost.config's default is <add accessType="Allow"
		// users="*"/>. Unlike ASP.NET's <authorization>, rule order does not matter. A denied request gets 401, which forms
		// authentication turns into its login redirect, as with ASP.NET's UrlAuthorizationModule.
		sealed class IisUrlAuthorizationModule : IHttpModule
		{
			public void Init(HttpApplication application) => application.AuthorizeRequest += OnAuthorizeRequest;
			public void Dispose() { }

			static void OnAuthorizeRequest(object sender, EventArgs e)
			{
				var application = (HttpApplication)sender;
				var context = application.Context;
				var config = Configuration;
				if (config == null || context.SkipAuthorization) return;
				var rules = Items(config.GetSection(AppRelative(context.Request.Path)), "security/authorization").ToList();
				var user = context.User;
				bool anonymous = user?.Identity == null || !user.Identity.IsAuthenticated;
				bool Matches(XElement rule)
				{
					var verbs = Attr(rule, "verbs");
					if (!string.IsNullOrEmpty(verbs) && !VerbMatches(verbs, context.Request.HttpMethod)) return false;
					var users = (Attr(rule, "users") ?? "").Split(',').Select(u => u.Trim()).Where(u => u.Length > 0);
					var roles = (Attr(rule, "roles") ?? "").Split(',').Select(r => r.Trim()).Where(r => r.Length > 0);
					return users.Any(u => u == "*" || u == "?" && anonymous
							|| !anonymous && string.Equals(u, user.Identity.Name, StringComparison.OrdinalIgnoreCase))
						|| !anonymous && roles.Any(r => user.IsInRole(r));
				}
				bool denied = rules.Any(r => string.Equals(Attr(r, "accessType"), "Deny", StringComparison.OrdinalIgnoreCase) && Matches(r));
				bool allowed = !denied && rules.Any(r => string.Equals(Attr(r, "accessType"), "Allow", StringComparison.OrdinalIgnoreCase) && Matches(r));
				if (!allowed)
				{
					context.Response.StatusCode = 401; // IIS: 401.2 (HttpResponse.SubStatusCode is integrated-pipeline only)
					application.CompleteRequest();
				}
			}
		}
	}
}
#endif
