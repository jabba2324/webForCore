using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using System.Web.Http;

namespace WebApiSample.Controllers
{
    // What System.Diagnostics.Trace's listeners received: Web API's trace records (WebApiConfig's
    // EnableSystemDiagnosticsTracing), for WebApi.Gate.Tests' TracingTests; and what Web.config's <system.diagnostics>
    // created (trace listeners, sources, shared listeners, switches), for its SystemDiagnosticsTests (#213).
    [RoutePrefix("api/trace")]
    public class TraceController : ApiController
    {
        // A request's records, as one listener received them: from the first that contains the text (a marker in its
        // URL, which its first record has) to the next request's first record.
        [Route("records")]
        public string[] GetRecords(string contains, string listener = "code")
        {
            string[] records = TraceRecordListener.Snapshot().Where(r => r.StartsWith(listener + " ", StringComparison.Ordinal)).ToArray();
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

        // Trace's settings and listeners: <trace autoflush indentsize><listeners>, and <assert assertuienabled>.
        [Route("config")]
        public object GetConfig()
        {
            DefaultTraceListener defaultListener = Trace.Listeners.OfType<DefaultTraceListener>().FirstOrDefault();
            return new
            {
                Trace.AutoFlush,
                Trace.IndentSize,
                Trace.UseGlobalLock,
                AssertUiEnabled = defaultListener == null ? (bool?)null : defaultListener.AssertUiEnabled,
                Listeners = Trace.Listeners.Cast<TraceListener>().Select(Describe).ToArray(),
            };
        }

        // Trace sources as <sources> and <sharedListeners> configure them, and what a trace through each reaches: the
        // records with the marker that the sample's listeners received.
        [Route("sources")]
        public object GetSources(string marker)
        {
            return new[] { "WebApiSample.Orders", "WebApiSample.Payments", "WebApiSample.Custom", "WebApiSample.Unconfigured" }
                .Select(name =>
                {
                    TraceSource source = name == "WebApiSample.Custom" ? new SampleTraceSource(name) : new TraceSource(name);
                    source.TraceEvent(TraceEventType.Information, 1, marker + " information from " + name);
                    source.TraceEvent(TraceEventType.Warning, 2, marker + " warning from " + name);
                    source.TraceEvent(TraceEventType.Error, 3, marker + " error from " + name);
                    return new
                    {
                        source.Name,
                        Switch = DescribeSwitch(source.Switch),
                        Listeners = source.Listeners.Cast<TraceListener>().Select(Describe).ToArray(),
                        Attributes = Sorted(source.Attributes),
                        Records = TraceRecordListener.Snapshot().Where(r => r.Contains(marker + " ") && r.EndsWith(" from " + name, StringComparison.Ordinal)).ToArray(),
                    };
                })
                .ToArray();
        }

        // Switches as <switches> sets them.
        [Route("switches")]
        public object GetSwitches()
        {
            return new
            {
                Enabled = new BooleanSwitch("WebApiSample.Enabled", "A BooleanSwitch <switches> sets").Enabled,
                Level = new TraceSwitch("WebApiSample.Level", "A TraceSwitch <switches> sets").Level.ToString(),
                Unconfigured = new TraceSwitch("WebApiSample.NotInSwitches", "A TraceSwitch <switches> doesn't list", "Info").Level.ToString(),
                Owned = DescribeSwitch(new SampleSwitch("WebApiSample.Owned", "A switch with an attribute")),
                Payments = DescribeSwitch(new SourceSwitch("PaymentsSwitch")),
            };
        }

        // Trace.Refresh() reads <system.diagnostics> again: the settings of a source and a switch changed in code go back
        // to configuration's.
        [Route("refresh/sources")]
        public object GetRefreshSources()
        {
            var payments = new TraceSource("WebApiSample.Payments");
            var orders = new TraceSource("WebApiSample.Orders");
            var level = new TraceSwitch("WebApiSample.Level", "A TraceSwitch <switches> sets");
            payments.Switch.Level = SourceLevels.All;
            orders.Switch.Level = SourceLevels.All;
            orders.Listeners.Clear();
            level.Level = TraceLevel.Verbose;
            var before = new
            {
                Payments = payments.Switch.Level.ToString(),
                Orders = orders.Switch.Level.ToString(),
                OrdersListeners = orders.Listeners.Cast<TraceListener>().Select(l => l.Name).ToArray(),
                Level = level.Level.ToString(),
            };
            List<TraceListener> saved = Trace.Listeners.Cast<TraceListener>().ToList();
            try
            {
                Trace.Refresh();
            }
            finally
            {
                RestoreAndRestart(saved);
            }
            return new
            {
                Before = before,
                After = new
                {
                    Payments = payments.Switch.Level.ToString(),
                    Orders = orders.Switch.Level.ToString(),
                    OrdersListeners = orders.Listeners.Cast<TraceListener>().Select(l => l.Name).ToArray(),
                    Level = level.Level.ToString(),
                },
            };
        }

        // Trace.Refresh() and Trace's own listeners: on .NET Framework, Trace.Listeners is <trace><listeners> again, new
        // instances (the listener Application_Start added is gone), and autoflush and indentsize are configuration's.
        // The application restarts afterwards (RestoreAndRestart).
        [Route("refresh/listeners")]
        public object GetRefreshListeners()
        {
            List<TraceListener> saved = Trace.Listeners.Cast<TraceListener>().ToList();
            Trace.AutoFlush = false;
            Trace.IndentSize = 8;
            try
            {
                Trace.Refresh();
                return new
                {
                    Listeners = Trace.Listeners.Cast<TraceListener>().Select(l => l.Name).ToArray(),
                    New = Trace.Listeners.Cast<TraceListener>().Where(l => !(l is DefaultTraceListener)).Select(l => !saved.Contains(l)).ToArray(),
                    Trace.AutoFlush,
                    Trace.IndentSize,
                };
            }
            finally
            {
                RestoreAndRestart(saved);
            }
        }

        // TextWriterTraceListener, DelimitedListTraceListener and XmlWriterTraceListener from <sharedListeners>, writing
        // to files whose relative paths (initializeData) are under Web.config's folder: the lines of each with the marker.
        [Route("files")]
        public object GetFiles(string marker)
        {
            string logs = HostingEnvironment.MapPath("~/Logs");
            Directory.CreateDirectory(logs);
            var source = new TraceSource("WebApiSample.Files");
            source.TraceEvent(TraceEventType.Warning, 7, marker);
            source.Flush();
            return new
            {
                Listeners = source.Listeners.Cast<TraceListener>().Select(Describe).ToArray(),
                Text = ReadLines(Path.Combine(logs, "text.log"), marker),
                Delimited = ReadLines(Path.Combine(logs, "delimited.log"), marker),
                Xml = ReadLines(Path.Combine(logs, "trace.svclog"), marker),
            };
        }

        // A source whose listener is an EventLogTraceListener (initializeData: the event source).
        [Route("eventlog")]
        public object GetEventLog()
        {
            try
            {
                var source = new TraceSource("WebApiSample.EventLog");
                TraceListener listener = source.Listeners.Cast<TraceListener>().Single();
                var eventLog = listener as EventLogTraceListener;
                return new { Listener = Describe(listener), EventSource = eventLog == null ? null : eventLog.EventLog.Source };
            }
            catch (Exception e)
            {
                return Error(e);
            }
        }

        // A source whose listener's type doesn't exist: the error configuration gives.
        [Route("broken")]
        public object GetBroken()
        {
            try
            {
                var source = new TraceSource("WebApiSample.Broken");
                return source.Listeners.Cast<TraceListener>().Select(l => l.Name).ToArray();
            }
            catch (Exception e)
            {
                return Error(e);
            }
            finally
            {
                // On .NET Framework the source stays broken: each use, and Trace.Refresh() (which refreshes every live
                // source), throws the error again. So the application restarts after the response, for the requests
                // that follow.
                System.Web.HttpRuntime.UnloadAppDomain();
            }
        }

        private static object Error(Exception e)
        {
            return new { Error = e.GetType().FullName, e.Message, Inner = e.InnerException == null ? null : e.InnerException.GetType().FullName };
        }

        // Trace.Refresh() changes more than Trace: the reference source's DiagnosticsConfiguration.Refresh() clears the
        // attributes of the section's sources, shared listeners and switches, and ASP.NET's configuration system doesn't read
        // the section again (HttpConfigurationSystem.RefreshConfig does nothing), so they stay cleared. So the application
        // restarts after its response, and the requests that follow see Web.config's section as at the start, on IIS too.
        private static void RestoreAndRestart(List<TraceListener> listeners)
        {
            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(listeners.ToArray());
            Trace.AutoFlush = true;
            Trace.IndentSize = 2;
            System.Web.HttpRuntime.UnloadAppDomain();
        }

        private static string[] ReadLines(string path, string marker)
        {
            if (!File.Exists(path)) return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains(marker)).ToArray();
            }
        }

