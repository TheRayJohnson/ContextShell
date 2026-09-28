using ContextShell.Settings.Pages;
using ContextShell.UI;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ContextShell.Settings
{
	public partial class MainWindow : Window
	{
		public ConfigStore Store { get; }
		public ShellConfig Shell { get; }

		private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

		public MainWindow(ConfigStore store, string page = null)
		{
			Store = store;
			Shell = new ShellConfig(store);
			InitializeComponent();
			ThemeManager.Attach(this);
			VersionText.Text = "Version " + AppInfo.VersionText;

			Store.Changed += (s, e) => UpdateSaveBar();
			_toastTimer.Tick += (s, e) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };

			Navigate(page ?? "appearance");
		}

		public void Navigate(string page)
		{
			RadioButton target;
			switch(page)
			{
				case "menu": target = NavMenu; break;
				case "updates": target = NavUpdates; break;
				case "about": target = NavAbout; break;
				default: target = NavAppearance; break;
			}
			if(target.IsChecked == true)
				Nav_Checked(target, null); // already selected: reload the page
			else
				target.IsChecked = true;
		}

		/// <summary>Show a page that isn't in the nav, like the theme editor.</summary>
		public void ShowPage(UserControl page) => PageHost.Content = page;

		public void ShowUpdateAvailable(bool available) => UpdateDot.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

		private void Nav_Checked(object sender, RoutedEventArgs e)
		{
			if(!(sender is RadioButton rb) || PageHost == null)
				return;
			switch(rb.Tag as string)
			{
				case "menu": PageHost.Content = new MenuPage(this); break;
				case "updates": PageHost.Content = new UpdatesPage(this); break;
				case "about": PageHost.Content = new AboutPage(this); break;
				default: PageHost.Content = new AppearancePage(this); break;
			}
		}

		private void UpdateSaveBar()
		{
			SaveBar.Visibility = Store.HasChanges ? Visibility.Visible : Visibility.Collapsed;
			SaveError.Visibility = Visibility.Collapsed;
		}

		private void Save_Click(object sender, RoutedEventArgs e) => SaveChanges();

		public bool SaveChanges()
		{
			var error = Store.Apply();
			if(error != null)
			{
				SaveError.Text = error;
				SaveError.Visibility = Visibility.Visible;
				return false;
			}
			ShowToast("Saved. Right-click to see your changes.");
			(PageHost.Content as IRefreshable)?.Refresh();
			return true;
		}

		private void Discard_Click(object sender, RoutedEventArgs e)
		{
			Store.Discard();
			(PageHost.Content as IRefreshable)?.Refresh();
		}

		public void ShowToast(string text)
		{
			ToastText.Text = text;
			Toast.Visibility = Visibility.Visible;
			_toastTimer.Stop();
			_toastTimer.Start();
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			if(Store.HasChanges)
			{
				var answer = ConfirmDialog.Ask(this, "Save your changes?",
					"You changed settings that haven't been saved yet.", "Save", "Save", "Don't save", "Cancel");
				if(answer == null || answer == "Cancel" || (answer == "Save" && !SaveChanges()))
				{
					e.Cancel = true;
					return;
				}
			}
			base.OnClosing(e);
		}
	}

	public interface IRefreshable
	{
		void Refresh();
	}
}
