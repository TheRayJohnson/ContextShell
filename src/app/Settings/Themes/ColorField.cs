using ContextShell.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ContextShell.Settings.Themes
{
	/// <summary>A color swatch with a palette popup and a hex/expression text box.</summary>
	public sealed class ColorField : StackPanel
	{
		private static readonly string[] Palette =
		{
			"#ffffff", "#f5f5f7", "#e5e5ea", "#c7c7cc", "#8e8e93", "#636366", "#3a3a3c", "#2c2c2e", "#1c1c1e", "#000000",
			"#13dafc", "#139efd", "#1364fd", "#0a84ff", "#5e5ce6", "#bf5af2", "#ff375f", "#ff453a", "#ff9f0a", "#ffd60a",
			"#30d158", "#66d4cf", "#64d2ff", "#0078d4", "#107c10", "#ca5010", "#c42b1c", "#e3008c", "#881798", "#744da9",
		};

		private readonly Border _swatch;
		private readonly TextBox _text;
		private readonly Popup _popup;
		private string _value;
		private bool _updating;

		public event Action<string> ValueChanged;

		public ColorField(string placeholder = "Default")
		{
			Orientation = Orientation.Horizontal;

			var swatchButton = new Button
			{
				Style = (Style)Application.Current.FindResource("SubtleButton"),
				Padding = new Thickness(0),
				Width = 32,
				Height = 32,
				ToolTip = "Pick a color",
			};
			var checker = new Border { CornerRadius = new CornerRadius(4), Background = ColorUtil.Checkerboard(ThemeManager.IsDark), Width = 28, Height = 28 };
			_swatch = new Border
			{
				CornerRadius = new CornerRadius(4),
				BorderThickness = new Thickness(1),
			};
			_swatch.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");
			var g = new Grid { Width = 28, Height = 28 };
			g.Children.Add(checker);
			g.Children.Add(_swatch);
			swatchButton.Content = g;
			swatchButton.Click += (s, e) => _popup.IsOpen = true;
			Children.Add(swatchButton);

			_text = new TextBox { Width = 132, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Hex color like #1c1c1e, or an expression" };
			_text.LostKeyboardFocus += (s, e) => Commit();
			_text.KeyDown += (s, e) => { if(e.Key == Key.Enter) Commit(); };
			Children.Add(_text);

			_popup = new Popup
			{
				PlacementTarget = swatchButton,
				Placement = PlacementMode.Bottom,
				StaysOpen = false,
				AllowsTransparency = true,
				PopupAnimation = PopupAnimation.Fade,
				Child = BuildPalette(),
			};
			Placeholder = placeholder;
			Refresh();
		}

		public string Placeholder { get; set; }

		public string Value
		{
			get => _value;
			set
			{
				_value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
				Refresh();
			}
		}

		private UIElement BuildPalette()
		{
			var wrap = new WrapPanel { Width = 10 * 26 };
			foreach(var hex in Palette)
			{
				ColorUtil.TryParse(hex, out var c);
				var b = new Button
				{
					Style = (Style)Application.Current.FindResource("SubtleButton"),
					Width = 26,
					Height = 26,
					Padding = new Thickness(0),
					ToolTip = hex,
					Content = new Border
					{
						Width = 20,
						Height = 20,
						CornerRadius = new CornerRadius(4),
						Background = new SolidColorBrush(c),
						BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
						BorderThickness = new Thickness(1),
					},
				};
				b.Click += (s, e) => { Set(hex); _popup.IsOpen = false; };
				wrap.Children.Add(b);
			}

			var reset = new Button { Content = "Use default", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
			reset.Click += (s, e) => { Set(null); _popup.IsOpen = false; };

			var panel = new StackPanel();
			panel.Children.Add(wrap);
			panel.Children.Add(reset);

			var border = new Border
			{
				Padding = new Thickness(10),
				Margin = new Thickness(0, 4, 8, 8),
				CornerRadius = new CornerRadius(8),
				BorderThickness = new Thickness(1),
				Child = panel,
				Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.2 },
			};
			border.SetResourceReference(Border.BackgroundProperty, "DialogBrush");
			border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");
			return border;
		}

		private void Commit()
		{
			if(_updating)
				return;
			var t = _text.Text.Trim();
			if(!string.IsNullOrEmpty(t) && !t.StartsWith("#") && System.Text.RegularExpressions.Regex.IsMatch(t, "^[0-9a-fA-F]{6}$"))
				t = "#" + t;
			Set(t.Length == 0 ? null : t);
		}

		private void Set(string value)
		{
			var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			if(v == _value)
			{
				Refresh();
				return;
			}
			_value = v;
			Refresh();
			ValueChanged?.Invoke(_value);
		}

		private void Refresh()
		{
			_updating = true;
			_text.Text = _value ?? "";
			_text.Tag = Placeholder;
			if(ColorUtil.TryParse(_value, out var c))
				_swatch.Background = new SolidColorBrush(c);
			else
				_swatch.Background = Brushes.Transparent;
			_swatch.ToolTip = _value == null ? Placeholder : _value;
			_updating = false;
		}
	}
}
