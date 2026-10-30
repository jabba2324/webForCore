// webForCore: configuration builders (.NET Framework 4.7.1), written from their documented public API. MIT license.
// Neither the .NET Framework Reference Source nor .NET contains them; BaseConfigurationRecord, SectionInformation and
// the config hosts (Reference Source) use them as declared here.
using System.Collections.Generic;
using System.Configuration.Internal;
using System.Configuration.Provider;
using System.Xml;

namespace System.Configuration
{
	// Transforms a section before it is used: its raw XML (ProcessRawXml) and the deserialized section
	// (ProcessConfigurationSection). Applied to sections with configBuilders="name[,name...]".
	public abstract class ConfigurationBuilder : ProviderBase
	{
		public virtual XmlNode ProcessRawXml(XmlNode rawXml) => rawXml;

		public virtual ConfigurationSection ProcessConfigurationSection(ConfigurationSection configSection) => configSection;
	}

	public class ConfigurationBuilderCollection : ProviderCollection
	{
		public new ConfigurationBuilder this[string name] => (ConfigurationBuilder)base[name];

		public override void Add(ProviderBase builder)
		{
			if (builder == null)
				throw new ArgumentNullException(nameof(builder));
			if (!(builder is ConfigurationBuilder))
				throw new ArgumentException(SR.GetString(SR.Config_provider_must_implement_type, typeof(ConfigurationBuilder).ToString()), nameof(builder));
			base.Add(builder);
		}
	}

	// <builders>: the <add name="..." type="..." .../> entries of <configBuilders>.
	public class ConfigurationBuilderSettings : ConfigurationElement
	{
		private static readonly ConfigurationProperty s_builders =
			new ConfigurationProperty(null, typeof(ProviderSettingsCollection), null, ConfigurationPropertyOptions.IsDefaultCollection);
		private static readonly ConfigurationPropertyCollection s_properties = new ConfigurationPropertyCollection { s_builders };

		protected internal override ConfigurationPropertyCollection Properties => s_properties;

		[ConfigurationProperty("", IsDefaultCollection = true, Options = ConfigurationPropertyOptions.IsDefaultCollection)]
		public ProviderSettingsCollection Builders => (ProviderSettingsCollection)base[s_builders];
	}

	// <configBuilders><builders>...</builders></configBuilders>
	public sealed class ConfigurationBuildersSection : ConfigurationSection
	{
		private static readonly ConfigurationProperty s_builders =
			new ConfigurationProperty("builders", typeof(ConfigurationBuilderSettings), new ConfigurationBuilderSettings(), ConfigurationPropertyOptions.None);
		private static readonly ConfigurationPropertyCollection s_properties = new ConfigurationPropertyCollection { s_builders };
		private readonly Dictionary<string, ConfigurationBuilder> _created = new Dictionary<string, ConfigurationBuilder>(StringComparer.Ordinal);

		protected internal override ConfigurationPropertyCollection Properties => s_properties;

		[ConfigurationProperty("builders")]
		public ProviderSettingsCollection Builders => ((ConfigurationBuilderSettings)base[s_builders]).Builders;

		// The builder for builderName; a comma-separated list gives a chain that applies them in order.
		public ConfigurationBuilder GetBuilderFromName(string builderName)
		{
			string[] names = builderName.Split(',');
			if (names.Length == 1)
				return GetBuilder(names[0].Trim());
			var chain = new ConfigurationBuilderChain();
			foreach (string name in names)
			{
				string trimmed = name.Trim();
				if (trimmed.Length == 0)
					throw new ConfigurationErrorsException(SR.GetString(SR.Config_builder_invalid_format, builderName));
				chain.Builders.Add(GetBuilder(trimmed));
			}
			chain.Initialize(builderName, null);
			return chain;
		}

		private ConfigurationBuilder GetBuilder(string name)
		{
			lock (_created)
			{
				if (_created.TryGetValue(name, out ConfigurationBuilder existing))
					return existing;
				ProviderSettings settings = Builders[name]
					?? throw new ConfigurationErrorsException(SR.GetString(SR.Config_builder_not_found, name));
				ConfigurationBuilder builder;
				try
				{
					Type type = TypeUtil.GetTypeWithReflectionPermission(settings.Type, true);
					if (!typeof(ConfigurationBuilder).IsAssignableFrom(type))
						throw new ArgumentException(SR.GetString(SR.Config_provider_must_implement_type, typeof(ConfigurationBuilder).ToString()));
					builder = (ConfigurationBuilder)Activator.CreateInstance(type);
					var parameters = new Collections.Specialized.NameValueCollection(settings.Parameters.Count, StringComparer.Ordinal);
					foreach (string key in settings.Parameters)
						parameters[key] = settings.Parameters[key];
					builder.Initialize(settings.Name, parameters);
				}
				catch (Exception e) when (!(e is ConfigurationException))
				{
					throw new ConfigurationErrorsException(SR.GetString(SR.ConfigBuilder_init_error, name), e, settings.ElementInformation.Source, settings.ElementInformation.LineNumber);
				}
				_created[name] = builder;
				return builder;
			}
		}
	}

	// Several builders applied in order (configBuilders="a, b").
	internal class ConfigurationBuilderChain : ConfigurationBuilder
	{
		public List<ConfigurationBuilder> Builders { get; } = new List<ConfigurationBuilder>();

		public override XmlNode ProcessRawXml(XmlNode rawXml)
		{
			XmlNode processed = rawXml;
			foreach (ConfigurationBuilder builder in Builders)
				processed = builder.ProcessRawXml(processed);
			return processed;
		}

		public override ConfigurationSection ProcessConfigurationSection(ConfigurationSection configSection)
		{
			ConfigurationSection processed = configSection;
			foreach (ConfigurationBuilder builder in Builders)
				processed = builder.ProcessConfigurationSection(processed);
			return processed;
		}
	}
}

namespace System.Configuration.Internal
{
	// Implemented by configuration hosts that run configuration builders.
	public interface IInternalConfigurationBuilderHost
	{
		XmlNode ProcessRawXml(XmlNode rawXml, ConfigurationBuilder builder);

		ConfigurationSection ProcessConfigurationSection(ConfigurationSection configSection, ConfigurationBuilder builder);
	}
}
