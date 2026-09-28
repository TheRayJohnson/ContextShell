using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ContextShell.Settings
{
	public sealed class MenuSection
	{
		public string File;   // imports/<File>.nss
		public string Title;
		public string Description;
		public string Glyph;
	}

	/// <summary>
	/// Small, line-level edits to shell.nss and imports/theme.nss. Everything the user wrote by hand
	/// is left alone: sections are switched off by commenting their import line, not deleting it.
	/// </summary>
	public sealed class ShellConfig
	{
		private const string OffMarker = "// [off] ";

		public static readonly MenuSection[] Sections =
		{
			new MenuSection { File = "modify", Title = "Tidy up Windows items", Glyph = "",
				Description = "Groups Pin/Unpin and rarely used items into submenus and hides a few you rarely need." },
			new MenuSection { File = "file-manage", Title = "File manage", Glyph = "",
				Description = "Copy path, change extension, take ownership, register DLLs and more." },
			new MenuSection { File = "terminal", Title = "Terminal", Glyph = "",
				Description = "Open Command Prompt, PowerShell or Windows Terminal here, as user or admin." },
			new MenuSection { File = "develop", Title = "Develop", Glyph = "",
				Description = "Open the folder or file in your code editor, and developer tools." },
			new MenuSection { File = "goto", Title = "Go To", Glyph = "",
				Description = "Jump to common folders and Windows settings pages." },
			new MenuSection { File = "taskbar", Title = "Taskbar menu", Glyph = "",
				Description = "Adds apps, window tools and ContextShell shortcuts to the taskbar right-click menu." },
		};

		public static readonly (int Value, string Label)[] Delays =
		{
			(0, "Instant"), (100, "Fast"), (200, "Normal"), (400, "Relaxed"),
		};

		private readonly ConfigStore _store;

		public ShellConfig(ConfigStore store)
		{
			_store = store;
		}

		private string Shell => _store.Read(_store.ShellNss) ?? "";

		private static Regex ImportLine(string file) =>
			new Regex(@"^(?<indent>[ \t]*)(?<off>//\s*(\[off\]\s*)?)?import\s+['""]imports/" + Regex.Escape(file) + @"\.nss['""][^\r\n]*",
				RegexOptions.Multiline | RegexOptions.IgnoreCase);

		public bool HasSection(string file) => ImportLine(file).IsMatch(Shell);

		public bool IsSectionOn(string file)
		{
			var m = ImportLine(file).Match(Shell);
			return m.Success && !m.Groups["off"].Success;
		}

		public void SetSection(string file, bool on)
		{
			var text = Shell;
			var re = ImportLine(file);
			var m = re.Match(text);
			if(!m.Success)
			{
				if(on)
					text = text.TrimEnd() + Environment.NewLine + $"import 'imports/{file}.nss'" + Environment.NewLine;
			}
			else
			{
				var line = m.Value.Substring(m.Groups["indent"].Length + (m.Groups["off"].Success ? m.Groups["off"].Length : 0));
				var replacement = m.Groups["indent"].Value + (on ? "" : OffMarker) + line;
				text = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);
			}
			_store.Stage(_store.ShellNss, text);
		}

		private static readonly Regex SettingLine = new Regex(@"(?<key>\b(showdelay|tip\.enabled))\s*=\s*(?<value>[^\s\r\n]+)", RegexOptions.IgnoreCase);

		private string GetSetting(string key)
		{
			foreach(Match m in SettingLine.Matches(Shell))
				if(m.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
					return m.Groups["value"].Value;
			return null;
		}

		private void SetSetting(string key, string value)
		{
			var text = Shell;
			bool found = false;
			text = SettingLine.Replace(text, m =>
			{
				if(!m.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
					return m.Value;
				found = true;
				return m.Groups["key"].Value + " = " + value;
			});
			if(!found)
			{
				var settings = new Regex(@"settings\s*\{", RegexOptions.IgnoreCase).Match(text);
				text = settings.Success
					? text.Insert(settings.Index + settings.Length, Environment.NewLine + "\t" + key + " = " + value)
					: "settings" + Environment.NewLine + "{" + Environment.NewLine + "\t" + key + " = " + value + Environment.NewLine + "}" + Environment.NewLine + text;
			}
			_store.Stage(_store.ShellNss, text);
		}

		public int ShowDelay
		{
			get => int.TryParse(GetSetting("showdelay"), out var v) ? v : 200;
			set => SetSetting("showdelay", value.ToString());
		}

		public bool Tooltips
		{
			get => !string.Equals(GetSetting("tip.enabled"), "false", StringComparison.OrdinalIgnoreCase) && GetSetting("tip.enabled") != "0";
			set => SetSetting("tip.enabled", value ? "true" : "false");
		}

		// Active theme (imports/theme.nss imports one file from imports/themes)

		private static readonly Regex ThemeImport = new Regex(@"^\s*import\s+['""]themes/(?<name>[^'""]+)\.nss['""]", RegexOptions.Multiline | RegexOptions.IgnoreCase);

		/// <summary>File name (without .nss) of the active preset, or null when theme.nss holds its own theme block.</summary>
		public string ActiveTheme
		{
			get
			{
				var text = _store.Read(_store.ThemeNss) ?? "";
				var m = ThemeImport.Match(text);
				return m.Success ? m.Groups["name"].Value : null;
			}
		}

		public void SetActiveTheme(string name)
		{
			var text =
				"// Active theme, set by ContextShell Settings. Pick another in Settings > Appearance," + Environment.NewLine +
				"// or change the import below to any file in imports\\themes." + Environment.NewLine +
				$"import 'themes/{name}.nss'" + Environment.NewLine;
			_store.Stage(_store.ThemeNss, text);
		}

		/// <summary>
		/// Installs from before theme presets have their theme inline in theme.nss. Move it into
		/// themes/my-theme.nss so it shows up in the gallery and nothing is lost.
		/// </summary>
		public string MigrateInlineTheme()
		{
			var text = _store.Read(_store.ThemeNss);
			if(text == null || ActiveTheme != null || !Regex.IsMatch(text, @"\btheme\s*\{", RegexOptions.IgnoreCase))
				return null;
			var path = Path.Combine(_store.ThemesFolder, "my-theme.nss");
			if(!_store.Exists(path))
				_store.Stage(path, "// Your theme from before ContextShell Settings." + Environment.NewLine + text);
			SetActiveTheme("my-theme");
			return "my-theme";
		}
	}
}
