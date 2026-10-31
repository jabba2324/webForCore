// webForCore: System.Net.Http.WebRequest's WebRequestHandler, written from its documented API (MIT license). .NET
// Framework's HttpClient handler over HttpWebRequest; .NET has no System.Net.Http.WebRequest.dll, so libraries that
// create one (every Katana social provider: `options.BackchannelHttpHandler ?? new WebRequestHandler()`) failed to load
// (#18: OpenOrderFramework and SocialGoal, every request). Here it is an HttpClientHandler: the settings .NET's handler
// has are passed to it, and the HttpWebRequest-only ones are kept but have no effect.
using System.Net.Cache;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace System.Net.Http
{
    public class WebRequestHandler : HttpClientHandler
    {
        private RemoteCertificateValidationCallback _serverCertificateValidationCallback;

        public WebRequestHandler()
        {
        }

        /// <summary>Kept for compatibility: .NET doesn't pipeline HTTP/1.1 requests.</summary>
        public bool AllowPipelining { get; set; } = true;

        /// <summary>Kept for compatibility (HttpWebRequest's mutual authentication level).</summary>
        public AuthenticationLevel AuthenticationLevel { get; set; } = AuthenticationLevel.MutualAuthRequested;

        /// <summary>Kept for compatibility: HttpClient doesn't use the WinINet cache.</summary>
        public RequestCachePolicy CachePolicy { get; set; }

        /// <summary>Kept for compatibility (the 100-Continue wait, 350 ms on .NET Framework).</summary>
        public TimeSpan ContinueTimeout { get; set; } = TimeSpan.FromMilliseconds(350);

        /// <summary>Kept for compatibility (HttpWebRequest's impersonation level).</summary>
        public TokenImpersonationLevel ImpersonationLevel { get; set; } = TokenImpersonationLevel.Delegation;

        /// <summary>Kept for compatibility: HttpClient's Timeout covers reads and writes.</summary>
        public int ReadWriteTimeout { get; set; } = 300000;

        /// <summary>Kept for compatibility (HttpWebRequest's connection sharing for authenticated connections).</summary>
        public bool UnsafeAuthenticatedConnectionSharing { get; set; }

        /// <summary>
        /// Validates the server's certificate, as on .NET Framework; the callback's sender is the request message.
        /// </summary>
        public RemoteCertificateValidationCallback ServerCertificateValidationCallback
        {
            get => _serverCertificateValidationCallback;
            set
            {
                _serverCertificateValidationCallback = value;
                ServerCertificateCustomValidationCallback = value == null ? null
                    : (request, certificate, chain, errors) => value(request, certificate, chain, errors);
            }
        }
    }
}
