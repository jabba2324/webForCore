#if NETCOREAPP
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;

namespace System.Web
{
    // webForCore: services that every .NET Framework process has and .NET does not, made available when System.Web loads.
    internal static class FrameworkDefaults
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            // .NET Framework has all Windows code pages (windows-1252, shift_jis, ...); .NET only after this registration.
            // Without it, globalization requestEncoding/responseEncoding and Encoding.GetEncoding(1252) fail.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // An earlier start of the application in this process (a PseudoAppDomain) may have left its instances.
            System.Web.Configuration.DbProviderFactoriesConfiguration.ResetRegistrations(System.Web.Configuration.DbProviderFactoriesConfiguration.IsFromOtherDomain);

            // .NET Framework registers these providers in machine.config (<system.data><DbProviderFactories>); .NET's
            // DbProviderFactories starts empty, so SqlDataSource with ProviderName="System.Data.SqlClient" threw.
            // Registered by type name: nothing loads until a provider is requested; Microsoft.Data.SqlClient only if the
            // application references it. (System.Data.OracleClient has no .NET version.)
            Register("System.Data.SqlClient", "System.Data.SqlClient.SqlClientFactory, System.Data.SqlClient");
            Register("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");
            Register("System.Data.OleDb", "System.Data.OleDb.OleDbFactory, System.Data.OleDb");
            Register("System.Data.Odbc", "System.Data.Odbc.OdbcFactory, System.Data.Odbc");
        }

        private static void Register(string invariantName, string factoryType)
        {
            // Never replace a registration the application made itself.
            foreach (string name in DbProviderFactories.GetProviderInvariantNames())
                if (string.Equals(name, invariantName, StringComparison.OrdinalIgnoreCase))
                    return;
            DbProviderFactories.RegisterFactory(invariantName, factoryType);
        }
    }
}
#endif
