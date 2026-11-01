// ASP.NET Core middleware

#if NETCOREAPP

using Microsoft.AspNetCore.Builder;
using Core = Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;
using System.Web;
using System.Web.Hosting;
using System.Web.SessionState;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using WebFormsForCore.Hosting.IIS;

namespace Microsoft.AspNetCore.Builder
{
	public class WebFormsMiddleware
	{
		public string VirtualPath { get; set; }
		public string PhysicalPath { get; set; }
		public string AppId { get; set; }
		public bool HandleAllRequestsWithWebForms = false;
		public bool UseClassicMode = true;
		public IEnumerable<string> HandleExtensions = null;
		public IEnumerable<string> AddHandleExtensions = Array.Empty<string>();
		public IEnumerable<string> DefaultDocuments = null;
		public IEnumerable<string> AddDefaultDocuments = Array.Empty<string>();
		public bool UseAspNetCoreSession = false;
		// webForCore: run the application in a PseudoAppDomain, as IIS ran it in a worker AppDomain: the application
		// restarts in-process (web.config, bin or App_Code changes, HttpRuntime.UnloadAppDomain, recycling) with fresh
		// static state. Without it, a restart stops the process, for the service manager to start it again.
		public bool IsolateApplication = PseudoAppDomain.IsSupported;
		public readonly RecyclingOptions Recycling = new RecyclingOptions();
		// webForCore: IIS's modules in front of the application, from <system.webServer> (IIS/IISModules.cs).
		public readonly IISModules.Options IIS = new IISModules.Options();
		IISModules iisModules;

		private readonly Core.RequestDelegate next;
		private readonly IHostApplicationLifetime lifetime;
		private readonly ILogger logger;
		private readonly object sync = new object();
		private readonly Timer recycleTimer;
		private volatile Application current;
		private volatile bool stopping;
		private int generation;
		// Domains shutting down, until they are collected: (start, domain, checks so far).
		private readonly List<(int Generation, WeakReference Domain, int Checks, DateTime Since)> unloading = new List<(int, WeakReference, int, DateTime)>();
		private int stuckDomains;

		// One start of the application: its domain (null without isolation) and the host's entry points in it.
		sealed class Application
		{
			public AssemblyLoadContext Domain;
			public Func<Core.HttpContext, bool> IsLegacyRequest;
			public Func<Core.HttpContext, Task> ProcessRequest;
			public Action Shutdown;
			public Func<string, bool> RouteClaims;
			public DateTime Started = DateTime.UtcNow;
			public long Requests;
			public int Generation;
			public int Retired;
		}

		// IIS application pool recycling (applicationHost.config <recycling><periodicRestart>), with IIS's defaults.
		public class RecyclingOptions
		{
			// <periodicRestart time="1.05:00:00"/>; TimeSpan.Zero disables.
			public TimeSpan Time { get; set; } = TimeSpan.FromMinutes(1740);
			// <periodicRestart requests="0"/>; 0 disables.
			public long Requests { get; set; }
			// <periodicRestart privateMemory="0"/> in KB, the process's private bytes; 0 disables.
			public long PrivateMemory { get; set; }
			// webForCore: earlier starts that could not be unloaded (still referenced 5 minutes after their shutdown) before
			// the process itself is stopped, for the service manager to start it again (tools/deploy/webforcore@.service);
			// 0 never stops it.
			public int MaxStuckDomains { get; set; } = 3;
			// webForCore: how often recycling conditions are checked and unloading domains collected, released and reported.
			public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
		}

		// An earlier start still referenced this long after its shutdown is reported, and counts towards MaxStuckDomains.
		static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(5);

