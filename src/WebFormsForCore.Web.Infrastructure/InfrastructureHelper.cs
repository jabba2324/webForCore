// webForCore: written from Microsoft.Web.Infrastructure's documented public API. MIT license.
using System.Web;
using WebFormsForCore.CodeDom.Compiler;

namespace Microsoft.Web.Infrastructure
{
	public static class InfrastructureHelper
	{
		// Restarts the application (on webForCore, its PseudoAppDomain), as when the bin folder changes.
		public static void UnloadAppDomain() => HttpRuntime.UnloadAppDomain();

		// Whether a compiler is registered for the file extension (<system.codedom>), as System.Web checks itself.
		public static bool IsCodeDomDefinedExtension(string extension) => CodeDomProvider.IsDefinedExtension(extension);
	}
}
