// webForCore: System.AppDomainManager, which .NET does not have, written from its documented public API (MIT license).
// System.Web's AspNetAppDomainManager derives from it; there is one domain per load context on .NET, so CreateDomain
// returns the current one.
#if NETCOREAPP
using System.Reflection;
using System.Security;
using System.Security.Policy;
using System.Threading;

namespace System
{
	public class AppDomainManager : MarshalByRefObject
	{
		public AppDomainManager()
		{
		}

		public virtual HostSecurityManager HostSecurityManager => null;

		// .NET has no host execution context manager to fall back on.
		public virtual HostExecutionContextManager HostExecutionContextManager => throw new NotSupportedException();

		public virtual Assembly EntryAssembly => Assembly.GetEntryAssembly();

		public virtual AppDomain CreateDomain(string friendlyName, Evidence securityInfo, AppDomainSetup appDomainInfo) =>
			CreateDomainHelper(friendlyName, securityInfo, appDomainInfo);

		protected static AppDomain CreateDomainHelper(string friendlyName, Evidence securityInfo, AppDomainSetup appDomainInfo) =>
			AppDomain.CurrentDomain;

		public virtual void InitializeNewDomain(AppDomainSetup appDomainInfo)
		{
		}

		public virtual bool CheckSecuritySettings(SecurityState state) => false;
	}
}
#endif
