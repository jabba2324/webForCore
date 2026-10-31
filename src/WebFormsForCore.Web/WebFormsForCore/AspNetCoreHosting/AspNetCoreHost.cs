#if NETCOREAPP

using System;
using System.Globalization;
using System.Security.Permissions;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Loader;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Web;
using System.Web.Hosting;
using Core = Microsoft.AspNetCore.Http;

namespace System.Web.Hosting
{
    public class AspNetCoreHost : MarshalByRefObject, IRegisteredObject
    {
        private bool disableDirectoryListing;

        private string installPath;

        private string lowerCasedClientScriptPathWithTrailingSlash;

        private string lowerCasedVirtualPath;

        private string lowerCasedVirtualPathWithTrailingSlash;

        private volatile int pendingCallsCount;
        // webForCore: the requests in progress, for Stop(immediate: true) to abort.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Core.HttpContext, byte> requests =
            new System.Collections.Concurrent.ConcurrentDictionary<Core.HttpContext, byte>();
        private int unregistered;

        private string physicalClientScriptPath;

        private string physicalPath;

        private int port;

        private bool requireAuthentication;

		private IntPtr processToken;

		private string processUser;

        private string virtualPath;
        public bool HandleAllRequestsWithWebForms { get; set; } = false;
		public string[] HandleExtensions { get; set; } = new string[] { ".aspx", ".ashx", ".asmx", ".axd" };
        // webForCore: only files System.Web knows nothing about. The machine.config maps the others as .NET Framework's root
        // web.config does (HttpForbiddenHandler for *.cs, *.vb, *.resx, *.master, *.asax, *.ascx, *.config, ...: 403;
        // *.cshtml and *.vbhtml served by Web Pages when it is enabled), so they go to System.Web as on IIS.
        public string[] ProhibitedExtensions { get; set; } = new string[] { ".razor", "appsettings.json", ".deps.json", ".runtimeconfig.json" };
        // webForCore: IIS request filtering's default hidden segments (answered 404), plus .NET's build folder. The app's
        // output folder (bin_dotnet) is hidden too (IsHiddenSegment). Without this, with HandleAllRequestsWithWebForms,
        // System.Web's static file handler served the app's assemblies and build files.
        public string[] HiddenSegments { get; set; } = new string[] { "bin", "App_Code", "App_GlobalResources", "App_LocalResources",
            "App_WebReferences", "App_Data", "App_Browsers", "obj" };
        public string[] DefaultDocuments { get; set; } = new string[] { "index.htm", "index.html", "Index.html", "Index.htm", "Default.htm", "iistart.htm", "Default.aspx", "default.aspx" };
        public bool UseClassicMode = false;
        public Compilation.ClientBuildManager ClientBuildManager { get; set; } 
		public AppDomain AppDomain
        {
            get { return AppDomain.CurrentDomain; }
        }
		public AssemblyLoadContext LoadContext
		{
			get { return ApplicationManager.GetLoadContext(Assembly.GetCallingAssembly()); }
		}

		public AspNetCoreHost()
        {
            HostingEnvironment.RegisterObject(this);

        }

        public bool DisableDirectoryListing
        {
            get { return disableDirectoryListing; }
        }

        public string InstallPath
        {
            get { return installPath; }
        }

        public string NormalizedClientScriptPath
        {
            get { return lowerCasedClientScriptPathWithTrailingSlash; }
        }

        public string NormalizedVirtualPath
        {
            get { return lowerCasedVirtualPathWithTrailingSlash; }
        }

        public string PhysicalClientScriptPath
        {
            get { return physicalClientScriptPath; }
        }

        public string PhysicalPath
        {
            get { return physicalPath; }
        }

        public int Port
        {
            get { return port; }
        }

        public bool RequireAuthentication
        {
            get { return requireAuthentication; }
        }

        public string VirtualPath
        {
            get { return virtualPath; }
        }

        public HostingEnvironment HostingEnvironment { get; set; }

        // webForCore: called once when the hosting environment starts shutting the application down.
        public Action OnShutdown;

