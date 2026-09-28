using ContextShell.UI;
using System;
using System.Windows;

namespace ContextShell.Settings
{
	/// <summary>Small popup near the taskbar that announces an update found at sign-in.</summary>
	public partial class UpdateToast : Window
	{
		private readonly ReleaseInfo _release;
		private bool _busy;

		public UpdateToast(ReleaseInfo release)
		{
			_release = release;
			InitializeComponent();
			ThemeManager.Attach(this);
			TitleText.Text = $"ContextShell {release.Version.ToString(3)} is available";
			var summary = UpdateFlow.Summary(release.Notes, 220);
			BodyText.Text = string.IsNullOrEmpty(summary) ? $"You have {AppInfo.VersionText}." : summary;

			Loaded += (s, e) =>
			{
				var area = SystemParameters.WorkArea;
				Left = area.Right - ActualWidth - 16;
				Top = area.Bottom - ActualHeight - 16;
			};
		}

		private async void Update_Click(object sender, RoutedEventArgs e)
		{
			if(_busy)
				return;
			_busy = true;
			UpdateButton.IsEnabled = LaterButton.IsEnabled = false;
			SkipButton.Visibility = Visibility.Collapsed;
			ErrorText.Visibility = Visibility.Collapsed;
			Bar.Visibility = Visibility.Visible;
			Bar.IsIndeterminate = true;
			BodyText.Text = "Downloading and verifying the installer…";

			var error = await UpdateFlow.InstallAsync(_release, new Progress<double>(p =>
			{
				Bar.IsIndeterminate = false;
				Bar.Value = p;
			}));
			if(error == null)
			{
				Close();
				return;
			}
			_busy = false;
			Bar.Visibility = Visibility.Collapsed;
			ErrorText.Text = error;
			ErrorText.Visibility = Visibility.Visible;
			UpdateButton.IsEnabled = LaterButton.IsEnabled = true;
			UpdateButton.Content = "Try again";
		}

		private void Later_Click(object sender, RoutedEventArgs e) => Close();

		private void Skip_Click(object sender, RoutedEventArgs e)
		{
			Updates.SkippedVersion = _release.Version.ToString(3);
			Close();
		}
	}
}
