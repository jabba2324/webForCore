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

            // .NET Framework registers these providers in machine.config (<system.data><DbProviderFactories>); .NET's
            // DbProviderFactories starts empty, so SqlDataSource with ProviderName="System.Data.SqlClient" threw.
            // Registered by type name: nothing loads until a provider is requested; Microsoft.Data.SqlClient only if the
            // application references it. (System.Data.OracleClient has no .NET version.)
            Register("System.Data.SqlClient", "System.Data.SqlClient.SqlClientFactory, System.Data.SqlClient");
            Register("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");
            Register("System.Data.OleDb", "System.Data.OleDb.OleDbFactory, System.Data.OleDb");
            Register("System.Data.Odbc", "System.Data.Odbc.OdbcFactory, System.Data.Odbc");
        }

        private static Encoding s_ansiEncoding;

        // webForCore (#90): what Encoding.Default was on .NET Framework, the system's ANSI code page (windows-1252 on a
        // Western Windows), which System.Web used to read .aspx/.ascx/.master/.cshtml files without a BOM when
        // <globalization fileEncoding> isn't set. .NET's Encoding.Default is UTF-8, so Umbraco's windows-1252 user
        // controls showed U+FFFD. On Windows this is the system's code page (Encoding.GetEncoding(0) once the code pages
        // provider is registered). Linux has no ANSI code page, so it is the one Windows has for the system locale
        // (LANG; windows-1252 for English, Western European and the invariant culture, 932 for Japanese, 1251 for
        // Russian, ...), and UTF-8 for locales Windows has no ANSI code page for.
        internal static Encoding AnsiEncoding => s_ansiEncoding ??= GetAnsiEncoding();

        private static Encoding GetAnsiEncoding()
        {
            if (OperatingSystem.IsWindows())
                return Encoding.GetEncoding(0);
            int codePage = System.Globalization.CultureInfo.InstalledUICulture.TextInfo.ANSICodePage;
            if (codePage == 0)
                return Encoding.UTF8;
            try
            {
                return Encoding.GetEncoding(codePage);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
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
