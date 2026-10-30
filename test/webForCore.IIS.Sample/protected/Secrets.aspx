<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.Configuration" %>
main=<%= WebConfigurationManager.ConnectionStrings["Main"].ConnectionString %>;
apikey=<%= WebConfigurationManager.AppSettings["ApiKey"] %>;
rsa=<%= WebConfigurationManager.GetSection("connectionStrings") is System.Configuration.ConnectionStringsSection c && c.SectionInformation.IsProtected %>;