		public WebFormsMiddleware(Core.RequestDelegate next, Action<WebFormsOptions> optionsBuilder,
			IHostApplicationLifetime lifetime = null, ILoggerFactory loggerFactory = null, IWebHostEnvironment environment = null,
			WebServerConfigurationAccessor webServerAccessor = null)
		{
			this.next = next;
			this.lifetime = lifetime;
			logger = (ILogger)loggerFactory?.CreateLogger<WebFormsMiddleware>() ?? NullLogger.Instance;
			var path = AppDomain.CurrentDomain.BaseDirectory;
			if (path.EndsWith(Path.DirectorySeparatorChar.ToString())) path = path.Substring(0, path.Length - 1);
			PhysicalPath = Path.GetDirectoryName(path);
			VirtualPath = "/";
			// app.UseAspNetCoreSessionProvider() sets the provider before UseWebForms.
			UseAspNetCoreSession = SessionStateModule.ProviderOverride != null;

			optionsBuilder?.Invoke(new WebFormsOptions(this));

			AppId = ApplicationManager.CreateApplicationId(VirtualPath, PhysicalPath);
			if (IIS.Enabled)
			{
				var webServer = new WebServerConfiguration(PhysicalPath);
				// The application's output folder (bin_dotnet) and .NET's build folder are not part of the site.
				webServer.ExcludedDirectories.Add(Path.GetFileName(path));
				webServer.ExcludedDirectories.Add("obj");
				// W3C logs an application keeps in the site are never served (#15).
				var services = webServerAccessor?.Options;
				if (services != null && services.W3CLogging)
				{
					var logs = Path.GetRelativePath(PhysicalPath, Path.GetFullPath(services.LogDirectory));
					if (!logs.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(logs) && logs != ".")
						webServer.ExcludedDirectories.Add(logs.Replace(Path.DirectorySeparatorChar, '/') + "/");
				}
				iisModules = new IISModules(webServer, IIS, loggerFactory, environment);
				iisModules.RouteClaims = virtualPath => GetApplication().RouteClaims?.Invoke(virtualPath) ?? false;
				if (webServerAccessor != null) webServerAccessor.Configuration = webServer;
			}
			lifetime?.ApplicationStopping.Register(() =>
			{
				stopping = true;
				var app = current;
				if (app != null) InDomain(app, app.Shutdown);
			});
			if (IsolateApplication && OperatingSystem.IsWindows()) StartProcessWideWaits();
			GetApplication();
			recycleTimer = new Timer(_ => CheckRecycling(), null, Recycling.CheckInterval, Recycling.CheckInterval);
		}

		// On Windows, two process-wide listeners of the shared framework start on first use and keep the ExecutionContext
		// they were started on in a registered wait: HttpClient's system proxy (HttpWindowsProxy, for changes to the proxy
		// settings) and NetworkChange (address changes; WebProxy subscribes the first time it checks for a local address).
		// Started during a request, they keep that request's domain alive after a restart (#46), so the host starts them
		// first, without a context, and keeps them.
		static int processWideWaits;
		static void StartProcessWideWaits()
		{
			if (Interlocked.Exchange(ref processWideWaits, 1) != 0) return;
			using (ExecutionContext.SuppressFlow())
			{
				_ = System.Net.Http.HttpClient.DefaultProxy;
				System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (sender, e) => { };
			}
		}

		Application GetApplication()
		{
			var app = current;
			if (app != null) return app;
			lock (sync)
			{
				return current ??= StartApplication();
			}
		}

		Application StartApplication()
		{
			var app = new Application { Generation = ++generation };
			if (IsolateApplication)
			{
				app.Domain = new PseudoAppDomain($"{AppId}-{app.Generation}", PhysicalPath);
				app.Domain.Unloading += _ => logger.LogInformation("Application {AppId} (start {Generation}) is unloading.", AppId, app.Generation);
			}
			Action onShutdown = () => OnApplicationShutdown(app);
			var args = new object[] { VirtualPath, PhysicalPath, HandleExtensions?.ToArray(), AddHandleExtensions.ToArray(),
				DefaultDocuments?.ToArray(), AddDefaultDocuments.ToArray(), HandleAllRequestsWithWebForms, UseClassicMode,
				UseAspNetCoreSession, IIS.Enabled, onShutdown };
			object[] host;
			try
			{
				// On a clean ExecutionContext: the timers and threads the application creates capture it, and the caller's
				// (an old domain's shutdown, a request) would keep that domain or request alive.
				Task<object[]> create;
				using (ExecutionContext.SuppressFlow())
				{
					create = Task.Run(() => app.Domain is PseudoAppDomain domain
						? (object[])domain.InvokeStatic(typeof(AspNetCoreHost), nameof(AspNetCoreHost.CreateForMiddleware), args)
						: AspNetCoreHost.CreateForMiddleware((string)args[0], (string)args[1], (string[])args[2], (string[])args[3],
							(string[])args[4], (string[])args[5], HandleAllRequestsWithWebForms, UseClassicMode, UseAspNetCoreSession, IIS.Enabled, onShutdown));
				}
				host = create.GetAwaiter().GetResult();
			}
			catch
			{
				app.Domain?.Unload();
				throw;
			}
			app.IsLegacyRequest = (Func<Core.HttpContext, bool>)host[0];
			app.ProcessRequest = (Func<Core.HttpContext, Task>)host[1];
			app.Shutdown = (Action)host[2];
			app.RouteClaims = (Func<string, bool>)host[3];
			logger.LogInformation("Started application {AppId} (start {Generation}{Domain}).", AppId, app.Generation,
				app.Domain != null ? ", in a PseudoAppDomain" : "");
			return app;
		}

