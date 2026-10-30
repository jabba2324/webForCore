// webForCore: written from Microsoft.Web.Infrastructure's documented public API. MIT license.
using System;
using System.Web;

namespace Microsoft.Web.Infrastructure
{
	public static class HttpContextHelper
	{
		// Runs an action with HttpContext.Current set to null (Web Pages runs _AppStart this way), then restores it.
		public static void ExecuteInNullContext(Action action)
		{
			if (action == null)
				throw new ArgumentNullException(nameof(action));
			HttpContext current = HttpContext.Current;
			try
			{
				HttpContext.Current = null;
				action();
			}
			finally
			{
				HttpContext.Current = current;
			}
		}
	}
}
