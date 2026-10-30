<%--
    webForCore: the page ASMX shows a browser that opens a .asmx file (and ?op=<name>). MIT license.

    .NET Framework rendered this with DefaultWsdlHelpGenerator.aspx from its CONFIG folder, which is not open source,
    so this is webForCore's own page. It covers the same ground in less detail: the service and its operations, a link
    to the service description, each operation's documentation and, for requests from the local machine (when the
    HttpPost protocol is enabled for them), a form that invokes the operation. DocumentationServerProtocol passes the
    service descriptions in Context.Items, as it did for the original page.
--%>
<%@ Page Language="C#" Debug="false" EnableViewState="false" %>
<%@ Import Namespace="System.Collections.Generic" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Web.Services.Description" %>
<script runat="server">
    ServiceDescriptionCollection descriptions;
    Service service;
    List<Operation> operations = new List<Operation>();
    Operation operation;
    Binding postBinding;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        descriptions = (ServiceDescriptionCollection)(Context.Items["wsdlsWithPost"] ?? Context.Items["wsdls"]);
        if (descriptions == null) return;
        service = descriptions.Cast<ServiceDescription>().SelectMany(d => d.Services.Cast<Service>()).FirstOrDefault();

        // The operations are those of the SOAP bindings' port types (each SOAP version describes the same ones).
        foreach (ServiceDescription description in descriptions)
        {
            foreach (Binding binding in description.Bindings)
            {
                if (binding.Extensions.Find(typeof(HttpBinding)) is HttpBinding http)
                {
                    if (http.Verb == "POST") postBinding = binding;
                    continue;
                }
                PortType portType = descriptions.GetPortType(binding.Type);
                foreach (Operation op in portType.Operations)
                {
                    if (!operations.Any(o => o.Name == op.Name)) operations.Add(op);
                }
            }
        }
        operations.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        string name = Request.QueryString["op"];
        if (name != null) operation = operations.FirstOrDefault(o => o.Name == name);
    }

    string ServiceName { get { return service != null ? service.Name : "Web Service"; } }

    string Encode(string value) { return Server.HtmlEncode(value ?? ""); }

    // The HttpPost operation that matches the one shown, with the names of its form fields.
    bool TryGetPostForm(out string action, out IList<string> fields)
    {
        action = null;
        fields = null;
        if (postBinding == null) return false;
        OperationBinding opBinding = postBinding.Operations.Cast<OperationBinding>().FirstOrDefault(o => o.Name == operation.Name);
        if (opBinding == null) return false;
        HttpOperationBinding http = (HttpOperationBinding)opBinding.Extensions.Find(typeof(HttpOperationBinding));
        PortType portType = descriptions.GetPortType(postBinding.Type);
        Operation postOperation = portType.Operations.Cast<Operation>().FirstOrDefault(o => o.Name == operation.Name);
        if (http == null || postOperation == null) return false;
        Message input = descriptions.GetMessage(postOperation.Messages.Input.Message);
        action = Request.FilePath + http.Location;
        fields = input.Parts.Cast<MessagePart>().Select(p => p.Name).ToList();
        return true;
    }
</script>
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title><%= Encode(ServiceName) %> Web Service</title>
    <style>
        body { font-family: Verdana, Arial, sans-serif; margin: 0; color: #000; background: #fff; }
        #header { background: #003366; color: #fff; padding: 12px 24px; font-size: 1.6em; }
        #content { margin: 0 24px 24px; font-size: 0.85em; }
        h2 { font-size: 1.3em; margin-top: 1.2em; }
        table { border-collapse: collapse; }
        td { padding: 4px 8px 4px 0; }
        pre { background: #f4f4f4; padding: 8px; }
    </style>
</head>
<body>
    <div id="header"><%= Encode(ServiceName) %></div>
    <div id="content">
<% if (operation == null) { %>
        <% if (service != null && !string.IsNullOrEmpty(service.Documentation)) { %>
        <p><%= Encode(service.Documentation) %></p>
        <% } %>
        <p>The following operations are supported. For a formal definition, please review the
            <a href="<%= Encode(Request.FilePath) %>?WSDL">Service Description</a>.</p>
        <ul>
        <% foreach (Operation op in operations) { %>
            <li><a href="<%= Encode(Request.FilePath) %>?op=<%= Server.UrlEncode(op.Name) %>"><%= Encode(op.Name) %></a>
            <% if (!string.IsNullOrEmpty(op.Documentation)) { %><br /><%= Encode(op.Documentation) %><% } %></li>
        <% } %>
        </ul>
<% } else { %>
        <p>Click <a href="<%= Encode(Request.FilePath) %>">here</a> for a complete list of operations.</p>
        <h2><%= Encode(operation.Name) %></h2>
        <% if (!string.IsNullOrEmpty(operation.Documentation)) { %>
        <p><%= Encode(operation.Documentation) %></p>
        <% } %>
        <h3>Test</h3>
        <% string action; IList<string> fields; %>
        <% if (TryGetPostForm(out action, out fields)) { %>
        <p>To test the operation using the HTTP POST protocol, click the 'Invoke' button.</p>
        <form action="<%= Encode(action) %>" method="post" target="_blank">
            <table>
                <tr><td><b>Parameter</b></td><td><b>Value</b></td></tr>
            <% foreach (string field in fields) { %>
                <tr><td><%= Encode(field) %>:</td><td><input type="text" name="<%= Encode(field) %>" size="50" /></td></tr>
            <% } %>
                <tr><td></td><td><input type="submit" value="Invoke" /></td></tr>
            </table>
        </form>
        <% } else { %>
        <p>The test form is only available for requests from the local machine.</p>
        <% } %>
<% } %>
    </div>
</body>
</html>
