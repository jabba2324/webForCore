using System.Collections.Generic;
using System.Threading;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin.Security.Jwt;

namespace OidcSample
{
    // The issuer and signing keys of an OpenID Connect provider, from its discovery document, for Katana's JwtFormat:
    // the class Microsoft's Azure AD Web API samples for Katana add to the application.
    public class OpenIdConnectCachingSecurityTokenProvider : IIssuerSecurityKeyProvider
    {
        private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

        public OpenIdConnectCachingSecurityTokenProvider(string metadataEndpoint)
        {
            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(metadataEndpoint, new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = false });
        }

        public string Issuer
        {
            get { return Configuration.Issuer; }
        }

        public IEnumerable<SecurityKey> SecurityKeys
        {
            get { return Configuration.SigningKeys; }
        }

        private OpenIdConnectConfiguration Configuration
        {
            get { return _configurationManager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        }
    }
}
