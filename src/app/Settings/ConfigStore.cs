using ContextShell.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace ContextShell.Settings
{
	/// <summary>
	/// Reads ContextShell's config files and writes changes back. The files live in Program Files,
	/// so writing needs admin rights: changes are staged, and Apply writes them in one elevated step
	/// (one UAC prompt) by re-running this app with --apply.
	/// </summary>
	public sealed class ConfigStore
	{
		public string InstallFolder { get; }
		public string ImportsFolder => Path.Combine(InstallFolder, "imports");
		public string ThemesFolder => Path.Combine(ImportsFolder, "themes");
		public string ShellNss => Path.Combine(InstallFolder, "shell.nss");
		public string ThemeNss => Path.Combine(ImportsFolder, "theme.nss");

		private readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _deletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public event EventHandler Changed;

		public ConfigStore(string installFolder)
		{
			// The MSI records "C:\...\ContextShell\" with a trailing backslash. Drop it: in a quoted
			// command-line argument, \" escapes the closing quote and corrupts the path.
			InstallFolder = installFolder?.TrimEnd('\\');
		}

		public bool IsValid => InstallFolder != null && File.Exists(ShellNss);
		public bool HasChanges => _pending.Count > 0 || _deletes.Count > 0;

		/// <summary>Current text of a file, including staged changes.</summary>
		public string Read(string path)
		{
			if(_deletes.Contains(path))
				return null;
			if(_pending.TryGetValue(path, out var text))
				return text;
			try
			{
				return File.Exists(path) ? File.ReadAllText(path) : null;
			}
			catch
			{
				return null;
			}
		}

		public bool Exists(string path) => !_deletes.Contains(path) && (_pending.ContainsKey(path) || File.Exists(path));

		public IEnumerable<string> ThemeFiles()
		{
			var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if(Directory.Exists(ThemesFolder))
				foreach(var f in Directory.GetFiles(ThemesFolder, "*.nss"))
					files.Add(f);
			foreach(var f in _pending.Keys.Where(k => k.StartsWith(ThemesFolder + "\\", StringComparison.OrdinalIgnoreCase)))
				files.Add(f);
			return files.Where(f => !_deletes.Contains(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
		}

		public void Stage(string path, string text)
		{
			CheckPath(path);
			_deletes.Remove(path);
			var current = File.Exists(path) ? SafeRead(path) : null;
			if(current == text)
				_pending.Remove(path);
			else
				_pending[path] = text;
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public void StageDelete(string path)
		{
			CheckPath(path);
			_pending.Remove(path);
			if(File.Exists(path))
				_deletes.Add(path);
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public void Discard()
		{
			_pending.Clear();
			_deletes.Clear();
			Changed?.Invoke(this, EventArgs.Empty);
		}

		private static string SafeRead(string path)
		{
			try { return File.ReadAllText(path); } catch { return null; }
		}

		private void CheckPath(string path)
		{
			var full = Path.GetFullPath(path);
			if(!full.StartsWith(InstallFolder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
			   || !full.EndsWith(".nss", StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Only .nss files inside the ContextShell folder can be changed.");
		}

		/// <summary>Write staged changes. Returns null on success or an error message.</summary>
		public string Apply()
		{
			if(!HasChanges)
				return null;

			string error = CanWriteDirectly() ? WriteFiles(InstallFolder, _pending, _deletes) : ApplyElevated();
			if(error == null)
			{
				_pending.Clear();
				_deletes.Clear();
				Changed?.Invoke(this, EventArgs.Empty);
			}
			return error;
		}

		private bool CanWriteDirectly()
		{
			if(IsElevated)
				return true;
			try
			{
				var probe = Path.Combine(ImportsFolder, ".write-test");
				File.WriteAllText(probe, "");
				File.Delete(probe);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public static bool IsElevated
		{
			get
			{
				using(var id = WindowsIdentity.GetCurrent())
					return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
			}
		}

		// Staged changes travel to the elevated helper as a folder of files plus a manifest.
		private const string ManifestName = "apply.txt";

		private string ApplyElevated()
		{
			var stage = Path.Combine(Path.GetTempPath(), "ContextShell-Apply-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			Directory.CreateDirectory(stage);
			try
			{
				var manifest = new StringBuilder();
				int n = 0;
				foreach(var kv in _pending)
				{
					var name = $"{n++}.nss";
					File.WriteAllText(Path.Combine(stage, name), kv.Value, new UTF8Encoding(false));
					manifest.Append("W\t").Append(name).Append('\t').AppendLine(RelativePath(kv.Key));
				}
				foreach(var d in _deletes)
					manifest.Append("D\t\t").AppendLine(RelativePath(d));
				File.WriteAllText(Path.Combine(stage, ManifestName), manifest.ToString());

				var exe = Process.GetCurrentProcess().MainModule.FileName;
				var psi = new ProcessStartInfo(exe, $"--apply {QuoteArg(stage)} --target {QuoteArg(InstallFolder)}")
				{
					UseShellExecute = true,
					Verb = "runas",
				};
				using(var p = Process.Start(psi))
				{
					p.WaitForExit();
					if(p.ExitCode != 0)
						return File.Exists(Path.Combine(stage, "error.txt"))
							? File.ReadAllText(Path.Combine(stage, "error.txt"))
							: "Couldn't save the changes.";
				}
				return null;
			}
			catch(Win32Exception ex) when(ex.NativeErrorCode == 1223)
			{
				return "Changes weren't saved because administrator permission was declined.";
			}
			catch(Exception ex)
			{
				return ex.Message;
			}
			finally
			{
				try { Directory.Delete(stage, true); } catch { }
			}
		}

		/// <summary>Quote a path for a command line. Trailing backslashes would escape the closing quote.</summary>
		private static string QuoteArg(string path) => "\"" + path.TrimEnd('\\') + "\"";

		private string RelativePath(string full) => full.Substring(InstallFolder.TrimEnd('\\').Length + 1);

		/// <summary>Entry point for the elevated helper (--apply). Returns the process exit code.</summary>
		public static int RunElevatedApply(string stage, string target)
		{
			try
			{
				var installed = AppInfo.FindInstallFolder();
				// Only ever write into the real install folder, whatever the command line says.
				if(installed == null || !Path.GetFullPath(target).TrimEnd('\\').Equals(Path.GetFullPath(installed).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException("The target folder isn't the ContextShell install folder.");

				var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var deletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach(var line in File.ReadAllLines(Path.Combine(stage, ManifestName)))
				{
					var parts = line.Split('\t');
					if(parts.Length != 3)
						continue;
					var dest = Path.GetFullPath(Path.Combine(installed, parts[2]));
					if(parts[0] == "W")
						pending[dest] = File.ReadAllText(Path.Combine(stage, Path.GetFileName(parts[1])));
					else if(parts[0] == "D")
						deletes.Add(dest);
				}
				var error = WriteFiles(installed, pending, deletes);
				if(error != null)
					throw new InvalidOperationException(error);
				return 0;
			}
			catch(Exception ex)
			{
				try { File.WriteAllText(Path.Combine(stage, "error.txt"), ex.Message); } catch { }
				return 1;
			}
		}

		private static string WriteFiles(string root, Dictionary<string, string> writes, HashSet<string> deletes)
		{
			var prefix = root.TrimEnd('\\') + "\\";
			try
			{
				foreach(var kv in writes)
				{
					var full = Path.GetFullPath(kv.Key);
					if(!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".nss", StringComparison.OrdinalIgnoreCase))
						return "Refused to write outside the ContextShell folder.";
					Directory.CreateDirectory(Path.GetDirectoryName(full));
					File.WriteAllText(full, kv.Value, new UTF8Encoding(false));
				}
				foreach(var d in deletes)
				{
					var full = Path.GetFullPath(d);
					if(!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".nss", StringComparison.OrdinalIgnoreCase))
						return "Refused to delete outside the ContextShell folder.";
					if(File.Exists(full))
						File.Delete(full);
				}
				return null;
			}
			catch(Exception ex)
			{
				return ex.Message;
			}
		}

		/// <summary>Run shell.exe with arguments, elevated. Used for register/unregister and Explorer restart.</summary>
		public string RunShellExe(string args)
		{
			var exe = Path.Combine(InstallFolder, "shell.exe");
			try
			{
				using(var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WorkingDirectory = InstallFolder }))
					p.WaitForExit(60000);
				return null;
			}
			catch(Win32Exception ex) when(ex.NativeErrorCode == 1223)
			{
				return "Administrator permission was declined.";
			}
			catch(Exception ex)
			{
				return ex.Message;
			}
		}
	}
}
