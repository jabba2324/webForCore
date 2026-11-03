using System;
using System.Collections.Generic;
using System.Data.Entity.SqlServer;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Web.Mvc;

namespace SmokeMvc.Controllers
{
    // Issue #41: Assembly.GetTypes() on Microsoft's System.Data.SqlClient packages throws on .NET (SqlGuidCaster), so an
    // application that scans every loaded assembly failed (Opserver). webForCore's System.Data.SqlClient has it fixed, and
    // it is the one the application runs, although Entity Framework brings Microsoft's System.Data.SqlClient 4.8.6 with
    // its runtimes/unix and runtimes/win builds.
    public class ScanController : Controller
    {
        public ContentResult Index()
        {
            var sqlClient = typeof(SqlConnection).Assembly;
            string getTypes;
            try { getTypes = "GetTypes ok"; sqlClient.GetTypes(); }
            catch (ReflectionTypeLoadException e) { getTypes = "GetTypes threw: " + e.LoaderExceptions.FirstOrDefault()?.Message; }

            // Entity Framework's SQL Server provider binds to the same System.Data.SqlClient.
            var efSqlClient = typeof(SqlProviderServices).Assembly.GetReferencedAssemblies().First(a => a.Name == "System.Data.SqlClient");
            bool efBinds = Assembly.Load(efSqlClient) == sqlClient && SqlProviderServices.Instance != null;

            var failures = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { failures.Add(assembly.GetName().Name); }
            }

            return Content("SqlClient " + FileVersionInfo.GetVersionInfo(sqlClient.Location).FileVersion + ", " + getTypes
                + ", Entity Framework binds: " + efBinds + ", scan failures: " + failures.Count + " " + string.Join(" ", failures),
                "text/plain");
        }
    }
}
