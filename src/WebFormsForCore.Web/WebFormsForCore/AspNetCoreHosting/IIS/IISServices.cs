#if NETCOREAPP
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static WebFormsForCore.Hosting.IIS.WebServerConfiguration;

namespace WebFormsForCore.Hosting.IIS
{
	// webForCore: the parts of IIS that ASP.NET Core has middleware for, configured the IIS way. services.AddWebForms()
	// registers them and app.UseWebForms() then runs them in front of the application:
	//   compression: <urlCompression doStaticCompression="true" doDynamicCompression="false"/> and <httpCompression>'s
	//     staticTypes/dynamicTypes and minFileSizeForComp, with gzip (IIS's default scheme);
	//   W3C logging: IIS's default fields, one file a day, in LogDirectory (IIS: %SystemDrive%\inetpub\logs\LogFiles\W3SVC1);
	//     sc-substatus and sc-win32-status are not available.
	// Service managers (systemd, Windows services, containers) are the deployment's business: webForCore has no code for
	// them (#56).
	public sealed class WebFormsServicesOptions
	{
		public bool Compression { get; set; } = true;
		public bool W3CLogging { get; set; } = true;
		// Outside the site, as IIS keeps its logs (#15): logs/<site> next to the site folder (the parent of the build
		// output, bin_dotnet), unless set. It used to be <site>/logs/W3SVC1, which the site served: client addresses and
		// every URL with its query string.
		public string LogDirectory { get; set; } = DefaultLogDirectory();

		internal static string DefaultLogDirectory()
		{
			var site = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
			var parent = Path.GetDirectoryName(site) ?? site;
			return Path.Combine(parent, "logs", Path.GetFileName(site), "W3SVC1");
		}
	}

	// The application's <system.webServer> configuration, for the services (set by WebFormsMiddleware).
	public sealed class WebServerConfigurationAccessor
	{
		public WebServerConfiguration Configuration { get; set; }
		public WebFormsServicesOptions Options { get; set; }
	}

	public static class WebFormsServiceCollectionExtensions
	{
		public static IServiceCollection AddWebForms(this IServiceCollection services, Action<WebFormsServicesOptions> configure = null)
		{
			var options = new WebFormsServicesOptions();
			configure?.Invoke(options);
			services.TryAddSingleton(new WebServerConfigurationAccessor { Options = options });
			if (options.Compression)
			{
				services.AddResponseCompression(o =>
				{
					o.EnableForHttps = true;
					o.Providers.Add<GzipCompressionProvider>();
				});
				services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
				services.Replace(ServiceDescriptor.Singleton<IResponseCompressionProvider, IISCompressionProvider>());
			}
			if (options.W3CLogging)
			{
				services.AddW3CLogging(o =>
				{
					o.LoggingFields = W3CLoggingFields.Date | W3CLoggingFields.Time | W3CLoggingFields.ServerIpAddress | W3CLoggingFields.Method
						| W3CLoggingFields.UriStem | W3CLoggingFields.UriQuery | W3CLoggingFields.ServerPort | W3CLoggingFields.UserName
						| W3CLoggingFields.ClientIpAddress | W3CLoggingFields.UserAgent | W3CLoggingFields.Referer
						| W3CLoggingFields.ProtocolStatus | W3CLoggingFields.TimeTaken;
					o.LogDirectory = Path.GetFullPath(options.LogDirectory);
					o.FileName = "u_ex";
					o.FlushInterval = TimeSpan.FromSeconds(1);
				});
			}
			return services;
		}

		// Called by UseWebForms: the registered services, in IIS's order (logging sees the final response).
		internal static void UseWebFormsServices(IApplicationBuilder app)
		{
			var accessor = app.ApplicationServices.GetService<WebServerConfigurationAccessor>();
			if (accessor == null) return;
			if (accessor.Options.W3CLogging) app.UseW3CLogging();
			if (accessor.Options.Compression) app.UseResponseCompression();
		}
	}

	// Decides compression as IIS's static and dynamic compression modules do.
	sealed class IISCompressionProvider : IResponseCompressionProvider
	{
		readonly ResponseCompressionProvider inner;
		readonly WebServerConfigurationAccessor accessor;

		public IISCompressionProvider(IServiceProvider services, IOptions<ResponseCompressionOptions> options, WebServerConfigurationAccessor accessor)
		{
			inner = new ResponseCompressionProvider(services, options);
			this.accessor = accessor;
		}

		public ICompressionProvider GetCompressionProvider(HttpContext context) => inner.GetCompressionProvider(context);
		public bool CheckRequestAcceptsCompression(HttpContext context) => inner.CheckRequestAcceptsCompression(context);

		public bool ShouldCompressResponse(HttpContext context)
		{
			var config = accessor.Configuration;
			if (config == null || context.Response.Headers.ContainsKey("Content-Range")) return false;
			var section = config.GetSection(context.Request.Path.Value ?? "/");
			var url = Element(section, "urlCompression");
			bool isStatic = context.Items.ContainsKey(IISModules.IsStaticKey);
			if (!Bool(url, isStatic ? "doStaticCompression" : "doDynamicCompression", isStatic)) return false;
			if (isStatic && context.Response.ContentLength < Long(Element(section, "httpCompression"), "minFileSizeForComp", 2700)) return false;
			var contentType = context.Response.ContentType;
			if (string.IsNullOrEmpty(contentType)) return false;
			var mime = contentType.Split(';')[0].Trim();
			// The first matching entry decides, "*/*" last as in applicationHost.config.
			foreach (var type in Items(section, isStatic ? "httpCompression/staticTypes" : "httpCompression/dynamicTypes"))
			{
				var pattern = Attr(type, "mimeType") ?? "";
				var parts = pattern.Split('/');
				var mimeParts = mime.Split('/');
				if (parts.Length == 2 && mimeParts.Length == 2
					&& (parts[0] == "*" || string.Equals(parts[0], mimeParts[0], StringComparison.OrdinalIgnoreCase))
					&& (parts[1] == "*" || string.Equals(parts[1], mimeParts[1], StringComparison.OrdinalIgnoreCase)))
					return Bool(type, "enabled", true);
			}
			return false;
		}
	}

}
#endif
