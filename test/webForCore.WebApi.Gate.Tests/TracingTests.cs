using System.Text.Json;
using Xunit;

namespace WebApiGate
{
    // System.Web.Http.Tracing (Microsoft.AspNet.WebApi.Tracing): the sample's WebApiConfig calls
    // config.EnableSystemDiagnosticsTracing(), as "Tracing in ASP.NET Web API 2" does, and Web API's
    // SystemDiagnosticsTraceWriter writes each request's trace records through System.Diagnostics.Trace to its
    // listeners: the sample's TraceRecordListener added in Application_Start ("code"), and the one Web.config's
    // <system.diagnostics><trace><listeners> adds ("webconfig", #213), which /api/trace reads back.
    public partial class GateTests
    {
        [Theory]
        [InlineData("", "code")]
        [InlineData("/owin", "code")]
        [InlineData("", "webconfig")]
        [InlineData("/owin", "webconfig")]
        public async Task ARequestWritesItsTraceRecordsThroughSystemDiagnosticsTrace(string prefix, string listener)
        {
            string marker = "marker=" + Guid.NewGuid().ToString("N");
            string url = new Uri(_server.BaseAddress, prefix + "/api/values?" + marker).ToString();
            Assert.Equal(200, (int)(await _server.Client.GetAsync(url)).StatusCode);

            HttpResponseMessage response = await _server.Client.GetAsync(prefix + "/api/trace/records?contains=" + Uri.EscapeDataString(marker) + "&listener=" + listener);
            Assert.Equal(200, (int)response.StatusCode);
            string[] records = JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync());

            // SystemDiagnosticsTraceWriter's defaults (MinimumLevel Info, IsVerbose false), in its format, each record
            // through Trace.TraceInformation: the request's begin record, Web API's pipeline (WebApiConfig's tracers),
            // and the end record, with Web API's request URL. After the response come the formatter's write and the
            // controller's Dispose.
            string[] expected =
            {
                $"Information: Request, Method=GET, Url={url}, Message='{url}'",
                "Information: Message='Values', Operation=DefaultHttpControllerSelector.SelectController",
                "Information: Message='WebApiSample.Controllers.ValuesController', Operation=DefaultHttpControllerActivator.Create",
                "Information: Message='WebApiSample.Controllers.ValuesController', Operation=HttpControllerDescriptor.CreateController",
                "Information: Message='Selected action 'Get()'', Operation=ApiControllerActionSelector.SelectAction",
                "Information: Operation=HttpActionBinding.ExecuteBindingAsync",
                "Information: Message='Action returned 'System.String[]'', Operation=ReflectedHttpActionDescriptor.ExecuteAsync",
                "Information: Message='Will use same 'JsonMediaTypeFormatter' formatter', Operation=JsonMediaTypeFormatter.GetPerRequestFormatterInstance",
                "Information: Message='Selected formatter='JsonMediaTypeFormatter', content-type='application/json; charset=utf-8'', Operation=DefaultContentNegotiator.Negotiate",
                "Information: Operation=ApiControllerActionInvoker.InvokeActionAsync, Status=200 (OK)",
                "Information: Operation=ValuesController.ExecuteAsync, Status=200 (OK)",
                $"Information: Response, Status=200 (OK), Method=GET, Url={url}, Message='Content-type='application/json; charset=utf-8', content-length=unknown'",
            };
            expected = expected.Select(r => listener + " " + r).ToArray();
            Assert.True(records.Length >= expected.Length, string.Join("\n", records));
            Assert.Equal(expected, records.Take(expected.Length));
            Assert.Contains(listener + " Information: Operation=JsonMediaTypeFormatter.WriteToStreamAsync", records.Skip(expected.Length));
        }
    }
}
