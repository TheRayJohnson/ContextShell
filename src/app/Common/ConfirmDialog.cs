using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ContextShell.UI
{
	/// <summary>A small themed question dialog, used instead of the system MessageBox.</summary>
	public sealed class ConfirmDialog : Window
	{
		public string Result { get; private set; }

		private ConfirmDialog(Window owner, string title, string text, string[] buttons, string accent)
		{
			Owner = owner;
			Title = owner?.Title ?? AppInfo.Name;
			Width = 440;
			SizeToContent = SizeToContent.Height;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
			ShowInTaskbar = false;
			UseLayoutRounding = true;
			FontFamily = (FontFamily)Application.Current.FindResource("UIFont");
			FontSize = 14;
			ThemeManager.Attach(this);

			var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
			body.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("SubtitleText") });
			body.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });

			var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 14, 24, 14) };
			for(int i = 0; i < buttons.Length; i++)
			{
				var label = buttons[i];
				var b = new Button { Content = label, Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0), IsDefault = label == accent, IsCancel = i == buttons.Length - 1 };
				if(label == accent)
					b.Style = (Style)FindResource("AccentButton");
				b.Click += (s, e) => { Result = label; Close(); };
				row.Children.Add(b);
			}
			var footer = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
			footer.SetResourceReference(Border.BackgroundProperty, "FooterBrush");
			footer.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");

			var root = new StackPanel();
			root.Children.Add(body);
			root.Children.Add(footer);
			Content = root;
		}

		/// <summary>Show modally. Returns the clicked button's label, or null if closed.</summary>
		public static string Ask(Window owner, string title, string text, string accent, params string[] buttons)
		{
			var d = new ConfirmDialog(owner, title, text, buttons, accent);
			d.ShowDialog();
			return d.Result;
		}
	}
}
