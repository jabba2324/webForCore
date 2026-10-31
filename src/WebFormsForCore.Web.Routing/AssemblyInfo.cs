// webForCore: System.Web.Routing 4.0.0.0. On .NET Framework 4 this assembly is a facade in the GAC: routing moved into
// System.Web in 4.0, and it forwards there. web.configs from MVC 2-4 projects list it in <compilation><assemblies>, and
// libraries built for .NET 3.5 reference it (#20; Roadkill). It keeps the facade's name, version and public key so
// those bind to it. MIT license.
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Web.Routing;

[assembly: AssemblyTitle("System.Web.Routing")]
[assembly: AssemblyProduct("webForCore")]
[assembly: AssemblyCopyright("Copyright (c) webForCore contributors. MIT License.")]
[assembly: AssemblyVersion("4.0.0.0")]
[assembly: AssemblyFileVersion("4.0.0.0")]
[assembly: ComVisible(false)]
[assembly: CLSCompliant(true)]

// The types of System.Web.Routing 3.5, which .NET Framework 4's facade forwards.
[assembly: TypeForwardedTo(typeof(HttpMethodConstraint))]
[assembly: TypeForwardedTo(typeof(IRouteConstraint))]
[assembly: TypeForwardedTo(typeof(IRouteHandler))]
[assembly: TypeForwardedTo(typeof(RequestContext))]
[assembly: TypeForwardedTo(typeof(Route))]
[assembly: TypeForwardedTo(typeof(RouteBase))]
[assembly: TypeForwardedTo(typeof(RouteCollection))]
[assembly: TypeForwardedTo(typeof(RouteData))]
[assembly: TypeForwardedTo(typeof(RouteDirection))]
[assembly: TypeForwardedTo(typeof(RouteTable))]
[assembly: TypeForwardedTo(typeof(RouteValueDictionary))]
[assembly: TypeForwardedTo(typeof(StopRoutingHandler))]
[assembly: TypeForwardedTo(typeof(UrlRoutingHandler))]
[assembly: TypeForwardedTo(typeof(UrlRoutingModule))]
[assembly: TypeForwardedTo(typeof(VirtualPathData))]
