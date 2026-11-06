using System.Text.Json.Nodes;
using Xunit;

namespace WebApiGate
{
    // Web.config's <system.diagnostics> (#213): .NET Framework's System.Diagnostics reads it, through ASP.NET's
    // configuration system, and creates Trace's listeners, trace sources with their switches and listeners (their own
    // and shared ones), and switches. The sample's section has each (test/webForCore.WebApi.Sample/Web.config) and
    // TraceController reports what was created; each recorded request is sent to the same sample on IIS 10 by CI's
    // iis-compare job, which must answer the same.
    public partial class GateTests
    {
        // <trace><listeners>: the default listener and the two Web.config adds, created when Trace is first used, which
        // was in Application_Start; then the one Application_Start added.
        [Fact]
        public async Task WebConfigTraceListenersAreApplied()
        {
            JsonNode listeners = await GetTrace("/api/trace/listeners");
            Assert.Equal("""["Default","webconfig","filtered","code"]""", listeners.ToJsonString());
        }

        // <trace autoflush indentsize>, <assert assertuienabled>, and each listener: its type from the application's
        // assembly, initializeData (TraceRecordListener's name), traceOutputOptions, a <filter> with its initializeData,
        // and an attribute of its own.
        [Fact]
        public async Task WebConfigTraceSettingsAndListenersAreTheConfiguredOnes()
        {
            JsonNode config = await GetTrace("/api/trace/config");
            Assert.True((bool)config["AutoFlush"]);
            Assert.Equal(2, (int)config["IndentSize"]);
            Assert.True((bool)config["UseGlobalLock"]);
            Assert.False((bool)config["AssertUiEnabled"]);
            Assert.Equal(
                Listener("Default", "System.Diagnostics.DefaultTraceListener") +
                "," + Listener("webconfig", "WebApiSample.TraceRecordListener") +
                "," + Listener("filtered", "WebApiSample.TraceRecordListener", "DateTime, ThreadId", "System.Diagnostics.EventTypeFilter", "Warning", """{"category":"warnings"}""") +
                "," + Listener("code", "WebApiSample.TraceRecordListener"),
                config["Listeners"].ToJsonString().Trim('[', ']'));
        }

        // <sources>: switchValue; switchName and switchType with the switch from <switches>; <remove name="Default"/>,
        // <clear/>, a source's own listener and a reference to <sharedListeners> (one instance for every source); an
        // attribute; and a source Web.config doesn't list (SourceLevels.Off and the default listener). Records: what
        // each source's three events (Information, Warning, Error) wrote to the sample's listeners.
        [Fact]
        public async Task WebConfigSourcesAreApplied()
        {
            string marker = "m" + Guid.NewGuid().ToString("N");
            JsonArray sources = (await GetTrace("/api/trace/sources?marker=" + marker)).AsArray();
            Assert.Equal(4, sources.Count);

            JsonNode orders = sources[0];
            Assert.Equal("WebApiSample.Orders", (string)orders["Name"]);
            Assert.Equal(Switch("System.Diagnostics.SourceSwitch", "WebApiSample.Orders", "Warning"), orders["Switch"].ToJsonString());
            Assert.Equal("[" + Listener("orders", "WebApiSample.TraceRecordListener") + "," + Listener("shared", "WebApiSample.TraceRecordListener") + "]", orders["Listeners"].ToJsonString());
            Assert.Equal("{}", orders["Attributes"].ToJsonString());
            Assert.Equal(Records(marker, "WebApiSample.Orders", "orders Warning", "shared Warning", "orders Error", "shared Error"), orders["Records"].ToJsonString());

            JsonNode payments = sources[1];
            Assert.Equal(Switch("System.Diagnostics.SourceSwitch", "PaymentsSwitch", "Error"), payments["Switch"].ToJsonString());
            Assert.Equal("[" + Listener("shared", "WebApiSample.TraceRecordListener") + "]", payments["Listeners"].ToJsonString());
            Assert.Equal(Records(marker, "WebApiSample.Payments", "shared Error"), payments["Records"].ToJsonString());

            JsonNode custom = sources[2];
            Assert.Equal(Switch("System.Diagnostics.SourceSwitch", "WebApiSample.Custom", "All"), custom["Switch"].ToJsonString());
            Assert.Equal("[" + Listener("Default", "System.Diagnostics.DefaultTraceListener") + "," + Listener("shared", "WebApiSample.TraceRecordListener") + "]", custom["Listeners"].ToJsonString());
            Assert.Equal("""{"owner":"sample"}""", custom["Attributes"].ToJsonString());
            Assert.Equal(Records(marker, "WebApiSample.Custom", "shared Information", "shared Warning", "shared Error"), custom["Records"].ToJsonString());

            JsonNode unconfigured = sources[3];
            Assert.Equal(Switch("System.Diagnostics.SourceSwitch", "WebApiSample.Unconfigured", "Off"), unconfigured["Switch"].ToJsonString());
            Assert.Equal("[" + Listener("Default", "System.Diagnostics.DefaultTraceListener") + "]", unconfigured["Listeners"].ToJsonString());
            Assert.Equal("[]", unconfigured["Records"].ToJsonString());
        }