        // webForCore: creates the host for WebFormsMiddleware in the domain this copy of System.Web is loaded in (a
        // PseudoAppDomain, or the middleware's own context). Parameters and results are shared-framework types, so that
        // the middleware's copy of System.Web can call it in another domain: the host is returned as delegates.
        internal static object[] CreateForMiddleware(string virtualPath, string physicalPath, string[] handleExtensions,
            string[] addHandleExtensions, string[] defaultDocuments, string[] addDefaultDocuments, bool handleAllRequestsWithWebForms,
            bool useClassicMode, bool useAspNetCoreSession, bool iisModules, Action onShutdown)
        {
            AssemblyLoaderNetCore.Init();
            if (iisModules) WebFormsForCore.Hosting.IIS.WebServerModules.Configuration = new WebFormsForCore.Hosting.IIS.WebServerConfiguration(physicalPath);
            if (useAspNetCoreSession) SessionState.SessionStateModule.UseAspNetCoreSessionProvider();

            var applicationManager = new ApplicationManager();
            // create BuildManagerHost in the worker app domain
            Type buildManagerHostType = typeof(HttpRuntime).Assembly.GetType("System.Web.Compilation.BuildManagerHost");
            var appId = ApplicationManager.CreateApplicationId(virtualPath, physicalPath);
            var vpath = System.Web.VirtualPath.Create(virtualPath);
            IRegisteredObject buildManagerHost = applicationManager.CreateInstanceInNewWorkerLoadContext(buildManagerHostType, appId, vpath, physicalPath, false) as IRegisteredObject;
            // call BuildManagerHost.RegisterAssembly to make Host type loadable in the worker app domain
            var hostType = typeof(AspNetCoreHost);
            buildManagerHostType.InvokeMember("RegisterAssembly", BindingFlags.Instance | BindingFlags.InvokeMethod | BindingFlags.NonPublic,
                null, buildManagerHost, new object[] { hostType.Assembly.FullName, hostType.Assembly.Location });
            // create Host in the worker app domain
            var host = (AspNetCoreHost)applicationManager.CreateObject(appId, hostType, virtualPath, physicalPath, false);

            if (handleExtensions != null) host.HandleExtensions = handleExtensions;
            if (addHandleExtensions.Length > 0) host.HandleExtensions = host.HandleExtensions.Concat(addHandleExtensions).ToArray();
            if (defaultDocuments != null) host.DefaultDocuments = defaultDocuments;
            if (addDefaultDocuments.Length > 0) host.DefaultDocuments = host.DefaultDocuments.Concat(addDefaultDocuments).ToArray();
            host.HandleAllRequestsWithWebForms = handleAllRequestsWithWebForms;
            host.UseClassicMode = useClassicMode;
            host.OnShutdown = onShutdown;
            host.Configure(virtualPath, physicalPath);

            return new object[] {
                new Func<Core.HttpContext, bool>(host.IsLegacyRequest),
                new Func<Core.HttpContext, Task>(host.ProcessRequest),
                new Action(host.Shutdown),
                new Func<string, bool>(host.RouteClaims),
            };
        }

        // webForCore: whether one of the application's routes takes the URL, for the IIS modules in front of it. On IIS,
        // UrlRoutingModule maps a request to its route's handler before the native modules' courtesy redirect of a
        // directory to its trailing slash, which runs only for the static file handler; RouteTable.GetRouteData skips
        // existing files and directories unless RouteExistingFiles is set, as on IIS (#13). The routes are those of the
        // started application: Application_Start runs with the first request it processes.
        public bool RouteClaims(string virtualPath)
        {
            var routes = System.Web.Routing.RouteTable.Routes;
            if (!routes.RouteExistingFiles || routes.Count == 0) return false;
            try
            {
                var request = new HttpRequest(string.Empty, "http://localhost" + virtualPath, string.Empty);
                var context = new HttpContextWrapper(new HttpContext(request, new HttpResponse(System.IO.TextWriter.Null)));
                return routes.GetRouteData(context) != null;
            }
            catch (Exception)
            {
                return false; // a route that can't be evaluated outside a request leaves the URL to IIS's behaviour
            }
        }

        #region IRegisteredObject Members

