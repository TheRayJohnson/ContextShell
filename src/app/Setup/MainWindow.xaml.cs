using ContextShell.UI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ContextShell.Setup
{
	public partial class MainWindow : Window
	{
		private enum Page { Welcome, Options, Progress, Finish, Maintenance, Error }

		private readonly SetupSession _session;
		private Page _page;
		private MsiEngine _engine;
		private MsiOperation _operation;
		private bool _running;
		private bool _rebootRequired;
		private TaskCompletionSource<int> _dialog;

		public int ExitCode { get; private set; } = MsiEngine.ERROR_INSTALL_USEREXIT;

		public MainWindow(SetupSession session)
		{
			_session = session;
			InitializeComponent();
			ThemeManager.Attach(this);

			var v = session.PackageVersion.ToString(3);
			WelcomeVersion.Text = "Version " + v;
			FolderBox.Text = session.InstallFolder;
			StartMenuBox.IsChecked = session.StartMenuShortcut;
			DesktopBox.IsChecked = session.DesktopShortcut;
			AutoUpdateBox.IsChecked = session.AutoUpdate;

			if(session.IsUpgrade)
			{
				Title = "ContextShell Setup";
				WelcomeTitle.Text = "Update ContextShell";
				WelcomeBody.Text = $"Version {session.InstalledVersion.ToString(3)} is installed. Setup will update it to {v}. Your menu configuration is kept.";
			}
			else
			{
				WelcomeBody.Text = AppInfo.Description + " Setup will install ContextShell on this PC and add it to File Explorer.";
			}

			Loaded += (s, e) => Start();
		}

		private void Start()
		{
			var s = _session;
			if(s.Uninstall)
			{
				if(s.IsInstalled) Run(MsiOperation.Uninstall);
				else ShowError("ContextShell isn't installed", "There's nothing to remove.", 1605, info: true);
			}
			else if(s.Repair)
			{
				if(s.IsInstalled) Run(MsiOperation.Repair);
				else ShowError("ContextShell isn't installed", "There's nothing to repair. Run setup without /repair to install it.", 1605, info: true);
			}
			else if(s.IsNewerInstalled)
			{
				ShowError("A newer version is installed",
					$"ContextShell {s.InstalledVersion.ToString(3)} is already installed. To go back to {s.PackageVersion.ToString(3)}, uninstall it first from Settings > Apps.",
					1638, info: true);
			}
			else if(s.IsSameVersion && !s.UpdateMode && !s.Passive)
			{
				MaintenanceTitle.Text = $"ContextShell {s.InstalledVersion.ToString(3)} is installed";
				ShowPage(Page.Maintenance);
			}
			else if(s.IsSameVersion)
				Run(MsiOperation.Repair);
			else if(s.UpdateMode || s.Passive)
				Run(s.IsUpgrade ? MsiOperation.Upgrade : MsiOperation.Install);
			else
				ShowPage(Page.Welcome);
		}

		// Pages

		private void ShowPage(Page page)
		{
			_page = page;
			WelcomePage.Visibility = page == Page.Welcome ? Visibility.Visible : Visibility.Collapsed;
			OptionsPage.Visibility = page == Page.Options ? Visibility.Visible : Visibility.Collapsed;
			ProgressPage.Visibility = page == Page.Progress ? Visibility.Visible : Visibility.Collapsed;
			FinishPage.Visibility = page == Page.Finish ? Visibility.Visible : Visibility.Collapsed;
			MaintenancePage.Visibility = page == Page.Maintenance ? Visibility.Visible : Visibility.Collapsed;
			ErrorPage.Visibility = page == Page.Error ? Visibility.Visible : Visibility.Collapsed;

			SecondaryButton.Visibility = Visibility.Visible;
			SecondaryButton.IsEnabled = true;
			PrimaryButton.Visibility = Visibility.Visible;
			PrimaryButton.IsEnabled = true;
			PrimaryArrow.Visibility = Visibility.Collapsed;

			switch(page)
			{
				case Page.Welcome:
					SecondaryButton.Content = "Cancel";
					PrimaryText.Text = _session.IsUpgrade ? "Update" : "Continue";
					PrimaryArrow.Visibility = _session.IsUpgrade ? Visibility.Collapsed : Visibility.Visible;
					break;
				case Page.Options:
					SecondaryButton.Content = "Back";
					PrimaryText.Text = "Install";
					ValidateFolder();
					FolderBox.Focus();
					break;
				case Page.Progress:
					SecondaryButton.Content = "Cancel";
					PrimaryButton.Visibility = Visibility.Collapsed;
					break;
				case Page.Finish:
					SecondaryButton.Visibility = _rebootRequired ? Visibility.Visible : Visibility.Collapsed;
					SecondaryButton.Content = "Restart later";
					PrimaryText.Text = _rebootRequired ? "Restart now" : "Close";
					break;
				case Page.Maintenance:
					SecondaryButton.Visibility = Visibility.Collapsed;
					PrimaryText.Text = "Close";
					break;
				case Page.Error:
					SecondaryButton.Visibility = Visibility.Collapsed;
					PrimaryText.Text = "Close";
					break;
			}
		}

		private void Primary_Click(object sender, RoutedEventArgs e)
		{
			switch(_page)
			{
				case Page.Welcome:
					if(_session.IsUpgrade)
						Run(MsiOperation.Upgrade);
					else
						ShowPage(Page.Options);
					break;
				case Page.Options:
					if(!ValidateFolder())
						return;
					_session.InstallFolder = FolderBox.Text.Trim();
					_session.StartMenuShortcut = StartMenuBox.IsChecked == true;
					_session.DesktopShortcut = DesktopBox.IsChecked == true;
					_session.AutoUpdate = AutoUpdateBox.IsChecked == true;
					Run(MsiOperation.Install);
					break;
				case Page.Finish:
					if(_rebootRequired)
						Restart();
					else if(LaunchBox.Visibility == Visibility.Visible && LaunchBox.IsChecked == true)
						_session.LaunchSettings();
					Close();
					break;
				default:
					Close();
					break;
			}
		}

		private void Secondary_Click(object sender, RoutedEventArgs e)
		{
			switch(_page)
			{
				case Page.Options:
					ShowPage(Page.Welcome);
					break;
				case Page.Progress:
					ConfirmCancel();
					break;
				default:
					Close();
					break;
			}
		}

		private void FolderBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if(IsLoaded)
				ValidateFolder();
		}

		private bool ValidateFolder()
		{
			var error = SetupSession.ValidateFolder(FolderBox.Text.Trim());
			FolderError.Text = error ?? "";
			FolderError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
			FolderHint.Visibility = error == null ? Visibility.Visible : Visibility.Collapsed;
			if(_page == Page.Options)
				PrimaryButton.IsEnabled = error == null;
			return error == null;
		}

		private void Browse_Click(object sender, RoutedEventArgs e)
		{
			var picked = FolderPicker.Show(this, "Choose where to install ContextShell", FolderBox.Text);
			if(picked == null)
				return;
			// Picking a parent folder like "D:\Apps" installs into "D:\Apps\ContextShell".
			if(!picked.TrimEnd('\\').EndsWith(AppInfo.Name, StringComparison.OrdinalIgnoreCase))
				picked = System.IO.Path.Combine(picked, AppInfo.Name);
			FolderBox.Text = picked;
		}

		private void Repair_Click(object sender, RoutedEventArgs e) => Run(MsiOperation.Repair);

		private async void Uninstall_Click(object sender, RoutedEventArgs e)
		{
			var answer = await ShowDialog("Uninstall ContextShell?",
				"Your right-click menu goes back to the Windows default. File Explorer restarts for a moment.",
				("Uninstall", MsiEngine.IDYES, true), ("Cancel", MsiEngine.IDNO, false));
			if(answer == MsiEngine.IDYES)
				Run(MsiOperation.Uninstall);
		}

		private void License_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl + "/blob/main/LICENSE");
		private void GitHub_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl);
		private void Log_Click(object sender, RoutedEventArgs e) => _session.OpenLog();

		// Running Windows Installer

		private async void Run(MsiOperation op)
		{
			_operation = op;
			_running = true;
			switch(op)
			{
				case MsiOperation.Install: ProgressTitle.Text = "Installing ContextShell…"; break;
				case MsiOperation.Upgrade: ProgressTitle.Text = "Updating ContextShell…"; break;
				case MsiOperation.Repair: ProgressTitle.Text = "Repairing ContextShell…"; break;
				case MsiOperation.Uninstall: ProgressTitle.Text = "Removing ContextShell…"; break;
			}
			ProgressStatus.Text = "Preparing…";
			Bar.IsIndeterminate = true;
			ProgressPercent.Text = "";
			ShowPage(Page.Progress);

			string package = null;
			if(op != MsiOperation.Uninstall)
			{
				package = await Task.Run(() => MsiEngine.ExtractPackage());
				if(package == null)
				{
					_running = false;
					ShowError("Setup is damaged", "This setup file doesn't contain the ContextShell package. Download it again from GitHub.", 1620);
					return;
				}
			}

			_engine = new MsiEngine { LogPath = _session.LogPath };
			_engine.Progress += (value, status) => Dispatcher.BeginInvoke(new Action(() =>
			{
				if(value > 0)
				{
					Bar.IsIndeterminate = false;
					Bar.Value = value;
					ProgressPercent.Text = $"{Math.Round(value * 100)}%";
				}
				if(!string.IsNullOrEmpty(status) && !_engine.CancelRequested)
					ProgressStatus.Text = status;
				SecondaryButton.IsEnabled = _engine.CanCancel && !_engine.CancelRequested;
			}));
			_engine.Prompt = prompt => Dispatcher.Invoke(() => ShowPrompt(prompt)).Result;

			var properties = _session.BuildProperties();
			int rc = await Task.Run(() =>
			{
				SetupSession.CloseRunningApp();
				int result = _engine.Run(op, package, _session.InstalledProductCode, properties);
				if(op == MsiOperation.Repair && (result == 0 || result == MsiEngine.ERROR_SUCCESS_REBOOT_REQUIRED))
					_session.Reregister();
				return result;
			});

			App.Cleanup(package);
			_running = false;
			ExitCode = rc;
			OnFinished(rc);
		}

		private void OnFinished(int rc)
		{
			if(rc == MsiEngine.ERROR_SUCCESS || rc == MsiEngine.ERROR_SUCCESS_REBOOT_REQUIRED || rc == MsiEngine.ERROR_SUCCESS_REBOOT_INITIATED)
			{
				_rebootRequired = rc == MsiEngine.ERROR_SUCCESS_REBOOT_REQUIRED && !_session.NoRestart;
				FinishBadge.Background = (System.Windows.Media.Brush)FindResource("AccentBrush");
				FinishGlyph.Text = "\uE73E";
				LaunchBox.Visibility = Visibility.Visible;
				switch(_operation)
				{
					case MsiOperation.Install:
						FinishTitle.Text = "ContextShell is installed";
						FinishBody.Text = "Right-click any file, folder or the desktop to see your new menu. Use ContextShell Settings to pick a theme and choose what the menu shows.";
						break;
					case MsiOperation.Upgrade:
						FinishTitle.Text = "ContextShell is up to date";
						FinishBody.Text = $"Version {_session.PackageVersion.ToString(3)} is installed and your configuration was kept.";
						break;
					case MsiOperation.Repair:
						FinishTitle.Text = "Repair complete";
						FinishBody.Text = "ContextShell's files were restored and it was added to File Explorer again.";
						break;
					case MsiOperation.Uninstall:
						FinishTitle.Text = "ContextShell was removed";
						FinishBody.Text = "Your right-click menu is back to the Windows default. Thanks for trying ContextShell.";
						LaunchBox.Visibility = Visibility.Collapsed;
						break;
				}
				if(_rebootRequired)
					FinishBody.Text += " Restart your PC to finish.";
				if(_session.Passive || _session.UpdateMode && !_rebootRequired && _operation != MsiOperation.Uninstall)
				{
					// Unattended runs close by themselves; updates reopen Settings where the user started.
					if(_session.UpdateMode)
						_session.LaunchSettings();
					Close();
					return;
				}
				ShowPage(Page.Finish);
			}
			else if(rc == MsiEngine.ERROR_INSTALL_USEREXIT)
			{
				ShowError("Setup was cancelled", "No changes were made to your PC.", rc, info: true);
			}
			else
			{
				ShowError("Setup couldn't finish", MsiEngine.Describe(rc), rc);
			}
		}

		private void ShowError(string title, string body, int code, bool info = false)
		{
			ExitCode = code;
			ErrorTitle.Text = title;
			ErrorBody.Text = body;
			ErrorCode.Text = info ? "" : $"Error code {code}";
			ErrorCode.Visibility = info ? Visibility.Collapsed : Visibility.Visible;
			LogLink.Visibility = !info && System.IO.File.Exists(_session.LogPath) ? Visibility.Visible : Visibility.Collapsed;
			ErrorGlyph.Text = info ? "\uE946" : "\uE783";
			ErrorBadge.Background = (System.Windows.Media.Brush)FindResource(info ? "AccentSubtleBrush" : "CriticalSubtleBrush");
			ErrorGlyph.Foreground = (System.Windows.Media.Brush)FindResource(info ? "AccentForegroundBrush" : "CriticalBrush");
			ShowPage(Page.Error);
		}

		private static void Restart()
		{
			try
			{
				Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
			}
			catch
			{
			}
		}

		// Cancel and close

		private async void ConfirmCancel()
		{
			if(_engine == null || !_engine.CanCancel || _engine.CancelRequested)
				return;
			var answer = await ShowDialog("Cancel setup?",
				"Setup will stop and undo the changes it has made so far.",
				("Cancel setup", MsiEngine.IDYES, true), ("Keep going", MsiEngine.IDNO, false));
			if(answer == MsiEngine.IDYES && _running)
			{
				_engine.CancelRequested = true;
				ProgressStatus.Text = "Cancelling and undoing changes…";
				SecondaryButton.IsEnabled = false;
			}
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			if(_running)
			{
				e.Cancel = true;
				ConfirmCancel();
				return;
			}
			base.OnClosing(e);
		}

		// In-window dialog

		private Task<int> ShowPrompt(MsiPrompt prompt)
		{
			var title = prompt.IsError ? "Setup needs your attention" : "ContextShell Setup";
			switch(prompt.Buttons)
			{
				case 1: return ShowDialog(title, prompt.Text, ("OK", MsiEngine.IDOK, true), ("Cancel", MsiEngine.IDCANCEL, false));
				case 2: return ShowDialog(title, prompt.Text, ("Retry", MsiEngine.IDRETRY, true), ("Ignore", MsiEngine.IDIGNORE, false), ("Abort", MsiEngine.IDABORT, false));
				case 3: return ShowDialog(title, prompt.Text, ("Yes", MsiEngine.IDYES, true), ("No", MsiEngine.IDNO, false), ("Cancel", MsiEngine.IDCANCEL, false));
				case 4: return ShowDialog(title, prompt.Text, ("Yes", MsiEngine.IDYES, true), ("No", MsiEngine.IDNO, false));
				case 5: return ShowDialog(title, prompt.Text, ("Retry", MsiEngine.IDRETRY, true), ("Cancel", MsiEngine.IDCANCEL, false));
				default: return ShowDialog(title, prompt.Text, ("OK", MsiEngine.IDOK, true));
			}
		}

		internal Task<int> ShowDialog(string title, string text, params (string Label, int Result, bool Accent)[] buttons)
		{
			_dialog?.TrySetResult(MsiEngine.IDCANCEL);
			var tcs = new TaskCompletionSource<int>();
			_dialog = tcs;

			DialogTitle.Text = title;
			DialogText.Text = text;
			DialogButtons.Children.Clear();
			for(int i = 0; i < buttons.Length; i++)
			{
				var b = buttons[i];
				var button = new Button
				{
					Content = b.Label,
					Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
					MinWidth = 96,
					IsDefault = b.Accent,
				};
				if(b.Accent)
					button.Style = (Style)FindResource("AccentButton");
				button.Click += (s, e) =>
				{
					DialogLayer.Visibility = Visibility.Collapsed;
					tcs.TrySetResult(b.Result);
				};
				DialogButtons.Children.Add(button);
			}
			DialogLayer.Visibility = Visibility.Visible;
			(DialogButtons.Children[0] as Button)?.Focus();
			return tcs.Task;
		}

		// Screenshot support (see Screenshots.cs)

		internal void PreviewPage(string name)
		{
			switch(name)
			{
				case "welcome": ShowPage(Page.Welcome); break;
				case "options": ShowPage(Page.Options); break;
				case "progress":
					ProgressTitle.Text = "Installing ContextShell…";
					ProgressStatus.Text = "Copying files…";
					Bar.IsIndeterminate = false;
					Bar.Value = 0.62;
					ProgressPercent.Text = "62%";
					ShowPage(Page.Progress);
					break;
				case "cancel":
					PreviewPage("progress");
					_ = ShowDialog("Cancel setup?", "Setup will stop and undo the changes it has made so far.",
						("Cancel setup", 1, true), ("Keep going", 2, false));
					break;
				case "finish":
					_operation = MsiOperation.Install;
					OnFinishedPreview();
					break;
				case "maintenance":
					MaintenanceTitle.Text = $"ContextShell {_session.PackageVersion.ToString(3)} is installed";
					ShowPage(Page.Maintenance);
					break;
				case "error":
					ShowError("Setup couldn't finish", MsiEngine.Describe(1603), 1603);
					LogLink.Visibility = Visibility.Visible;
					break;
			}
		}

		private void OnFinishedPreview()
		{
			FinishTitle.Text = "ContextShell is installed";
			FinishBody.Text = "Right-click any file, folder or the desktop to see your new menu. Use ContextShell Settings to pick a theme and choose what the menu shows.";
			ShowPage(Page.Finish);
		}
	}
}
