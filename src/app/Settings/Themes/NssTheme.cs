using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextShell.Settings.Themes
{
	/// <summary>
	/// A theme as a flat map of dotted option paths to raw .nss values, for example
	/// "background.color" -> "#1c1c1e" or "@if(sys.dark, #1c1c1e, #f5f5f7)".
	/// Reads the theme { } block of a .nss file (nested or dotted keys) and writes it back flat.
	/// </summary>
	public sealed class NssTheme
	{
		public string Name;
		public string Description = "";
		public string FilePath;
		public bool IsPreset;

		/// <summary>Option path -> raw value. Missing means "use the default".</summary>
		public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public string Get(string path) => Values.TryGetValue(path, out var v) ? v : null;

		public void Set(string path, string raw)
		{
			if(string.IsNullOrWhiteSpace(raw))
				Values.Remove(path);
			else
				Values[path] = raw.Trim();
		}

		public NssTheme Clone(string name)
		{
			var t = new NssTheme { Name = name, Description = Description };
			foreach(var kv in Values)
				t.Values[kv.Key] = kv.Value;
			return t;
		}

		// Parsing

		public static NssTheme Load(string path)
		{
			var theme = Parse(File.ReadAllText(path));
			theme.FilePath = path;
			theme.Name = Path.GetFileNameWithoutExtension(path);
			return theme;
		}

		public static NssTheme Parse(string text)
		{
			var theme = new NssTheme();
			var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var lines = new List<string>();

			// Leading // comment lines describe the theme.
			foreach(var line in text.Split('\n'))
			{
				var l = line.Trim();
				if(l.StartsWith("//"))
					lines.Add(l.TrimStart('/').Trim());
				else if(l.Length > 0)
					break;
			}
			theme.Description = string.Join(" ", lines).Trim();

			var p = new Reader(StripComments(text));
			var prefix = new Stack<string>();
			bool inTheme = false;
			int themeDepth = 0;

			while(!p.End)
			{
				p.SkipSpace();
				if(p.End)
					break;
				char c = p.Peek;
				if(c == '}')
				{
					p.Next();
					if(inTheme)
					{
						if(prefix.Count == themeDepth)
							inTheme = false;
						else
							prefix.Pop();
					}
					continue;
				}
				if(c == '$')
				{
					// Variable: $name = value
					p.Next();
					var name = p.ReadIdent();
					p.SkipInline();
					if(p.Peek == '=')
					{
						p.Next();
						vars[name] = p.ReadValue();
					}
					continue;
				}

				var key = p.ReadIdent();
				if(key.Length == 0)
				{
					p.Next(); // skip anything we don't understand
					continue;
				}
				p.SkipSpace();
				if(p.Peek == '{')
				{
					p.Next();
					if(!inTheme && key.Equals("theme", StringComparison.OrdinalIgnoreCase))
					{
						inTheme = true;
						themeDepth = prefix.Count;
					}
					else
						prefix.Push(key);
					continue;
				}
				if(p.Peek == '=')
				{
					p.Next();
					var value = p.ReadValue();
					if(inTheme)
					{
						var full = string.Join(".", prefix.Reverse().Concat(new[] { key }));
						theme.Values[full] = ResolveVariables(value, vars);
					}
					continue;
				}
				// A bare word like "sep" or unknown syntax outside the theme; move on.
			}
			return theme;
		}

		private static string ResolveVariables(string value, Dictionary<string, string> vars)
		{
			if(vars.Count == 0)
				return value;
			var v = value.Trim();
			if(v.StartsWith("$"))
				v = v.Substring(1);
			return vars.TryGetValue(v, out var resolved) ? resolved : value;
		}

		private static string StripComments(string text)
		{
			var sb = new StringBuilder(text.Length);
			bool inString = false;
			char quote = '\0';
			for(int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if(inString)
				{
					sb.Append(c);
					if(c == quote)
						inString = false;
					continue;
				}
				if(c == '"' || c == '\'')
				{
					inString = true;
					quote = c;
					sb.Append(c);
					continue;
				}
				if(c == '/' && i + 1 < text.Length && text[i + 1] == '/')
				{
					while(i < text.Length && text[i] != '\n') i++;
					sb.Append('\n');
					continue;
				}
				if(c == '/' && i + 1 < text.Length && text[i + 1] == '*')
				{
					i += 2;
					while(i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
					i++;
					continue;
				}
				sb.Append(c);
			}
			return sb.ToString();
		}

		private sealed class Reader
		{
			private readonly string _s;
			private int _i;
			public Reader(string s) { _s = s; }
			public bool End => _i >= _s.Length;
			public char Peek => _i < _s.Length ? _s[_i] : '\0';
			public void Next() => _i++;

			public void SkipSpace()
			{
				while(_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ';' || _s[_i] == ','))
					_i++;
			}

			public void SkipInline()
			{
				while(_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t'))
					_i++;
			}

			public string ReadIdent()
			{
				int start = _i;
				while(_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '.' || _s[_i] == '-'))
					_i++;
				return _s.Substring(start, _i - start);
			}

			/// <summary>Read to the end of the line, continuing over lines while brackets or quotes are open.</summary>
			public string ReadValue()
			{
				SkipInline();
				int start = _i, depth = 0;
				char quote = '\0';
				while(_i < _s.Length)
				{
					char c = _s[_i];
					if(quote != '\0')
					{
						if(c == quote) quote = '\0';
					}
					else if(c == '"' || c == '\'') quote = c;
					else if(c == '(' || c == '[') depth++;
					else if(c == ')' || c == ']') depth--;
					else if(depth <= 0 && (c == '\n' || c == '\r' || c == ';' || c == '}'))
						break;
					_i++;
				}
				return Regex.Replace(_s.Substring(start, _i - start).Trim(), @"\s+", " ");
			}
		}

		// Writing

		public string ToNss()
		{
			var sb = new StringBuilder();
			if(!string.IsNullOrWhiteSpace(Description))
				foreach(var line in Wrap(Description, 96))
					sb.Append("// ").AppendLine(line);
			sb.AppendLine("// Made with ContextShell Settings. Options: docs/configuration/themes.html");
			sb.AppendLine("theme");
			sb.AppendLine("{");
			foreach(var kv in Values.OrderBy(kv => ThemeSchema.Order(kv.Key)).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
				sb.Append('\t').Append(kv.Key).Append(" = ").AppendLine(kv.Value);
			sb.AppendLine("}");
			return sb.ToString();
		}

		private static IEnumerable<string> Wrap(string text, int width)
		{
			var line = new StringBuilder();
			foreach(var word in text.Split(' '))
			{
				if(line.Length + word.Length + 1 > width && line.Length > 0)
				{
					yield return line.ToString();
					line.Clear();
				}
				if(line.Length > 0) line.Append(' ');
				line.Append(word);
			}
			if(line.Length > 0)
				yield return line.ToString();
		}

		// Value helpers shared by the editor and the preview

		/// <summary>Split @if(sys.dark, A, B) into its dark and light parts. Returns false for other values.</summary>
		public static bool TrySplitDarkLight(string raw, out string dark, out string light)
		{
			dark = light = null;
			if(raw == null)
				return false;
			var m = Regex.Match(raw.Trim(), @"^@?if\(\s*sys\.dark\s*,\s*(.+?)\s*,\s*(.+?)\s*\)$", RegexOptions.IgnoreCase);
			if(!m.Success || m.Groups[1].Value.Contains(",") || m.Groups[2].Value.Contains(","))
				return false;
			dark = m.Groups[1].Value;
			light = m.Groups[2].Value;
			return true;
		}

		public static string JoinDarkLight(string dark, string light) =>
			string.Equals(dark, light, StringComparison.OrdinalIgnoreCase) ? light : $"@if(sys.dark, {dark}, {light})";

		/// <summary>The value that applies in the given mode (handles @if(sys.dark, a, b)).</summary>
		public static string ForMode(string raw, bool dark) =>
			TrySplitDarkLight(raw, out var d, out var l) ? (dark ? d : l) : raw;

		public static bool TryParseNumber(string raw, out double value) =>
			double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

		public static bool IsHexColor(string raw) =>
			raw != null && Regex.IsMatch(raw.Trim(), "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");

		/// <summary>Parse [a, b, c] into its top-level items.</summary>
		public static List<string> SplitArray(string raw)
		{
			var items = new List<string>();
			if(raw == null)
				return items;
			var s = raw.Trim();
			if(!(s.StartsWith("[") && s.EndsWith("]")))
				return items;
			s = s.Substring(1, s.Length - 2);
			int depth = 0, start = 0;
			for(int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				if(c == '(' || c == '[') depth++;
				else if(c == ')' || c == ']') depth--;
				else if(c == ',' && depth == 0)
				{
					items.Add(s.Substring(start, i - start).Trim());
					start = i + 1;
				}
			}
			if(s.Trim().Length > 0)
				items.Add(s.Substring(start).Trim());
			return items;
		}
	}
}
