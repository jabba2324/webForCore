#if NETCOREAPP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading;

namespace System.Web.Hosting
{
	// webForCore: .NET has no AppDomains (AppDomain.CreateDomain throws PlatformNotSupportedException). A PseudoAppDomain
	// stands in for the worker AppDomain ASP.NET creates for an application (ApplicationHost.CreateApplicationHost,
	// WebFormsMiddleware): a collectible AssemblyLoadContext into which System.Web, the application's assemblies and the
	// other assemblies it deploys are loaded again. Their static state (HttpRuntime, HostingEnvironment, BuildManager,
	// caches, configuration, the application's own statics) belongs to the domain, as in an AppDomain, and is released
	// when the domain is unloaded. The shared framework (Microsoft.NETCore.App, Microsoft.AspNetCore.App) is shared, as
	// .NET Framework shared the GAC'd framework domain-neutral, and so are the assemblies named in SharedAssemblies.
	//
	// Differences from an AppDomain: there is no remoting, so an object created in a domain is the domain's own type and
	// is reached through reflection, dynamic, a delegate or an interface from the shared framework, not through a
	// transparent proxy; process-wide state (AppDomain.CurrentDomain.SetData, environment, current directory) is shared;
	// and unloading is cooperative: it completes once nothing outside the domain references its objects.
	public class PseudoAppDomain : AssemblyLoadContext
	{
		// Shared with the creating context besides the shared framework (names; a trailing '*' matches a prefix). Test
		// frameworks are shared so that their assertion exceptions keep their type across the boundary.
		public static readonly List<string> SharedAssemblies = new List<string> {
			"nunit.framework", "nunit.framework.legacy", "xunit*", "Microsoft.TestPlatform*", "Microsoft.VisualStudio.TestPlatform*", "testhost*"
		};

		// Shared-framework assemblies loaded into each domain all the same: their static caches hold the types they are
		// used with (the C# dynamic binder's symbol table, the VB late binder), which would keep unloaded domains alive.
		public static readonly List<string> PerDomainFrameworkAssemblies = new List<string> { "Microsoft.CSharp", "Microsoft.VisualBasic.Core" };

