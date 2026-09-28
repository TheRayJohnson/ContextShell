using ContextShell.UI;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace ContextShell.Setup
{
	public partial class App : Application
	{
		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int processId);

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			var session = SetupSession.Parse(e.Args);

			if(session.Help)
			{
				// Print to the calling console when there is one, otherwise show a window.
				if(AttachConsole(-1))
					Console.WriteLine(Environment.NewLine + SetupSession.HelpText);
				else
					MessageBox.Show(SetupSession.HelpText, "ContextShell Setup");
				Shutdown(0);
				return;
			}

			if(session.ExtractDir != null)
			{
				var path = MsiEngine.ExtractPackage(session.ExtractDir);
				Shutdown(path != null ? 0 : 1);
				return;
			}

			ThemeManager.Initialize(this);

			if(session.ScreenshotDir != null)
			{
				Screenshots.RenderAll(this, session.ScreenshotDir);
				Shutdown(0);
				return;
			}

			session.Detect();

			if(session.Quiet)
			{
				Shutdown(RunQuiet(session));
				return;
			}

			var window = new MainWindow(session);
			window.Closed += (s, a) => Shutdown(window.ExitCode);
			window.Show();
		}

		/// <summary>No UI at all, like msiexec /qn. Returns the Windows Installer result.</summary>
		private static int RunQuiet(SetupSession session)
		{
			MsiOperation op;
			string target;
			if(session.Uninstall)
			{
				if(!session.IsInstalled)
					return 1605;
				op = MsiOperation.Uninstall;
				target = session.InstalledProductCode;
			}
			else if(session.Repair || session.IsSameVersion)
			{
				if(!session.IsInstalled)
					return 1605;
				op = MsiOperation.Repair;
				target = session.InstalledProductCode;
			}
			else
			{
				if(session.IsNewerInstalled)
					return 1638;
				op = session.IsUpgrade ? MsiOperation.Upgrade : MsiOperation.Install;
				target = MsiEngine.ExtractPackage();
				if(target == null)
					return 1620;
			}

			SetupSession.CloseRunningApp();
			var engine = new MsiEngine { Quiet = true, LogPath = session.LogPath };
			int rc = engine.Run(op, target, session.BuildProperties());
			if(op == MsiOperation.Repair && (rc == 0 || rc == 3010))
				session.Reregister();
			Cleanup(target);
			return rc;
		}

		internal static void Cleanup(string packagePath)
		{
			try
			{
				if(packagePath != null && packagePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && File.Exists(packagePath))
					Directory.Delete(Path.GetDirectoryName(packagePath), true);
			}
			catch
			{
			}
		}
	}
}
