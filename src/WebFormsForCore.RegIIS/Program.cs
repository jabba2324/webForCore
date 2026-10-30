// webForCore: aspnet_regiis's protected-configuration commands, for the fork on any platform.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Configuration;

namespace WebFormsForCore.RegIIS
{
	// Usage, as aspnet_regiis's:
	//   -pef <section> <physical directory> [-prov <provider>]   protect a section of <directory>/web.config
	//   -pdf <section> <physical directory>                      unprotect it
	//   -pc  <container> [-exp] [-size <bits>]                   create an RSA key container
	//   -pz  <container>                                         delete it
	//   -pi  <container> <xml file> [-exp]                       import a key (aspnet_regiis -px's XML)
	//   -px  <container> <xml file> [-pri]                       export it (-pri: with the private key)
	//   -pku                                                     a user container instead of the machine's
	// Providers: RsaProtectedConfigurationProvider (the default) and DataProtectionConfigurationProvider, as in
	// machine.config; key files live in WebFormsForCore.Security.LocalDataProtection.KeyDirectory.
	public static class Program
	{
		public static int Main(string[] args)
		{
			try
			{
				return Run(args.ToList());
			}
			catch (Exception e) when (e is ConfigurationException || e is IOException || e is UnauthorizedAccessException || e is System.Security.Cryptography.CryptographicException || e is ArgumentException)
			{
				Console.Error.WriteLine("Failed: " + e.Message);
				return 1;
			}
		}

		static int Run(List<string> args)
		{
			if (args.Count == 0) return Usage();
			bool user = Take(args, "-pku");
			bool exportable = Take(args, "-exp");
			bool privateKey = Take(args, "-pri");
			string provider = Value(args, "-prov") ?? "RsaProtectedConfigurationProvider";
			string container = Value(args, "-cont");
			int size = int.TryParse(Value(args, "-size"), out var s) ? s : 2048;
			switch (args[0])
			{
				case "-pef":
				case "-pdf":
					if (args.Count < 3) return Usage();
					return Section(args[0] == "-pef", args[1], args[2], provider);
				case "-pc":
				case "-pz":
				case "-pi":
				case "-px":
					if (args.Count < 2) return Usage();
					var rsa = Rsa(container ?? args[1], !user);
					switch (args[0])
					{
						case "-pc": rsa.AddKey(size, exportable); break;
						case "-pz": rsa.DeleteKey(); break;
						case "-pi": if (args.Count < 3) return Usage(); rsa.ImportKey(args[2], exportable); break;
						default: if (args.Count < 3) return Usage(); rsa.ExportKey(args[2], privateKey); break;
					}
					Console.WriteLine("Succeeded!");
					return 0;
				default:
					return Usage();
			}
		}

		static RsaProtectedConfigurationProvider Rsa(string container, bool machine)
		{
			var provider = new RsaProtectedConfigurationProvider();
			provider.Initialize("RsaProtectedConfigurationProvider", new NameValueCollection
			{
				["keyContainerName"] = container,
				["useMachineContainer"] = machine ? "true" : "false",
			});
			return provider;
		}

		static int Section(bool protect, string name, string directory, string provider)
		{
			var map = new WebConfigurationFileMap();
			map.VirtualDirectories.Add("/", new VirtualDirectoryMapping(Path.GetFullPath(directory), true));
			var configuration = WebConfigurationManager.OpenMappedWebConfiguration(map, "/");
			var section = configuration.GetSection(name) ?? throw new ArgumentException("The configuration section '" + name + "' was not found.");
			Console.WriteLine((protect ? "Encrypting" : "Decrypting") + " configuration section '" + name + "'...");
			if (protect) section.SectionInformation.ProtectSection(provider);
			else section.SectionInformation.UnprotectSection();
			section.SectionInformation.ForceSave = true;
			configuration.Save(ConfigurationSaveMode.Modified);
			Console.WriteLine("Succeeded!");
			return 0;
		}

		static bool Take(List<string> args, string flag) => args.Remove(flag);

		static string Value(List<string> args, string option)
		{
			int i = args.IndexOf(option);
			if (i < 0 || i + 1 >= args.Count) return null;
			var value = args[i + 1];
			args.RemoveRange(i, 2);
			return value;
		}

		static int Usage()
		{
			Console.Error.WriteLine("webforcore-regiis: aspnet_regiis's protected-configuration commands.");
			Console.Error.WriteLine("  -pef <section> <dir> [-prov <provider>] | -pdf <section> <dir>");
			Console.Error.WriteLine("  -pc <container> [-exp] [-size <bits>] | -pz <container> | -pi <container> <file> [-exp] | -px <container> <file> [-pri]");
			Console.Error.WriteLine("  -pku: user container. Keys: $WEBFORCORE_KEYS, /var/lib/webforcore/keys, ~/.webforcore/keys");
			return 2;
		}
	}
}
