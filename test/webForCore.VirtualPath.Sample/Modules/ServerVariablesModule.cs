using System;
using System.Web;

namespace VirtualPathSample
{
    // An application's own module behind a proxy that ends TLS, as such applications had on IIS (#194): it copies the
    // client's address and the scheme from the proxy's headers into REMOTE_ADDR and HTTPS. On IIS's integrated pipeline,
    // Request.UserHostAddress and Request.IsSecureConnection read those server variables. The header names are the
    // sample's own, so that the module doesn't act on the X-Forwarded-* headers ForwardedHeadersTests send.
    public class ServerVariablesModule : IHttpModule
    {
        public void Init(HttpApplication application)
        {
            application.BeginRequest += (sender, e) =>
            {
                var request = ((HttpApplication)sender).Request;
                string remote = request.Headers["X-Module-Remote-Addr"];
                if (!string.IsNullOrEmpty(remote)) request.ServerVariables["REMOTE_ADDR"] = remote;
                if (request.Headers["X-Module-Https"] == "on") request.ServerVariables["HTTPS"] = "on";
            };
        }

        public void Dispose()
        {
        }
    }
}
