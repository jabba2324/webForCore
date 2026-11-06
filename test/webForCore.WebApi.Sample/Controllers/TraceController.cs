using System;
using System.Diagnostics;
using System.Linq;
using System.Web.Http;

namespace WebApiSample.Controllers
{
    // What System.Diagnostics.Trace's listeners received: Web API's trace records (WebApiConfig's
    // EnableSystemDiagnosticsTracing), for WebApi.Gate.Tests' TracingTests.
    [RoutePrefix("api/trace")]
    public class TraceController : ApiController
    {
        // A request's records: from the first that contains the text (a marker in its URL, which its first record has)
        // to the next request's first record.
        [Route("records")]
        public string[] GetRecords(string contains)
        {
            string[] records = TraceRecordListener.Snapshot();
            int first = Array.FindIndex(records, r => r.Contains(contains));
            if (first < 0) return new string[0];
            int next = Array.FindIndex(records, first + 1, r => r.Contains(": Request, "));
            return records.Skip(first).Take((next < 0 ? records.Length : next) - first).ToArray();
        }

        // The names of Trace.Listeners.
        [Route("listeners")]
        public string[] GetListeners()
        {
            return Trace.Listeners.Cast<TraceListener>().Select(l => l.Name).ToArray();
        }
    }
}
