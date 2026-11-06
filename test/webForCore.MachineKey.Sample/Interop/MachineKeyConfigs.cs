// webForCore (#191): the <machineKey> configurations whose output is compared with .NET Framework 4.8's, in both
// directions. No System.Web here: this file is compiled into the sample (webForCore), into
// tools/machinekey/FrameworkMachineKey (.NET Framework 4.8) and into IIS.Gate.Tests, which writes each configuration's
// Web.config for the sample.
using System;
using System.Collections.Generic;
using System.Text;

namespace MachineKeyInterop
{
    public sealed class MachineKeyConfig
    {
        public MachineKeyConfig(string name, string validation, string decryption, string compatibilityMode, string targetFramework,
            string keySuffix = "", string virtualPath = "/", string ticketCompatibilityMode = null)
        {
            Name = name;
            Validation = validation;
            Decryption = decryption;
            CompatibilityMode = compatibilityMode;
            TargetFramework = targetFramework;
            KeySuffix = keySuffix;
            VirtualPath = virtualPath;
            TicketCompatibilityMode = ticketCompatibilityMode;
        }

        public string Name { get; }

        // <machineKey validation>: SHA1, HMACSHA256, HMACSHA512.
        public string Validation { get; }

        // <machineKey decryption>: AES, 3DES.
        public string Decryption { get; }

        // <machineKey compatibilityMode>, or null for none (the <httpRuntime targetFramework> default).
        public string CompatibilityMode { get; }

        // <httpRuntime targetFramework>, or null for none.
        public string TargetFramework { get; }

        // ",IsolateApps" or ",IsolateByAppId" after the keys.
        public string KeySuffix { get; }

        // The application's virtual path.
        public string VirtualPath { get; }

        // <forms ticketCompatibilityMode>, or null for the default (Framework20).
        public string TicketCompatibilityMode { get; }

        public string ValidationKey => MachineKeyConfigs.ValidationKey + KeySuffix;

        public string DecryptionKey => (Decryption == "3DES" ? MachineKeyConfigs.TripleDesKey : MachineKeyConfigs.AesKey) + KeySuffix;

        // The application's Web.config: this <machineKey>, and for the webForCore sample its handler and OWIN startup
        // (.NET Framework's runner ignores them).
        public string WebConfig()
        {
            var config = new StringBuilder();
            config.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            config.AppendLine("<configuration>");
            config.AppendLine("  <appSettings>");
            config.AppendLine("    <add key=\"interop:config\" value=\"" + Name + "\" />");
            config.AppendLine("    <add key=\"owin:appStartup\" value=\"MachineKeySample.Startup, webForCore.MachineKey.Sample\" />");
            config.AppendLine("    <add key=\"owin:AutomaticAppStartup\" value=\"false\" />");
            config.AppendLine("  </appSettings>");
            config.AppendLine("  <system.web>");
            config.AppendLine("    <compilation debug=\"false\" />");
            config.AppendLine(TargetFramework == null ? "    <httpRuntime />" : "    <httpRuntime targetFramework=\"" + TargetFramework + "\" />");
            config.Append("    <machineKey validationKey=\"" + ValidationKey + "\" decryptionKey=\"" + DecryptionKey + "\" validation=\"" + Validation +
                "\" decryption=\"" + Decryption + "\"");
            if (CompatibilityMode != null) config.Append(" compatibilityMode=\"" + CompatibilityMode + "\"");
            config.AppendLine(" />");
            config.AppendLine("    <authentication mode=\"Forms\">");
            config.AppendLine(TicketCompatibilityMode == null ? "      <forms />" : "      <forms ticketCompatibilityMode=\"" + TicketCompatibilityMode + "\" />");
            config.AppendLine("    </authentication>");
            config.AppendLine("    <httpHandlers>");
            config.AppendLine("      <add path=\"interop.axd\" verb=\"*\" type=\"MachineKeySample.InteropHandler, webForCore.MachineKey.Sample\" />");
            config.AppendLine("    </httpHandlers>");
            // The site has no bin folder for Katana's PreApplicationStartMethod to be found in.
            config.AppendLine("    <httpModules>");
            config.AppendLine("      <add name=\"OwinHttpModule\" type=\"Microsoft.Owin.Host.SystemWeb.OwinHttpModule, Microsoft.Owin.Host.SystemWeb\" />");
            config.AppendLine("    </httpModules>");
            config.AppendLine("  </system.web>");
            config.AppendLine("</configuration>");
            return config.ToString();
        }
    }

    public static class MachineKeyConfigs
    {
        // Fixed keys, as a farm's <machineKey> has them: 512 bits of validation key (enough for HMACSHA512), a 256-bit AES
        // key and a 192-bit 3DES key.
        public const string ValidationKey =
            "0A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F9" +
            "F9E8D7C6B5A4938271605F4E3D2C1B0AF9E8D7C6B5A4938271605F4E3D2C1B0A";
        public const string AesKey = "1F2E3D4C5B6A79880F1E2D3C4B5A69781F2E3D4C5B6A79880F1E2D3C4B5A6978";
        public const string TripleDesKey = "7A6B5C4D3E2F1001C8D9E0F1A2B3C4D5E6F708192A3B4C5D";

        // Katana's cookies: a plain claims identity, and ASP.NET Identity's (DefaultAuthenticationTypes.ApplicationCookie).
        public const string CookiesType = "Cookies";
        public const string ApplicationCookieType = "ApplicationCookie";
        public static readonly string[] OwinTypes = { CookiesType, ApplicationCookieType };

