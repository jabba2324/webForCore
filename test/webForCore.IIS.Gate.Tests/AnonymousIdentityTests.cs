using System.Net;
using System.Security.Principal;
using Xunit;

namespace IISGate
{
    // #165: what an anonymous request has of Windows identity, on test/webForCore.Requests.Sample's identity/ pages
    // (recorded for CI's iis-compare job). LOGON_USER and AUTH_TYPE are empty on both platforms, as IIS gives them for an
    // anonymous request. Off Windows there is no Windows token: the worker request's GetUserToken() is IntPtr.Zero, so
    // Request.LogonUserIdentity is null, as .NET Framework's HttpWorkerRequest.GetLogonUserIdentity returns for a worker
    // request without a token; it threw PlatformNotSupportedException (WindowsIdentity.GetCurrent). On Windows the worker
    // request has the worker process's token, as before, and LogonUserIdentity is its account, unauthenticated.
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
            Assert.Contains("LOGON_USER: []\n", body);
            Assert.Contains("AUTH_TYPE: []\n", body);
            Assert.Contains("User: [] IsAuthenticated False\n", body);
            if (OperatingSystem.IsWindows())
            {
                Assert.Contains($"LogonUserIdentity: [{WindowsIdentity.GetCurrent().Name}] AuthenticationType [", body);
                Assert.Contains("] IsAuthenticated False\n", body);
            }
            else
            {
                Assert.Contains("LogonUserIdentity: (null)\n", body);
            }
        }

        // <identity impersonate="true"/>: off Windows there is no token, and ASP.NET impersonates only a token
        // (ClientImpersonationContext), so the page runs as the worker process's user. Not on Windows: there the worker
        // request's token is the process's, and impersonating it fails (a 500, unchanged by #165).
        [NotOnWindowsFact("the page answers 500 there: impersonating the worker request's token (the process's, from WindowsIdentity.GetCurrent) fails; unchanged by #165")]
        public async Task APageUnderImpersonationRuns()
        {
            var (response, body) = await Get("/identity/impersonate/Default.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("LOGON_USER: []\n", body);
            Assert.Contains("AUTH_TYPE: []\n", body);
            Assert.Contains("ran\n", body);
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
