using ContextShell.UI;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ContextShell.Settings.Pages
{
	public partial class MenuPage : UserControl, IRefreshable
	{
		private readonly MainWindow _main;
		private bool _loading;

		public MenuPage(MainWindow main)
		{
			_main = main;
			InitializeComponent();
			foreach(var d in ShellConfig.Delays)
				DelayBox.Items.Add(new ComboBoxItem { Content = d.Label, Tag = d.Value });
			Refresh();
		}

		public void Refresh()
		{
			_loading = true;
			var shell = _main.Shell;

			Sections.Children.Clear();
			foreach(var section in ShellConfig.Sections.Where(s => shell.HasSection(s.File)))
			{
				var toggle = new CheckBox { Style = (Style)FindResource("ToggleSwitch"), IsChecked = shell.IsSectionOn(section.File) };
				var text = new StackPanel();
				text.Children.Add(new TextBlock { Text = section.Title });
				text.Children.Add(new TextBlock { Text = section.Description, Style = (Style)FindResource("CaptionText") });
				toggle.Content = text;
				var file = section.File;
				toggle.Checked += (s, e) => { if(!_loading) shell.SetSection(file, true); };
				toggle.Unchecked += (s, e) => { if(!_loading) shell.SetSection(file, false); };

				var grid = new Grid();
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
				grid.ColumnDefinitions.Add(new ColumnDefinition());
				grid.Children.Add(new TextBlock { Text = section.Glyph, Style = (Style)FindResource("IconText") });
				Grid.SetColumn(toggle, 1);
				grid.Children.Add(toggle);
				Sections.Children.Add(new Border { Style = (Style)FindResource("SettingRow"), Child = grid });
			}

			var delay = shell.ShowDelay;
			var best = ShellConfig.Delays.OrderBy(d => Math.Abs(d.Value - delay)).First();
			DelayBox.SelectedIndex = Array.IndexOf(ShellConfig.Delays, best);
			TipsBox.IsChecked = shell.Tooltips;

			bool registered = IsRegistered();
			RegisteredText.Text = registered
				? "On. ContextShell draws the right-click menu."
				: "Off. File Explorer uses the standard Windows menu.";
			RegisterButton.Content = registered ? "Turn off" : "Turn on";
			_loading = false;
		}

		private static bool IsRegistered()
		{
			try
			{
				using(var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				using(var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"))
					return key?.GetValue(AppInfo.ContextMenuClsid) != null;
			}
			catch
			{
				return false;
			}
		}

		private void DelayBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if(!_loading && DelayBox.SelectedItem is ComboBoxItem item)
				_main.Shell.ShowDelay = (int)item.Tag;
		}

		private void Tips_Changed(object sender, RoutedEventArgs e)
		{
			if(!_loading)
				_main.Shell.Tooltips = TipsBox.IsChecked == true;
		}

		private async void Register_Click(object sender, RoutedEventArgs e)
		{
			var args = IsRegistered() ? "-unregister -restart -silent" : "-register -treat -restart -silent";
			await RunShell(args);
		}

		private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
		{
			ActionError.Visibility = Visibility.Collapsed;
			await Task.Run(() =>
			{
				try
				{
					foreach(var p in Process.GetProcessesByName("explorer"))
					{
						p.Kill();
						p.WaitForExit(5000);
					}
				}
				catch
				{
				}
			});
			// Windows usually restarts the shell by itself; make sure it's back.
			await Task.Delay(1500);
			if(Process.GetProcessesByName("explorer").Length == 0)
				Process.Start("explorer.exe");
			_main.ShowToast("File Explorer restarted.");
		}

		private async Task RunShell(string args)
		{
			ActionError.Visibility = Visibility.Collapsed;
			RegisterButton.IsEnabled = false;
			var error = await Task.Run(() => _main.Store.RunShellExe(args));
			RegisterButton.IsEnabled = true;
			if(error != null)
			{
				ActionError.Text = error;
				ActionError.Visibility = Visibility.Visible;
			}
			Refresh();
		}
	}
}