		// The application's hosting environment is shutting down (file change, UnloadAppDomain, recycling, idle).
		void OnApplicationShutdown(Application app)
		{
			if (Interlocked.Exchange(ref app.Retired, 1) != 0) return;
			lock (sync)
			{
				if (current == app) current = null;
			}
			logger.LogInformation("Application {AppId} (start {Generation}) is shutting down.", AppId, app.Generation);
			if (stopping) return;
			if (app.Domain != null)
				lock (unloading) unloading.Add((app.Generation, new WeakReference(app.Domain), 0, DateTime.UtcNow));
			if (app.Domain == null)
			{
				// No domain to replace it with: the process is the unit of restart, as a worker process is for IIS.
				logger.LogWarning("Application {AppId} restarted without IsolateApplication: stopping the process.", AppId);
				lifetime?.StopApplication();
				return;
			}
			// Overlapped recycle: the next start is ready while the old domain drains its requests.
			Task.Run(() =>
			{
				try { GetApplication(); }
				catch (Exception e) { logger.LogError(e, "Restarting application {AppId} failed.", AppId); }
			});
		}

		void Recycle(Application app, string reason)
		{
			if (app == null || app.Retired != 0) return;
			logger.LogInformation("Recycling application {AppId} (start {Generation}): {Reason}.", AppId, app.Generation, reason);
			InDomain(app, app.Shutdown);
		}

		// The number of earlier starts whose domain is not collected yet.
		public int UnloadingDomains { get { lock (unloading) return unloading.Count; } }

		// A domain unloads once nothing references it; one still referenced StuckAfter its shutdown is reported.
		void CheckUnloading()
		{
			lock (unloading)
			{
				if (unloading.Count == 0) return;
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				for (int i = unloading.Count - 1; i >= 0; i--)
				{
					var (start, domain, checks, since) = unloading[i];
					if (domain.Target is PseudoAppDomain pseudo)
					{
						// Network change subscriptions too: a request still running when the domain started unloading can add one (#46).
						int timers = pseudo.ReleaseTimers(), handlers = pseudo.ReleaseProcessEventHandlers() + pseudo.ReleaseNetworkChangeSubscribers(),
							handles = pseudo.ReleaseSystemWebHandles(), caches = pseudo.ReleaseFrameworkCaches();
						if (timers + handlers + handles + caches > 0)
							logger.LogDebug("Application {AppId} (start {Generation}): released {Timers} timers, {Handlers} event handlers, {Handles} GC handles, {Caches} cache entries.",
								AppId, start, timers, handlers, handles, caches);
						if (checks == 0)
							foreach (var error in pseudo.UnloadErrors)
								logger.LogWarning(error, "Application {AppId} (start {Generation}): releasing its resources failed.", AppId, start);
					}
					if (!domain.IsAlive)
					{
						logger.LogInformation("Application {AppId} (start {Generation}) unloaded.", AppId, start);
						unloading.RemoveAt(i);
					}
					else if (++checks > 0 && DateTime.UtcNow - since >= StuckAfter)
					{
						logger.LogWarning("Application {AppId} (start {Generation}) has not unloaded: objects outside its domain still reference it.", AppId, start);
						unloading.RemoveAt(i);
						if (Recycling.MaxStuckDomains > 0 && ++stuckDomains >= Recycling.MaxStuckDomains && !stopping)
						{
							logger.LogWarning("{Count} earlier starts of application {AppId} are still in memory: stopping the process.", stuckDomains, AppId);
							lifetime?.StopApplication();
						}
					}
					else unloading[i] = (start, domain, checks, since);
				}
			}
		}

		void CheckRecycling()
		{
			CheckUnloading();
			var app = current;
			if (app == null || stopping) return;
			if (Recycling.Time > TimeSpan.Zero && DateTime.UtcNow - app.Started >= Recycling.Time)
				Recycle(app, $"periodic restart after {Recycling.Time}");
			else if (Recycling.PrivateMemory > 0)
			{
				using var process = System.Diagnostics.Process.GetCurrentProcess();
				if (process.PrivateMemorySize64 / 1024 >= Recycling.PrivateMemory)
					Recycle(app, $"private memory {process.PrivateMemorySize64 / 1024} KB over the limit of {Recycling.PrivateMemory} KB");
			}
		}

		static void InDomain(Application app, Action action)
		{
			if (app.Domain == null) { action(); return; }
			using (app.Domain.EnterContextualReflection()) action();
		}

		public void RestartApplication() => Recycle(current, "restart requested");

		public void AllowSynchronousIO(Core.HttpContext context)
		{
			var syncIoFeature = context.Features.Get<Core.Features.IHttpBodyControlFeature>();
			if (syncIoFeature != null)
			{
				syncIoFeature.AllowSynchronousIO = true;
			}
		}

		public Task Invoke(Core.HttpContext context) =>
			iisModules != null ? iisModules.Invoke(context, InvokeApplication) : InvokeApplication(context);

