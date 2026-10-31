// webForCore: System.Web.Abstractions 4.0.0.0. On .NET Framework 4 this assembly is a facade in the GAC: the
// abstractions (HttpContextBase and the rest) moved into System.Web in 4.0, and it forwards them there. web.configs from
// MVC 2-4 projects list it in <compilation><assemblies>, and libraries built for .NET 3.5 reference it (#20; Roadkill).
// It keeps the facade's name, version and public key so those bind to it. MIT license.
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web;

[assembly: AssemblyTitle("System.Web.Abstractions")]
[assembly: AssemblyProduct("webForCore")]
[assembly: AssemblyCopyright("Copyright (c) webForCore contributors. MIT License.")]
[assembly: AssemblyVersion("4.0.0.0")]
[assembly: AssemblyFileVersion("4.0.0.0")]
[assembly: ComVisible(false)]
[assembly: CLSCompliant(true)]

// The types of System.Web.Abstractions 3.5, which .NET Framework 4's facade forwards.
[assembly: TypeForwardedTo(typeof(HttpApplicationStateBase))]
[assembly: TypeForwardedTo(typeof(HttpApplicationStateWrapper))]
[assembly: TypeForwardedTo(typeof(HttpBrowserCapabilitiesBase))]
[assembly: TypeForwardedTo(typeof(HttpBrowserCapabilitiesWrapper))]
[assembly: TypeForwardedTo(typeof(HttpCachePolicyBase))]
[assembly: TypeForwardedTo(typeof(HttpCachePolicyWrapper))]
[assembly: TypeForwardedTo(typeof(HttpContextBase))]
[assembly: TypeForwardedTo(typeof(HttpContextWrapper))]
[assembly: TypeForwardedTo(typeof(HttpFileCollectionBase))]
[assembly: TypeForwardedTo(typeof(HttpFileCollectionWrapper))]
[assembly: TypeForwardedTo(typeof(HttpPostedFileBase))]
[assembly: TypeForwardedTo(typeof(HttpPostedFileWrapper))]
[assembly: TypeForwardedTo(typeof(HttpRequestBase))]
[assembly: TypeForwardedTo(typeof(HttpRequestWrapper))]
[assembly: TypeForwardedTo(typeof(HttpResponseBase))]
[assembly: TypeForwardedTo(typeof(HttpResponseWrapper))]
[assembly: TypeForwardedTo(typeof(HttpServerUtilityBase))]
[assembly: TypeForwardedTo(typeof(HttpServerUtilityWrapper))]
[assembly: TypeForwardedTo(typeof(HttpSessionStateBase))]
[assembly: TypeForwardedTo(typeof(HttpSessionStateWrapper))]
[assembly: TypeForwardedTo(typeof(HttpStaticObjectsCollectionBase))]
[assembly: TypeForwardedTo(typeof(HttpStaticObjectsCollectionWrapper))]
