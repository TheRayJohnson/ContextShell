using ContextShell.UI;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ContextShell.Settings.Pages
{
	public partial class UpdatesPage : UserControl
	{
		private readonly MainWindow _main;
		private ReleaseInfo _release;
		private CancellationTokenSource _cts;
		private bool _busy;

		/// <summary>Set by the sign-in check so the page opens straight on the update.</summary>
		public static ReleaseInfo Pending;

		public UpdatesPage(MainWindow main)
		{
			_main = main;
			InitializeComponent();
			AutoBox.IsChecked = Updates.AutoCheckEnabled;
			ShowLastChecked();

			if(Pending != null)
				ShowRelease(Pending);
			else
				SetStatus("", "Check for updates", $"You have ContextShell {AppInfo.VersionText}.", "Check for updates");
		}

		private void ShowLastChecked()
		{
			var last = Updates.LastCheck;
			LastChecked.Text = last == DateTime.MinValue ? "" : "Last checked " + last.ToLocalTime().ToString("g");
		}

		private void SetStatus(string glyph, string title, string text, string action, bool success = false, bool error = false)
		{
			StatusGlyph.Text = glyph;
			StatusTitle.Text = title;
			StatusText.Text = text;
			ActionButton.Content = action;
			ActionButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
			StatusBadge.Background = (Brush)FindResource(error ? "CriticalSubtleBrush" : "AccentSubtleBrush");
			StatusGlyph.Foreground = (Brush)FindResource(error ? "CriticalBrush" : success ? "SuccessBrush" : "AccentForegroundBrush");
		}

		private async void Action_Click(object sender, RoutedEventArgs e)
		{
			if(_busy)
			{
				_cts?.Cancel();
				return;
			}
			if(_release != null && Updates.IsNewer(_release))
				await Install();
			else
				await Check();
		}

		private async System.Threading.Tasks.Task Check()
		{
			_busy = true;
			SetStatus("", "Checking for updates…", "Asking GitHub for the latest release.", null);
			try
			{
				var release = await UpdateFlow.CheckAsync();
				ShowLastChecked();
				if(release == null)
					SetStatus("", "You're up to date", $"ContextShell {AppInfo.VersionText} is the latest version.", "Check again", success: true);
				else
					ShowRelease(release);
			}
			catch(Exception ex)
			{
				SetStatus("", "Couldn't check for updates", "Check your internet connection and try again. " + ex.Message, "Try again", error: true);
			}
			_busy = false;
		}

		private void ShowRelease(ReleaseInfo release)
		{
			_release = release;
			_main.ShowUpdateAvailable(Updates.IsNewer(release));
			NotesCard.Visibility = Visibility.Visible;
			NotesTitle.Text = $"What's in {release.Tag}";
			NotesText.Text = UpdateFlow.Summary(release.Notes);
			if(Updates.IsNewer(release))
			{
				SetStatus("", $"ContextShell {release.Version.ToString(3)} is available",
					$"You have {AppInfo.VersionText}. The update keeps your themes and menu settings.", "Download and install");
				SkipButton.Visibility = Visibility.Visible;
			}
			else
			{
				SetStatus("", "You're up to date", $"ContextShell {AppInfo.VersionText} is the latest version.", "Check again", success: true);
				SkipButton.Visibility = Visibility.Collapsed;
			}
		}

		private async System.Threading.Tasks.Task Install()
		{
			if(_main.Store.HasChanges && !_main.SaveChanges())
				return;
			_busy = true;
			_cts = new CancellationTokenSource();
			SkipButton.Visibility = Visibility.Collapsed;
			DownloadBar.Visibility = Visibility.Visible;
			DownloadBar.IsIndeterminate = true;
			SetStatus("", $"Downloading ContextShell {_release.Version.ToString(3)}…", "The installer is checked against the release's SHA-256 checksum before it runs.", "Cancel");
			ActionButton.Style = (Style)FindResource(typeof(Button));

			var progress = new Progress<double>(p =>
			{
				DownloadBar.IsIndeterminate = false;
				DownloadBar.Value = p;
			});
			var error = await UpdateFlow.InstallAsync(_release, progress, _cts.Token);
			_busy = false;
			DownloadBar.Visibility = Visibility.Collapsed;
			ActionButton.Style = (Style)FindResource("AccentButton");
			if(error == null)
			{
				// Setup takes it from here and reopens Settings when it's done.
				Application.Current.Shutdown();
				return;
			}
			SetStatus("", "The update didn't install", error, "Try again", error: true);
		}

		private void Skip_Click(object sender, RoutedEventArgs e)
		{
			if(_release == null)
				return;
			Updates.SkippedVersion = _release.Version.ToString(3);
			SkipButton.Visibility = Visibility.Collapsed;
			_main.ShowUpdateAvailable(false);
			SetStatus("", "Update skipped", $"You won't be reminded about {_release.Version.ToString(3)}. Newer versions will still show up.", "Check for updates");
			_release = null;
			Pending = null;
		}

		private void Auto_Changed(object sender, RoutedEventArgs e) => Updates.AutoCheckEnabled = AutoBox.IsChecked == true;

		private void Notes_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(_release?.PageUrl ?? AppInfo.RepositoryUrl + "/releases");
	}
}
