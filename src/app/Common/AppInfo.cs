using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ContextShell.UI
{
	public static class AppInfo
	{
		public const string Name = "ContextShell";
		public const string Publisher = "TheRayJohnson";
		public const string Description = "A lightweight, fully customizable right-click menu for Windows File Explorer.";

		/// <summary>MSI UpgradeCode from src/setup/wix/setup.wxs. Identifies every ContextShell version.</summary>
		public const string UpgradeCode = "{6FA65DF1-68C1-476C-A3CF-C09ED150D1C7}";

		/// <summary>CLSID of the context menu handler, as registered by shell.exe.</summary>
		public const string ContextMenuClsid = "{3F580C96-2A74-458D-8FBB-80DAC41FC5D1}";

		/// <summary>HKLM key the MSI writes install info to.</summary>
		public const string SetupKey = @"SOFTWARE\TheRayJohnson\ContextShell\Setup";

		/// <summary>HKCU key for per-user app settings (update preferences).</summary>
		public const string UserKey = @"Software\TheRayJohnson\ContextShell\App";

		public static Version Version
		{
			get
			{
				var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
				return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
			}
		}

		public static string VersionText => Version.ToString(3);

		public static string Repository =>
			Assembly.GetEntryAssembly()?
				.GetCustomAttributes<AssemblyMetadataAttribute>()
				.FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "TheRayJohnson/ContextShell";

		public static string RepositoryUrl => "https://github.com/" + Repository;

		/// <summary>Architecture name used in release asset names: x64, x86 or arm64.</summary>
		public static string OSArchitecture
		{
			get
			{
				switch(RuntimeInformation.OSArchitecture)
				{
					case Architecture.Arm64: return "arm64";
					case Architecture.X86: return "x86";
					default: return "x64";
				}
			}
		}

		/// <summary>Install folder of the current ContextShell install, or null.</summary>
		public static string FindInstallFolder()
		{
			try
			{
				using(var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				{
					using(var key = hklm.OpenSubKey(SetupKey))
					{
						if(key?.GetValue("InstallFolder") is string dir && Directory.Exists(dir))
							return dir.TrimEnd('\\');
					}
					// Fallback for 1.9.19, which had no Setup key: the COM registration.
					using(var key = hklm.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + ContextMenuClsid + @"\InprocServer32"))
					{
						if(key?.GetValue(null) is string dll && File.Exists(dll))
							return Path.GetDirectoryName(dll);
					}
				}
			}
			catch
			{
			}

			// Running from the install folder (the settings app ships next to shell.exe).
			var here = AppDomain.CurrentDomain.BaseDirectory;
			return File.Exists(Path.Combine(here, "shell.dll")) ? here.TrimEnd('\\') : null;
		}

		public static void OpenUrl(string url)
		{
			try
			{
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			}
			catch
			{
			}
		}

		/// <summary>
		/// Start a program as the normal (non-elevated) desktop user, even from an elevated process,
		/// by asking Explorer to launch it.
		/// </summary>
		public static void StartUnelevated(string path)
		{
			try
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
			}
			catch
			{
			}
		}
	}
}