        private static object Describe(TraceListener listener)
        {
            var filter = listener.Filter as EventTypeFilter;
            return new
            {
                listener.Name,
                Type = listener.GetType().FullName,
                TraceOutputOptions = listener.TraceOutputOptions.ToString(),
                Filter = listener.Filter == null ? null : listener.Filter.GetType().FullName,
                FilterEventType = filter == null ? null : filter.EventType.ToString(),
                Attributes = Sorted(listener.Attributes),
            };
        }

        private static object DescribeSwitch(Switch sw)
        {
            var source = sw as SourceSwitch;
            var sample = sw as SampleSwitch;
            return new
            {
                Type = sw.GetType().FullName,
                sw.DisplayName,
                Level = source == null ? null : source.Level.ToString(),
                Setting = sample == null ? (int?)null : sample.Setting,
                Attributes = Sorted(sw.Attributes),
            };
        }

        private static SortedDictionary<string, string> Sorted(System.Collections.Specialized.StringDictionary attributes)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in attributes)
            {
                sorted[(string)entry.Key] = (string)entry.Value;
            }
            return sorted;
        }
    }

    // A trace source and a switch that accept an attribute in configuration ("owner"): .NET Framework rejects an
    // attribute that GetSupportedAttributes doesn't list.
    public class SampleTraceSource : TraceSource
    {
        public SampleTraceSource(string name) : base(name)
        {
        }

        protected override string[] GetSupportedAttributes()
        {
            return new[] { "owner" };
        }
    }

    public class SampleSwitch : Switch
    {
        public SampleSwitch(string displayName, string description) : base(displayName, description)
        {
        }

        public int Setting
        {
            get { return SwitchSetting; }
        }

        protected override string[] GetSupportedAttributes()
        {
            return new[] { "owner" };
        }
    }
}