        public static readonly IReadOnlyList<MachineKeyConfig> All = Create();

        private static List<MachineKeyConfig> Create()
        {
            var configs = new List<MachineKeyConfig>();
            foreach (string validation in new[] { "SHA1", "HMACSHA256", "HMACSHA512" })
            {
                foreach (string decryption in new[] { "AES", "3DES" })
                {
                    string prefix = validation.ToLowerInvariant() + "-" + decryption.ToLowerInvariant() + "-";
                    configs.Add(new MachineKeyConfig(prefix + "framework20sp1", validation, decryption, "Framework20SP1", null));
                    configs.Add(new MachineKeyConfig(prefix + "framework20sp2", validation, decryption, "Framework20SP2", null));
                    configs.Add(new MachineKeyConfig(prefix + "framework45", validation, decryption, "Framework45", null));
                    // No compatibilityMode: <httpRuntime targetFramework="4.5"> makes it Framework45.
                    configs.Add(new MachineKeyConfig(prefix + "targetframework45", validation, decryption, null, "4.5"));
                }
            }
            // No compatibilityMode and no targetFramework: Framework20SP1 on .NET Framework.
            configs.Add(new MachineKeyConfig("hmacsha256-aes-default", "HMACSHA256", "AES", null, null));
            // Forms tickets with UTC dates (<forms ticketCompatibilityMode="Framework40">).
            configs.Add(new MachineKeyConfig("hmacsha256-aes-targetframework45-ticket40", "HMACSHA256", "AES", null, "4.5",
                ticketCompatibilityMode: "Framework40"));
            // IsolateApps and IsolateByAppId on explicit keys: only the Framework20 modes take them (Framework45 rejects a
            // key with a modifier); the key's first bytes are replaced with a hash of the application's virtual path or ID.
            configs.Add(new MachineKeyConfig("sha1-aes-framework20sp1-isolateapps-root", "SHA1", "AES", "Framework20SP1", null, ",IsolateApps"));
            configs.Add(new MachineKeyConfig("sha1-aes-framework20sp1-isolateapps", "SHA1", "AES", "Framework20SP1", null, ",IsolateApps", "/interop"));
            configs.Add(new MachineKeyConfig("hmacsha256-aes-framework20sp2-isolateapps", "HMACSHA256", "AES", "Framework20SP2", null, ",IsolateApps", "/interop"));
            configs.Add(new MachineKeyConfig("sha1-aes-framework20sp1-isolatebyappid", "SHA1", "AES", "Framework20SP1", null, ",IsolateByAppId"));
            return configs;
        }

        // Outputs (configuration, kind) that are known not to read on the other runtime, with the reason; null for none.
        // Both sides check that each listed one still differs.
        public static string KnownDifference(string config, string kind)
        {
            // IsolateByAppId on an explicit key replaces bytes 4 to 7 with a hash of HttpRuntime.AppDomainAppId (IIS's
            // /LM/W3SVC/1/ROOT here, as FrameworkMachineKey creates the application): on .NET Framework
            // StringComparer.InvariantCultureIgnoreCase's, as for IsolateApps below, so it differs even for the same ID.
            // (MachineKey.Protect, and so the cookies, reject a key with a modifier on both runtimes.)
            if (config.EndsWith("-isolatebyappid", StringComparison.Ordinal) && (kind == "forms-ticket" || kind == "encode"))
                return "IsolateByAppId on an explicit key: the hash of the application ID differs";
            // IsolateApps on an explicit key replaces its first 4 bytes with a hash of the virtual path: on .NET Framework
            // StringComparer.InvariantCultureIgnoreCase's (from Windows' NLS sort keys), which .NET doesn't have (its
            // string hashes are randomized; webForCore uses ASP.NET's stable string hash, StringUtil.GetStringHashCode).
            if (config.Contains("-isolateapps") && (kind == "forms-ticket" || kind == "encode"))
                return "IsolateApps on an explicit key: the hash of the virtual path differs";
            // No compatibilityMode and no targetFramework: Framework20SP1 on .NET Framework, Framework45 on webForCore,
            // which runs every application as targetFramework 4.5 or later (IIS-COMPATIBILITY.md, "Compatibility
            // decisions"). Only the forms ticket depends on it.
            if (config == "hmacsha256-aes-default" && kind == "forms-ticket")
                return "no targetFramework: .NET Framework's compatibilityMode is Framework20SP1, webForCore's Framework45";
            return null;
        }

        public static MachineKeyConfig Find(string name)
        {
            foreach (MachineKeyConfig config in All)
            {
                if (config.Name == name) return config;
            }
            return null;
        }
    }

    // One output: a configuration, what was protected (Kind: forms-ticket, protect, encode, owin), the case, the
    // protected value as the application has it (cookie text, base64 or hex), and whether two calls gave the same value.
    public sealed class InteropEntry
    {
        public string Config { get; set; }
        public string Kind { get; set; }
        public string Case { get; set; }
        public string Value { get; set; }
        public bool Deterministic { get; set; }
    }

    // The other runtime's verdict on an entry: null Problem when it read it as what was protected.
    public sealed class InteropResult
    {
        public string Config { get; set; }
        public string Kind { get; set; }
        public string Case { get; set; }
        public string Problem { get; set; }
    }
}
