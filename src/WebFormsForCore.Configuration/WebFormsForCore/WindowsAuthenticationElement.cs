// webForCore: System.Net.Configuration.WindowsAuthenticationElement (.NET Framework 4.7.2), written from its documented
// public API. MIT license. The Reference Source's SettingsSection uses it, but the snapshot has no source for it.
using System.Configuration;

namespace System.Net.Configuration
{
	// <system.net><settings><windowsAuthentication defaultCredentialsHandleCacheSize="..."/>
	public sealed class WindowsAuthenticationElement : ConfigurationElement
	{
		private static readonly ConfigurationProperty s_defaultCredentialsHandleCacheSize =
			new ConfigurationProperty(ConfigurationStrings.DefaultCredentialsHandleCacheSize, typeof(int), 0, null,
				new IntegerValidator(0, int.MaxValue), ConfigurationPropertyOptions.None);
		private static readonly ConfigurationPropertyCollection s_properties = new ConfigurationPropertyCollection { s_defaultCredentialsHandleCacheSize };

		protected internal override ConfigurationPropertyCollection Properties => s_properties;

		[ConfigurationProperty("defaultCredentialsHandleCacheSize", DefaultValue = 0)]
		public int DefaultCredentialsHandleCacheSize
		{
			get => (int)this[s_defaultCredentialsHandleCacheSize];
			set => this[s_defaultCredentialsHandleCacheSize] = value;
		}
	}
}
