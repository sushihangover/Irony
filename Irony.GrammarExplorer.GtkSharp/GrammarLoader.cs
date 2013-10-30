#region License
/* **********************************************************************************
 * Copyright (c) Robert Nees (https://github.com/sushihangover/Irony)
 * This source code is subject to terms and conditions of the MIT License
 * for Irony. A copy of the license can be found in the License.txt file
 * at the root of this distribution.
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the
 * MIT License.
 * You must not remove this notice from this software.
 * **********************************************************************************/
// Original Windows.Forms Version by Roman Ivantsov
// This file and all functionality of dynamic assembly reloading was contributed by Alexey Yakovlev (yallie)
#endregion
using Gtk;
using Gdk;
using GLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Irony.Parsing;
using System.IO;
using System.Threading;

namespace Irony.GrammarExplorer
{
	/// <summary>
	/// Maintains grammar assemblies, reloads updated files automatically.
	/// </summary>
	class GrammarLoader
	{
		private TimeSpan _autoRefreshDelay = TimeSpan.FromMilliseconds (1000);
		private static HashSet<string> _probingPaths = new HashSet<string> ();
		private Dictionary<string, CachedAssembly> _cachedGrammarAssemblies = new Dictionary<string, CachedAssembly> ();
		private static Dictionary<string, Assembly> _loadedAssembliesByNames = new Dictionary<string, Assembly> ();
		private static HashSet<Assembly> _loadedAssemblies = new HashSet<Assembly> ();
		private static bool _enableBrowsingForAssemblyResolution = false;

		static GrammarLoader ()
		{
			AppDomain.CurrentDomain.AssemblyLoad += (sender, args) => _loadedAssembliesByNames [args.LoadedAssembly.FullName] = args.LoadedAssembly;
			AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => FindAssembly (args.Name);
		}

		static Assembly FindAssembly (string assemblyName)
		{
			if (_loadedAssembliesByNames.ContainsKey (assemblyName))
				return _loadedAssembliesByNames [assemblyName];
			// ignore resource assemblies
			if (assemblyName.ToLower ().Contains (".resources, version="))
				return _loadedAssembliesByNames [assemblyName] = null;
			// use probing paths to look for dependency assemblies
			var fileName = assemblyName.Split (',').First () + ".dll";
			foreach (var path in _probingPaths) {
				var fullName = Path.Combine (path, fileName);
				if (File.Exists (fullName)) {
					try {
						return LoadAssembly (fullName);
					} catch {
						// the file seems to be bad, let's try to find another one
					}
				}
			}
			// the last chance: try asking user to locate the assembly
			if (_enableBrowsingForAssemblyResolution) {
				fileName = BrowseFor (assemblyName);
				if (!string.IsNullOrWhiteSpace (fileName))
					return LoadAssembly (fileName);
			}
			// assembly not found, don't search for it again
			return _loadedAssembliesByNames [assemblyName] = null;
		}

		static string BrowseFor (string assemblyName)
		{
			// TODO: Add filters:
			// Filter = "Assemblies (*.dll)|*.dll|All files (*.*)|*.*"
			Gtk.FileChooserDialog fc =
				new Gtk.FileChooserDialog ("Locate Assembly",
				                          null,
				                          FileChooserAction.Open,
				                          "Cancel", ResponseType.Cancel,
				                          "Open", ResponseType.Accept);
			fc.Run ();
			string location = fc.Filename;
			fc.Destroy ();
			return location;
		}

		class CachedAssembly
		{
			public long FileSize;
			public DateTime LastWriteTime;
			public FileSystemWatcher Watcher;
			public Assembly Assembly;
			public bool UpdateScheduled;
		}

		public event EventHandler AssemblyUpdated;

		public GrammarItem SelectedGrammar { get; set; }

		public Grammar CreateGrammar ()
		{
			if (SelectedGrammar == null)
				return null;

			// resolve dependencies while loading and creating grammars
			_enableBrowsingForAssemblyResolution = true;
			try {
				var type = SelectedGrammarAssembly.GetType (SelectedGrammar.TypeName, true, true);
				return Activator.CreateInstance (type) as Grammar;
			} finally {
				_enableBrowsingForAssemblyResolution = false;
			}
		}