        // <switches>: a BooleanSwitch, a TraceSwitch, one Web.config doesn't list (its default value), a switch with an
        // attribute, and the switch a source names.
        [Fact]
        public async Task WebConfigSwitchesAreApplied()
        {
            JsonNode switches = await GetTrace("/api/trace/switches");
            Assert.True((bool)switches["Enabled"]);
            Assert.Equal("Warning", (string)switches["Level"]);
            Assert.Equal("Info", (string)switches["Unconfigured"]);
            Assert.Equal("""{"Type":"WebApiSample.Controllers.SampleSwitch","DisplayName":"WebApiSample.Owned","Level":null,"Setting":3,"Attributes":{"owner":"sample"}}""",
                switches["Owned"].ToJsonString());
            Assert.Equal(Switch("System.Diagnostics.SourceSwitch", "PaymentsSwitch", "Error"), switches["Payments"].ToJsonString());
        }

        // Trace.Refresh() reads the section again: sources and switches changed in code get configuration's settings and
        // listeners back. The sample restarts after the request (TraceController: what Trace.Refresh() leaves behind in
        // ASP.NET, on .NET Framework too, isn't what the other requests expect).
        [Fact]
        public async Task TraceRefreshAppliesWebConfigToSourcesAndSwitchesAgain()
        {
            JsonNode refresh = await GetTrace("/api/trace/refresh/sources");
            Assert.Equal("""{"Payments":"All","Orders":"All","OrdersListeners":[],"Level":"Verbose"}""", refresh["Before"].ToJsonString());
            Assert.Equal("""{"Payments":"Error","Orders":"Warning","OrdersListeners":["orders","shared"],"Level":"Warning"}""", refresh["After"].ToJsonString());
        }

        // Trace.Refresh() and Trace's own listeners: on .NET Framework, Trace.Listeners is <trace><listeners> again, with
        // new instances, and autoflush and indentsize are configuration's (IIS 10: ["Default","webconfig","filtered"],
        // [true,true], true, 2). .NET's Trace.Refresh() raises Trace.Refreshing, the only point where configuration can
        // apply them, before it resets Trace's listeners to its default listener and autoflush and indentsize to their
        // defaults, so webForCore can't apply them again (docs/NAMESPACES.md; a known difference in iis-compare).
        [Fact]
        public async Task TraceRefreshLeavesTraceWithDotNetsDefaults()
        {
            JsonNode refresh = await GetTrace("/api/trace/refresh/listeners");
            Assert.Equal("""{"Listeners":["Default"],"New":[],"AutoFlush":false,"IndentSize":4}""", refresh.ToJsonString());
            // The sample restarts after the request (TraceController), with Web.config's listeners and the one
            // Application_Start adds.
            Assert.Equal("""["Default","webconfig","filtered","code"]""", (await GetTrace("/api/trace/listeners")).ToJsonString());
        }

        // A listener whose type doesn't exist: the source fails with .NET Framework's ConfigurationErrorsException when
        // it is first used.
        [Fact]
        public async Task AListenerOfAnUnknownTypeIsAConfigurationError()
        {
            JsonNode broken = await GetTrace("/api/trace/broken");
            Assert.Equal("System.Configuration.ConfigurationErrorsException", (string)broken["Error"]);
            Assert.Equal("Couldn't find type for class WebApiSample.NoSuchListener, webForCore.WebApi.Sample.", (string)broken["Message"]);
            Assert.Null(broken["Inner"]);
        }

