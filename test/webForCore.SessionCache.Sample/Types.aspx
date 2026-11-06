<%@ Page Language="C#" EnableSessionState="False" %>
<script runat="server">
    // Loads each type named in ?type= (assembly-qualified) and writes its base type: the providers' assemblies load
    // with their dependencies (CosmosDBSessionStateProviderAsync is only built and loaded, as there is no Cosmos DB here).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        foreach (string name in Request.QueryString.GetValues("type"))
        {
            Type type = Type.GetType(name, true);
            object instance = Activator.CreateInstance(type);
            Response.Write(type.FullName + ": " + (type.BaseType == null ? "none" : type.BaseType.FullName) + "\n");
        }
    }
</script>
