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
		// System.Configuration is the framework's facade that .NET Framework libraries bind to (Version=4.0.0.0); it
		// forwards to System.Configuration.ConfigurationManager, the fork's, which is per domain. Loaded in the default
		// context, its forwards resolved there too, to a second ConfigurationManager that can't read web.config (#1).
		public static readonly List<string> PerDomainFrameworkAssemblies = new List<string> { "Microsoft.CSharp", "Microsoft.VisualBasic.Core", "System.Configuration" };

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
				// Kept as text: an exception's stack trace keeps the domain's code alive (see "Fields" below).
				var errors = domain.GetType().GetField(nameof(unloadErrors), BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(domain) as List<Exception>;
				if (errors != null) lock (errors) errors.Add(new Exception(e.ToString()));
			}
		}

		// Fields: once unloading starts, the runtime holds the context through a strong handle until the domain's code is
		// collected (AssemblyLoadContext.InitiateUnload). A field of the context that references anything of the domain (an
		// assembly, a type, an object, an exception thrown by its code) closes that cycle and the domain is never
		// collected, so the fields below hold only shared-framework objects, or the domain's weakly.
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
		public int ReleaseProcessResources() => DisposeDefaultMemoryCache() + ReleaseDefaultProxy() + ReleaseTimers() + ReleaseNetworkChangeSubscribers() + ReleaseProcessEventHandlers() + ReleaseSystemWebHandles() + ReleaseFrameworkCaches();

		// <system.net><defaultProxy><module> makes HttpClient.DefaultProxy, which is process-wide, an instance of the
		// application's type (WebFormsForCore.Net.Configuration.NetConfiguration); .NET's default proxy is restored.
		public int ReleaseDefaultProxy()
		{
			if (!References(System.Net.Http.HttpClient.DefaultProxy)
				|| AppContext.GetData("WebFormsForCore.OriginalDefaultProxy") is not System.Net.IWebProxy original) return 0;
			System.Net.Http.HttpClient.DefaultProxy = original;
			return 1;
		}

		// System.Runtime.Caching is loaded into each domain, and a MemoryCache keeps its stores and statistics timer in strong
		// GC handles that only Dispose frees. .NET Framework's MemoryCache disposed itself on DomainUnload; .NET's no longer
		// subscribes, so the domain's MemoryCache.Default (MVC's model metadata and output cache, ObjectCache users) is
		// disposed here if it was created. Caches the application creates itself must be disposed by it (Application_End).
		// Only while the domain still lists its assemblies (when it starts unloading).
		public int DisposeDefaultMemoryCache()
		{
			var caching = Assemblies.FirstOrDefault(a => a.GetName().Name == "System.Runtime.Caching");
			var field = caching?.GetType("System.Runtime.Caching.MemoryCache")?.GetField("s_defaultCache", BindingFlags.Static | BindingFlags.NonPublic);
			if (field?.GetValue(null) is not IDisposable cache) return 0;
			cache.Dispose();
			return 1;
		}

		// Static caches of the shared framework that hold types (or objects) they were used with, strongly: entries for the
		// domain's types are removed, as AppDomain.Unload discarded the domain's copy of the cache. (declaring type, cache
		// field (a static field, then instance fields separated by '.'), lock field or null for a ConcurrentDictionary).
		// Runtime internals, read by reflection; a missing field is skipped.
		static readonly (string Type, string Cache, string Lock)[] FrameworkCaches = {
			// GetDefaultAttribute's cache of each attribute type's default (TypeDescriptor attribute lookups).
			("System.ComponentModel.AttributeCollection, System.ComponentModel.TypeConverter", "s_defaultAttributes", "s_internalSyncObject"),
			// [MetadataType] (buddy classes): DataAnnotations' caches of metadata types, member attributes and validation.
			("System.ComponentModel.DataAnnotations.AssociatedMetadataTypeTypeDescriptor+TypeDescriptorCache, System.ComponentModel.Annotations", "s_metadataTypeCache", null),
			("System.ComponentModel.DataAnnotations.AssociatedMetadataTypeTypeDescriptor+TypeDescriptorCache, System.ComponentModel.Annotations", "s_typeMemberCache", null),
			("System.ComponentModel.DataAnnotations.AssociatedMetadataTypeTypeDescriptor+TypeDescriptorCache, System.ComponentModel.Annotations", "s_validatedMetadataTypeCache", null),
			// DataContractSerializer (Web API's XML formatter, WCF clients): built-in contract lookups of every type. Its
			// ContextAwareDictionary keeps collectible types weakly, but tells them by their assembly, so a generic type of
			// the framework over a domain type (IEnumerable<Product>) is kept in the strong half.
			("System.Runtime.Serialization.DataContracts.DataContract+DataContractCriticalHelper, System.Private.DataContractSerialization", "s_typeToBuiltInContract._fastDictionary", null),
			// [OnSerializing]/[OnDeserialized] methods of each serialized type.
			("System.Runtime.Serialization.SerializationEventsCache, System.Runtime.Serialization.Formatters", "s_cache", null),
			// DataTable columns of application types.
			("System.Data.Common.DataStorage, System.Data.Common", "s_typeImplementsInterface", null),
			("System.Data.Common.SqlUdtStorage, System.Data.Common", "s_typeToNull", null),
		};

		public int ReleaseFrameworkCaches()
		{
			int released = 0;
			foreach (var (typeName, cacheName, lockName) in FrameworkCaches)
			{
				const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
				var type = Type.GetType(typeName, throwOnError: false);
				var path = cacheName.Split('.');
				object value = type?.GetField(path[0], AnyStatic)?.GetValue(null);
				for (int i = 1; i < path.Length && value != null; i++)
					value = value.GetType().GetField(path[i], AnyInstance)?.GetValue(value);
				if (value is not System.Collections.IDictionary cache) continue;
				var sync = lockName != null ? type.GetField(lockName, AnyStatic)?.GetValue(null) ?? cache : cache;
				lock (sync)
				{
					var keys = new List<object>();
					foreach (System.Collections.DictionaryEntry entry in cache)
					{
						if (References(entry.Key) || References(entry.Value)) keys.Add(entry.Key);
					}
					foreach (var key in keys) cache.Remove(key);
					released += keys.Count;
				}
			}
			released += ReleaseDataContracts();
			// DbProviderFactories' registrations keep the factory instance the domain resolved.
			released += System.Web.Configuration.DbProviderFactoriesConfiguration.ResetRegistrations(factory => References(factory));
			return released;
		}

		// DataContractSerializer numbers each type it meets (s_typeToIDCache, by type handle) and keeps its contract in a
		// table by that number, weakly for collectible types; but the same assembly test as above keeps the contracts of
		// generic types over domain types (List<Product>) strongly. Their slots are cleared, and their numbers forgotten
		// so that a type handle reused after the domain is collected cannot find a slot. Locks taken in the serializer's order.
		int ReleaseDataContracts()
		{
			const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic;
			var helper = Type.GetType("System.Runtime.Serialization.DataContracts.DataContract+DataContractCriticalHelper, System.Private.DataContractSerialization", throwOnError: false);
			var index = helper?.GetField("s_dataContractCache", AnyStatic)?.GetValue(null);
			var contracts = index?.GetType().GetField("_contracts", AnyInstance)?.GetValue(index) as Array;
			var ids = helper?.GetField("s_typeToIDCache", AnyStatic)?.GetValue(null) as System.Collections.IDictionary;
			var createLock = helper?.GetField("s_createDataContractLock", AnyStatic)?.GetValue(null);
			var cacheLock = helper?.GetField("s_cacheLock", AnyStatic)?.GetValue(null);
			var underlyingType = typeof(System.Runtime.Serialization.DataContracts.DataContract).GetProperty("UnderlyingType");
			var elementType = contracts?.GetType().GetElementType();
			var strong = elementType?.GetField("Item1");
			if (contracts == null || ids == null || createLock == null || cacheLock == null || underlyingType == null || strong == null) return 0;
			var released = new HashSet<int>();
			lock (createLock)
			{
				lock (cacheLock)
				{
					// The table grows by replacement: read it again under the lock.
					contracts = (Array)index.GetType().GetField("_contracts", AnyInstance).GetValue(index);
					for (int i = 0; i < contracts.Length; i++)
					{
						if (strong.GetValue(contracts.GetValue(i)) is object contract && References(underlyingType.GetValue(contract)))
						{
							contracts.SetValue(Activator.CreateInstance(elementType), i);
							released.Add(i);
						}
					}
					if (released.Count > 0)
					{
						var keys = new List<object>();
						foreach (System.Collections.DictionaryEntry entry in ids)
							if (released.Contains((int)entry.Value)) keys.Add(entry.Key);
						foreach (var key in keys) ids.Remove(key);
					}
				}
			}
			return released.Count;
		}

		// An object of the domain or one of its types, or a tuple (a composite cache key) with one.
		bool References(object value)
		{
			if (value is Type type) return IsInDomain(type);
			if (value is MemberInfo member) return IsInDomain(member.DeclaringType);
			if (value is System.Runtime.CompilerServices.ITuple tuple)
			{
				for (int i = 0; i < tuple.Length; i++)
					if (References(tuple[i])) return true;
			}
			return value != null && IsInDomain(value.GetType());
		}

		// The process-wide event holders: static events of AppContext and AssemblyLoadContext, and the events stored on the
		// process's AppDomain (DomainUnload, which .NET never raises, ...) and on the default load context (Resolving, ...).
		static (Type Type, object Instance)[] ProcessEventHolders => new (Type, object)[] {
			(typeof(AppContext), null), (typeof(AssemblyLoadContext), null),
			(typeof(AppDomain), AppDomain.CurrentDomain), (typeof(AssemblyLoadContext), Default),
		};

		public int ReleaseProcessEventHandlers()
		{
			int released = 0;
			foreach (var (type, instance) in ProcessEventHolders)
			{
				var flags = (instance == null ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic;
				foreach (var field in type.GetFields(flags))
				{
					if (!typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
					for (int attempt = 0; attempt < 10; attempt++)
					{
						var current = (Delegate)field.GetValue(instance);
						if (current == null) break;
						var handlers = current.GetInvocationList();
						var kept = handlers.Where(h => !IsInDomain(h.Method.DeclaringType) && !IsInDomain(h.Target?.GetType())).ToArray();
						if (kept.Length == handlers.Length) break;
						var replacement = Delegate.Combine(kept);
						// The events are updated with Interlocked.CompareExchange; do the same.
						lock (field)
						{
							if (field.GetValue(instance) != (object)current) continue;
							field.SetValue(instance, replacement);
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
			var release = SystemWeb?.GetType("System.Web.Util.GCUtil")?.GetMethod("ReleaseDomainHandles", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
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

		// NetworkChange keeps each NetworkAddressChanged and NetworkAvailabilityChanged handler with the ExecutionContext
		// it was added in (WebProxy adds one the first time it checks for a local address, during a request): handlers of
		// the domain are removed, and contexts that captured the domain are dropped (the handler then runs without one, as
		// when flow is suppressed). Runtime internals, read by reflection; missing fields release nothing.
		static readonly Type NetworkChangeType = Type.GetType("System.Net.NetworkInformation.NetworkChange, System.Net.NetworkInformation");

		public int ReleaseNetworkChangeSubscribers()
		{
			var gate = NetworkChangeType?.GetField("s_gate", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
			if (gate == null) return 0;
			int released = 0;
			lock (gate)
			{
				foreach (var name in new[] { "s_addressChangedSubscribers", "s_availabilityChangedSubscribers" })
				{
					if (NetworkChangeType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is not System.Collections.IDictionary subscribers) continue;
					foreach (var handler in subscribers.Keys.Cast<Delegate>().ToList())
					{
						if (IsInDomain(handler.Method.DeclaringType) || IsInDomain(handler.Target?.GetType()))
						{
							subscribers.Remove(handler);
							released++;
						}
						else if (subscribers[handler] is ExecutionContext context && CapturesDomain(context))
						{
							subscribers[handler] = null;
							released++;
						}
					}
				}
			}
			return released;
		}

		// A type of the domain, or a type built from one (a generic instantiation over it, an array of it): those are
		// collectible with the domain although their definition (IEnumerable<T>, T[]) is the shared framework's.
		bool IsInDomain(Type type)
		{
			if (type == null) return false;
			if (GetLoadContext(type.Assembly) == this) return true;
			if (!type.IsCollectible) return false;
			if (type.HasElementType) return IsInDomain(type.GetElementType());
			return type.IsGenericType && type.GetGenericArguments().Any(IsInDomain);
		}

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
			if (type.Assembly == typeof(PseudoAppDomain).Assembly) systemWeb = new WeakReference<Assembly>(assembly);
			return assembly.GetType(type.FullName, throwOnError: true);
		}

		// The domain's System.Web (an unloading context no longer lists its assemblies); weak, see "Fields" above.
		WeakReference<Assembly> systemWeb;
		Assembly SystemWeb => systemWeb != null && systemWeb.TryGetTarget(out var assembly) ? assembly : null;

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