		Assembly SelectedGrammarAssembly {
			get {
				if (SelectedGrammar == null)
					return null;

				// create assembly cache entry as needed
				var location = SelectedGrammar.Location;
				if (!_cachedGrammarAssemblies.ContainsKey (location)) {
					var fileInfo = new FileInfo (location);
					_cachedGrammarAssemblies [location] =
            new CachedAssembly {
						LastWriteTime = fileInfo.LastWriteTime,
						FileSize = fileInfo.Length,
						Assembly = null
					};

					// set up file system watcher
					_cachedGrammarAssemblies [location].Watcher = CreateFileWatcher (location);
				}

				// get loaded assembly from cache if possible
				var assembly = _cachedGrammarAssemblies [location].Assembly;
				if (assembly == null) {
					assembly = LoadAssembly (location);
					_cachedGrammarAssemblies [location].Assembly = assembly;
				}

				return assembly;
			}
		}

		private FileSystemWatcher CreateFileWatcher (string location)
		{

			var folder = Path.GetDirectoryName (location);
			var watcher = new FileSystemWatcher (folder);
			// Bug 428270 - FileSystemWatcher does not raise Changed() event on MacOSX Tiger 10.4
			// https://bugzilla.novell.com/show_bug.cgi?id=428270
			// This bug has been open since 2008 with as a P5, thus it will never be fixed, still broken up to 10.9
			if ((int)Environment.OSVersion.Platform == 6 || (int)Environment.OSVersion.Platform == 4 ) {
				// Why does mono report OS-X as 4 (unix/linux) vs. 6?, well, look it up in the mono bugs and you get the dumest answer:
				//<Quote> Originally .NET had no enum value for OSX and we returned Unix.
				//The, when it was introduced by microsoft, we tried to switch to the new
				//value
				//but too much stuff broke. We never tried again ever since a couple of years
				//ago. </Quote>
				if (OpenTK.Configuration.RunningOnMacOS) {
					watcher.Filter = "*";
				} else {
					watcher.Filter = Path.GetFileName (location);
				}
				// It seems that watching ALL file changes in a dir will work under OS-X 10.8.5, if we where using 
				// MonoMac then the natvie FSEvent could be used... please report any issues about other versions of OS-X 
				// or Linux
			} else {
				// All other OSs (well, Windows) can watch just the actual assembly
				watcher.Filter = Path.GetFileName (location);
			}
	
			watcher.Changed += (s, args) => {
				if (args.ChangeType != WatcherChangeTypes.Changed)
					return;

				lock (this) {
					// check if assembly file was changed indeed since the last event
					var cacheEntry = _cachedGrammarAssemblies [location];
					var fileInfo = new FileInfo (location);
					if (cacheEntry.LastWriteTime == fileInfo.LastWriteTime && cacheEntry.FileSize == fileInfo.Length)
						return;

					// reset cached assembly and save last file update time
					cacheEntry.LastWriteTime = fileInfo.LastWriteTime;
					cacheEntry.FileSize = fileInfo.Length;
					cacheEntry.Assembly = null;

					// check if file update is already scheduled (work around multiple FileSystemWatcher event firing)
					if (!cacheEntry.UpdateScheduled) {
						cacheEntry.UpdateScheduled = true;
						// delay auto-refresh to make sure the file is closed by the writer
						ThreadPool.QueueUserWorkItem (_ => {
							System.Threading.Thread.Sleep (_autoRefreshDelay);
							cacheEntry.UpdateScheduled = false;
							OnAssemblyUpdated (location);
						});
					}
				}
			};

			watcher.EnableRaisingEvents = true;
			return watcher;
		}

		private void OnAssemblyUpdated (string location)
		{
			if (AssemblyUpdated == null || SelectedGrammar == null || SelectedGrammar.Location != location)
				return;
			AssemblyUpdated (this, EventArgs.Empty);
		}

		public static Assembly LoadAssembly (string fileName)
		{
			// normalize the filename
			fileName = new FileInfo (fileName).FullName;
			// save assembly path for dependent assemblies probing
			var path = Path.GetDirectoryName (fileName);
			_probingPaths.Add (path);
			// try to load assembly using the standard policy
			var assembly = Assembly.LoadFrom (fileName);
			// if the standard policy returned the old version, force reload
			if (_loadedAssemblies.Contains (assembly)) {
				assembly = Assembly.Load (File.ReadAllBytes (fileName));
			}
			// cache the loaded assembly by its location
			_loadedAssemblies.Add (assembly);
			return assembly;
		}
	}
}
