// webForCore: the SQL scripts SqlServices runs (#123).
// Licensed under the MIT license.
using System.IO;
using System.Reflection;

namespace System.Web.Management
{
	// On .NET Framework, SqlServices and aspnet_regsql read InstallCommon.sql, InstallMembership.sql and the others from
	// the framework folder (HttpRuntime.AspInstallDirectory). On .NET that is the shared framework's folder, which has
	// none of them, so System.Web carries .NET Framework 4.8's scripts as resources (FrameworkData/Sql), read as data
	// from its folder. The file names are matched without case, as Windows does: SqlServices asks for
	// "InstallProfile.sql", and the file is InstallProfile.SQL.
	internal static class SqlScripts
	{
		private const string Prefix = "System.Web.Management.Sql.";

		internal static string ReadAllText(string file)
		{
			Assembly assembly = typeof(SqlScripts).Assembly;
			foreach (string name in assembly.GetManifestResourceNames())
			{
				if (name.Length == Prefix.Length + file.Length && name.StartsWith(Prefix, StringComparison.Ordinal)
					&& string.Compare(name, Prefix.Length, file, 0, file.Length, StringComparison.OrdinalIgnoreCase) == 0)
				{
					// As File.ReadAllText reads them: UTF-8 unless there is a byte order mark (the scripts are ASCII).
					using (var reader = new StreamReader(assembly.GetManifestResourceStream(name)))
					{
						return reader.ReadToEnd();
					}
				}
			}
			// A name System.Web doesn't carry: the framework folder, as on .NET Framework (FileNotFoundException).
			return File.ReadAllText(Path.Combine(HttpRuntime.AspInstallDirectory, file));
		}
	}
}
