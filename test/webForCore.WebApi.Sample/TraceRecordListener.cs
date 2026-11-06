using System.Collections.Generic;
using System.Diagnostics;

namespace WebApiSample
{
    // A System.Diagnostics trace listener that keeps what it is given, for TraceController: Web API's
    // SystemDiagnosticsTraceWriter writes its records through System.Diagnostics.Trace (Trace.TraceInformation, ...).
    public class TraceRecordListener : TraceListener
    {
        private static readonly List<string> Records = new List<string>();

        public TraceRecordListener()
        {
        }

        // The name, as <add initializeData="..."> passes it to a listener created from configuration.
        public TraceRecordListener(string name) : base(name)
        {
        }

        public static string[] Snapshot()
        {
            lock (Records) return Records.ToArray();
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            Add(Name + " " + eventType + ": " + message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            Add(Name + " " + eventType + ": " + (args == null ? format : string.Format(format, args)));
        }

        public override void Write(string message)
        {
            Add(Name + " Write: " + message);
        }

        public override void WriteLine(string message)
        {
            Add(Name + " WriteLine: " + message);
        }

        private static void Add(string record)
        {
            lock (Records)
            {
                Records.Add(record);
                if (Records.Count > 2000) Records.RemoveRange(0, 1000);
            }
        }
    }
}