        // TextWriterTraceListener, DelimitedListTraceListener (with its delimiter attribute and traceOutputOptions) and
        // XmlWriterTraceListener from <sharedListeners>, by type names without an assembly and with System's, each
        // writing to the file its initializeData names, relative to Web.config's folder. Not sent to IIS: its
        // application pool can't write to the site's folder.
        [Fact]
        public async Task FileListenersWriteToFilesRelativeToWebConfig()
        {
            string marker = "m" + Guid.NewGuid().ToString("N");
            HttpResponseMessage response = await _server.Client.GetAsync("/api/trace/files?marker=" + marker);
            JsonNode files = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(200, (int)response.StatusCode);
            Assert.Equal(
                "[" + Listener("text", "System.Diagnostics.TextWriterTraceListener") +
                "," + Listener("delimited", "System.Diagnostics.DelimitedListTraceListener", "ProcessId", attributes: """{"delimiter":"|"}""") +
                "," + Listener("xml", "System.Diagnostics.XmlWriterTraceListener") + "]",
                files["Listeners"].ToJsonString());
            Assert.Equal($"""["WebApiSample.Files Warning: 7 : {marker}"]""", files["Text"].ToJsonString());
            string delimited = (string)files["Delimited"].AsArray().Single();
            Assert.StartsWith($"\"WebApiSample.Files\"|Warning|7|\"{marker}\"||", delimited);
            Assert.Contains($"<ApplicationData>{marker}</ApplicationData>", (string)files["Xml"].AsArray().Single());
            Assert.True(File.Exists(Path.Combine(_server.SampleDirectory, "Logs", "text.log")));
        }

        // EventLogTraceListener (initializeData: the event source), by the type name .NET Framework's System.dll had. On
        // Windows it is created; elsewhere .NET's System.Diagnostics.EventLog doesn't support event logs, and creating
        // it is a configuration error. Not sent to IIS (the gate's recording is Linux's).
        [Fact]
        public async Task AnEventLogTraceListenerIsCreatedOnWindows()
        {
            JsonNode eventLog = await GetTrace("/api/trace/eventlog", record: false);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("{\"Listener\":" + Listener("eventlog", "System.Diagnostics.EventLogTraceListener") + ",\"EventSource\":\"WebApiSample\"}", eventLog.ToJsonString());
            }
            else
            {
                Assert.Equal("System.Configuration.ConfigurationErrorsException", (string)eventLog["Error"]);
                Assert.Equal("Could not create System.Diagnostics.EventLogTraceListener.", (string)eventLog["Message"]);
                Assert.Equal("System.PlatformNotSupportedException", (string)eventLog["Inner"]);
            }
        }

        private async Task<JsonNode> GetTrace(string pathAndQuery, bool record = true)
        {
            HttpResponseMessage response = await _server.Client.GetAsync(pathAndQuery);
            string body = await response.Content.ReadAsStringAsync();
            if (record)
            {
                Record("trace GET " + pathAndQuery.Split('?')[0], null, response, body);
            }
            Assert.Equal(200, (int)response.StatusCode);
            return JsonNode.Parse(body);
        }

        private static string Listener(string name, string type, string options = "None", string filter = null, string filterEventType = null, string attributes = "{}")
        {
            return new JsonObject
            {
                ["Name"] = name,
                ["Type"] = type,
                ["TraceOutputOptions"] = options,
                ["Filter"] = filter,
                ["FilterEventType"] = filterEventType,
                ["Attributes"] = JsonNode.Parse(attributes),
            }.ToJsonString();
        }

        private static string Switch(string type, string displayName, string level)
        {
            return new JsonObject
            {
                ["Type"] = type,
                ["DisplayName"] = displayName,
                ["Level"] = level,
                ["Setting"] = null,
                ["Attributes"] = new JsonObject(),
            }.ToJsonString();
        }

        private static string Records(string marker, string source, params string[] records)
        {
            return new JsonArray(records.Select(r => (JsonNode)JsonValue.Create(
                $"{r}: {marker} {r.Split(' ')[1].ToLowerInvariant()} from {source}")).ToArray()).ToJsonString();
        }
    }
}
