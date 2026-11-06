using System.Net;
using System.Security.Principal;
using Xunit;

namespace IISGate
{
    // #165: what an anonymous request has of Windows identity, on test/webForCore.Requests.Sample's identity/ pages
    // (recorded for CI's iis-compare job). Off Windows there is no Windows token: the worker request's GetUserToken() is
    // IntPtr.Zero, so LOGON_USER and AUTH_TYPE are empty, as IIS gives them for an anonymous request, and
    // Request.LogonUserIdentity is null, as .NET Framework's HttpWorkerRequest.GetLogonUserIdentity returns for a worker
    // request without a token. They threw PlatformNotSupportedException (WindowsIdentity.GetCurrent). On Windows the
    // worker request gives the worker process's account, as before.
    [Collection(RequestsSampleCollection.Name)]
    public class AnonymousIdentityTests
    {
        private readonly RequestsSampleServer _server;

        public AnonymousIdentityTests(RequestsSampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task LogonUserAuthTypeAndLogonUserIdentity()
        {
            var (response, body) = await Get("/identity/Anonymous.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("User: [] IsAuthenticated False\n", body);
            if (OperatingSystem.IsWindows())
            {
                string name = WindowsIdentity.GetCurrent().Name;
                Assert.Contains($"LOGON_USER: [{name}]\n", body);
                Assert.Contains("AUTH_TYPE: [NTLM]\n", body);
                Assert.Contains($"LogonUserIdentity: [{name}] AuthenticationType [NTLM] IsAuthenticated True\n", body);
            }
            else
            {
                Assert.Contains("LOGON_USER: []\n", body);
                Assert.Contains("AUTH_TYPE: []\n", body);
                Assert.Contains("LogonUserIdentity: (null)\n", body);
            }
        }

        // <identity impersonate="true"/>: off Windows there is no token, and ASP.NET impersonates only a token
        // (ClientImpersonationContext), so the page runs as the worker process's user.
        [Fact]
        public async Task APageUnderImpersonationRuns()
        {
            var (response, body) = await Get("/identity/impersonate/Default.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("ran\n", body);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Contains("LOGON_USER: []\n", body);
                Assert.Contains("AUTH_TYPE: []\n", body);
            }
        }

        private async Task<(HttpResponseMessage Response, string Body)> Get(string path)
        {
            var response = await _server.Client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            RequestHandlingTests.Record("GET " + path, response, body);
            return (response, body);
        }
    }
}
