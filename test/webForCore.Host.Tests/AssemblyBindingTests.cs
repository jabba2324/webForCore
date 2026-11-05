using System.Net;
using Xunit;

namespace HostTests
{
    public partial class HostTests
    {
        // #158: a <codeBase> whose href is a file: URL. Fusion loads a strong-named assembly from it wherever it is, and
        // one without a strong name only from the application's folder. The IIS gate's BindingRedirectsAndCodeBasesAreApplied
        // has relative hrefs; a file: URL needs the absolute path of the copy, so the Web.config is written here.
        [Fact]
        public async Task ACodeBaseFileUrlLoadsAStrongNamedAssemblyFromOutsideTheSite()
        {
            const string token = "6b8e2037dda5564b";
            var elsewhere = Directory.CreateTempSubdirectory("webforcore-codebase-").FullName;
            try
            {
                var strong = Path.Combine(elsewhere, "strong", "CodeBaseLib.dll");
                var simple = Path.Combine(elsewhere, "simple", "UnprobedLib.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(strong));
                Directory.CreateDirectory(Path.GetDirectoryName(simple));
                File.Copy(Path.Combine(Repository, "test", "webForCore.IIS.Sample.CodeBaseLib", "bin_dotnet", "CodeBaseLib.dll"), strong);
                File.Copy(Path.Combine(Repository, "test", "webForCore.IIS.Sample.Unprobed", "bin_dotnet", "UnprobedLib.dll"), simple);

                using var site = new Site(null, $"""
                    <runtime>
                      <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                        <dependentAssembly>
                          <assemblyIdentity name="CodeBaseLib" publicKeyToken="{token}" culture="neutral" />
                          <codeBase version="1.0.0.0" href="{new Uri(strong).AbsoluteUri}" />
                        </dependentAssembly>
                        <dependentAssembly>
                          <assemblyIdentity name="UnprobedLib" />
                          <codeBase href="{new Uri(simple).AbsoluteUri}" />
                        </dependentAssembly>
                      </assemblyBinding>
                    </runtime>
                    """);
                File.WriteAllText(Path.Combine(site.Directory, "CodeBase.aspx"), $$"""
                    <%@ Page Language="C#" %>
                    <%@ Import Namespace="System.Reflection" %>
                    <script runat="server">
                        static string Describe(Func<object> load)
                        {
                            try { return load().ToString(); }
                            catch (Exception ex) { return ex.GetType().FullName; }
                        }

                        protected void Page_Load(object sender, EventArgs e)
                        {
                            Response.ContentType = "text/plain";
                            Response.Write("CodeBaseLib: " + Describe(() => Assembly.Load("CodeBaseLib, Version=1.0.0.0, Culture=neutral, PublicKeyToken={{token}}").Location) + "\n");
                            Response.Write("UnprobedLib: " + Describe(() => Assembly.Load("UnprobedLib").Location) + "\n");
                        }
                    </script>
                    """);
                using var app = site.Start();
                var response = await app.GetAsync("/CodeBase.aspx");
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}\n{app.Output}");
                var lines = body.Split('\n');
                // (On Windows the temporary folder may be named with its short name on one side.)
                var loaded = lines.Single(l => l.StartsWith("CodeBaseLib: "));
                Assert.Contains(Path.GetFileName(elsewhere), loaded);
                Assert.EndsWith(Path.Combine("strong", "CodeBaseLib.dll"), loaded);
                Assert.Contains("UnprobedLib: System.IO.FileNotFoundException", lines);
            }
            finally
            {
                try { Directory.Delete(elsewhere, recursive: true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