		static readonly string SharedFrameworkRoot = GetSharedFrameworkRoot();
		static readonly Lazy<Dictionary<string, string>> TrustedPlatformAssemblies = new Lazy<Dictionary<string, string>>(() =>
			((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
				.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
				.GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));

		// The shared framework is at <dotnet>/shared/<framework>/<version>. A self-contained app carries the framework in
		// its own directory, where it cannot be told from the app's assemblies; PseudoAppDomains are not available there.
		static string GetSharedFrameworkRoot()
		{
			var framework = Path.GetDirectoryName(typeof(object).Assembly.Location);
			var shared = Path.GetDirectoryName(Path.GetDirectoryName(framework));
			return shared != null && string.Equals(Path.GetFileName(shared), "shared", StringComparison.OrdinalIgnoreCase)
				? shared + Path.DirectorySeparatorChar : null;
		}

		public static bool IsSupported => SharedFrameworkRoot != null;

		// The domain an assembly was loaded in, or null when it is not in a PseudoAppDomain.
		public static AssemblyLoadContext GetDomain(Assembly assembly)
		{
			var context = GetLoadContext(assembly);
			return context != null && context.GetType().FullName == typeof(PseudoAppDomain).FullName ? context : null;
		}

		// Runs the action when the domain the assembly is loaded in starts unloading: for handlers System.Web adds to
		// process-wide events, which would otherwise keep the domain from being collected. Nothing outside a domain.
		// Each action runs on its own: the runtime raises Unloading as one multicast delegate, which stops at the first
		// exception. Failures are kept for the host (UnloadErrors).
		public static void OnUnload(Assembly assembly, Action action)
		{
			var domain = GetDomain(assembly);
			if (domain != null) domain.Unloading += _ => RunUnloadAction(domain, action);
		}

		static void RunUnloadAction(AssemblyLoadContext domain, Action action)
		{
			try
			{
				action();
			}
			catch (Exception e)
			{
				// The domain object is this assembly's PseudoAppDomain type only in the creating context; reach the list by name.
				var errors = domain.GetType().GetField(nameof(unloadErrors), BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(domain) as List<Exception>;
				if (errors != null) lock (errors) errors.Add(e);
			}
		}

		readonly List<Exception> unloadErrors = new List<Exception>();
		public IReadOnlyList<Exception> UnloadErrors { get { lock (unloadErrors) return unloadErrors.ToArray(); } }

		public string ApplicationBase { get; }
		public string FriendlyName => Name;

		public PseudoAppDomain(string friendlyName, string applicationBase) : base(friendlyName, isCollectible: true)
		{
			if (!IsSupported)
				throw new PlatformNotSupportedException("webForCore: PseudoAppDomains need a framework-dependent app (the shared framework could not be located).");
			ApplicationBase = applicationBase?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? AppContext.BaseDirectory;
			Unloading += domain => RunUnloadAction(domain, () => ReleaseProcessResources());
		}

		// AppDomain.Unload stopped the domain's timers with it. A collectible context is collected only once nothing
		// references it, and the runtime's timer queues reference every active timer: the domain's own timers (System.Web's
		// cache, request timeout, idle and memory monitors, the application's) are disposed, and timers of the shared
		// framework that captured the domain's ExecutionContext (its contextual reflection scope) lose that context.
		// The queues are runtime internals, read by reflection; if they change, nothing is released and unloading is
		// reported as incomplete (WebFormsMiddleware). Called when the domain starts unloading and while it is not collected.
		static readonly Type TimerQueueType = typeof(Timer).Assembly.GetType("System.Threading.TimerQueue");
		static readonly Type TimerQueueTimerType = typeof(Timer).Assembly.GetType("System.Threading.TimerQueueTimer");
		const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		// Everything process-wide that references the domain and that AppDomain.Unload discarded: timers (below), the
		// handlers its code added to process-wide events (AppDomain's AssemblyLoad, AssemblyResolve, UnhandledException,
		// ProcessExit/DomainUnload, FirstChanceException...; on .NET these are static events of AppContext and
		// AssemblyLoadContext), and the GC handles System.Web allocated (System.Web.Util.GCUtil). Returns the number released.
		public int ReleaseProcessResources() => ReleaseTimers() + ReleaseProcessEventHandlers() + ReleaseSystemWebHandles();

		static readonly Type[] ProcessEventTypes = { typeof(AppContext), typeof(AssemblyLoadContext) };

		public int ReleaseProcessEventHandlers()
		{
			int released = 0;
			foreach (var type in ProcessEventTypes)
			{
				foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
					for (int attempt = 0; attempt < 10; attempt++)
					{
						var current = (Delegate)field.GetValue(null);
						if (current == null) break;
						var handlers = current.GetInvocationList();
						var kept = handlers.Where(h => !IsInDomain(h.Method.DeclaringType) && !IsInDomain(h.Target?.GetType())).ToArray();
						if (kept.Length == handlers.Length) break;
						var replacement = Delegate.Combine(kept);
						// The events are updated with Interlocked.CompareExchange; do the same.
						lock (field)
						{
							if (field.GetValue(null) != (object)current) continue;
							field.SetValue(null, replacement);
						}
						released += handlers.Length - kept.Length;
						break;
					}
				}
			}
			return released;
		}

		public int ReleaseSystemWebHandles()
		{
			var release = systemWeb?.GetType("System.Web.Util.GCUtil")?.GetMethod("ReleaseDomainHandles", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			return release != null ? (int)release.Invoke(null, null) : 0;
		}

		public int ReleaseTimers()
		{
			var instances = TimerQueueType?.GetProperty("Instances", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			var sharedLock = TimerQueueType?.GetProperty("SharedLock", AnyInstance);
			var getTimers = TimerQueueType?.GetMethod("GetTimersForDebugger", AnyInstance);
			var callbackField = TimerQueueTimerType?.GetField("_timerCallback", AnyInstance);
			var contextField = TimerQueueTimerType?.GetField("_executionContext", AnyInstance);
			var dispose = TimerQueueTimerType?.GetMethod("Dispose", AnyInstance, Type.EmptyTypes);
			if (instances == null || sharedLock == null || getTimers == null || callbackField == null || contextField == null || dispose == null) return 0;

			var timers = new List<object>();
			foreach (var queue in (Array)instances.GetValue(null))
			{
				if (queue == null) continue;
				using (((Lock)sharedLock.GetValue(queue)).EnterScope())
				{
					foreach (var timer in (IEnumerable<object>)getTimers.Invoke(queue, null)) timers.Add(timer);
				}
			}
			int released = 0;
			foreach (var timer in timers)
			{
				var callback = (Delegate)callbackField.GetValue(timer);
				if (callback != null && (IsInDomain(callback.Method.DeclaringType) || IsInDomain(callback.Target?.GetType())))
				{
					dispose.Invoke(timer, null);
					released++;
				}
				else if (contextField.GetValue(timer) is ExecutionContext context && CapturesDomain(context))
				{
					contextField.SetValue(timer, null);
					released++;
				}
			}
			return released;
		}

		bool IsInDomain(Type type) => type != null && GetLoadContext(type.Assembly) == this;

		bool CapturesDomain(ExecutionContext context)
		{
			AssemblyLoadContext captured = null;
			ExecutionContext.Run(context, _ => captured = CurrentContextualReflectionContext, null);
			return captured == this;
		}

		static bool IsShared(string name) => SharedAssemblies.Any(shared => shared.EndsWith('*')
			? name.StartsWith(shared[..^1], StringComparison.OrdinalIgnoreCase)
			: string.Equals(name, shared, StringComparison.OrdinalIgnoreCase));

		protected override Assembly Load(AssemblyName assemblyName)
		{
			var name = assemblyName.Name;
			if (string.IsNullOrEmpty(name) || IsShared(name)) return null;
			// The app's dependencies as the host resolved them (deps.json): the shared framework is shared, the rest is
			// loaded into the domain from where the default context would load it.
			if (TrustedPlatformAssemblies.Value.TryGetValue(name, out var path))
			{
				return path.StartsWith(SharedFrameworkRoot, StringComparison.OrdinalIgnoreCase)
					&& !PerDomainFrameworkAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase) ? null : LoadFromAssemblyPath(path);
			}
			// Otherwise the host's output folder (the app's build: bin_dotnet), then the application's bin directory and base,
			// where .NET Framework's AppDomain looked (a bin folder can hold the app's .NET Framework build).
			foreach (var dir in new[] { AppContext.BaseDirectory, Path.Combine(ApplicationBase, "bin"), ApplicationBase })
			{
				var file = Path.Combine(dir, name + ".dll");
				if (File.Exists(file)) return LoadFromAssemblyPath(file);
			}
			return null;
		}

		// The domain's copy of a type of the creating context.
		public Type GetDomainType(Type type)
		{
			var assembly = LoadFromAssemblyName(type.Assembly.GetName());
			if (type.Assembly == typeof(PseudoAppDomain).Assembly) systemWeb = assembly;
			return assembly.GetType(type.FullName, throwOnError: true);
		}

		// The domain's System.Web (an unloading context no longer lists its assemblies).
		Assembly systemWeb;

		// Calls a static method of the domain's copy of a type; arguments and result cross the boundary unchanged, so they
		// must be types the two sides share (the shared framework's) or types already taken from the domain.
		public object InvokeStatic(Type type, string method, params object[] args)
		{
			var domainType = GetDomainType(type);
			var target = domainType.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new MissingMethodException(domainType.FullName, method);
			using (EnterContextualReflection())
			{
				try
				{
					return target.Invoke(null, args);
				}
				catch (TargetInvocationException e) when (e.InnerException != null)
				{
					ExceptionDispatchInfo.Capture(e.InnerException).Throw();
					throw;
				}
			}
		}

		// AppDomain.CreateInstanceAndUnwrap: creates an instance of the domain's copy of the type.
		public object CreateInstanceAndUnwrap(string assemblyName, string typeName)
		{
			using (EnterContextualReflection())
			{
				var type = LoadFromAssemblyName(new AssemblyName(assemblyName)).GetType(typeName, throwOnError: true);
				return Activator.CreateInstance(type);
			}
		}

		public override string ToString() => $"PseudoAppDomain {Name} ({ApplicationBase})";
	}
}
#endif
