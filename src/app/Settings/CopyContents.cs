using ContextShell.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace ContextShell.Settings
{
	/// <summary>
	/// ContextShell.exe --copy-contents &lt;file&gt; [&lt;file&gt; ...]. Run by the "Copy contents" menu items.
	/// Copies the text of one file to the clipboard, or of several files with a header line naming each one.
	/// Runs in its own process so a large file never makes Explorer wait.
	/// </summary>
	public static class CopyContents
	{
		/// <summary>Largest total size it copies. Bigger pastes are slow or fail in most apps.</summary>
		public const long MaxTotalBytes = 10 * 1024 * 1024;

		// Bytes inspected for a NUL character when deciding whether a file is text.
		private const int BinaryProbeBytes = 8000;

		public static int Run(IEnumerable<string> paths)
		{
			var files = new List<string>();
			var skipped = new List<string>();
			long total = 0;

			foreach(var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				if(Directory.Exists(path))
				{
					skipped.Add($"{DisplayName(path)} (folder)");
					continue;
				}
				FileInfo info;
				try
				{
					info = new FileInfo(path);
					if(!info.Exists)
					{
						skipped.Add($"{DisplayName(path)} (not found)");
						continue;
					}
				}
				catch(Exception)
				{
					skipped.Add($"{DisplayName(path)} (can't be opened)");
					continue;
				}
				files.Add(info.FullName);
				total += info.Length;
			}

			if(total > MaxTotalBytes)
			{
				Tell("Too much to copy",
					$"The selected files add up to {FormatSize(total)}. Copy contents works with up to {FormatSize(MaxTotalBytes)} at a time.");
				return 1;
			}

			var texts = new List<KeyValuePair<string, string>>();
			foreach(var file in files)
			{
				try
				{
					var text = ReadText(file);
					if(text == null)
						skipped.Add($"{DisplayName(file)} (not a text file)");
					else
						texts.Add(new KeyValuePair<string, string>(file, text));
				}
				catch(Exception)
				{
					skipped.Add($"{DisplayName(file)} (can't be read)");
				}
			}

			if(texts.Count == 0)
			{
				Tell("Nothing to copy", skipped.Count > 0
					? "None of the selected items could be copied as text:\n" + List(skipped)
					: "No files were selected.");
				return 1;
			}

			var clip = Combine(texts);
			if(clip.Length == 0)
			{
				Tell("Nothing to copy", texts.Count == 1 ? "The file is empty." : "The files are empty.");
				return 1;
			}

			if(!SetClipboard(clip))
			{
				Tell("Couldn't copy", "Another app is using the clipboard. Try again in a moment.");
				return 1;
			}

			if(skipped.Count > 0)
				Tell($"Copied {texts.Count} {(texts.Count == 1 ? "file" : "files")}", "Skipped:\n" + List(skipped));
			return 0;
		}

		/// <summary>One file: its text as is. Several: each file's text under a header line with its name.</summary>
		internal static string Combine(IList<KeyValuePair<string, string>> texts)
		{
			if(texts.Count == 1)
				return texts[0].Value;

			// Use full paths only when two selected files share a name.
			var names = texts.Select(t => Path.GetFileName(t.Key)).ToList();
			bool clash = names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count;

			var sb = new StringBuilder();
			for(int i = 0; i < texts.Count; i++)
			{
				if(i > 0)
					sb.Append("\r\n");
				sb.Append("========== ").Append(clash ? texts[i].Key : names[i]).Append(" ==========\r\n");
				var text = texts[i].Value;
				sb.Append(text);
				if(text.Length > 0 && !text.EndsWith("\n"))
					sb.Append("\r\n");
			}
			return sb.ToString();
		}

		/// <summary>Reads a file as text, or returns null when it looks binary.</summary>
		internal static string ReadText(string path)
		{
			byte[] bytes;
			using(var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				bytes = new byte[stream.Length];
				int read = 0;
				while(read < bytes.Length)
				{
					int n = stream.Read(bytes, read, bytes.Length - read);
					if(n == 0)
						break;
					read += n;
				}
				if(read < bytes.Length)
					Array.Resize(ref bytes, read);
			}
			return Decode(bytes);
		}

		internal static string Decode(byte[] bytes)
		{
			// A byte order mark says exactly how the text is stored.
			if(StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
				return new UTF32Encoding(false, false).GetString(bytes, 4, bytes.Length - 4);
			if(StartsWith(bytes, 0xEF, 0xBB, 0xBF))
				return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
			if(StartsWith(bytes, 0xFF, 0xFE))
				return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			if(StartsWith(bytes, 0xFE, 0xFF))
				return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

			// Without one, a NUL byte means an image, archive, executable or similar.
			for(int i = 0; i < Math.Min(bytes.Length, BinaryProbeBytes); i++)
			{
				if(bytes[i] == 0)
					return null;
			}

			// Most code and config files are UTF-8. Older ones use the system code page.
			try
			{
				return new UTF8Encoding(false, true).GetString(bytes);
			}
			catch(DecoderFallbackException)
			{
				return Encoding.Default.GetString(bytes);
			}
		}

		private static bool StartsWith(byte[] bytes, params byte[] prefix)
		{
			if(bytes.Length < prefix.Length)
				return false;
			for(int i = 0; i < prefix.Length; i++)
			{
				if(bytes[i] != prefix[i])
					return false;
			}
			return true;
		}

		private static bool SetClipboard(string text)
		{
			// The clipboard can be briefly held by another app, so retry for about a second.
			for(int attempt = 0; attempt < 10; attempt++)
			{
				try
				{
					Clipboard.SetText(text, TextDataFormat.UnicodeText);
					return true;
				}
				catch(ExternalException)
				{
					Thread.Sleep(100);
				}
			}
			return false;
		}

		private static string DisplayName(string path)
		{
			var name = Path.GetFileName(path.TrimEnd('\\', '/'));
			return string.IsNullOrEmpty(name) ? path : name;
		}

		private static string List(List<string> items)
		{
			const int shown = 6;
			var lines = items.Take(shown).Select(s => "  • " + s);
			if(items.Count > shown)
				lines = lines.Concat(new[] { $"  and {items.Count - shown} more" });
			return string.Join("\n", lines);
		}

		private static string FormatSize(long bytes)
		{
			if(bytes >= 1024 * 1024)
				return $"{bytes / (1024.0 * 1024.0):0.#} MB";
			return $"{Math.Max(1, bytes / 1024)} KB";
		}

		private static void Tell(string title, string text) => ConfirmDialog.Ask(null, title, text, "OK", "OK");
	}
}
