// webForCore: the assembly identities the Reference Source names through its build-generated AssemblyRef class. MIT
// license. System.Web and its companions carry webForCore's version (11.0.0.0) under Microsoft's names and keys.
internal static class AssemblyRef
{
	internal const string EcmaPublicKeyToken = "b77a5c561934e089";
	internal const string MicrosoftPublicKeyToken = "b03f5f7f11d50a3a";
	internal const string SharedLibPublicKeyToken = "31bf3856ad364e35";
	internal const string System = "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + EcmaPublicKeyToken;
	// This assembly, which is System.Configuration on .NET Framework. The Reference Source creates some of its internal
	// types by this name (ConfigurationManagerInternal, ClientConfigurationHost, InternalConfigConfigurationFactory);
	// .NET's System.Configuration 4.0.0.0 is a facade that forwards only the public types, so they must be named in this
	// assembly (#154: every ApplicationSettingsBase threw TypeLoadException). The version is Properties/AssemblyInfo.cs's.
	internal const string SystemConfiguration = "System.Configuration.ConfigurationManager, Version=14.5.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
	internal const string SystemWeb = "System.Web, Version=11.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKeyToken;
	internal const string WebFormsForCoreWeb = SystemWeb;
	internal const string MicrosoftJScript = "Microsoft.JScript, Version=10.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKeyToken;
	internal const string MicrosoftVisualCCppCodeProvider = "CppCodeProvider, Version=10.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKeyToken;
}