        void IRegisteredObject.Stop(bool immediate)
        {
            // webForCore: tell the middleware first, so that new requests go to a new domain while this one drains.
            Interlocked.Exchange(ref OnShutdown, null)?.Invoke();

            // webForCore: the hosting environment calls Stop(false), waits up to <hostingEnvironment shutdownTimeout> (30 s)
            // for registered objects to unregister, then calls Stop(true) and unloads the domain. This waited for the
            // requests in progress here, without limit, so the timeout never applied and one long request (a WebSocket, a
            // long poll) kept the old domain alive as long as its client stayed. Now Stop(false) unregisters once the
            // requests finish, and Stop(true) aborts those left, as AppDomain.Unload did on IIS.
            if (immediate)
            {
                foreach (var context in requests.Keys)
                {
                    try { context.Abort(); } catch { }
                }
                Unregister();
            }
            else if (pendingCallsCount <= 0) Unregister();
            else
            {
                Task.Run(() =>
                {
                    WaitForPendingCallsToFinish();
                    Unregister();
                });
            }
        }

        // webForCore: only unregister, as a registered object does (Cassini's Host). This called HttpRuntime.Close(),
        // which marked the runtime as shutting down, so that the hosting environment's shutdown, which called Stop,
        // then skipped releasing the runtime and unloading the domain (HttpRuntime.ShutdownAppDomain).
        private void Unregister()
        {
            if (Interlocked.Exchange(ref unregistered, 1) == 0) HostingEnvironment.UnregisterObject(this);
        }

        #endregion

        public void Configure(string virtualPath, string physicalPath,
                              bool requireAuthentication)
        {
            Configure(virtualPath, physicalPath, requireAuthentication, false);
        }

        public void Configure(string virtualPath, string physicalPath)
        {
            Configure(virtualPath, physicalPath, false, false);
        }

        public void Configure(string virtualPath, string physicalPath,
                              bool requireAuthentication, bool disableDirectoryListing)
        {
            installPath = null;
            if (!virtualPath.StartsWith('/')) virtualPath = "/" + virtualPath;
            this.virtualPath = virtualPath;
            this.requireAuthentication = requireAuthentication;
            this.disableDirectoryListing = disableDirectoryListing;
            lowerCasedVirtualPath = CultureInfo.InvariantCulture.TextInfo.ToLower(virtualPath);
            lowerCasedVirtualPathWithTrailingSlash = virtualPath.EndsWith("/", StringComparison.Ordinal)
                                                          ? virtualPath
                                                          : virtualPath + "/";
            lowerCasedVirtualPathWithTrailingSlash =
                CultureInfo.InvariantCulture.TextInfo.ToLower(lowerCasedVirtualPathWithTrailingSlash);
            this.physicalPath = physicalPath;

			var assembly = GetType().Assembly;
            ApplicationManager.SetLoadContextData(".appPath", physicalPath, assembly);
			ApplicationManager.SetLoadContextData(".appVPath", virtualPath, assembly);

			physicalClientScriptPath = HttpRuntime.AspClientScriptPhysicalPath + Path.DirectorySeparatorChar;
            lowerCasedClientScriptPathWithTrailingSlash =
                CultureInfo.InvariantCulture.TextInfo.ToLower(HttpRuntime.AspClientScriptVirtualPath + "/");

        }

		private void ObtainProcessToken()
		{
			/*
			if (Interop.ImpersonateSelf(2))
			{
				Interop.OpenThreadToken(Interop.GetCurrentThread(), 0xf01ff, true, ref processToken);
				Interop.RevertToSelf();
				// ReSharper disable PossibleNullReferenceException
				// ReSharper restore PossibleNullReferenceException
			} */
		    processUser = WindowsIdentity.GetCurrent().Name;
		}


		public SecurityIdentifier GetProcessSid() => WindowsIdentity.GetCurrent().User;
        public IntPtr GetProcessToken() => WindowsIdentity.GetCurrent().Token;
        public string GetProcessUser() => WindowsIdentity.GetCurrent().Name;

        public override object InitializeLifetimeService()
        {
            // never expire the license
            return null;
        }

        public bool IsVirtualPathAppPath(string path)
        {
            if (path == null)
            {
                return false;
            }
            path = CultureInfo.InvariantCulture.TextInfo.ToLower(path);
            return (path == lowerCasedVirtualPath || path == lowerCasedVirtualPathWithTrailingSlash);
        }

