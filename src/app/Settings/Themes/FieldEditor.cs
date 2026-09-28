using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ContextShell.Settings.Themes
{
	/// <summary>
	/// Builds one settings row per theme option: label, the right control for its kind, and a
	/// reset button. Values that don't fit a control (expressions) fall back to a text box, so
	/// nothing in a hand-written theme is lost or silently changed.
	/// </summary>
	public sealed class FieldEditor
	{
		private readonly NssTheme _theme;
		private bool _building;

		public event Action Changed;

		public FieldEditor(NssTheme theme)
		{
			_theme = theme;
		}

		public UIElement BuildGroup(ThemeGroup group)
		{
			var panel = new StackPanel();
			foreach(var field in group.Fields)
				panel.Children.Add(BuildRow(field));
			return panel;
		}

		private static Style S(string key) => (Style)Application.Current.FindResource(key);

		private UIElement BuildRow(ThemeField field)
		{
			var row = new Grid { Margin = new Thickness(0, 6, 0, 6), MinHeight = 32 };
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

			var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
			labels.Children.Add(new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap });
			if(!string.IsNullOrEmpty(field.Help))
				labels.Children.Add(new TextBlock { Text = field.Help, Style = S("CaptionText") });
			row.Children.Add(labels);

			var host = new ContentControl { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Focusable = false };
			Grid.SetColumn(host, 1);
			row.Children.Add(host);

			var reset = new Button
			{
				Style = S("SubtleButton"),
				Content = new TextBlock { Text = "", Style = S("IconText"), FontSize = 14 },
				ToolTip = "Reset to default",
				Width = 32,
				Height = 32,
				Padding = new Thickness(0),
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			AutomationPropertiesHelper.SetName(reset, "Reset " + field.Label);
			Grid.SetColumn(reset, 2);
			row.Children.Add(reset);

			void Rebuild()
			{
				_building = true;
				host.Content = BuildControl(field, v =>
				{
					if(_building)
						return;
					_theme.Set(field.Path, v);
					reset.Visibility = _theme.Get(field.Path) == null ? Visibility.Hidden : Visibility.Visible;
					Changed?.Invoke();
				}, Rebuild);
				reset.Visibility = _theme.Get(field.Path) == null ? Visibility.Hidden : Visibility.Visible;
				_building = false;
			}

			reset.Click += (s, e) =>
			{
				_theme.Set(field.Path, null);
				Rebuild();
				Changed?.Invoke();
			};
			Rebuild();
			return row;
		}

		private UIElement BuildControl(ThemeField field, Action<string> set, Action rebuild)
		{
			var raw = _theme.Get(field.Path);
			switch(field.Kind)
			{
				case FieldKind.Choice: return ChoiceBox(field.Choices, raw, set);
				case FieldKind.Toggle: return ChoiceBox(new[] { ("true", "On"), ("false", "Off") }, raw, set);
				case FieldKind.Number: return NumberBox(field, raw, set);
				case FieldKind.Color: return ColorWithDark(raw, set, rebuild);
				case FieldKind.Font: return FontBox(raw, set);
				case FieldKind.Text: return TextField(raw, set, 200);
				case FieldKind.Spacing:
				case FieldKind.Numbers: return NumberArray(field, raw, set);
				case FieldKind.ColorList: return ColorList(field, raw, set);
				case FieldKind.Stops: return StopsEditor(raw, set);
			}
			return TextField(raw, set, 200);
		}

		// Choice / toggle

		private UIElement ChoiceBox((string Value, string Label)[] choices, string raw, Action<string> set)
		{
			var box = new ComboBox { MinWidth = 200 };
			box.Items.Add(new ComboBoxItem { Content = "Default", Tag = null });
			int selected = 0;
			var normalized = raw?.Trim().Trim('"');
			for(int i = 0; i < choices.Length; i++)
			{
				box.Items.Add(new ComboBoxItem { Content = choices[i].Label, Tag = choices[i].Value });
				if(normalized != null && string.Equals(normalized, choices[i].Value.Trim('"'), StringComparison.OrdinalIgnoreCase))
					selected = i + 1;
				// "view.compact" and "compact" are the same.
				if(normalized != null && choices[i].Value.StartsWith("view.") && string.Equals("view." + normalized, choices[i].Value, StringComparison.OrdinalIgnoreCase))
					selected = i + 1;
			}
			if(normalized != null && selected == 0)
			{
				box.Items.Add(new ComboBoxItem { Content = "Custom: " + raw, Tag = raw });
				selected = box.Items.Count - 1;
			}
			box.SelectedIndex = selected;
			box.SelectionChanged += (s, e) => set((box.SelectedItem as ComboBoxItem)?.Tag as string);
			return box;
		}

		// Number

		private UIElement NumberBox(ThemeField field, string raw, Action<string> set)
		{
			bool plain = raw == null || NssTheme.TryParseNumber(raw, out _);
			if(!plain)
				return ExpressionBox(raw, set);

			var panel = new StackPanel { Orientation = Orientation.Horizontal };
			var slider = new Slider
			{
				Minimum = field.Min,
				Maximum = field.Max,
				Width = 160,
				SmallChange = field.Step,
				LargeChange = Math.Max(field.Step, (field.Max - field.Min) / 10),
				IsSnapToTickEnabled = true,
				TickFrequency = field.Step,
				VerticalAlignment = VerticalAlignment.Center,
			};
			var text = new TextBox { Width = 64, Margin = new Thickness(12, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Right };
			if(NssTheme.TryParseNumber(raw, out var v))
			{
				slider.Value = v;
				text.Text = raw.Trim();
			}
			else
			{
				slider.Value = field.Min;
				text.Text = "";
				text.ToolTip = field.AllowsAuto ? "Default. Type auto or a number." : "Default";
			}

			bool sync = false;
			slider.ValueChanged += (s, e) =>
			{
				if(sync) return;
				sync = true;
				text.Text = slider.Value.ToString(CultureInfo.InvariantCulture);
				sync = false;
				set(text.Text);
			};
			void CommitText()
			{
				if(sync) return;
				var t = text.Text.Trim();
				if(t.Length == 0)
				{
					set(null);
					return;
				}
				if(NssTheme.TryParseNumber(t, out var n))
				{
					sync = true;
					slider.Value = Math.Max(field.Min, Math.Min(field.Max, n));
					sync = false;
				}
				set(t);
			}
			text.LostKeyboardFocus += (s, e) => CommitText();
			text.KeyDown += (s, e) => { if(e.Key == Key.Enter) CommitText(); };
			panel.Children.Add(slider);
			panel.Children.Add(text);
			return panel;
		}

		// Color, optionally different in dark mode

		private UIElement ColorWithDark(string raw, Action<string> set, Action rebuild)
		{
			string light = raw, dark = null;
			bool split = NssTheme.TrySplitDarkLight(raw, out var d, out var l);
			if(split)
			{
				light = l;
				dark = d;
			}

			var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
			var lightField = new ColorField { Value = light };
			var darkField = new ColorField("Same as light") { Value = dark };
			var darkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
			darkRow.Children.Add(new TextBlock { Text = "Dark mode", Style = S("CaptionText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
			darkRow.Children.Add(darkField);
			var toggle = new CheckBox { Content = "Different color in dark mode", FontSize = 12, MinHeight = 24, IsChecked = split, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
			darkRow.Visibility = split ? Visibility.Visible : Visibility.Collapsed;

			void Push()
			{
				var lv = lightField.Value;
				var dv = darkField.Value;
				if(toggle.IsChecked == true && lv != null && dv != null)
					set(NssTheme.JoinDarkLight(dv, lv));
				else
					set(lv);
			}
			lightField.ValueChanged += v => Push();
			darkField.ValueChanged += v => Push();
			toggle.Checked += (s, e) =>
			{
				darkRow.Visibility = Visibility.Visible;
				if(darkField.Value == null)
					darkField.Value = lightField.Value;
				Push();
			};
			toggle.Unchecked += (s, e) =>
			{
				darkRow.Visibility = Visibility.Collapsed;
				Push();
			};

			panel.Children.Add(lightField);
			panel.Children.Add(toggle);
			panel.Children.Add(darkRow);
			return panel;
		}

		// Font

		private static List<string> _fonts;

		private UIElement FontBox(string raw, Action<string> set)
		{
			if(_fonts == null)
				_fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
			var name = raw?.Trim().Trim('"', '\'');
			var box = new ComboBox { MinWidth = 220 };
			box.Items.Add(new ComboBoxItem { Content = "Default", Tag = null });
			int selected = 0;
			foreach(var f in _fonts)
			{
				var item = new ComboBoxItem { Content = f, Tag = f, FontFamily = new FontFamily(f) };
				box.Items.Add(item);
				if(name != null && f.Equals(name, StringComparison.OrdinalIgnoreCase))
					selected = box.Items.Count - 1;
			}
			if(name != null && selected == 0)
			{
				box.Items.Add(new ComboBoxItem { Content = name + " (not installed)", Tag = name });
				selected = box.Items.Count - 1;
			}
			box.SelectedIndex = selected;
			box.SelectionChanged += (s, e) =>
			{
				var tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;
				set(tag == null ? null : "\"" + tag + "\"");
			};
			return box;
		}

		// Arrays

		private UIElement NumberArray(ThemeField field, string raw, Action<string> set)
		{
			var parts = NssTheme.SplitArray(raw);
			bool single = raw != null && parts.Count == 0 && NssTheme.TryParseNumber(raw, out _);
			if(raw != null && parts.Count == 0 && !single)
				return ExpressionBox(raw, set);

			var values = new string[field.Parts.Length];
			if(single)
				for(int i = 0; i < values.Length; i++) values[i] = raw.Trim();
			else if(field.Kind == FieldKind.Spacing && parts.Count == 2)
			{
				// [horizontal, vertical]
				values[0] = values[1] = parts[0];
				values[2] = values[3] = parts[1];
			}
			else
				for(int i = 0; i < values.Length && i < parts.Count; i++) values[i] = parts[i];

			var panel = new StackPanel { Orientation = Orientation.Horizontal };
			var boxes = new List<TextBox>();
			for(int i = 0; i < field.Parts.Length; i++)
			{
				var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0) };
				var box = new TextBox { Width = 52, Text = values[i] ?? "", HorizontalContentAlignment = HorizontalAlignment.Center };
				cell.Children.Add(box);
				cell.Children.Add(new TextBlock { Text = field.Parts[i], Style = S("CaptionText"), HorizontalAlignment = HorizontalAlignment.Center });
				boxes.Add(box);
				panel.Children.Add(cell);
			}
			void Commit()
			{
				var texts = boxes.Select(b => b.Text.Trim()).ToList();
				if(texts.All(t => t.Length == 0))
				{
					set(null);
					return;
				}
				set("[" + string.Join(", ", texts.Select(t => t.Length == 0 ? "0" : t)) + "]");
			}
			foreach(var b in boxes)
			{
				b.LostKeyboardFocus += (s, e) => Commit();
				b.KeyDown += (s, e) => { if(e.Key == Key.Enter) Commit(); };
			}
			return panel;
		}

		private UIElement ColorList(ThemeField field, string raw, Action<string> set)
		{
			var parts = NssTheme.SplitArray(raw);
			if(raw != null && parts.Count == 0)
				parts = new List<string> { raw };
			var panel = new StackPanel();
			var fields = new List<ColorField>();
			for(int i = 0; i < field.Parts.Length; i++)
			{
				var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0) };
				line.Children.Add(new TextBlock { Text = field.Parts[i], Style = S("CaptionText"), Width = 72, VerticalAlignment = VerticalAlignment.Center });
				var cf = new ColorField { Value = i < parts.Count ? parts[i] : null };
				fields.Add(cf);
				line.Children.Add(cf);
				panel.Children.Add(line);
				cf.ValueChanged += v =>
				{
					var vals = fields.Select(f => f.Value).ToList();
					while(vals.Count > 0 && vals[vals.Count - 1] == null) vals.RemoveAt(vals.Count - 1);
					set(vals.Count == 0 ? null : "[" + string.Join(", ", vals.Select(x => x ?? "default")) + "]");
				};
			}
			return panel;
		}

		private UIElement StopsEditor(string raw, Action<string> set)
		{
			var stops = new List<(string Offset, string Color, string Opacity)>();
			var items = NssTheme.SplitArray(raw);
			// Either [[o, c, a], [o, c, a]] or a single [o, c, a].
			if(items.Count > 0 && items[0].StartsWith("["))
			{
				foreach(var it in items)
				{
					var p = NssTheme.SplitArray(it);
					stops.Add((p.ElementAtOrDefault(0), p.ElementAtOrDefault(1), p.ElementAtOrDefault(2)));
				}
			}
			else if(items.Count >= 2)
				stops.Add((items.ElementAtOrDefault(0), items.ElementAtOrDefault(1), items.ElementAtOrDefault(2)));
			else if(raw != null)
				return ExpressionBox(raw, set);

			var panel = new StackPanel { MinWidth = 320 };
			var rows = new StackPanel();
			var entries = new List<(TextBox Offset, ColorField Color, TextBox Opacity)>();

			void Commit()
			{
				var parts = entries
					.Where(en => en.Offset.Text.Trim().Length > 0 && en.Color.Value != null)
					.Select(en => $"[{en.Offset.Text.Trim()}, {en.Color.Value}, {(en.Opacity.Text.Trim().Length == 0 ? "100" : en.Opacity.Text.Trim())}]")
					.ToList();
				set(parts.Count == 0 ? null : "[" + string.Join(", ", parts) + "]");
			}

			void AddRow((string Offset, string Color, string Opacity) stop)
			{
				var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
				var off = new TextBox { Width = 56, Text = stop.Offset ?? "", ToolTip = "Offset, 0 to 1" };
				var col = new ColorField { Value = stop.Color, Margin = new Thickness(6, 0, 0, 0) };
				var op = new TextBox { Width = 56, Text = stop.Opacity ?? "", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Opacity, 0 to 100" };
				var remove = new Button
				{
					Style = S("SubtleButton"),
					Content = new TextBlock { Text = "", Style = S("IconText"), FontSize = 12 },
					ToolTip = "Remove stop",
					Width = 32,
					Height = 32,
					Padding = new Thickness(0),
					Margin = new Thickness(4, 0, 0, 0),
				};
				var entry = (off, col, op);
				entries.Add(entry);
				off.LostKeyboardFocus += (s, e) => Commit();
				op.LostKeyboardFocus += (s, e) => Commit();
				col.ValueChanged += v => Commit();
				remove.Click += (s, e) =>
				{
					entries.Remove(entry);
					rows.Children.Remove(line);
					Commit();
				};
				line.Children.Add(off);
				line.Children.Add(col);
				line.Children.Add(op);
				line.Children.Add(remove);
				rows.Children.Add(line);
			}

			foreach(var s in stops)
				AddRow(s);

			var header = new TextBlock { Text = "Offset     Color                              Opacity", Style = S("CaptionText"), Margin = new Thickness(0, 0, 0, 4) };
			var add = new Button { Content = "Add stop", HorizontalAlignment = HorizontalAlignment.Left };
			add.Click += (s, e) =>
			{
				AddRow((entries.Count == 0 ? "0" : "1", "#ffffff", "20"));
				Commit();
			};
			panel.Children.Add(header);
			panel.Children.Add(rows);
			panel.Children.Add(add);
			return panel;
		}

		// Text / expression fallback

		private UIElement TextField(string raw, Action<string> set, double width)
		{
			var box = new TextBox { Width = width, Text = raw ?? "" };
			box.LostKeyboardFocus += (s, e) => set(box.Text.Trim().Length == 0 ? null : box.Text.Trim());
			box.KeyDown += (s, e) => { if(e.Key == Key.Enter) set(box.Text.Trim().Length == 0 ? null : box.Text.Trim()); };
			return box;
		}

		private UIElement ExpressionBox(string raw, Action<string> set)
		{
			var panel = new StackPanel();
			panel.Children.Add(TextField(raw, set, 260));
			panel.Children.Add(new TextBlock
			{
				Text = "Expression. Edit it as text, or reset to use the controls.",
				Style = S("CaptionText"),
				HorizontalAlignment = HorizontalAlignment.Right,
			});
			return panel;
		}
	}

	internal static class AutomationPropertiesHelper
	{
		public static void SetName(DependencyObject o, string name) => System.Windows.Automation.AutomationProperties.SetName(o, name);
	}
}
