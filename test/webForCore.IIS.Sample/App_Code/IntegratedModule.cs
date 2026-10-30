using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Web;

// A module written for IIS's integrated pipeline: integrated-only events, CurrentNotification/IsPostNotification,
// writable request headers and server variables, AddOnSendingHeaders, OnExecuteRequestStep.
public class IntegratedModule : IHttpModule
{
    public static readonly ConcurrentDictionary<string, int> Logged = new ConcurrentDictionary<string, int>();
    public static int Steps;

    public void Init(HttpApplication app)
    {
        app.OnExecuteRequestStep((context, step) => { System.Threading.Interlocked.Increment(ref Steps); step(); });
        app.BeginRequest += (s, e) =>
        {
            var context = ((HttpApplication)s).Context;
            Note(context);
            context.Request.Headers.Add("X-Integrated", "from-module");
            context.Request.ServerVariables["HTTP_X_SERVER_VARIABLE"] = "set-by-module";
            context.Response.AddOnSendingHeaders(c => c.Response.AppendHeader("X-Sending-Headers", c.CurrentNotification.ToString()));
        };
        app.PostAuthenticateRequest += (s, e) => Note(((HttpApplication)s).Context);
        app.MapRequestHandler += (s, e) => Note(((HttpApplication)s).Context);
        app.PostMapRequestHandler += (s, e) => Note(((HttpApplication)s).Context);
        app.LogRequest += (s, e) => Logged.AddOrUpdate("log:" + ((HttpApplication)s).Context.Request.Path, 1, (k, v) => v + 1);
        app.PostLogRequest += (s, e) => Logged.AddOrUpdate("postlog:" + ((HttpApplication)s).Context.Request.Path, 1, (k, v) => v + 1);
    }

    static void Note(HttpContext context)
    {
        var list = context.Items["notifications"] as List<string> ?? new List<string>();
        list.Add(context.CurrentNotification + (context.IsPostNotification ? "(post)" : ""));
        context.Items["notifications"] = list;
    }

    public void Dispose() { }
}
