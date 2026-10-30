// webForCore: written from Microsoft.Web.Infrastructure's documented public API. MIT license.
using System;
using System.Web;

namespace Microsoft.Web.Infrastructure.DynamicModuleHelper
{
	// Registers an IHttpModule from code, during application start (PreApplicationStartMethod), as if it were listed in
	// web.config. MVC, Web Pages, Web Optimization and Katana's System.Web host register their modules this way.
	public static class DynamicModuleUtility
	{
		public static void RegisterModule(Type moduleType) => HttpApplication.RegisterModule(moduleType);
	}
}
