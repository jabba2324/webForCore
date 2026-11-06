<%@ Page Language="C#" ContentType="text/plain" %>
<%@ Import Namespace="System.Web.Optimization" %>
<%@ Import Namespace="System.Web.Security" %>
<script runat="server">
    // What System.Web sees of the request and of the application's virtual path (#190), and of the connection (#194).
    // Values with the port or the site's address in them are given as the address's parts, so that the comparison with
    // IIS (tools/ci/iis_compare.cs) doesn't depend on the port.
    protected override void Render(HtmlTextWriter writer)
    {
        var r = Request;
        var v = r.ServerVariables;
        var lines = new System.Collections.Generic.List<string>
        {
            "app_domain_app_virtual_path=" + HttpRuntime.AppDomainAppVirtualPath,
            "application_path=" + r.ApplicationPath,
            "path=" + r.Path,
            "file_path=" + r.FilePath,
            "path_info=" + r.PathInfo,
            "current_execution_file_path=" + r.CurrentExecutionFilePath,
            "app_relative=" + r.AppRelativeCurrentExecutionFilePath,
            "raw_url=" + r.RawUrl,
            "url_path=" + r.Url.AbsolutePath,
            "url_scheme=" + r.Url.Scheme,
            "url_host=" + r.Url.Host,
            "url_port_is_server_port=" + (r.Url.Port.ToString() == v["SERVER_PORT"]),
            "resolve_url=" + ResolveUrl("~/Scripts/site.js"),
            "resolve_client_url=" + ResolveClientUrl("~/Scripts/site.js"),
            "to_absolute=" + VirtualPathUtility.ToAbsolute("~/Account/Login.aspx"),
            "to_absolute_root=" + VirtualPathUtility.ToAbsolute("~/"),
            "to_app_relative=" + VirtualPathUtility.ToAppRelative(r.Path),
            "login_url=" + FormsAuthentication.LoginUrl,
            "default_url=" + FormsAuthentication.DefaultUrl,
            "forms_cookie_path=" + FormsAuthentication.FormsCookiePath,
            "bundle_script=" + Scripts.Url("~/bundles/site"),
            "bundle_style=" + Styles.Url("~/Content/css"),
            // The application's part of APPL_MD_PATH: the site's number is IIS's (webForCore's site is 1).
            "appl_md_path=" + System.Text.RegularExpressions.Regex.Replace(v["APPL_MD_PATH"], @"^/LM/W3SVC/\d+/", "/LM/W3SVC/n/"),
            "app_domain_app_id=" + System.Text.RegularExpressions.Regex.Replace(HttpRuntime.AppDomainAppId, @"^/LM/W3SVC/\d+/", "/LM/W3SVC/n/"),
            "path_info_var=" + v["PATH_INFO"],
            "script_name=" + v["SCRIPT_NAME"],
            "url_var=" + v["URL"],
            "user_host_address=" + r.UserHostAddress,
            "remote_addr=" + v["REMOTE_ADDR"],
            "remote_host=" + v["REMOTE_HOST"],
            "is_secure_connection=" + r.IsSecureConnection,
            "https=" + v["HTTPS"],
            "server_port_secure=" + v["SERVER_PORT_SECURE"],
            "server_name=" + v["SERVER_NAME"],
            "x_forwarded_for=" + r.Headers["X-Forwarded-For"],
        };
        writer.Write(string.Join("\n", lines) + "\n");
    }
</script>
