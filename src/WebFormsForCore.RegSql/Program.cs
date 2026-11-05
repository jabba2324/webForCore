// webForCore: aspnet_regsql's command-line switches, for the fork on any platform (#123).
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Caching;
using System.Web.Management;

namespace WebFormsForCore.RegSql
{
	// The switches are aspnet_regsql's, and the work is System.Web's SqlServices and SqlCacheDependencyAdmin, as there,
	// with .NET Framework 4.8's scripts (System.Web carries them):
	//   -S <server> [-U <login> -P <password> | -E] | -C <connection string>      where
	//   -d <database>                                                            (aspnetdb by default)
	//   -A all|m|r|p|c|w   add the features: membership, roles, profile, personalization, web events
	//   -R all|m|r|p|c|w   remove them (-Q: without asking)
	//   -ssadd | -ssremove [-sstype t|p|c]   session state (temporary in tempdb, persisted in ASPState, custom in -d)
	//   -sqlexportonly <file>   write the script of -A, -R, -ssadd or -ssremove to a file instead of running it
	//   -ed | -dd | -et -t <table> | -dt -t <table> | -lt    SQL cache dependency (SqlCacheDependencyAdmin)
	public static class Program
	{
		public static int Main(string[] args)
		{
			Console.WriteLine("webForCore ASP.NET SQL Registration Tool (aspnet_regsql), version " +
				(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0]);
			Console.WriteLine("Administrative utility to install and uninstall ASP.NET features on a SQL server.");
			Console.WriteLine();
			try
			{
				return Run(args.ToList());
			}
			catch (Exception e) when (e is HttpException || e is SqlException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is ConfigurationException || e is InvalidOperationException)
			{
				Console.Error.WriteLine("An error has happened.  Details of the exception:");
				Console.Error.WriteLine(e.Message);
				return 1;
			}
		}

		static int Run(List<string> args)
		{
			if (args.Count == 0 || Take(args, "-?") || Take(args, "/?") || Take(args, "-help")) return Usage(args.Count == 0 ? 2 : 0);
			string server = Value(args, "-S");
			string user = Value(args, "-U");
			string password = Value(args, "-P");
			bool trusted = Take(args, "-E");
			string connectionString = Value(args, "-C");
			string database = Value(args, "-d");
			string export = Value(args, "-sqlexportonly");
			string add = Value(args, "-A");
			string remove = Value(args, "-R");
			bool quiet = Take(args, "-Q");
			bool ssadd = Take(args, "-ssadd");
			bool ssremove = Take(args, "-ssremove");
			string sstype = Value(args, "-sstype");
			bool ed = Take(args, "-ed"), dd = Take(args, "-dd"), et = Take(args, "-et"), dt = Take(args, "-dt"), lt = Take(args, "-lt");
			string table = Value(args, "-t");
			if (args.Count > 0)
			{
				Console.Error.WriteLine("Unknown switch: " + args[0]);
				return Usage(2);
			}
			int actions = new[] { add != null, remove != null, ssadd, ssremove, ed, dd, et, dt, lt }.Count(b => b);
			if (actions != 1)
			{
				Console.Error.WriteLine("Give one of -A, -R, -ssadd, -ssremove, -ed, -dd, -et, -dt or -lt.");
				return Usage(2);
			}
			if (user != null && trusted)
			{
				Console.Error.WriteLine("-E and -U can't both be given.");
				return 2;
			}

			// Exporting needs no server.
			if (export != null)
			{
				string script;
				if (add != null || remove != null) script = SqlServices.GenerateApplicationServicesScripts(add != null, Features(add ?? remove), database);
				else if (ssadd || ssremove) script = SqlServices.GenerateSessionStateScripts(ssadd, SessionType(sstype), database);
				else
				{
					Console.Error.WriteLine("-sqlexportonly writes the scripts of -A, -R, -ssadd and -ssremove.");
					return 2;
				}
				Console.WriteLine("Start exporting the SQL scripts.");
				File.WriteAllText(export, script);
				Console.WriteLine();
				Console.WriteLine("Finished.");
				return 0;
			}

			if (server == null && connectionString == null)
			{
				Console.Error.WriteLine("Give the server (-S) or a connection string (-C).");
				return 2;
			}

			if (add != null || remove != null)
			{
				SqlFeatures features = Features(add ?? remove);
				if (remove != null && !quiet && !Confirm("Are you sure you want to remove the features and their data from the database?"))
					return 1;
				Console.WriteLine((add != null ? "Start adding" : "Start removing") + " the following features:");
				foreach (SqlFeatures f in new[] { SqlFeatures.Membership, SqlFeatures.Profile, SqlFeatures.RoleManager, SqlFeatures.Personalization, SqlFeatures.SqlWebEventProvider })
					if ((features & f) == f) Console.WriteLine(f);
				Console.WriteLine();
				if (connectionString != null)
				{
					if (add != null) SqlServices.Install(database, features, connectionString);
					else SqlServices.Uninstall(database, features, connectionString);
				}
				else if (user != null)
				{
					if (add != null) SqlServices.Install(server, user, password, database, features);
					else SqlServices.Uninstall(server, user, password, database, features);
				}
				else
				{
					if (add != null) SqlServices.Install(server, database, features);
					else SqlServices.Uninstall(server, database, features);
				}
			}
			else if (ssadd || ssremove)
			{
				SessionStateType type = SessionType(sstype);
				Console.WriteLine(ssadd ? "Start adding session state." : "Start removing session state.");
				Console.WriteLine();
				if (connectionString != null)
				{
					if (ssadd) SqlServices.InstallSessionState(database, type, connectionString);
					else SqlServices.UninstallSessionState(database, type, connectionString);
				}
				else if (user != null)
				{
					if (ssadd) SqlServices.InstallSessionState(server, user, password, database, type);
					else SqlServices.UninstallSessionState(server, user, password, database, type);
				}
				else
				{
					if (ssadd) SqlServices.InstallSessionState(server, database, type);
					else SqlServices.UninstallSessionState(server, database, type);
				}
			}
			else
			{
				// SqlCacheDependencyAdmin takes a connection string, with the database in it.
				string cs = connectionString ?? Connection(server, user, password, database);
				if ((et || dt) && table == null)
				{
					Console.Error.WriteLine("-et and -dt need the table (-t).");
					return 2;
				}
				if (ed) SqlCacheDependencyAdmin.EnableNotifications(cs);
				else if (dd) SqlCacheDependencyAdmin.DisableNotifications(cs);
				else if (et) SqlCacheDependencyAdmin.EnableTableForNotifications(cs, table);
				else if (dt) SqlCacheDependencyAdmin.DisableTableForNotifications(cs, table);
				else
				{
					Console.WriteLine("Listing all tables enabled for SQL cache dependency:");
					foreach (string t in SqlCacheDependencyAdmin.GetTablesEnabledForNotifications(cs)) Console.WriteLine(t);
					return 0;
				}
			}
			Console.WriteLine("Finished.");
			return 0;
		}

