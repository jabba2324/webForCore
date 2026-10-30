// webForCore: the assembly identities the Reference Source names through its build-generated AssemblyRef class (MIT
// license). System.Web, System.Web.Mobile and System.Configuration carry webForCore's identities on .NET.
namespace System.Web
{
	internal static class AssemblyRef
	{
		internal const string EcmaPublicKeyToken = "b77a5c561934e089";
		internal const string MicrosoftPublicKey = "b03f5f7f11d50a3a";
		internal const string SharedLibPublicKeyToken = "31bf3856ad364e35";
		internal const string MicrosoftPublicKeyToken = "b03f5f7f11d50a3a";
		internal const string SharedLibPublicKeyFull = "0024000004800000940000000602000000240000525341310004000001000100B5FC90E7027F67871E773A8FDE8938C81DD402BA65B9201D60593E96C492651E889CC13F1415EBB53FAC1131AE0BD333C5EE6021672D9718EA31A8AEBD0DA0072F25D87DBA6FC90FFD598ED4DA35E44C398C454307E8E33B8426143DAEC9F596836F97C8F74750E5975C64E2189F45DEF46B2A2B1247ADC3652BF5C308055DA9";
		internal const string Mscorlib = "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + EcmaPublicKeyToken;
		internal const string System = "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + EcmaPublicKeyToken;
#if NETFRAMEWORK
		internal const string SystemConfiguration = "System.Configuration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
		internal const string SystemWeb = "System.Web, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
		internal const string SystemWebMobile = "System.Web.Mobile, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
#else
		internal const string SystemConfiguration = "System.Configuration.ConfigurationManager, Version=13.5.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
		internal const string SystemWeb = "System.Web, Version=11.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
		internal const string SystemWebMobile = "System.Web.Mobile, Version=11.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
#endif
		internal const string SystemDesign = "System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
		internal const string SystemDrawingDesign = "System.Drawing.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
		internal const string SystemWebDynamicData = "System.Web.DynamicData, Version=4.0.0.0, Culture=neutral, PublicKeyToken=" + SharedLibPublicKeyToken;
		internal const string MicrosoftVisualStudioWeb = "Microsoft.VisualStudio.Web, Version=10.0.0.0, Culture=neutral, PublicKeyToken=" + MicrosoftPublicKey;
		internal const string SystemData = "System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
		internal const string SystemServiceModelWeb = "System.ServiceModel.Web, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";
		internal const string SystemWebDynamicDataDesign = "System.Web.DynamicData.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";
		internal const string SystemWebEntityDesign = "System.Web.Entity.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
		internal const string SystemWebExtensionsDesign = "System.Web.Extensions.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";
	}
}
