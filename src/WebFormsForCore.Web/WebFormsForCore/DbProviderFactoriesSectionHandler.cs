// webForCore: <system.data><DbProviderFactories> (MIT license). On .NET Framework, machine.config and web.config list the
// ADO.NET providers there, and DbProviderFactories.GetFactory reads them; .NET's DbProviderFactories ignores
// configuration and has RegisterFactory instead. machine.config maps <system.data> to this handler, and HttpRuntime
// applies the application's list when it starts (DbProviderFactoriesConfiguration.Apply).
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml;

namespace System.Web.Configuration
{
	// Reads <system.data><DbProviderFactories> with <add name invariant description type/>, <remove invariant/> and
	// <clear/>. Each configuration level's operations follow its parent's, as the levels inherit.
	public sealed class DbProviderFactoriesSectionHandler : IConfigurationSectionHandler
	{
		public object Create(object parent, object configContext, XmlNode section)
		{
			var operations = new List<DbProviderFactoriesConfiguration.Operation>();
			if (parent is IEnumerable<DbProviderFactoriesConfiguration.Operation> inherited)
				operations.AddRange(inherited);
			foreach (XmlNode child in section.ChildNodes)
			{
				if (child.NodeType != XmlNodeType.Element)
					continue;
				if (child.Name != "DbProviderFactories")
					throw new ConfigurationErrorsException("Unrecognized element '" + child.Name + "'.", child);
				foreach (XmlNode node in child.ChildNodes)
				{
					if (node.NodeType != XmlNodeType.Element)
						continue;
					string invariant = node.Attributes?["invariant"]?.Value;
					switch (node.Name)
					{
						case "add":
							string type = node.Attributes?["type"]?.Value;
							if (string.IsNullOrEmpty(invariant) || string.IsNullOrEmpty(type))
								throw new ConfigurationErrorsException("<add> needs the attributes 'invariant' and 'type'.", node);
							operations.Add(new DbProviderFactoriesConfiguration.Operation("add", invariant, type));
							break;
						case "remove":
							if (string.IsNullOrEmpty(invariant))
								throw new ConfigurationErrorsException("<remove> needs the attribute 'invariant'.", node);
							operations.Add(new DbProviderFactoriesConfiguration.Operation("remove", invariant, null));
							break;
						case "clear":
							operations.Add(new DbProviderFactoriesConfiguration.Operation("clear", null, null));
							break;
						default:
							throw new ConfigurationErrorsException("Unrecognized element '" + node.Name + "'.", node);
					}
				}
			}
			return operations;
		}
	}

	internal static class DbProviderFactoriesConfiguration
	{
		internal sealed record Operation(string Kind, string Invariant, string Type);

		// DbProviderFactories is process-wide on .NET, but was per AppDomain on .NET Framework. A registration keeps the
		// factory instance once it has been requested, so a PseudoAppDomain's instance would serve every later start (its
		// copy of the provider's assembly) and keep the domain from being collected. Registrations whose instance is stale
		// go back to their type name, to be resolved again in the context that next asks. Runtime internals, read by
		// reflection; if they change, nothing is reset. Returns the number reset.
		internal static int ResetRegistrations(Func<DbProviderFactory, bool> isStale)
		{
			const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic, AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			if (!(typeof(DbProviderFactories).GetField("_registeredFactories", AnyStatic)?.GetValue(null) is IDictionary registrations))
				return 0;
			int reset = 0;
			foreach (DictionaryEntry entry in registrations)
			{
				var registration = entry.Value;
				var type = registration?.GetType();
				if (type?.GetProperty("FactoryInstance", AnyInstance)?.GetValue(registration) is DbProviderFactory factory && isStale(factory)
					&& type.GetProperty("FactoryTypeAssemblyQualifiedName", AnyInstance)?.GetValue(registration) is string typeName)
				{
					DbProviderFactories.RegisterFactory((string)entry.Key, typeName);
					reset++;
				}
			}
			return reset;
		}

		// A factory of another PseudoAppDomain than the caller's (an earlier start of the application).
		internal static bool IsFromOtherDomain(DbProviderFactory factory)
		{
			var context = AssemblyLoadContext.GetLoadContext(factory.GetType().Assembly);
			return context != null && context.IsCollectible && context != AssemblyLoadContext.GetLoadContext(typeof(DbProviderFactoriesConfiguration).Assembly);
		}

		// Registers the application's providers with .NET's DbProviderFactories, by type name: nothing loads until a
		// provider is requested, as on .NET Framework. <clear/> and <remove/> also act on the providers System.Web
		// registers by default (FrameworkDefaults), as they did on machine.config's.
		internal static void Apply()
		{
			if (!(WebConfigurationManager.GetSection("system.data") is IEnumerable<Operation> operations))
				return;
			foreach (Operation operation in operations)
			{
				switch (operation.Kind)
				{
					case "add":
						DbProviderFactories.RegisterFactory(operation.Invariant, operation.Type);
						break;
					case "remove":
						DbProviderFactories.UnregisterFactory(operation.Invariant);
						break;
					case "clear":
						foreach (string name in DbProviderFactories.GetProviderInvariantNames())
							DbProviderFactories.UnregisterFactory(name);
						break;
				}
			}
		}
	}
}