		static string Connection(string server, string user, string password, string database)
		{
			var builder = new SqlConnectionStringBuilder { DataSource = server };
			if (user != null)
			{
				builder.UserID = user;
				builder.Password = password ?? "";
			}
			else builder.IntegratedSecurity = true;
			if (database != null) builder.InitialCatalog = database;
			return builder.ConnectionString;
		}

		// aspnet_regsql's feature letters, or "all".
		static SqlFeatures Features(string value)
		{
			if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)) return SqlFeatures.All;
			SqlFeatures features = SqlFeatures.None;
			foreach (char c in value.ToLowerInvariant())
			{
				switch (c)
				{
					case 'm': features |= SqlFeatures.Membership; break;
					case 'r': features |= SqlFeatures.RoleManager; break;
					case 'p': features |= SqlFeatures.Profile; break;
					case 'c': features |= SqlFeatures.Personalization; break;
					case 'w': features |= SqlFeatures.SqlWebEventProvider; break;
					default: throw new ArgumentException("Unknown feature '" + c + "': use all, or the letters m, r, p, c and w.");
				}
			}
			if (features == SqlFeatures.None) throw new ArgumentException("No feature given: use all, or the letters m, r, p, c and w.");
			return features;
		}

		static SessionStateType SessionType(string value)
		{
			switch ((value ?? "t").ToLowerInvariant())
			{
				case "t": return SessionStateType.Temporary;
				case "p": return SessionStateType.Persisted;
				case "c": return SessionStateType.Custom;
				default: throw new ArgumentException("-sstype is t (temporary), p (persisted) or c (custom).");
			}
		}

		static bool Confirm(string question)
		{
			if (Console.IsInputRedirected)
			{
				Console.Error.WriteLine("Removing features deletes their data: give -Q to remove them without asking.");
				return false;
			}
			Console.Write(question + " (Y/N) ");
			return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
		}

		static bool Take(List<string> args, string flag)
		{
			int i = args.FindIndex(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
			if (i < 0) return false;
			args.RemoveAt(i);
			return true;
		}

		static string Value(List<string> args, string option)
		{
			int i = args.FindIndex(a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));
			if (i < 0) return null;
			if (i + 1 >= args.Count) throw new ArgumentException(option + " needs a value.");
			var value = args[i + 1];
			args.RemoveRange(i, 2);
			return value;
		}

		static int Usage(int code)
		{
			var w = code == 0 ? Console.Out : Console.Error;
			w.WriteLine("webforcore-regsql: aspnet_regsql's command-line switches.");
			w.WriteLine("  Where:     -S <server> [-U <login> -P <password> | -E] | -C <connection string>; -d <database> (aspnetdb)");
			w.WriteLine("  Features:  -A all|m|r|p|c|w (add) | -R all|m|r|p|c|w (remove; -Q: without asking)");
			w.WriteLine("             m membership, r role manager, p profile, c personalization, w SQL web event provider");
			w.WriteLine("  Session:   -ssadd | -ssremove [-sstype t|p|c] (temporary, persisted in ASPState, custom in -d)");
			w.WriteLine("  Export:    -sqlexportonly <file> with -A, -R, -ssadd or -ssremove: the script, not run");
			w.WriteLine("  Cache:     -ed | -dd | -et -t <table> | -dt -t <table> | -lt (SQL cache dependency)");
			return code;
		}
	}
}
