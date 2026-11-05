<%@ Page Language="C#" %>
<%-- #181: a long request that restarts its own application, as DNN's installer streams its progress and changes bin.
     Writes and flushes a line every second for ?seconds=, and after 2 s writes ?touch= (a file name) in bin_dotnet.
     On .NET Framework the old domain's requests run on until <httpRuntime shutdownTimeout> while new requests go to
     the new domain. --%>
<%
    int seconds = int.Parse(Request.QueryString["seconds"] ?? "10");
    string touch = Request.QueryString["touch"];
    Response.Write("process=" + System.Environment.ProcessId + "\n");
    Response.Flush();
    for (int i = 1; i <= seconds; i++)
    {
        System.Threading.Thread.Sleep(1000);
        if (i == 2 && !string.IsNullOrEmpty(touch))
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, System.IO.Path.GetFileName(touch)),
                System.DateTime.UtcNow.ToString("O"));
        Response.Write("line " + i + "\n");
        Response.Flush();
    }
    Response.Write("done\n");
%>
