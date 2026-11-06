using System.Text.Json;
using Xunit;

namespace WebApiGate
{
    // System.Web.Http.Tracing (Microsoft.AspNet.WebApi.Tracing): the sample's WebApiConfig calls
    // config.EnableSystemDiagnosticsTracing(), as "Tracing in ASP.NET Web API 2" does, and Web API's
    // SystemDiagnosticsTraceWriter writes each request's trace records through System.Diagnostics.Trace to its
    // listeners: here the sample's TraceRecordListener, added in Application_Start, which /api/trace reads back.
    public partial class GateTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("/owin")]
        public async Task ARequestWritesItsTraceRecordsThroughSystemDiagnosticsTrace(string prefix)
        {
            string marker = "marker=" + Guid.NewGuid().ToString("N");
            string url = new Uri(_server.BaseAddress, prefix + "/api/values?" + marker).ToString();
            Assert.Equal(200, (int)(await _server.Client.GetAsync(url)).StatusCode);

            HttpResponseMessage response = await _server.Client.GetAsync(prefix + "/api/trace/records?contains=" + Uri.EscapeDataString(marker));
            Assert.Equal(200, (int)response.StatusCode);
            string[] records = JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync());

            // SystemDiagnosticsTraceWriter's defaults (MinimumLevel Info, IsVerbose false), in its format, each record
            // through Trace.TraceInformation: the request's begin record, Web API's pipeline (WebApiConfig's tracers),
            // and the end record, with Web API's request URL. After the response come the formatter's write and the
            // controller's Dispose.
            string[] expected =
            {
                $"code Information: Request, Method=GET, Url={url}, Message='{url}'",
                "code Information: Message='Values', Operation=DefaultHttpControllerSelector.SelectController",
                "code Information: Message='WebApiSample.Controllers.ValuesController', Operation=DefaultHttpControllerActivator.Create",
                "code Information: Message='WebApiSample.Controllers.ValuesController', Operation=HttpControllerDescriptor.CreateController",
                "code Information: Message='Selected action 'Get()'', Operation=ApiControllerActionSelector.SelectAction",
                "code Information: Operation=HttpActionBinding.ExecuteBindingAsync",
                "code Information: Message='Action returned 'System.String[]'', Operation=ReflectedHttpActionDescriptor.ExecuteAsync",
                "code Information: Message='Will use same 'JsonMediaTypeFormatter' formatter', Operation=JsonMediaTypeFormatter.GetPerRequestFormatterInstance",
                "code Information: Message='Selected formatter='JsonMediaTypeFormatter', content-type='application/json; charset=utf-8'', Operation=DefaultContentNegotiator.Negotiate",
                "code Information: Operation=ApiControllerActionInvoker.InvokeActionAsync, Status=200 (OK)",
                "code Information: Operation=ValuesController.ExecuteAsync, Status=200 (OK)",
                $"code Information: Response, Status=200 (OK), Method=GET, Url={url}, Message='Content-type='application/json; charset=utf-8', content-length=unknown'",
            };
            Assert.True(records.Length >= expected.Length, string.Join("\n", records));
            Assert.Equal(expected, records.Take(expected.Length));
            Assert.Contains("code Information: Operation=JsonMediaTypeFormatter.WriteToStreamAsync", records.Skip(expected.Length));
        }

        // webForCore doesn't apply Web.config's <system.diagnostics>: its listeners aren't created, so only the
        // default listener and the one Application_Start adds receive the records. On .NET Framework, System.dll's
        // DiagnosticsConfiguration reads the section through ASP.NET's configuration system, and Trace.Listeners also
        // has "webconfig", as CI's iis-compare job checks on IIS 10. .NET's System.Diagnostics reads configuration only
        // when System.Configuration.ConfigurationManager's System.Diagnostics.TraceConfiguration.Register() is called,
        // and webForCore replaces that assembly with .NET Framework's System.Configuration, which has no such type and
        // whose machine.config declares the section as an IgnoreSection. docs/NAMESPACES.md and docs/MIGRATING.md say
        // to add listeners in code.
        [Fact]
        public async Task WebConfigTraceListenersAreNotApplied()
        {
            HttpResponseMessage response = await _server.Client.GetAsync("/api/trace/listeners");
            string body = await response.Content.ReadAsStringAsync();
            Record("trace GET /api/trace/listeners", null, response, body);
            string[] listeners = JsonSerializer.Deserialize<string[]>(body);
            Assert.Equal(new[] { "Default", "code" }, listeners);
        }
    }
}
