// webForCore: System.Runtime.Remoting.Messaging.CallContext, which .NET does not have, written from its documented
// public API (MIT license). Values live in one store per asynchronous flow (AsyncLocal), shared by the "logical" and
// "illogical" accessors, as WebFormsForCore's System.Web expects; .NET Remoting's headers are not supported.
#if !NETFRAMEWORK
using System.Collections.Concurrent;
using System.Security.Principal;
using System.Threading;

namespace System.Runtime.Remoting.Messaging
{
	[Serializable]
	public sealed class CallContext
	{
		private static readonly AsyncLocal<ConcurrentDictionary<string, object>> s_data = new AsyncLocal<ConcurrentDictionary<string, object>>();

		private CallContext()
		{
		}

		private static ConcurrentDictionary<string, object> Data => s_data.Value ??= new ConcurrentDictionary<string, object>();

		public static object HostContext
		{
			get => LogicalGetData("CallContext.HostContext");
			set => Data["CallContext.HostContext"] = value;
		}

		internal static IPrincipal Principal
		{
			get => (IPrincipal)LogicalGetData("CallContext.Principal") ?? Thread.CurrentPrincipal;
			set => Data["CallContext.Principal"] = value;
		}

		public static object GetData(string name) => LogicalGetData(name);

		public static void SetData(string name, object data) => Data[name] = data;

		public static object LogicalGetData(string name) => Data.TryGetValue(name, out object value) ? value : null;

		public static void LogicalSetData(string name, object data) => Data[name] = data;

		public static void FreeNamedDataSlot(string name) => Data.TryRemove(name, out _);
	}
}
#endif
