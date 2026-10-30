using System;
using System.Web;

// Registered in <system.webServer><modules> only, as an integrated-mode application does.
public class GateModule : IHttpModule
{
    public void Init(HttpApplication application)
    {
        application.BeginRequest += (sender, e) => ((HttpApplication)sender).Response.AppendHeader("X-Gate-Module", "1");
    }

    public void Dispose()
    {
    }
}
