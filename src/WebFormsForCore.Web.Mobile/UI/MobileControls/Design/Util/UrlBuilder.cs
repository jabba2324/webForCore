// webForCore: System.Web.UI.Design.UrlBuilder (System.Design), written from its documented public API. MIT license.
// The URL picker is a Visual Studio designer service; there is no designer on .NET, so no URL is built.
using System.ComponentModel;

namespace System.Web.UI.Design
{
	public enum UrlBuilderOptions
	{
		None = 0,
		NoAbsolute = 1,
	}

	public sealed class UrlBuilder
	{
		private UrlBuilder()
		{
		}

		public static string BuildUrl(IComponent component, string initialUrl, string caption, string filter) =>
			BuildUrl(component, initialUrl, caption, filter, UrlBuilderOptions.None);

		public static string BuildUrl(IComponent component, string initialUrl, string caption, string filter, UrlBuilderOptions options) =>
			component?.Site == null ? null : BuildUrl(component.Site, initialUrl, caption, filter, options);

		public static string BuildUrl(IServiceProvider serviceProvider, string initialUrl, string caption, string filter, UrlBuilderOptions options) => null;
	}
}
