using ContextShell.UI;
using System.IO;
using System.Windows;

namespace ContextShell.Setup
{
	internal static class Screenshots
	{
		private static readonly string[] Pages = { "welcome", "options", "progress", "cancel", "finish", "maintenance", "error" };

		public static void RenderAll(Application app, string dir)
		{
			foreach(var dark in new[] { false, true })
			{
				ThemeManager.ForceDark = dark;
				ThemeManager.Apply(app);
				foreach(var page in Pages)
				{
					var session = new SetupSession { InstallFolder = SetupSession.DefaultFolder, LogPath = Path.GetTempFileName() };
					var window = new MainWindow(session);
					window.PreviewPage(page);
					Snapshot.Save(window, Path.Combine(dir, $"setup-{page}-{(dark ? "dark" : "light")}.png"), 640, 500);
					window.Close();
				}
			}
		}
	}
}
