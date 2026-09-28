using ContextShell.Settings.Pages;
using ContextShell.Settings.Themes;
using ContextShell.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace ContextShell.Settings
{
	/// <summary>
	/// ContextShell.exe. With no arguments it opens Settings. Other modes:
	///   --check-updates --background   sign-in update check (Run key); silent unless an update is found
	///   --page updates|menu|about       open Settings on a page
	///   --apply &lt;dir&gt; --target &lt;dir&gt;   elevated helper that writes staged config changes
	///   --screenshots &lt;dir&gt; [--config &lt;dir&gt;]   render every page to PNG (docs and CI)
	/// </summary>
	public partial class App : Application
	{
		private Mutex _single;

		[DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
		[DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			var args = e.Args;
			string Arg(string name)
			{
				int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
				return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
			}
			bool Has(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

			if(Has("--apply"))
			{
				Shutdown(ConfigStore.RunElevatedApply(Arg("--apply"), Arg("--target")));
				return;
			}

			ThemeManager.Initialize(this);

			if(Has("--screenshots"))
			{
				RenderScreenshots(Arg("--screenshots"), Arg("--config") ?? AppInfo.FindInstallFolder());
				Shutdown(0);
				return;
			}

			if(Has("--check-updates") && Has("--background"))
			{
				BackgroundCheck();
				return;
			}

			// One Settings window at a time: bring the existing one forward.
			_single = new Mutex(true, @"Local\ContextShell.Settings", out bool first);
			if(!first)
			{
				foreach(var p in Process.GetProcessesByName("ContextShell").Where(p => p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero))
				{
					ShowWindow(p.MainWindowHandle, 9 /*SW_RESTORE*/);
					SetForegroundWindow(p.MainWindowHandle);
				}
				Shutdown(0);
				return;
			}

			var folder = AppInfo.FindInstallFolder();
			var window = new MainWindow(new ConfigStore(folder), Arg("--page") ?? (Has("--check-updates") ? "updates" : null));
			window.Closed += (s, a) => Shutdown(0);
			window.Show();
		}

		private async void BackgroundCheck()
		{
			try
			{
				if(!Updates.AutoCheckEnabled || !UpdateFlow.CheckDue)
				{
					Shutdown(0);
					return;
				}
				// Give sign-in a moment to settle before using the network.
				await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30));
				var release = await UpdateFlow.CheckAsync();
				if(release == null || !Updates.IsNewer(release) || Updates.SkippedVersion == release.Version.ToString(3))
				{
					Shutdown(0);
					return;
				}
				var toast = new UpdateToast(release);
				toast.Closed += (s, e) => Shutdown(0);
				toast.Show();
			}
			catch
			{
				// Offline or GitHub unavailable: try again next sign-in.
				Shutdown(0);
			}
		}

		private void RenderScreenshots(string dir, string configFolder)
		{
			foreach(var dark in new[] { false, true })
			{
				ThemeManager.ForceDark = dark;
				ThemeManager.Apply(this);
				var mode = dark ? "dark" : "light";
				var store = new ConfigStore(configFolder);

				foreach(var page in new[] { "appearance", "menu", "updates", "about" })
				{
					var w = new MainWindow(store, page);
					Snapshot.Save(w, Path.Combine(dir, $"settings-{page}-{mode}.png"), 1100, 720);
					w.Close();
				}

				var editorWindow = new MainWindow(store);
				var theme = NssTheme.Load(Path.Combine(store.ThemesFolder, "liquid-glass.nss"));
				theme.IsPreset = true;
				var editor = new ThemeEditorPage(editorWindow, theme);
				editorWindow.ShowPage(editor);
				Snapshot.Save(editorWindow, Path.Combine(dir, $"settings-editor-{mode}.png"), 1100, 720);
				editorWindow.Close();

				var toast = new UpdateToast(new ReleaseInfo
				{
					Version = new Version(AppInfo.Version.Major, AppInfo.Version.Minor, AppInfo.Version.Build + 1),
					Tag = "v-next",
					Notes = "## What's new\nNew Liquid Glass theme, a theme editor for every option, and a new installer.",
				});
				Snapshot.Save(toast, Path.Combine(dir, $"settings-toast-{mode}.png"), 400, 200);
				toast.Close();
			}
		}
	}
}
