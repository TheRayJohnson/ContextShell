using ContextShell.UI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ContextShell.Settings.Pages
{
	public partial class AboutPage : UserControl
	{
		private readonly MainWindow _main;

		public AboutPage(MainWindow main)
		{
			_main = main;
			InitializeComponent();
			VersionText.Text = $"Version {AppInfo.VersionText} · {AppInfo.OSArchitecture}";
			Description.Text = AppInfo.Description;
			FolderText.Text = main.Store.InstallFolder ?? "Not found";
		}

		private void GitHub_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl);
		private void Issues_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl + "/issues");
		private void Releases_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl + "/releases");
		private void Docs_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl("https://nilesoft.org/docs");
		private void License_Click(object sender, RoutedEventArgs e) => AppInfo.OpenUrl(AppInfo.RepositoryUrl + "/blob/main/LICENSE");

		private void Folder_Click(object sender, RoutedEventArgs e)
		{
			if(Directory.Exists(_main.Store.InstallFolder))
				AppInfo.OpenUrl(_main.Store.InstallFolder);
		}

		private void Edit_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _main.Store.ShellNss + "\"") { UseShellExecute = true, Verb = "runas" });
			}
			catch(Win32Exception)
			{
				// Declined: nothing to do.
			}
		}

		private void Classic_Click(object sender, RoutedEventArgs e)
		{
			var exe = Path.Combine(_main.Store.InstallFolder ?? "", "shell.exe");
			if(File.Exists(exe))
				AppInfo.OpenUrl(exe);
		}
	}
}
