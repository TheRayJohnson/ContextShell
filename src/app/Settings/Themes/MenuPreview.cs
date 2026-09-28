using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ContextShell.Settings.Themes
{
	/// <summary>
	/// An approximate, live rendering of a context menu using a theme's values, drawn over a sample
	/// wallpaper so transparency is visible. Blur and acrylic can't be reproduced exactly outside
	/// Explorer, so they are approximated with a frosted overlay.
	/// </summary>
	public sealed class MenuPreview : Border
	{
		private NssTheme _theme;
		private bool _dark;

		public MenuPreview()
		{
			CornerRadius = new CornerRadius(8);
			ClipToBounds = true;
			MinHeight = 340;
		}

		public void Show(NssTheme theme, bool dark)
		{
			_theme = theme;
			_dark = dark;
			Rebuild();
		}

		private string V(string path) => NssTheme.ForMode(_theme?.Get(path), _dark);

		private Color C(string path, Color fallback) => ColorUtil.TryParse(V(path), out var c) ? c : fallback;

		private double N(string path, double fallback) => NssTheme.TryParseNumber(V(path), out var n) ? n : fallback;

		private bool B(string path, bool fallback)
		{
			var v = V(path)?.Trim().ToLowerInvariant();
			if(v == "true" || v == "1") return true;
			if(v == "false" || v == "0") return false;
			return fallback;
		}

		private void Rebuild()
		{
			var modeRaw = V("dark")?.Trim().ToLowerInvariant();
			bool dark = modeRaw == "true" ? true : modeRaw == "false" ? false : _dark;

			// Sample wallpaper
			Background = new LinearGradientBrush(
				dark ? Color.FromRgb(0x1b, 0x2a, 0x4a) : Color.FromRgb(0x9c, 0xc8, 0xf5),
				dark ? Color.FromRgb(0x4a, 0x1f, 0x3d) : Color.FromRgb(0xf5, 0xc6, 0xa5), 35);
			var wallpaper = new Grid();
			wallpaper.Children.Add(new Ellipse(Color.FromArgb(0x90, 0x13, 0xDA, 0xFC), 220, 60, -40));
			wallpaper.Children.Add(new Ellipse(Color.FromArgb(0x90, 0x13, 0x64, 0xFD), 260, 260, 140));
			wallpaper.Children.Add(new Ellipse(Color.FromArgb(0x70, 0xff, 0x9f, 0x43), 160, 40, 220));

			var fg = dark ? Colors.White : Color.FromRgb(0x1a, 0x1a, 0x1a);
			var bgDefault = dark ? Color.FromRgb(0x2b, 0x2b, 0x2b) : Color.FromRgb(0xf9, 0xf9, 0xf9);

			var bg = C("background.color", bgDefault);
			var effectRaw = V("background.effect")?.Trim().ToLowerInvariant();
			double effect = NssTheme.TryParseNumber(effectRaw, out var e) ? e : (effectRaw == "auto" || effectRaw == null ? 2 : 0);
			double opacity = N("background.opacity", effect > 0 ? 80 : 100);
			if(effect <= 0)
				opacity = 100;
			var fill = ColorUtil.WithOpacity(bg, opacity);

			double radius = Math.Max(4, N("border.radius", 3) * 2.5);
			var borderOn = B("border.enabled", true);
			double borderSize = borderOn ? N("border.size", 1) : 0;
			var borderColor = ColorUtil.WithOpacity(C("border.color", dark ? Color.FromRgb(0x45, 0x45, 0x45) : Color.FromRgb(0xd0, 0xd0, 0xd0)), N("border.opacity", 100));

			var menu = new Border
			{
				Width = 260,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				CornerRadius = new CornerRadius(radius),
				BorderThickness = new Thickness(borderSize),
				BorderBrush = new SolidColorBrush(borderColor),
				Padding = Spacing("border.padding", new Thickness(4)),
			};

			// Frosted glass approximation: blur can't be sampled here, so add a soft wash of the
			// background color on top of the see-through tint.
			var glass = new Grid();
			if(effect >= 2 && opacity < 100)
			{
				glass.Children.Add(new Border
				{
					CornerRadius = menu.CornerRadius,
					Background = new SolidColorBrush(ColorUtil.WithOpacity(bg, effect >= 3 ? 45 : 30)),
					Margin = new Thickness(-menu.Padding.Left, -menu.Padding.Top, -menu.Padding.Right, -menu.Padding.Bottom),
				});
			}
			glass.Children.Add(new Border
			{
				CornerRadius = menu.CornerRadius,
				Background = new SolidColorBrush(fill),
				Margin = new Thickness(-menu.Padding.Left, -menu.Padding.Top, -menu.Padding.Right, -menu.Padding.Bottom),
			});
			if(B("background.gradient.enabled", false))
			{
				var g = Gradient();
				if(g != null)
					glass.Children.Add(new Border { CornerRadius = menu.CornerRadius, Background = g, Margin = new Thickness(-menu.Padding.Left, -menu.Padding.Top, -menu.Padding.Right, -menu.Padding.Bottom) });
			}

			if(B("shadow.enabled", true))
			{
				menu.Effect = new DropShadowEffect
				{
					BlurRadius = N("shadow.size", 12) * 2,
					ShadowDepth = N("shadow.offset", 3),
					Direction = 270,
					Color = C("shadow.color", Colors.Black),
					Opacity = N("shadow.opacity", 30) / 100.0,
				};
			}

			var font = V("font.name")?.Trim('"', '\'');
			var family = new FontFamily(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font + ", Segoe UI");
			var size = N("font.size", 12) * 1.1;
			var weight = FontWeight.FromOpenTypeWeight((int)Math.Max(1, Math.Min(999, N("font.weight", 4) * 100)));
			var italic = B("font.italic", false);

			var textNormal = C("item.text.normal", fg);
			var textSelect = C("item.text.select", textNormal);
			var textDisabled = C("item.text.normal.disabled", Color.FromArgb(0x80, fg.R, fg.G, fg.B));
			var backSelect = C("item.back.select", dark ? Color.FromArgb(0x20, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0));
			if(_theme?.Get("item.opacity") != null)
				backSelect = ColorUtil.WithOpacity(backSelect, N("item.opacity", 100));
			var borderSelect = C("item.border.select", Colors.Transparent);
			var symbol = C("symbol.normal", textNormal);
			var chevron = C("symbol.chevron.normal", symbol);
			double itemRadius = N("item.radius", 3) * 1.5;
			var itemPad = Spacing("item.padding", new Thickness(10, 5, 10, 5));
			var itemMargin = Spacing("item.margin", new Thickness(0));
			var icons = B("image.enabled", true);

			var sepColor = ColorUtil.WithOpacity(C("separator.color", dark ? Color.FromRgb(0x50, 0x50, 0x50) : Color.FromRgb(0xd8, 0xd8, 0xd8)), N("separator.opacity", 100));
			double sepSize = N("separator.size", 1);
			var sepMargin = Spacing("separator.margin", new Thickness(6, 4, 6, 4));

			var stack = new StackPanel();
			void Item(string glyph, string text, bool hover = false, bool disabled = false, bool sub = false)
			{
				var row = new Grid { Margin = itemMargin };
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(icons ? 28 : 6) });
				row.ColumnDefinitions.Add(new ColumnDefinition());
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
				var color = disabled ? textDisabled : hover ? textSelect : textNormal;
				if(icons)
					row.Children.Add(new TextBlock
					{
						Text = glyph,
						FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
						FontSize = size,
						Foreground = new SolidColorBrush(disabled ? textDisabled : symbol),
						VerticalAlignment = VerticalAlignment.Center,
					});
				var label = new TextBlock
				{
					Text = text,
					FontFamily = family,
					FontSize = size,
					FontWeight = weight,
					FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
					Foreground = new SolidColorBrush(color),
					VerticalAlignment = VerticalAlignment.Center,
				};
				Grid.SetColumn(label, 1);
				row.Children.Add(label);
				if(sub)
				{
					var arrow = new TextBlock
					{
						Text = "",
						FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
						FontSize = size * 0.75,
						Foreground = new SolidColorBrush(chevron),
						VerticalAlignment = VerticalAlignment.Center,
					};
					Grid.SetColumn(arrow, 2);
					row.Children.Add(arrow);
				}
				stack.Children.Add(new Border
				{
					Child = row,
					Padding = itemPad,
					CornerRadius = new CornerRadius(itemRadius),
					Background = hover ? new SolidColorBrush(backSelect) : Brushes.Transparent,
					BorderBrush = hover ? new SolidColorBrush(borderSelect) : Brushes.Transparent,
					BorderThickness = new Thickness(hover ? 1 : 0),
				});
			}
			void Sep()
			{
				if(sepSize > 0)
					stack.Children.Add(new Border { Height = sepSize, Margin = sepMargin, Background = new SolidColorBrush(sepColor) });
			}

			Item("", "Open");
			Item("", "Open in new window", hover: true);
			Item("", "Pin to Quick access");
			Sep();
			Item("", "Cut");
			Item("", "Copy");
			Item("", "Paste", disabled: true);
			Sep();
			Item("", "Terminal", sub: true);
			Item("", "File manage", sub: true);
			Sep();
			Item("", "Properties");

			glass.Children.Add(stack);
			menu.Child = glass;

			var root = new Grid();
			root.Children.Add(wallpaper);
			root.Children.Add(menu);
			Child = root;
		}

		private Thickness Spacing(string path, Thickness fallback)
		{
			var parts = NssTheme.SplitArray(V(path));
			var nums = parts.Select(p => NssTheme.TryParseNumber(p, out var n) ? n : double.NaN).ToList();
			if(nums.Count == 0)
				return NssTheme.TryParseNumber(V(path), out var one) ? new Thickness(one) : fallback;
			if(nums.Any(double.IsNaN))
				return fallback;
			if(nums.Count == 1) return new Thickness(nums[0]);
			if(nums.Count == 2) return new Thickness(nums[0], nums[1], nums[0], nums[1]);
			if(nums.Count >= 4) return new Thickness(nums[0], nums[2], nums[1], nums[3]); // [left, right, top, bottom]
			return fallback;
		}

		private Brush Gradient()
		{
			var stops = new GradientStopCollection();
			foreach(var stop in NssTheme.SplitArray(V("background.gradient.stop")))
			{
				var parts = NssTheme.SplitArray(stop);
				if(parts.Count < 2)
				{
					parts = NssTheme.SplitArray("[" + stop + "]");
				}
				if(parts.Count >= 2 && NssTheme.TryParseNumber(parts[0], out var offset))
				{
					var c = ColorUtil.TryParse(NssTheme.ForMode(parts[1], _dark), out var col) ? col : Colors.White;
					if(parts.Count >= 3 && NssTheme.TryParseNumber(NssTheme.ForMode(parts[2], _dark), out var op))
						c = ColorUtil.WithOpacity(c, op);
					stops.Add(new GradientStop(c, offset));
				}
			}
			// A single [offset, color, opacity] stop.
			if(stops.Count == 0)
			{
				var single = NssTheme.SplitArray(V("background.gradient.stop"));
				if(single.Count >= 2 && NssTheme.TryParseNumber(single[0], out var off) && ColorUtil.TryParse(single[1], out var sc))
					stops.Add(new GradientStop(single.Count >= 3 && NssTheme.TryParseNumber(single[2], out var so) ? ColorUtil.WithOpacity(sc, so) : sc, off));
			}
			if(stops.Count == 0)
				return null;

			var radial = NssTheme.SplitArray(V("background.gradient.radial"));
			if(radial.Count >= 3 && V("background.gradient.linear") == null)
			{
				double P(int i) => NssTheme.TryParseNumber(radial[i], out var v) ? v / 100.0 : 0.5;
				return new RadialGradientBrush(stops) { Center = new Point(P(0) / 2, P(1) / 2), RadiusX = P(2), RadiusY = P(2) };
			}
			var lin = NssTheme.SplitArray(V("background.gradient.linear"));
			double L(int i, double f) => i < lin.Count && NssTheme.TryParseNumber(lin[i], out var v) ? v / 100.0 : f;
			return new LinearGradientBrush(stops, new Point(L(0, 0), L(2, 0)), new Point(L(1, 0), L(3, 1)));
		}

		private sealed class Ellipse : Border
		{
			public Ellipse(Color c, double size, double x, double y)
			{
				Width = Height = size;
				CornerRadius = new CornerRadius(size / 2);
				Background = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B));
				HorizontalAlignment = HorizontalAlignment.Left;
				VerticalAlignment = VerticalAlignment.Top;
				Margin = new Thickness(x, y, 0, 0);
			}
		}
	}
}
