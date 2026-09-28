using ContextShell.Settings.Themes;
using ContextShell.UI;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace ContextShell.Settings.Pages
{
	public partial class ThemeEditorPage : UserControl
	{
		private readonly MainWindow _main;
		private readonly NssTheme _theme;
		private readonly string _originalName;
		private readonly MenuPreview _preview = new MenuPreview { MinHeight = 0 };
		private bool _dark = ThemeManager.IsDark;

		public ThemeEditorPage(MainWindow main, NssTheme source, bool isNew = false)
		{
			_main = main;
			InitializeComponent();

			bool builtIn = source.IsPreset;
			// Editing a built-in theme works on a copy so updates can still refresh the originals.
			_theme = source.Clone(builtIn || isNew ? UniqueName(builtIn ? source.Name + "-custom" : source.Name) : source.Name);
			_theme.FilePath = builtIn || isNew ? null : source.FilePath;
			_originalName = _theme.FilePath == null ? null : source.Name;

			HeaderTitle.Text = isNew ? "New theme" : builtIn ? $"Customize {AppearancePage.DisplayName(source.Name)}" : $"Edit {AppearancePage.DisplayName(source.Name)}";
			HeaderHint.Text = builtIn
				? "Built-in themes aren't changed. Your version is saved as a new theme."
				: "Every option from the theme docs. Leave an option on Default to use the base style.";

			NameBox.Text = _theme.Name;
			DescriptionBox.Text = _theme.Description;
			PreviewHost.Content = _preview;
			(_dark ? PreviewDark : PreviewLight).IsChecked = true;

			var editor = new FieldEditor(_theme);
			editor.Changed += UpdatePreview;
			foreach(var group in ThemeSchema.Groups)
				Groups.Children.Add(GroupCard(group, editor));

			SizeChanged += (s, e) => PreviewColumn.Width = new GridLength(e.NewSize.Width < 900 ? 280 : 340);
			UpdatePreview();
		}

		private UIElement GroupCard(ThemeGroup group, FieldEditor editor)
		{
			var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
			header.Children.Add(new TextBlock { Text = group.Glyph, Style = (Style)FindResource("IconText") });
			header.Children.Add(new TextBlock { Text = group.Title, Style = (Style)FindResource("BodyStrongText"), Margin = new Thickness(12, 0, 0, 0) });

			var body = editor.BuildGroup(group);
			var expander = new Expander
			{
				Header = header,
				Content = body,
				IsExpanded = group.Title == "General" || group.Title == "Background",
				Style = (Style)FindResource("CardExpander"),
				Margin = new Thickness(0, 0, 0, 8),
			};
			return expander;
		}

		private void Mode_Checked(object sender, RoutedEventArgs e)
		{
			_dark = PreviewDark.IsChecked == true;
			if(IsInitialized)
				UpdatePreview();
		}

		private void UpdatePreview() => _preview.Show(_theme, _dark);

		private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateName();

		private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e) => _theme.Description = DescriptionBox.Text.Trim();

		private static string Slug(string name) =>
			Regex.Replace(Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-"), "^-+|-+$", "");

		private string UniqueName(string name)
		{
			var slug = Slug(name);
			var candidate = slug;
			for(int i = 2; _main.Store.Exists(Path.Combine(_main.Store.ThemesFolder, candidate + ".nss")); i++)
				candidate = slug + "-" + i;
			return candidate;
		}

		private bool ValidateName()
		{
			var slug = Slug(NameBox.Text);
			string error = null;
			if(slug.Length == 0)
				error = "Give the theme a name.";
			else if(AppearancePage.BuiltIn.ContainsKey(slug))
				error = "That name is used by a built-in theme.";
			else if(!string.Equals(slug, _originalName, StringComparison.OrdinalIgnoreCase)
			        && _main.Store.Exists(Path.Combine(_main.Store.ThemesFolder, slug + ".nss")))
				error = "A theme with this name already exists.";
			NameError.Text = error ?? "";
			NameError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
			return error == null;
		}

		private bool Stage(bool use)
		{
			if(!ValidateName())
				return false;
			var slug = Slug(NameBox.Text);
			var path = Path.Combine(_main.Store.ThemesFolder, slug + ".nss");
			_main.Store.Stage(path, _theme.ToNss());

			// Renamed: remove the old file and keep the theme active if it was.
			bool wasActive = _originalName != null && string.Equals(_main.Shell.ActiveTheme, _originalName, StringComparison.OrdinalIgnoreCase);
			if(_originalName != null && !string.Equals(_originalName, slug, StringComparison.OrdinalIgnoreCase))
				_main.Store.StageDelete(Path.Combine(_main.Store.ThemesFolder, _originalName + ".nss"));
			if(use || wasActive)
				_main.Shell.SetActiveTheme(slug);
			return true;
		}

		private void Save_Click(object sender, RoutedEventArgs e)
		{
			if(Stage(false) && _main.SaveChanges())
				_main.Navigate("appearance");
		}

		private void SaveUse_Click(object sender, RoutedEventArgs e)
		{
			if(Stage(true) && _main.SaveChanges())
				_main.Navigate("appearance");
		}

		private void Back_Click(object sender, RoutedEventArgs e) => _main.ShowPage(new AppearancePage(_main));
	}
}