		async Task InvokeApplication(Core.HttpContext context)
		{
			var app = GetApplication();
			// Code that resolves types by name (Type.GetType, Assembly.Load) finds the domain's assemblies.
			var scope = app.Domain != null ? app.Domain.EnterContextualReflection() : default;
			try
			{
				if (app.IsLegacyRequest(context))
				{
					AllowSynchronousIO(context);
					var requests = Interlocked.Increment(ref app.Requests);
					await app.ProcessRequest(context);
					if (Recycling.Requests > 0 && requests == Recycling.Requests)
						Recycle(app, $"{requests} requests");
				}
				else
				{
					scope.Dispose();
					scope = default;
					await next.Invoke(context);
				}
			}
			finally
			{
				scope.Dispose();
			}
		}

		public virtual bool IsLegacyRequest(Core.HttpContext context) => GetApplication().IsLegacyRequest(context);
	}

	public class WebFormsOptions
	{
		WebFormsMiddleware Instance;

		public WebFormsOptions(WebFormsMiddleware instance) { Instance = instance; }
		public WebFormsOptions HandleExtensions(params string[] extensions) { Instance.HandleExtensions = extensions; return this; }
		public WebFormsOptions AddHandleExtensions(params string[] extensions) { Instance.AddHandleExtensions = Instance.AddHandleExtensions.Concat(extensions).ToArray(); return this; }
		public WebFormsOptions DefaultDocuments(params string[] docs) { Instance.DefaultDocuments = docs; return this; }
		public WebFormsOptions AddDefaultDocuments(params string[] docs) { Instance.AddDefaultDocuments = Instance.AddDefaultDocuments.Concat(docs).ToArray(); return this; }
		public WebFormsOptions VirtualPath(string path) {
			if (!path.StartsWith('/')) path = "/" + path;
			Instance.VirtualPath = path;
			return this;
		}
		public WebFormsOptions PhysicalPath(string path) { Instance.PhysicalPath = path; return this; }
		public WebFormsOptions HandleAllRequestsWithWebForms() { Instance.HandleAllRequestsWithWebForms = true; return this; }

        public WebFormsOptions UseClassicMode() { Instance.UseClassicMode = true; return this; }
        public WebFormsOptions UseIntegratedMode() { Instance.UseClassicMode = false; return this; }
        public WebFormsOptions UseAspNetCoreSessionProvider()
        {
            // Equivalent to a web.config entry of:
            // <sessionState mode="Custom" customProvider="AspNetCoreSession">
            //     <providers>
            //         <add name="AspNetCoreSession" type="System.Web.SessionState.AspNetCoreSessionProvider, System.Web" />
            //     </providers>
            // </sessionState>
            // Set in the application's domain when it starts (AspNetCoreHost.CreateForMiddleware).
            Instance.UseAspNetCoreSession = true;
            return this;
        }
        // webForCore: see WebFormsMiddleware.IsolateApplication.
        public WebFormsOptions IsolateApplication(bool isolate = true) { Instance.IsolateApplication = isolate; return this; }
        public WebFormsOptions Recycling(Action<WebFormsMiddleware.RecyclingOptions> configure) { configure(Instance.Recycling); return this; }
        // webForCore: see WebFormsMiddleware.IIS; IIS(o => o.Enabled = false) leaves <system.webServer> to the host.
        public WebFormsOptions IIS(Action<IISModules.Options> configure) { configure(Instance.IIS); return this; }
    }

    public static class WebFormsMiddlewareExtensions
	{
		public static IApplicationBuilder UseWebForms(this IApplicationBuilder builder, Action<WebFormsOptions> optionsBuilder = null)
		{
			AssemblyLoaderNetCore.Init();
			if (optionsBuilder == null) optionsBuilder = options => { };
			// webForCore: W3C logging and compression, when services.AddWebForms() registered them (IIS/IISServices.cs).
			WebFormsServiceCollectionExtensions.UseWebFormsServices(builder);
			// webForCore: HttpContext.AcceptWebSocketRequest needs ASP.NET Core's WebSocket handshake (IIS's WebSocket module).
			builder.UseWebSockets();
			return builder.UseMiddleware<WebFormsMiddleware>(optionsBuilder);
        }
        public static IApplicationBuilder UseAspNetCoreSessionProvider(this IApplicationBuilder builder)
        {
            AssemblyLoaderNetCore.Init();

            // Equivalent to a web.config entry of:
            // <sessionState mode="Custom" customProvider="AspNetCoreSession">
            //     <providers>
            //         <add name="AspNetCoreSession" type="System.Web.SessionState.AspNetCoreSessionProvider, System.Web" />
            //     </providers>
            // </sessionState>
            SessionStateModule.UseAspNetCoreSessionProvider();

			return builder;
        }

    }
}

#endif