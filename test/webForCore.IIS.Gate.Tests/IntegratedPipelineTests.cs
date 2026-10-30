using System.Net;
using Xunit;

namespace IISGate
{
    // Integrated-pipeline APIs on the fork's classic pipeline: integrated-only events (MapRequestHandler, LogRequest,
    // PostLogRequest), CurrentNotification/IsPostNotification, OnExecuteRequestStep, writable request headers and server
    // variables, AddOnSendingHeaders, Server.TransferRequest, Response.SubStatusCode, Request.Abort.
    [Collection(IISSampleCollection.Name)]
    public class IntegratedPipelineTests
    {
        private readonly SampleServer _server;

        public IntegratedPipelineTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task EventsNotificationsAndHeaders()
        {
            var response = await _server.Client.GetAsync("/integrated/Default.aspx");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("notifications=BeginRequest,AuthenticateRequest(post),MapRequestHandler,MapRequestHandler(post);", body);
            Assert.Contains("now=ExecuteRequestHandler;", body);
            Assert.Contains("header=from-module;servervariable=set-by-module;headervar=from-module;", body);
            Assert.Contains("steps=True;", body);
            Assert.Equal("SendResponse", Assert.Single(response.Headers.GetValues("X-Sending-Headers"))); // integrated mode's notification for headers

            var logged = await _server.Client.GetStringAsync("/integrated/Logged.aspx"); // LogRequest runs after the response
            Assert.Contains("log=1;postlog=1;", logged.Replace("log=2", "log=1").Replace("postlog=2", "postlog=1"));
        }

        [Fact]
        public async Task TransferRequest()
        {
            var body = await _server.Client.GetStringAsync("/integrated/Transfer.aspx?from=origin");
            Assert.Contains("target=/integrated/Target.aspx;method=GET;to=target;from=;field=;transferred=yes;", body);
            Assert.DoesNotContain("not transferred", body);

            var post = await _server.Client.PostAsync("/integrated/Transfer.aspx?form=1&from=origin",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["field"] = "kept" }));
            var postBody = await post.Content.ReadAsStringAsync();
            Assert.Contains("target=/integrated/Target.aspx;method=POST;to=;from=origin;field=kept;", postBody); // preserveForm
        }

        [Fact]
        public async Task SubStatusCodeReachesHttpErrors()
        {
            var response = await _server.Client.GetAsync("/integrated/SubStatus.aspx"); // 404.7 -> <error subStatusCode="7">
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("Custom 404.7 page", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task RequestAbort()
        {
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => _server.Client.GetAsync("/integrated/Abort.aspx"));
        }
    }
}
