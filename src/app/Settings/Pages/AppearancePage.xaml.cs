using ContextShell.Settings.Themes;
using ContextShell.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ContextShell.Settings.Pages
{
	public partial class AppearancePage : UserControl, IRefreshable
	{
		/// <summary>Themes shipped with ContextShell. Editing one saves a copy.</summary>
		public static readonly Dictionary<string, string> BuiltIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["auto"] = "Windows",
			["liquid-glass"] = "Liquid Glass",
			["dark"] = "Dark",
			["light"] = "Light",
			["acrylic"] = "Acrylic",
			["glass"] = "Classic glass",
		};

		private readonly MainWindow _main;
		private bool _dark = ThemeManager.IsDark;

		public AppearancePage(MainWindow main)
		{
			_main = main;
			InitializeComponent();
			(_dark ? PreviewDark : PreviewLight).IsChecked = true;
			Refresh();
		}

		public static string DisplayName(string name) =>
			BuiltIn.TryGetValue(name, out var n) ? n :
			System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Replace('-', ' ').Replace('_', ' '));

		private void Mode_Checked(object sender, RoutedEventArgs e)
		{
			_dark = PreviewDark.IsChecked == true;
			if(IsLoaded)
				Refresh();
		}

		public void Refresh()
		{
			Gallery.Children.Clear();
			var store = _main.Store;
			if(!store.IsValid)
			{
				ErrorText.Text = "ContextShell's config files weren't found. Reinstall ContextShell to fix this.";
				ErrorText.Visibility = Visibility.Visible;
				return;
			}

			_main.Shell.MigrateInlineTheme();
			var active = _main.Shell.ActiveTheme;

			var themes = store.ThemeFiles()
				.Select(f =>
				{
					try
					{
						var t = NssTheme.Parse(store.Read(f) ?? "");
						t.FilePath = f;
						t.Name = Path.GetFileNameWithoutExtension(f);
						t.IsPreset = BuiltIn.ContainsKey(t.Name);
						return t;
					}
					catch
					{
						return null;
					}
				})
				.Where(t => t != null)
				.OrderBy(t => t.IsPreset ? BuiltIn.Keys.ToList().IndexOf(t.Name) : 100)
				.ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			foreach(var theme in themes)
				Gallery.Children.Add(Card(theme, string.Equals(theme.Name, active, StringComparison.OrdinalIgnoreCase)));
			Gallery.Children.Add(NewCard());
		}

		private UIElement Card(NssTheme theme, bool isActive)
		{
			var preview = new MenuPreview { MinHeight = 0, Width = 520, Height = 420 };
			preview.Show(theme, _dark);
			var box = new Viewbox { Child = preview, Width = 234, Height = 189, Stretch = Stretch.Uniform };
			var clip = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = box };

			var title = new TextBlock { Text = DisplayName(theme.Name), FontWeight = FontWeights.SemiBold };
			var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
			if(isActive)
				badges.Children.Add(Badge("In use", accent: true));
			if(theme.Values.Values.Any(v => v.IndexOf("sys.dark", StringComparison.OrdinalIgnoreCase) >= 0)
			   || string.Equals(theme.Get("dark"), "auto", StringComparison.OrdinalIgnoreCase))
				badges.Children.Add(Badge("Light & dark"));
			if(!theme.IsPreset)
				badges.Children.Add(Badge("Custom"));

			var use = new Button { Content = isActive ? "In use" : "Use", IsEnabled = !isActive, MinWidth = 0, Padding = new Thickness(14, 4, 14, 5) };
			if(!isActive)
				use.Style = (Style)FindResource("AccentButton");
			use.Click += (s, e) =>
			{
				_main.Shell.SetActiveTheme(theme.Name);
				Refresh();
			};
			var edit = new Button { Content = theme.IsPreset ? "Customize" : "Edit", MinWidth = 0, Padding = new Thickness(14, 4, 14, 5), Margin = new Thickness(8, 0, 0, 0) };
			edit.Click += (s, e) => _main.ShowPage(new ThemeEditorPage(_main, theme));

			var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
			buttons.Children.Add(use);
			buttons.Children.Add(edit);
			if(!theme.IsPreset && !isActive)
			{
				var del = new Button
				{
					Style = (Style)FindResource("SubtleButton"),
					Content = new TextBlock { Text = "", Style = (Style)FindResource("IconText"), FontSize = 14 },
					ToolTip = "Delete theme",
					Margin = new Thickness(4, 0, 0, 0),
				};
				del.Click += (s, e) =>
				{
					_main.Store.StageDelete(theme.FilePath);
					Refresh();
				};
				buttons.Children.Add(del);
			}

			var stack = new StackPanel();
			stack.Children.Add(clip);
			stack.Children.Add(new StackPanel { Margin = new Thickness(2, 10, 0, 0), Children = { title, badges } });
			stack.Children.Add(buttons);

			var card = new Border
			{
				Style = (Style)FindResource("Card"),
				Width = 268,
				Margin = new Thickness(0, 0, 12, 12),
				Padding = new Thickness(16),
				Child = stack,
			};
			if(isActive)
			{
				card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
				card.BorderThickness = new Thickness(2);
				card.Padding = new Thickness(15);
			}
			return card;
		}

		private UIElement Badge(string text, bool accent = false)
		{
			var b = new Border
			{
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(6, 1, 6, 2),
				Margin = new Thickness(0, 0, 6, 0),
				Child = new TextBlock { Text = text, FontSize = 11 },
			};
			b.SetResourceReference(Border.BackgroundProperty, accent ? "AccentSubtleBrush" : "SubtleHoverBrush");
			if(accent)
				((TextBlock)b.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentForegroundBrush");
			return b;
		}

		private UIElement NewCard()
		{
			var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
			content.Children.Add(new TextBlock { Text = "", Style = (Style)FindResource("IconText"), FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
			content.Children.Add(new TextBlock { Text = "New theme", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
			content.Children.Add(new TextBlock { Text = "Start from the Windows theme", Style = (Style)FindResource("CaptionText"), HorizontalAlignment = HorizontalAlignment.Center });
			var button = new Button
			{
				Style = (Style)FindResource("CardButton"),
				Width = 268,
				Height = 268,
				Margin = new Thickness(0, 0, 12, 12),
				Content = content,
			};
			button.Click += (s, e) =>
			{
				var basePath = Path.Combine(_main.Store.ThemesFolder, "auto.nss");
				var start = _main.Store.Exists(basePath) ? NssTheme.Parse(_main.Store.Read(basePath)) : new NssTheme();
				var theme = start.Clone("my-theme");
				theme.Description = "";
				_main.ShowPage(new ThemeEditorPage(_main, theme, isNew: true));
			};
			return button;
		}
	}
}