        public bool IsVirtualPathInApp(string path, out bool isClientScriptPath)
        {
            isClientScriptPath = false;

            if (path == null)
            {
                return false;
            }

            if (virtualPath == "/" && path.StartsWith("/", StringComparison.Ordinal))
            {
                if (path.StartsWith(lowerCasedClientScriptPathWithTrailingSlash, StringComparison.Ordinal))
                {
                    isClientScriptPath = true;
                }
                return true;
            }

            path = CultureInfo.InvariantCulture.TextInfo.ToLower(path);

            if (path.StartsWith(lowerCasedVirtualPathWithTrailingSlash, StringComparison.Ordinal))
            {
                return true;
            }

            if (path == lowerCasedVirtualPath)
            {
                return true;
            }

            if (path.StartsWith(lowerCasedClientScriptPathWithTrailingSlash, StringComparison.Ordinal))
            {
                isClientScriptPath = true;
                return true;
            }

            return false;
        }

        public string MapPath(string path) => HostingEnvironment.MapPath(path);

        // webForCore: see HiddenSegments. Any segment of the path, compared ignoring case, as IIS does.
        bool IsHiddenSegment(string path)
        {
            string output = Path.GetFileName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (HiddenSegments.Any(hidden => string.Equals(hidden, segment, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(output, segment, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        public bool IsLegacyRequest(Core.HttpContext context)
        {
            var path = context.Request.Path.Value;
            var pathWithoutSlash = path;
            if (path.EndsWith('/')) pathWithoutSlash = path[..^1];
            if (path == "") path = "/";
            if (path != "/") path = pathWithoutSlash;

			var fullpath = $"{context.Request.PathBase.Value.TrimEnd('/')}/{path}";

            if (IsHiddenSegment(path)) return false;

			if (HandleExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) )) 
                return true;
            if (HandleExtensions.Any(ext => path.LastIndexOf('/') is var index and > 0 && path[..index].EndsWith(ext, StringComparison.OrdinalIgnoreCase))) 
                return true;
            if (ProhibitedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return false;
            // webForCore: ASP.NET Core's environment settings files (appsettings.Production.json, ...), not only appsettings.json.
            string fileName = path[(path.LastIndexOf('/') + 1)..];
            if (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            var mappedPath = MapPath(fullpath);
            if (Directory.Exists(mappedPath))
            {
				var defaultDoc = DefaultDocuments
                    .Select(doc => new { Mapped = Path.Combine(mappedPath, doc), Virtual = $"{pathWithoutSlash}/{doc}" })
                    .FirstOrDefault(doc => File.Exists(doc.Mapped));
                if (defaultDoc != null)
                {
                    context.Request.Path = defaultDoc.Virtual;
                    return true;
                }
            }

			return HandleAllRequestsWithWebForms || File.Exists(mappedPath) &&
                Path.GetDirectoryName(mappedPath) != AppDomain.CurrentDomain.BaseDirectory; // Do not serve bin_dotnet directory
		}

		public bool IsVirtualPathInApp(String path)
        {
            bool isClientScriptPath;
            return IsVirtualPathInApp(path, out isClientScriptPath);
        }

        public async Task ProcessRequest(Core.HttpContext context)
        {
            AddPendingCall();
            requests.TryAdd(context, 0);
            try
            {
                if (UseClassicMode) HttpRuntime.SetClassicPipeline();

                await new AspNetCoreWorkerRequest(this, context).Process();
            }
            finally
            {
                // webForCore: also when the request throws; Stop waits for the count to reach 0.
                requests.TryRemove(context, out _);
                RemovePendingCall();
            }
        }

        [SecurityPermission(SecurityAction.Assert, Unrestricted = true)]
        public void Shutdown()
        {
            HostingEnvironment.InitiateShutdown();
        }

        private void AddPendingCall()
        {
            //TODO: investigate this issue - ref var not volitile
#pragma warning disable 0420
            Interlocked.Increment(ref pendingCallsCount);
#pragma warning restore 0420
        }

        private void RemovePendingCall()
        {
            //TODO: investigate this issue - ref var not volitile
#pragma warning disable 0420
            Interlocked.Decrement(ref pendingCallsCount);
#pragma warning restore 0420
        }

        private void WaitForPendingCallsToFinish()
        {
            for (; ; )
            {
                if (pendingCallsCount <= 0 || unregistered != 0)
                {
                    break;
                }

                Thread.Sleep(250);
            }
        }
    }
}

#endif