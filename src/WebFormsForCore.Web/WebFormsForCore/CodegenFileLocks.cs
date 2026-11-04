using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace System.Web.Compilation
{
	// webForCore (#89): the compilation folder's assemblies stay while a worker has them loaded, as on IIS.
	//
	// The workers of an application share its compilation folder (Temporary ASP.NET Files), as an application's
	// AppDomains did on IIS. When the application restarts for a change in App_Code, bin or Web.config, the next worker
	// empties the folder (BuildManager.CheckTopLevelFilesUpToDate) while the old one may still compile a page against the
	// assemblies it has loaded (App_global.asax, App_Code). On .NET Framework the new AppDomain couldn't delete those, as
	// the loader kept a loaded assembly's file open, so DiskBuildResultCache.TryDeleteFile marked them .delete and a later
	// start removed them. Off Windows a file can always be deleted, so the old worker's compilation failed with CS0006
	// ("Metadata file '.../App_global.asax.xxxxxxxx.dll' could not be found"): Umbraco 6.2.6's install, whose
	// Application_Start creates ~/App_Code.
	//
	// Each worker keeps every assembly it loads from the compilation folder open, for as long as it runs, with
	// FileShare.Read: on Windows that handle doesn't share delete access, so File.Delete fails, whatever .NET's loader
	// allows; off Windows it is a shared lock (flock LOCK_SH), and Delete takes the exclusive lock first, so it fails
	// too. Both fail while any worker holds the file, this one included, as on .NET Framework. The handles go with the
	// process.
	internal static class CodegenFileLocks
	{
		private static readonly StringComparison s_pathComparison = OSInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		private static readonly ConcurrentDictionary<string, FileStream> s_held = new ConcurrentDictionary<string, FileStream>(
			OSInfo.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		private static string s_directory;

		// Starts holding the assemblies this process loads from the compilation folder.
		internal static void Start(string codegenDirectory)
		{
			if (string.IsNullOrEmpty(codegenDirectory)) return;
			string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(codegenDirectory));
			if (Interlocked.CompareExchange(ref s_directory, directory, null) != null) return;
			AppDomain.CurrentDomain.AssemblyLoad += (sender, e) => Hold(e.LoadedAssembly);
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) Hold(assembly);
		}

		private static void Hold(Assembly assembly)
		{
			if (assembly.IsDynamic) return;
			string path = assembly.Location; // empty for an assembly loaded from bytes
			if (string.IsNullOrEmpty(path) || !string.Equals(Path.GetDirectoryName(path), s_directory, s_pathComparison)) return;
			if (s_held.ContainsKey(path)) return;
			try
			{
				var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				if (!s_held.TryAdd(path, stream)) stream.Dispose();
			}
			catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
			{
				// Deleted meanwhile, or another worker is deleting it: nothing to keep.
			}
		}

		// File.Delete, which fails (IOException) while a worker holds the file.
		internal static void Delete(string path)
		{
			if (OSInfo.IsWindows)
			{
				File.Delete(path); // fails by itself: the holders don't share delete access
				return;
			}
			FileStream exclusive;
			try
			{
				exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
			}
			catch (Exception x) when (x is FileNotFoundException || x is DirectoryNotFoundException)
			{
				return; // as File.Delete
			}
			using (exclusive)
			{
				File.Delete(path);
			}
		}
	}
}
