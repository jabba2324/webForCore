using System.Net;
using System.Text.Json;
using Xunit;

namespace IISGate
{
    // Behind a reverse proxy that ends TLS (#194): the ways X-Forwarded-For, -Proto and -Host reach what System.Web sees
    // (Request.UserHostAddress, REMOTE_ADDR, IsSecureConnection, HTTPS, Url, requireSSL cookies, absolute URLs,
    // <ipSecurity> without proxy mode), and that a client can't set them past the proxy. Each test runs
    // test/webForCore.VirtualPath.Sample under webforcore-host with its own settings; the test client stands for the proxy,
    // on this machine. AModuleSetsRemoteAddrAndHttps and ForwardedHeadersAreOnlyHeadersByDefault (VirtualPathTests) are
    // the other two ways, compared with IIS.
    [Collection(ForwardedHeadersCollection.Name)]
    public class ForwardedHeadersTests
    {
        private static readonly Dictionary<string, string> ThroughTheProxy = new Dictionary<string, string>
        {
            // The client put an address of its own first; the proxy appended the address it saw.
            ["X-Forwarded-For"] = "198.51.100.66, 203.0.113.7",
            ["X-Forwarded-Proto"] = "https",
            ["X-Forwarded-Host"] = "www.example.com",
        };

        // WebForCore:Host:ForwardedHeaders: the host applies the trusted proxy's headers before it forwards the request,
        // and the worker gets the result. The proxy's entry is applied, not the client's; X-Forwarded-For keeps the rest.
        [Fact]
        public async Task TheHostAppliesATrustedProxysHeaders()
        {
            using var server = Start(new Dictionary<string, string>
            {
                ["WebForCore__Host__ForwardedHeaders__Headers"] = "XForwardedFor, XForwardedProto, XForwardedHost",
            });
            var values = await Probe(server, ThroughTheProxy);
            Assert.Equal("203.0.113.7", values["user_host_address"]);
            Assert.Equal("203.0.113.7", values["remote_addr"]);
            Assert.Equal("True", values["is_secure_connection"]);
            Assert.Equal("on", values["https"]);
            Assert.Equal("https", values["url_scheme"]);
            Assert.Equal("www.example.com", values["url_host"]);
            Assert.Equal("www.example.com", values["server_name"]);
            Assert.Equal("198.51.100.66", values["x_forwarded_for"]);
            Assert.Contains("Forwarded headers: XForwardedFor, XForwardedHost, XForwardedProto from loopback (ForwardLimit 1).", server.Output);

            // Absolute URLs (Web API's Url.Link), MVC's [RequireHttps], forms authentication's requireSSL cookie and
            // <ipSecurity> (the client's address, without enableProxyMode). Request.Url's port is SERVER_PORT, the
            // binding's, as on IIS behind such a proxy ("https://host:80/"), unless aspnet:UseHostHeaderForRequestUrl.
            var (response, body) = await server.Get("/app/api/links/1", ThroughTheProxy);
            Assert.Equal($"https://www.example.com:{server.BaseAddress.Port}/app/api/links/5", JsonSerializer.Deserialize<Dictionary<string, string>>(body)["link"]);
            Assert.Equal("True", values["url_port_is_server_port"]);
            (response, body) = await server.Get("/app/Home/Secure", ThroughTheProxy);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("secure https", body);
            (response, body) = await server.Get("/app/Account/Login.aspx?user=gate", ThroughTheProxy);
            Assert.Contains("cookie=.ASPXAUTH;path=/;secure=True;httponly=True", body);
            (response, body) = await server.Get("/app/Restricted/Page.aspx", ThroughTheProxy);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("restricted 203.0.113.7\n", body);
            (response, _) = await server.Get("/app/Restricted/Page.aspx", new Dictionary<string, string> { ["X-Forwarded-For"] = "198.51.100.66" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Headers from a peer that isn't one of KnownProxies (or KnownNetworks) are ignored: here the proxy is 192.0.2.1, so
        // a client on this machine, which reaches the host directly, can't set its own address or scheme.
        [Fact]
        public async Task TheHostIgnoresHeadersFromOtherPeers()
        {
            using var server = Start(new Dictionary<string, string>
            {
                ["WebForCore__Host__ForwardedHeaders__Headers"] = "XForwardedFor, XForwardedProto",
                ["WebForCore__Host__ForwardedHeaders__KnownProxies__0"] = "192.0.2.1",
            });
            var values = await Probe(server, ThroughTheProxy);
            Assert.Equal("127.0.0.1", values["user_host_address"]);
            Assert.Equal("False", values["is_secure_connection"]);
            Assert.Equal("http", values["url_scheme"]);
            Assert.Equal("198.51.100.66, 203.0.113.7", values["x_forwarded_for"]);
            Assert.Contains("Forwarded headers: XForwardedFor, XForwardedProto from 192.0.2.1 (ForwardLimit 1).", server.Output);
        }

        // XForwardedPrefix isn't taken: the host doesn't start, and says why (the application's virtual path, #190).
        [Fact]
        public void TheHostRefusesThePrefixHeader()
        {
            using var server = Start(new Dictionary<string, string>
            {
                ["WebForCore__Host__ForwardedHeaders__Headers"] = "XForwardedFor, XForwardedPrefix",
            });
            Assert.True(server.HasExited, server.Output);
            Assert.Contains("only XForwardedFor, XForwardedProto and XForwardedHost are taken", server.Output);
        }

        // ASP.NET Core's ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, as docs/MIGRATING.md has it for Entra ID (#188): the host
        // applies X-Forwarded-For and -Proto from any peer. The workers inherited it and applied X-Forwarded-For again, to
        // what the host had left of it: the client's own entry, 198.51.100.66, past the proxy. The host applies it once now.
        [Fact]
        public async Task AspNetCoresSettingIsAppliedOnceByTheHost()
        {
            using var server = Start(new Dictionary<string, string> { ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true" });
            var values = await Probe(server, ThroughTheProxy);
            Assert.Equal("203.0.113.7", values["user_host_address"]);
            Assert.Equal("True", values["is_secure_connection"]);
            Assert.Equal("https", values["url_scheme"]);
            Assert.Equal("127.0.0.1", values["url_host"]); // X-Forwarded-Host isn't in ASP.NET Core's setting
            Assert.Equal("198.51.100.66", values["x_forwarded_for"]);
        }

        // The application's own app.UseForwardedHeaders(...) before UseWebForms: the worker gives it the host's peer (the
        // proxy, this machine: ASP.NET Core's default KnownProxies), and System.Web sees the result.
        [Fact]
        public async Task TheApplicationsUseForwardedHeaders()
        {
            using var server = Start(new Dictionary<string, string> { ["GATE_FORWARDED"] = "app" });
            var values = await Probe(server, ThroughTheProxy);
            Assert.Equal("203.0.113.7", values["user_host_address"]);
            Assert.Equal("203.0.113.7", values["remote_addr"]);
            Assert.Equal("True", values["is_secure_connection"]);
            Assert.Equal("on", values["https"]);
            Assert.Equal("https", values["url_scheme"]);
            Assert.Equal("198.51.100.66", values["x_forwarded_for"]);
            var (response, body) = await server.Get("/app/Account/Login.aspx?user=gate", ThroughTheProxy);
            Assert.Contains("cookie=.ASPXAUTH;path=/;secure=True;httponly=True", body);
        }

        // Application Initialization's pages are relative to the application, as on IIS (#193 with #190): the worker tells
        // the host its virtual path, and "/Probe.aspx" is sent as /app/Probe.aspx.
        [Fact]
        public async Task InitializationPagesAreUnderTheVirtualPath()
        {
            using var server = Start(new Dictionary<string, string>
            {
                ["WebForCore__Host__InitializationPages__0__InitializationPage"] = "/Probe.aspx",
            });
            var (response, _) = await server.Get("/app/Probe.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Matches(@"initialization page /app/Probe\.aspx answered 200", server.Output);
        }

        private static VirtualPathSampleServer Start(IDictionary<string, string> settings) => new VirtualPathSampleServer(settings);

        private static async Task<Dictionary<string, string>> Probe(VirtualPathSampleServer server, IDictionary<string, string> headers)
        {
            var (response, body) = await server.Get("/app/Probe.aspx", headers);
            Assert.True(response.StatusCode == HttpStatusCode.OK, body + server.Output);
            return VirtualPathSampleServer.Values(body);
        }
    }

    // The tests start servers of their own, one at a time.
    [CollectionDefinition(Name)]
    public class ForwardedHeadersCollection
    {
        public const string Name = "Forwarded headers";
    }
}
