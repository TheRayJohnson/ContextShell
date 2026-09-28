using ContextShell.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ContextShell.Setup
{
	/// <summary>Command line, detected install state and the user's choices.</summary>
	public sealed class SetupSession
	{
		// Command line
		public bool Quiet, Passive, Uninstall, Repair, UpdateMode, Help, NoRestart;
		public string LogPath, ScreenshotDir, ExtractDir;
		public readonly List<string> ExtraProperties = new List<string>();

		// Detected state
		public string InstalledProductCode;
		public Version InstalledVersion;
		public string InstalledFolder;
		public Version PackageVersion = AppInfo.Version;

		// Choices
		public string InstallFolder;
		public bool StartMenuShortcut = true;
		public bool DesktopShortcut;
		public bool AutoUpdate = true;

		public bool IsInstalled => InstalledProductCode != null;
		public bool IsUpgrade => IsInstalled && InstalledVersion != null && InstalledVersion < PackageVersion;
		public bool IsSameVersion => IsInstalled && InstalledVersion == PackageVersion;
		public bool IsNewerInstalled => IsInstalled && InstalledVersion != null && InstalledVersion > PackageVersion;

		public static string DefaultFolder =>
			Path.Combine(Environment.GetFolderPath(Environment.Is64BitOperatingSystem
				? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.ProgramFilesX86), AppInfo.Name);

		public static SetupSession Parse(string[] args)
		{
			var s = new SetupSession();
			for(int i = 0; i < args.Length; i++)
			{
				var a = args[i];
				var opt = a.TrimStart('/', '-').ToLowerInvariant();
				bool isSwitch = a.StartsWith("/") || a.StartsWith("-");
				if(!isSwitch && a.Contains("="))
				{
					s.ExtraProperties.Add(a);
					continue;
				}
				switch(opt)
				{
					case "q": case "quiet": case "qn": case "silent": case "s": case "verysilent":
						s.Quiet = true; break;
					case "passive": case "qb":
						s.Passive = true; break;
					case "x": case "uninstall":
						s.Uninstall = true; break;
					case "repair": case "f":
						s.Repair = true; break;
					case "update":
						s.UpdateMode = true; break;
					case "norestart":
						s.NoRestart = true; break;
					case "l": case "log": case "l*v":
						if(i + 1 < args.Length) s.LogPath = Path.GetFullPath(args[++i]);
						break;
					case "screenshots":
						if(i + 1 < args.Length) s.ScreenshotDir = Path.GetFullPath(args[++i]);
						break;
					case "extract":
						s.ExtractDir = i + 1 < args.Length ? Path.GetFullPath(args[++i]) : Environment.CurrentDirectory;
						break;
					case "?": case "h": case "help":
						s.Help = true; break;
				}
			}

			if(string.IsNullOrEmpty(s.LogPath))
				s.LogPath = Path.Combine(Path.GetTempPath(), $"ContextShell-Setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");

			// INSTALLFOLDER=... on the command line also sets the folder the UI shows.
			var folderProp = s.ExtraProperties.FirstOrDefault(p => p.StartsWith("INSTALLFOLDER=", StringComparison.OrdinalIgnoreCase));
			if(folderProp != null)
				s.InstallFolder = folderProp.Substring("INSTALLFOLDER=".Length).Trim('"');
			return s;
		}

		public const string HelpText =
@"ContextShell Setup

Usage: ContextShell-Setup.exe [options] [PROPERTY=value ...]

  /quiet, /q        Install with no UI. Exit code is the Windows Installer result.
  /passive          Show progress only, no questions.
  /uninstall, /x    Remove ContextShell.
  /repair           Repair the current install.
  /log <file>       Write a verbose Windows Installer log to <file>.
  /norestart        Never restart Windows, even if needed.
  /extract [dir]    Save the embedded MSI to dir (for msiexec, Intune, GPO).
  /?                Show this help.

MSI properties:
  INSTALLFOLDER=<path>   Install location (default: Program Files\ContextShell)
  ADDSTARTMENU=0|1       Start menu shortcut (default 1)
  ADDDESKTOP=0|1         Desktop shortcut (default 0)
  AUTOUPDATE=0|1         Check GitHub for updates at sign-in (default 1)";

		public void Detect()
		{
			var products = MsiEngine.RelatedProducts(AppInfo.UpgradeCode);
			// Prefer the newest if more than one is registered.
			foreach(var code in products)
			{
				var v = Updates.ParseVersion(MsiEngine.ProductInfo(code, "VersionString"));
				if(InstalledProductCode == null || (v != null && (InstalledVersion == null || v > InstalledVersion)))
				{
					InstalledProductCode = code;
					InstalledVersion = v;
				}
			}

			if(IsInstalled)
			{
				InstalledFolder = MsiEngine.ProductInfo(InstalledProductCode, "InstallLocation");
				if(string.IsNullOrEmpty(InstalledFolder))
					InstalledFolder = AppInfo.FindInstallFolder();
				InstalledFolder = InstalledFolder?.TrimEnd('\\');

				// Carry the previous choices over to the update.
				if(!string.IsNullOrEmpty(InstalledFolder))
				{
					var hadSetupKey = ReadSetupValue("InstallFolder") != null;
					if(hadSetupKey)
					{
						StartMenuShortcut = ReadSetupValue("StartMenuShortcut") == "1";
						DesktopShortcut = ReadSetupValue("DesktopShortcut") == "1";
						AutoUpdate = ReadSetupValue("AutoUpdate") == "1";
					}
				}
			}

			if(string.IsNullOrEmpty(InstallFolder))
				InstallFolder = !string.IsNullOrEmpty(InstalledFolder) ? InstalledFolder : DefaultFolder;
		}

		private static string ReadSetupValue(string name)
		{
			try
			{
				using(var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				using(var key = hklm.OpenSubKey(AppInfo.SetupKey))
					return key?.GetValue(name)?.ToString();
			}
			catch
			{
				return null;
			}
		}

		/// <summary>Why the folder can't be used, or null if it's fine. Mirrors the MSI's ValidatePath check.</summary>
		public static string ValidateFolder(string folder)
		{
			if(string.IsNullOrWhiteSpace(folder))
				return "Choose a folder.";
			try
			{
				if(!Path.IsPathRooted(folder) || folder.StartsWith(@"\\"))
					return "Use a folder on a local drive, like C:\\Program Files\\ContextShell.";
				if(folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
					return "The folder name contains characters Windows doesn't allow.";
				var root = Path.GetPathRoot(Path.GetFullPath(folder));
				var drive = new DriveInfo(root);
				if(!drive.IsReady)
					return $"Drive {root} isn't available.";
				if(drive.DriveType != DriveType.Fixed)
					return "Install to a fixed local drive, not a network, removable or optical drive.";
			}
			catch
			{
				return "That isn't a valid folder.";
			}
			return null;
		}

		public string BuildProperties()
		{
			var sb = new StringBuilder();
			void Add(string name, string value) => sb.Append(name).Append("=\"").Append(value.Replace("\"", "")).Append("\" ");

			if(!IsInstalled || IsUpgrade)
			{
				Add("INSTALLFOLDER", InstallFolder.TrimEnd('\\') + "\\");
				Add("ADDSTARTMENU", StartMenuShortcut ? "1" : "0");
				Add("ADDDESKTOP", DesktopShortcut ? "1" : "0");
				Add("AUTOUPDATE", AutoUpdate ? "1" : "0");
			}
			if(NoRestart)
				Add("REBOOT", "ReallySuppress");
			// Command-line properties win over the UI's.
			foreach(var p in ExtraProperties)
				sb.Append(p).Append(' ');
			return sb.ToString().Trim();
		}

		public void OpenLog()
		{
			if(File.Exists(LogPath))
				AppInfo.OpenUrl(LogPath);
		}

		/// <summary>Start ContextShell Settings as the desktop user.</summary>
		public void LaunchSettings()
		{
			var folder = AppInfo.FindInstallFolder() ?? InstallFolder;
			var exe = Path.Combine(folder ?? "", "ContextShell.exe");
			if(File.Exists(exe))
				AppInfo.StartUnelevated(exe);
		}

		/// <summary>
		/// Re-run the registration the MSI's install custom action does (shell.exe -r -s -t -restart).
		/// Used after a repair, which Windows Installer runs without that custom action.
		/// </summary>
		public void Reregister()
		{
			var folder = AppInfo.FindInstallFolder() ?? InstalledFolder;
			var exe = Path.Combine(folder ?? "", "shell.exe");
			if(!File.Exists(exe))
				return;
			try
			{
				using(var p = Process.Start(new ProcessStartInfo(exe, "-r -s -t -restart")
				{
					WorkingDirectory = folder,
					UseShellExecute = false,
					CreateNoWindow = true,
				}))
					p?.WaitForExit(60000);
			}
			catch
			{
			}
		}

		/// <summary>Close a running settings app / update check so its files can be replaced.</summary>
		public static void CloseRunningApp()
		{
			foreach(var p in Process.GetProcessesByName("ContextShell"))
			{
				try
				{
					if(!p.CloseMainWindow() || !p.WaitForExit(3000))
						p.Kill();
				}
				catch
				{
				}
			}
		}
	}
}
