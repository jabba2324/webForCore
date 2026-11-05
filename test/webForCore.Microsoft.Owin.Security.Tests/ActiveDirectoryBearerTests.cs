// webForCore: added to the lifted Katana tests (not from aspnet/AspNetKatana), which have no unit tests of the Active
// Directory middleware (its functional test, WaadAuthentication, deploys to IIS Express or HttpListener and asks Azure).
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin.Security.ActiveDirectory;
using Microsoft.Owin.Testing;
using Owin;
using Xunit;

namespace Microsoft.Owin.Security.Tests.ActiveDirectory
{
    // UseWindowsAzureActiveDirectoryBearerAuthentication, built against IdentityModel 8: the tenant's federation
    // metadata (here from a handler standing in for login.windows.net) gives the issuer and signing certificate,
    // and a JWT they validate authenticates the request.
    public class ActiveDirectoryBearerTests
    {
        private const string Tenant = "contoso.onmicrosoft.com";
        private const string Issuer = "https://sts.windows.net/00000000-0000-0000-0000-0000000000a1/";
        private const string Audience = "api://webforcore-ad-test";

        private static readonly X509Certificate2 Certificate = CreateCertificate();

        [Fact]
        public async Task TokenSignedWithTheMetadataCertificateAuthenticates()
        {
            var metadata = new MetadataHandler();
            using TestServer server = CreateServer(metadata);
            HttpResponseMessage response = await GetAsync(server, CreateToken(new X509SecurityKey(Certificate), Issuer, Audience));

            Assert.Equal("Alice Example", await response.Content.ReadAsStringAsync());
            // Katana 4.2.3's address for a tenant's metadata (SecurityTokenServiceAddressFormat).
            Assert.Equal("https://login.windows.net/" + Tenant + "/federationmetadata/2007-06/federationmetadata.xml", metadata.RequestedUri);
        }

        [Theory]
        [InlineData("other key")]
        [InlineData("other issuer")]
        [InlineData("other audience")]
        public async Task OtherTokensDoNot(string problem)
        {
            using TestServer server = CreateServer(new MetadataHandler());
            SecurityKey key = problem == "other key" ? new RsaSecurityKey(RSA.Create(2048)) : new X509SecurityKey(Certificate);
            string token = CreateToken(key, problem == "other issuer" ? "https://sts.example.org/" : Issuer, problem == "other audience" ? "api://other" : Audience);

            HttpResponseMessage response = await GetAsync(server, token);

            Assert.Equal("anonymous", await response.Content.ReadAsStringAsync());
        }

        private static Task<HttpResponseMessage> GetAsync(TestServer server, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "http://example.org/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return server.HttpClient.SendAsync(request);
        }

        private static string CreateToken(SecurityKey key, string issuer, string audience)
        {
            return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Subject = new ClaimsIdentity(new[] { new Claim("name", "Alice Example"), new Claim("oid", "a11ce") }),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            });
        }

        private static TestServer CreateServer(MetadataHandler metadata)
        {
            return TestServer.Create(app =>
            {
                app.UseWindowsAzureActiveDirectoryBearerAuthentication(new WindowsAzureActiveDirectoryBearerAuthenticationOptions
                {
                    Tenant = Tenant,
                    TokenValidationParameters = new TokenValidationParameters { ValidAudience = Audience, NameClaimType = "name" },
                    BackchannelHttpHandler = metadata,
                });
                app.Run(context =>
                {
                    ClaimsIdentity identity = context.Authentication.User?.Identity as ClaimsIdentity;
                    return context.Response.WriteAsync(identity == null || !identity.IsAuthenticated ? "anonymous" : identity.Name);
                });
            });
        }

        private static X509Certificate2 CreateCertificate()
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=webForCore AD test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
        }

        // The tenant's WS-Federation metadata, as Entra ID serves it (without its signature).
        private sealed class MetadataHandler : HttpMessageHandler
        {
            public string RequestedUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestedUri = request.RequestUri.AbsoluteUri;
                string xml =
                    "<EntityDescriptor xmlns=\"urn:oasis:names:tc:SAML:2.0:metadata\" ID=\"_metadata\" entityID=\"" + Issuer + "\">" +
                    "<RoleDescriptor xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:fed=\"http://docs.oasis-open.org/wsfed/federation/200706\" " +
                    "xsi:type=\"fed:SecurityTokenServiceType\" protocolSupportEnumeration=\"http://docs.oasis-open.org/wsfed/federation/200706\">" +
                    "<KeyDescriptor use=\"signing\"><KeyInfo xmlns=\"http://www.w3.org/2000/09/xmldsig#\"><X509Data><X509Certificate>" +
                    Convert.ToBase64String(Certificate.RawData) +
                    "</X509Certificate></X509Data></KeyInfo></KeyDescriptor>" +
                    "<fed:PassiveRequestorEndpoint><wsa:EndpointReference xmlns:wsa=\"http://www.w3.org/2005/08/addressing\">" +
                    "<wsa:Address>https://login.microsoftonline.com/" + Tenant + "/wsfed</wsa:Address></wsa:EndpointReference></fed:PassiveRequestorEndpoint>" +
                    "</RoleDescriptor></EntityDescriptor>";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") });
            }
        }
    }
}
