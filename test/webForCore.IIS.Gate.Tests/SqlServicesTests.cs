using System.Diagnostics;
using System.Net;
using Xunit;

namespace IISGate
{
    // #123: the database of ASP.NET's SQL providers. webForCore had none of .NET Framework's scripts (InstallCommon.sql,
    // InstallMembership.sql, ...) and no aspnet_regsql, so applications on the default SqlMembershipProvider couldn't
    // create their database: MVC Music Store's Register and LogOn were a 500 on Linux. System.Web now carries .NET
    // Framework 4.8's scripts, and webforcore-regsql has aspnet_regsql's switches. Against SQL Server at
    // 127.0.0.1,14333 (tools/ci/sqlserver.sh locally, a service in CI), in the database WebForCoreAspNetDb, which the
    // sample's LocalSqlServer names.
    [Collection(IISSampleCollection.Name)]
    public class SqlServicesTests
    {
        private const string Server = "127.0.0.1,14333";
        private const string Password = "webForCore-Test1!";
        private const string Database = "WebForCoreAspNetDb";

        private readonly SampleServer _server;

        public SqlServicesTests(SampleServer server)
        {
            _server = server;
        }

        private static string Repository()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "WebFormsForCore.slnx"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }

        private static (int Code, string Output) RegSql(params string[] args)
        {
            var tool = Path.Combine(Repository(), "src", "WebFormsForCore.RegSql", "bin", "Release", "net10.0", "webforcore-regsql.dll");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            start.ArgumentList.Add(tool);
            foreach (var a in args) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            process.StandardInput.Close();
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd() + error.Result;
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        private async Task<string> Page(string step)
        {
            var response = await _server.Client.GetAsync("/regressions/SqlServices.aspx?step=" + step);
            string body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, step + ": " + body);
            return body;
        }

        // aspnet_regsql -A mr creates the database, and an application on machine.config's providers then creates,
        // validates and deletes a user through Membership, and puts it in a role through Roles. SqlServices from code
        // adds profile; aspnet_regsql -ssadd -sstype c adds session state to the same database.
        [Fact]
        public async Task RegSqlCreatesTheDatabaseOfMembershipAndRoles()
        {
            await Page("reset");

            var (code, output) = RegSql("-S", Server, "-U", "sa", "-P", Password, "-d", Database, "-A", "mr");
            Assert.True(code == 0, output);
            Assert.Contains("Start adding the following features:\nMembership\nRoleManager\n", output.ReplaceLineEndings("\n"));
            Assert.Contains("Finished.", output);

            string body = await Page("members");
            Assert.Contains("membership provider: System.Web.Security.SqlMembershipProvider\n", body);
            Assert.Contains("role provider: System.Web.Security.SqlRoleProvider\n", body);
            Assert.Contains("create: Success\n", body);
            Assert.Contains("duplicate: DuplicateUserName\n", body);
            Assert.Contains("validate: True\n", body);
            Assert.Contains("validate wrong password: False\n", body);
            Assert.Contains("email: True\n", body);
            Assert.Contains("in role: True\n", body);
            Assert.Contains("roles: Editors\n", body);
            Assert.Contains("delete: True\n", body);
            Assert.Contains("validate deleted: False\n", body);

            (code, output) = RegSql("-C", "Data Source=" + Server + ";User ID=sa;Password=" + Password + ";TrustServerCertificate=True",
                "-ssadd", "-sstype", "c", "-d", Database);
            Assert.True(code == 0, output);

            body = await Page("code");
            Assert.Contains("tables: aspnet_Applications,aspnet_Membership,aspnet_Profile,aspnet_Roles,aspnet_Users,aspnet_UsersInRoles,ASPStateTempSessions\n", body);
            Assert.Contains("schema versions: common,membership,profile,role manager\n", body);

            // Removing asks first: without -Q, and no one to answer, nothing is removed.
            (code, output) = RegSql("-S", Server, "-U", "sa", "-P", Password, "-d", Database, "-R", "p");
            Assert.NotEqual(0, code);
            Assert.Contains("-Q", output);
            (code, output) = RegSql("-S", Server, "-U", "sa", "-P", Password, "-d", Database, "-R", "p", "-Q");
            Assert.True(code == 0, output);
            body = await Page("code");
            Assert.Contains("tables: aspnet_Applications,aspnet_Membership,aspnet_Profile,", body); // added again by the page
        }

        // -sqlexportonly writes what .NET Framework 4.8's aspnet_regsql writes, byte for byte: FrameworkData has its
        // output for -A all -d aspnetdb and for -ssadd -sstype t (read on a Windows CI runner).
        [Theory]
        [InlineData("aspnet_regsql-A-all-d-aspnetdb.sql", "-A", "all", "-d", "aspnetdb")]
        [InlineData("aspnet_regsql-ssadd-sstype-t.sql", "-ssadd", "-sstype", "t")]
        public void ExportedScriptsAreAspNetRegSqls(string framework, params string[] args)
        {
            string file = Path.Combine(Path.GetTempPath(), "webforcore-regsql-" + Guid.NewGuid().ToString("N") + ".sql");
            try
            {
                var (code, output) = RegSql(args.Concat(new[] { "-sqlexportonly", file }).ToArray());
                Assert.True(code == 0, output);
                byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "FrameworkData", framework));
                byte[] actual = File.ReadAllBytes(file);
                Assert.True(expected.SequenceEqual(actual), framework + ": " + actual.Length + " bytes, .NET Framework's " + expected.Length);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void RegSqlUsage()
        {
            var (code, output) = RegSql();
            Assert.Equal(2, code);
            Assert.Contains("-A all|m|r|p|c|w", output);
            (code, output) = RegSql("-A", "x", "-sqlexportonly", "unused.sql");
            Assert.Equal(1, code);
            Assert.Contains("Unknown feature 'x'", output);
        }
    }
}
