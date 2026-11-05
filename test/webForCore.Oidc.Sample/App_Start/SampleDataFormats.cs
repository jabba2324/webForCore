using System;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.Owin.Security.DataHandler.Serializer;
using Microsoft.Owin.Security.DataProtection;

namespace OidcSample
{
    // The application's own ticket format for its sign-in cookie (CookieAuthenticationOptions.TicketDataFormat): Katana's
    // serializer and the app's data protector, with a version prefix the application checks.
    public class SampleTicketDataFormat : ISecureDataFormat<AuthenticationTicket>
    {
        public const string Prefix = "wfc1.";
        private readonly IDataProtector _protector;

        public SampleTicketDataFormat(IDataProtector protector)
        {
            _protector = protector;
        }

        public string Protect(AuthenticationTicket data)
        {
            return Prefix + TextEncodings.Base64Url.Encode(_protector.Protect(DataSerializers.Ticket.Serialize(data)));
        }

        public AuthenticationTicket Unprotect(string protectedText)
        {
            if (protectedText == null || !protectedText.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            try
            {
                return DataSerializers.Ticket.Deserialize(_protector.Unprotect(TextEncodings.Base64Url.Decode(protectedText.Substring(Prefix.Length))));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // ... and for OpenID Connect's state and nonce cookie (OpenIdConnectAuthenticationOptions.StateDataFormat).
    public class SampleStateDataFormat : ISecureDataFormat<AuthenticationProperties>
    {
        public const string Prefix = "st1.";
        private readonly IDataProtector _protector;

        public SampleStateDataFormat(IDataProtector protector)
        {
            _protector = protector;
        }

        public string Protect(AuthenticationProperties data)
        {
            return Prefix + TextEncodings.Base64Url.Encode(_protector.Protect(DataSerializers.Properties.Serialize(data)));
        }

        public AuthenticationProperties Unprotect(string protectedText)
        {
            if (protectedText == null || !protectedText.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            try
            {
                return DataSerializers.Properties.Deserialize(_protector.Unprotect(TextEncodings.Base64Url.Decode(protectedText.Substring(Prefix.Length))));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
