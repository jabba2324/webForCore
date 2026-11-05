using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace System.Web
{
	// What a watched directory is told: the names are relative to that directory, as a FileSystemWatcher on it reports them.
	internal interface IDirectoryWatchTarget
	{
		void OnCreated(string name);
		void OnDeleted(string name);
		void OnChanged(string name);
		void OnRenamed(string oldName, string newName);
		void OnError(Exception exception);
	}

	// webForCore (#145, #152): on Linux, the directories FileChangesMonitor watches inside the application share a
	// FileSystemWatcher per notify filter, instead of one each.
	//
	// On IIS each DirectoryMonitor was a ReadDirectoryChangesW handle, and DirMonCompletion gives each one a
	// FileSystemWatcher in its place. On .NET 10 every FileSystemWatcher on Linux is an inotify instance, and a user has
	// 128 of them by default (fs.inotify.max_user_instances): Orchard 1.10 (69 modules), Kooboo CMS and nopCommerce
	// watch a directory per module, view folder and web.config, and their requests failed with "The configured user
	// limit (128) on the number of inotify instances has been reached". A recursive watcher is one instance with an
	// inotify watch per directory (fs.inotify.max_user_watches, which is in the hundreds of thousands).
	//
	// So a directory in the application's folder (or the folder itself) is watched by a recursive watcher on the
	// application's folder, one per NotifyFilters value (FileChangesMonitor uses three), and each event goes to the
	// directories that would have seen it with a watcher of their own, under the same name:
	// - a directory watched alone gets the events for its own entries; one watched with its subtree, the events below it;
	// - a move from one directory to another is a deletion in the first and a creation in the second, and a rename in a
	//   directory watching both;
	// - a watched directory that is renamed or moved inside the application is still watched under its new name, as an
	//   inotify watch follows its directory; one that is deleted (or moved out) gets nothing more.
	// A directory is shared only once the watcher is known to watch it: it existed when the watcher started, or the
	// watcher reported its creation (it adds the new directory's watch before it reports it). A directory created a moment
	// ago is waited for. A directory below a symbolic link, which a recursive watcher doesn't follow, one outside the
	// application (the compilation folder), and every directory on other systems, keep a FileSystemWatcher of their own.
	internal sealed class SharedDirectoryWatcher
	{
		const int MaxWaitForNewDirectoryMilliseconds = 2000;

		static readonly object s_lock = new object();
		static readonly Dictionary<NotifyFilters, SharedDirectoryWatcher> s_watchers = new Dictionary<NotifyFilters, SharedDirectoryWatcher>();

		readonly string _root;
		readonly NotifyFilters _filter;
		readonly Dictionary<string, List<Subscription>> _directories = new Dictionary<string, List<Subscription>>(StringComparer.Ordinal);
		readonly List<Subscription> _subtrees = new List<Subscription>();
		readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);
		FileSystemWatcher _watcher;
		int _bufferSize;
		int _count;                 // subscriptions not disposed
		int _waiters;               // TryWatch calls waiting for a new directory

		SharedDirectoryWatcher(string root, NotifyFilters filter)
		{
			_root = root;
			_filter = filter;
		}

		// Watches dir (and its subtree) for target through the application's shared watcher, or returns null when it
		// needs a FileSystemWatcher of its own.
		internal static IDisposable TryWatch(string dir, bool watchSubtree, NotifyFilters filter, int bufferSize, IDirectoryWatchTarget target)
		{
			if (!OperatingSystem.IsLinux() || dir == null)
			{
				return null;
			}

			string root = HttpRuntime.AppDomainAppPathInternal;
			if (String.IsNullOrEmpty(root))
			{
				return null;
			}

			root = TrimSeparator(Path.GetFullPath(root));
			dir = TrimSeparator(dir);
			if (root.Length <= 1 || !IsSelfOrBelow(dir, root) || HasSymbolicLinkBelow(root, dir))
			{
				return null;
			}

			FileSystemWatcher stopped = null;
			try
			{
				lock (s_lock)
				{
					var deadline = Environment.TickCount64 + MaxWaitForNewDirectoryMilliseconds;
					while (true)
					{
						SharedDirectoryWatcher shared;
						if (!s_watchers.TryGetValue(filter, out shared))
						{
							shared = new SharedDirectoryWatcher(root, filter);
							shared.Start(bufferSize);
							s_watchers.Add(filter, shared);
						}
						else if (!String.Equals(shared._root, root, StringComparison.Ordinal))
						{
							// Another application's folder: there is one application per process, so this isn't expected.
							return null;
						}

						if (shared._known.Contains(dir))
						{
							var subscription = new Subscription(shared, dir, watchSubtree, target);
							shared.Attach(subscription);
							shared._count++;
							return subscription;
						}

						// A directory created a moment ago, whose creation the watcher hasn't reported yet.
						var remaining = deadline - Environment.TickCount64;
						if (remaining > 0 && Directory.Exists(dir))
						{
							shared._waiters++;
							try
							{
								Monitor.Wait(s_lock, (int)remaining);
							}
							finally
							{
								shared._waiters--;
							}

							// The watcher may have stopped meanwhile (its last directory's watch ended): look again.
							continue;
						}

						if (shared._count == 0 && shared._waiters == 0)
						{
							stopped = shared.Stop();
						}

						return null;
					}
				}
			}
			finally
			{
				stopped?.Dispose();
			}
		}

		static string TrimSeparator(string path)
		{
			return path.Length > 1 ? path.TrimEnd(Path.DirectorySeparatorChar) : path;
		}

		static bool IsSelfOrBelow(string path, string dir)
		{
			return path.Length == dir.Length ? String.Equals(path, dir, StringComparison.Ordinal)
				: path.Length > dir.Length && path[dir.Length] == Path.DirectorySeparatorChar && path.StartsWith(dir, StringComparison.Ordinal);
		}

		static bool IsBelow(string path, string dir)
		{
			return path.Length > dir.Length && path[dir.Length] == Path.DirectorySeparatorChar && path.StartsWith(dir, StringComparison.Ordinal);
		}

		static bool HasSymbolicLinkBelow(string root, string dir)
		{
			for (string path = dir; path.Length > root.Length; path = Path.GetDirectoryName(path))
			{
				if (new DirectoryInfo(path).LinkTarget != null)
				{
					return true;
				}
			}

			return false;
		}

		static bool IsDirectory(string path)
		{
			var info = new DirectoryInfo(path);
			return info.Exists && info.LinkTarget == null;
		}

		// Under s_lock.
		void Start(int bufferSize)
		{
			// The directories that exist before the watcher walks the tree: it watches each of them.
			AddKnown(_root);
			_bufferSize = bufferSize;
			_watcher = CreateWatcher(bufferSize);
		}

		// Under s_lock. Takes the watcher out of use, for the caller to dispose outside the lock.
		FileSystemWatcher Stop()
		{
			s_watchers.Remove(_filter);
			var watcher = _watcher;
			_watcher = null;
			_known.Clear();
			return watcher;
		}

		// Under s_lock: dir and the directories below it, which the watcher watches (not below symbolic links, which it
		// doesn't follow).
		void AddKnown(string dir)
		{
			_known.Add(dir);
			var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
			try
			{
				foreach (var subdirectory in Directory.EnumerateDirectories(dir, "*", options))
				{
					_known.Add(subdirectory);
				}
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
			{
				// Deleted meanwhile: its deletion follows.
			}
		}

		FileSystemWatcher CreateWatcher(int bufferSize)
		{
			var watcher = new FileSystemWatcher(_root)
			{
				IncludeSubdirectories = true,
				NotifyFilter = _filter,
				InternalBufferSize = bufferSize,
			};
			watcher.Created += OnCreated;
			watcher.Deleted += OnDeleted;
			watcher.Changed += OnChanged;
			watcher.Renamed += OnRenamed;
			watcher.Error += OnError;
			try
			{
				// The process's inotify reader keeps the ExecutionContext it starts on (a request's) while the process runs.
				using (ExecutionContext.SuppressFlow())
				{
					watcher.EnableRaisingEvents = true;
				}
			}
			catch
			{
				watcher.Dispose();
				throw;
			}

			return watcher;
		}

		// Under s_lock: puts the subscription in the dispatch tables.
		void Attach(Subscription subscription)
		{
			if (subscription.Subtree)
			{
				_subtrees.Add(subscription);
			}
			else
			{
				List<Subscription> list;
				if (!_directories.TryGetValue(subscription.Directory, out list))
				{
					_directories[subscription.Directory] = list = new List<Subscription>(1);
				}

				list.Add(subscription);
			}
		}

		// Under s_lock. Takes the subscription out of the dispatch tables (it gets no more events).
		void Detach(Subscription subscription)
		{
			if (subscription.Subtree)
			{
				_subtrees.Remove(subscription);
			}
			else
			{
				List<Subscription> list;
				if (_directories.TryGetValue(subscription.Directory, out list))
				{
					list.Remove(subscription);
					if (list.Count == 0)
					{
						_directories.Remove(subscription.Directory);
					}
				}
			}
		}

		void Remove(Subscription subscription)
		{
			FileSystemWatcher stopped = null;
			lock (s_lock)
			{
				if (subscription.Removed)
				{
					return;
				}

				subscription.Removed = true;
				if (!subscription.Gone)
				{
					Detach(subscription);
				}

				if (--_count == 0)
				{
					stopped = Stop();
				}
			}

			stopped?.Dispose();
		}

		void Grow(int bufferSize)
		{
			FileSystemWatcher old = null;
			lock (s_lock)
			{
				if (_watcher == null || bufferSize <= _bufferSize)
				{
					return;
				}

				// The new watcher starts before the old one stops, so that nothing is missed in between (a change may be
				// reported by both meanwhile, as twice by one watcher).
				var grown = CreateWatcher(bufferSize);
				old = _watcher;
				_watcher = grown;
				_bufferSize = bufferSize;
			}

			old.EnableRaisingEvents = false;
			old.Dispose();
		}

		string FullPath(string name)
		{
			return _root + Path.DirectorySeparatorChar + name;
		}

		// Under s_lock: the subscriptions that see an entry at path, with path's name relative to each.
		void AddTargets(string path, List<KeyValuePair<Subscription, string>> targets)
		{
			string parent = Path.GetDirectoryName(path);
			List<Subscription> list;
			if (parent != null && _directories.TryGetValue(parent, out list))
			{
				string name = path.Substring(parent.Length + 1);
				foreach (var subscription in list)
				{
					targets.Add(new KeyValuePair<Subscription, string>(subscription, name));
				}
			}

			foreach (var subscription in _subtrees)
			{
				if (IsBelow(path, subscription.Directory))
				{
					targets.Add(new KeyValuePair<Subscription, string>(subscription, path.Substring(subscription.Directory.Length + 1)));
				}
			}
		}

		void OnCreated(object sender, FileSystemEventArgs e)
		{
			if (String.IsNullOrEmpty(e.Name))
			{
				return;
			}

			string path = FullPath(e.Name);
			bool directory = IsDirectory(path);
			var targets = new List<KeyValuePair<Subscription, string>>();
			lock (s_lock)
			{
				if (directory)
				{
					// The watcher adds a new directory's watch, and those of the directories already in it (a tree moved
					// in, or made with its subdirectories), before it reports the directory, and it reports only that one.
					AddKnown(path);
					Monitor.PulseAll(s_lock);
				}

				AddTargets(path, targets);
			}

			foreach (var target in targets)
			{
				target.Key.Target.OnCreated(target.Value);
			}
		}

		void OnDeleted(object sender, FileSystemEventArgs e)
		{
			if (String.IsNullOrEmpty(e.Name))
			{
				return;
			}

			string path = FullPath(e.Name);
			var targets = new List<KeyValuePair<Subscription, string>>();
			lock (s_lock)
			{
				AddTargets(path, targets);
				if (_known.Contains(path))
				{
					// The directory and its subtree are gone, with what watched them.
					_known.RemoveWhere(known => IsSelfOrBelow(known, path));
					foreach (var subscription in Subscriptions(path))
					{
						Detach(subscription);
						subscription.Gone = true;
					}
				}
			}

			foreach (var target in targets)
			{
				target.Key.Target.OnDeleted(target.Value);
			}
		}

		void OnChanged(object sender, FileSystemEventArgs e)
		{
			if (String.IsNullOrEmpty(e.Name))
			{
				return;
			}

			var targets = new List<KeyValuePair<Subscription, string>>();
			lock (s_lock)
			{
				AddTargets(FullPath(e.Name), targets);
			}

			foreach (var target in targets)
			{
				target.Key.Target.OnChanged(target.Value);
			}
		}

		void OnRenamed(object sender, RenamedEventArgs e)
		{
			string oldPath = String.IsNullOrEmpty(e.OldName) ? null : FullPath(e.OldName);
			string newPath = String.IsNullOrEmpty(e.Name) ? null : FullPath(e.Name);
			bool directory = newPath != null && IsDirectory(newPath);
			var oldTargets = new List<KeyValuePair<Subscription, string>>();
			var newTargets = new List<KeyValuePair<Subscription, string>>();
			lock (s_lock)
			{
				if (oldPath != null)
				{
					AddTargets(oldPath, oldTargets);
				}

				if (newPath != null)
				{
					AddTargets(newPath, newTargets);
				}

				if (directory && oldPath != null)
				{
					// A watched directory keeps its watch under its new name.
					var moved = new List<string>();
					_known.RemoveWhere(known =>
					{
						if (!IsSelfOrBelow(known, oldPath))
						{
							return false;
						}

						moved.Add(newPath + known.Substring(oldPath.Length));
						return true;
					});
					foreach (var known in moved)
					{
						_known.Add(known);
					}

					if (_known.Add(newPath))
					{
						Monitor.PulseAll(s_lock);
					}

					foreach (var subscription in Subscriptions(oldPath))
					{
						Detach(subscription);
						subscription.Directory = newPath + subscription.Directory.Substring(oldPath.Length);
						Attach(subscription);
					}
				}
			}

			// A rename seen from both sides is a rename; from one side, a deletion or a creation.
			foreach (var target in oldTargets)
			{
				string newName = null;
				foreach (var other in newTargets)
				{
					if (other.Key == target.Key)
					{
						newName = other.Value;
						break;
					}
				}

				if (newName != null)
				{
					target.Key.Target.OnRenamed(target.Value, newName);
				}
				else
				{
					target.Key.Target.OnDeleted(target.Value);
				}
			}

			foreach (var target in newTargets)
			{
				if (!oldTargets.Exists(other => other.Key == target.Key))
				{
					target.Key.Target.OnCreated(target.Value);
				}
			}
		}

		void OnError(object sender, ErrorEventArgs e)
		{
			var targets = new List<Subscription>();
			lock (s_lock)
			{
				// Every directory the watcher watches may have missed changes.
				foreach (var list in _directories.Values)
				{
					targets.AddRange(list);
				}

				targets.AddRange(_subtrees);
			}

			var exception = e.GetException();
			foreach (var target in targets)
			{
				target.Target.OnError(exception);
			}
		}

		// Under s_lock: the subscriptions on dir and below it.
		List<Subscription> Subscriptions(string dir)
		{
			var result = new List<Subscription>();
			foreach (var entry in _directories)
			{
				if (IsSelfOrBelow(entry.Key, dir))
				{
					result.AddRange(entry.Value);
				}
			}

			foreach (var subscription in _subtrees)
			{
				if (IsSelfOrBelow(subscription.Directory, dir))
				{
					result.Add(subscription);
				}
			}

			return result;
		}

		sealed class Subscription : IDisposable, IGrowableWatch
		{
			readonly SharedDirectoryWatcher _owner;

			internal Subscription(SharedDirectoryWatcher owner, string directory, bool subtree, IDirectoryWatchTarget target)
			{
				_owner = owner;
				Directory = directory;
				Subtree = subtree;
				Target = target;
			}

			internal string Directory;          // under s_lock
			internal readonly bool Subtree;
			internal readonly IDirectoryWatchTarget Target;
			internal bool Gone;                 // deleted or moved out (under s_lock)
			internal bool Removed;              // disposed (under s_lock)

			public void Dispose()
			{
				_owner.Remove(this);
			}

			public void Grow(int bufferSize)
			{
				_owner.Grow(bufferSize);
			}
		}
	}

	// A shared watch whose watcher's buffer can grow (DirectoryMonitor's response to an overwhelming change).
	internal interface IGrowableWatch
	{
		void Grow(int bufferSize);
	}
}
