using ContextShell.UI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ContextShell.Settings
{
	/// <summary>Download a release's setup EXE, verify it, and hand over to it.</summary>
	public static class UpdateFlow
	{
		/// <summary>How often the sign-in check actually contacts GitHub.</summary>
		public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);

		public static bool CheckDue => DateTime.UtcNow - Updates.LastCheck.ToUniversalTime() > CheckInterval;

		public static async Task<ReleaseInfo> CheckAsync(CancellationToken ct = default)
		{
			var release = await Updates.GetLatestAsync(ct);
			Updates.LastCheck = DateTime.UtcNow;
			return release;
		}

		/// <summary>
		/// Downloads and starts the update. Returns null when setup started (the caller should exit
		/// so its files can be replaced) or an error message.
		/// </summary>
		public static async Task<string> InstallAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct = default)
		{
			string path;
			try
			{
				path = await Updates.DownloadAsync(release, progress, ct);
			}
			catch(OperationCanceledException)
			{
				return "Download cancelled.";
			}
			catch(Exception ex)
			{
				return "Couldn't download the update. " + ex.Message;
			}

			try
			{
				// Setup asks for administrator permission itself (its manifest requires it).
				Process.Start(new ProcessStartInfo(path, "/update") { UseShellExecute = true });
				return null;
			}
			catch(Win32Exception ex) when(ex.NativeErrorCode == 1223)
			{
				return "The update needs administrator permission to install.";
			}
			catch(Exception ex)
			{
				return "Couldn't start the installer. " + ex.Message;
			}
		}

		/// <summary>First paragraph or so of release notes, without Markdown clutter.</summary>
		public static string Summary(string notes, int maxLength = 600)
		{
			if(string.IsNullOrWhiteSpace(notes))
				return "";
			var text = notes.Replace("\r", "");
			text = System.Text.RegularExpressions.Regex.Replace(text, @"^#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
			text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
			text = System.Text.RegularExpressions.Regex.Replace(text, @"\[(.+?)\]\(.+?\)", "$1");
			text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");
			text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
			return text.Length > maxLength ? text.Substring(0, maxLength).TrimEnd() + "…" : text;
		}
	}
}
