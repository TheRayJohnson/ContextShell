using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ContextShell.UI
{
	/// <summary>
	/// Renders a window's content to PNG without showing it. Used by the --screenshots switch so CI
	/// and reviewers can see every page in light and dark mode.
	/// </summary>
	public static class Snapshot
	{
		public static void Save(Window window, string path, double width, double height, double scale = 2.0)
		{
			var content = (FrameworkElement)window.Content;
			window.Content = null;

			var host = new Border
			{
				Width = width,
				Height = height,
				Background = (Brush)Application.Current.FindResource("WindowBackgroundBrush"),
				Child = content,
			};
			TextElement.SetForeground(host, (Brush)Application.Current.FindResource("TextPrimaryBrush"));
			TextElement.SetFontFamily(host, window.FontFamily);
			TextElement.SetFontSize(host, window.FontSize);

			host.Measure(new Size(width, height));
			host.Arrange(new Rect(0, 0, width, height));
			host.UpdateLayout();

			var bmp = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
			bmp.Render(host);

			Directory.CreateDirectory(Path.GetDirectoryName(path));
			using(var file = File.Create(path))
			{
				var png = new PngBitmapEncoder();
				png.Frames.Add(BitmapFrame.Create(bmp));
				png.Save(file);
			}

			host.Child = null;
			window.Content = content;
		}
	}
}
