// webForCore: written from Microsoft.Web.Infrastructure's documented public API. MIT license.
using System;
using System.Collections.Specialized;
using System.Reflection;
using System.Web;

namespace Microsoft.Web.Infrastructure.DynamicValidationHelper
{
	// Granular (lazy) request validation for MVC and Web Pages. System.Web provides the implementation in the internal
	// class Microsoft.Web.Infrastructure.DynamicValidationHelper.DynamicValidationShim, which exists for this assembly to
	// find by reflection; these methods forward to it.
	public static class ValidationUtility
	{
		private delegate void GetUnvalidatedCollectionsDelegate(HttpContext context, out Func<NameValueCollection> formGetter, out Func<NameValueCollection> queryStringGetter);

		private static readonly Action<HttpContext> s_enable = Bind<Action<HttpContext>>("EnableDynamicValidation");
		private static readonly Func<HttpContext, bool> s_isEnabled = Bind<Func<HttpContext, bool>>("IsValidationEnabled");
		private static readonly GetUnvalidatedCollectionsDelegate s_getUnvalidated = Bind<GetUnvalidatedCollectionsDelegate>("GetUnvalidatedCollections");

		private static T Bind<T>(string name) where T : Delegate
		{
			Type shim = typeof(HttpContext).Assembly.GetType("Microsoft.Web.Infrastructure.DynamicValidationHelper.DynamicValidationShim", throwOnError: true);
			MethodInfo method = shim.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
				?? throw new MissingMethodException(shim.FullName, name);
			return (T)method.CreateDelegate(typeof(T));
		}

		// Turns on granular request validation for the request: form and query string values are validated when read.
		public static void EnableDynamicValidation(HttpContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			s_enable(context);
		}

		// Whether request validation was turned on for the request.
		public static bool? IsValidationEnabled(HttpContext context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			return s_isEnabled(context);
		}

		// Getters for the form and query string collections without request validation.
		public static void GetUnvalidatedCollections(HttpContext context, out Func<NameValueCollection> formGetter, out Func<NameValueCollection> queryStringGetter)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			s_getUnvalidated(context, out formGetter, out queryStringGetter);
		}
	}
}
