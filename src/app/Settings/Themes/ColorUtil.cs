using System;
using System.Globalization;
using System.Windows.Media;

namespace ContextShell.Settings.Themes
{
	internal static class ColorUtil
	{
		/// <summary>Parse #rgb, #rrggbb or #aarrggbb. Returns false for expressions like auto or color.accent.</summary>
		public static bool TryParse(string raw, out Color color)
		{
			color = Colors.Transparent;
			if(raw == null)
				return false;
			var s = raw.Trim();

			// [#rrggbb, opacity] with opacity in percent, as the engine accepts.
			var parts = NssTheme.SplitArray(s);
			if(parts.Count == 2 && TryParse(parts[0], out var baseColor) && NssTheme.TryParseNumber(parts[1], out var percent))
			{
				color = WithOpacity(Color.FromRgb(baseColor.R, baseColor.G, baseColor.B), percent);
				return true;
			}

			if(!s.StartsWith("#"))
				return false;
			s = s.Substring(1);
			try
			{
				if(s.Length == 3)
					s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
				if(s.Length == 6)
				{
					color = Color.FromRgb(Hex(s, 0), Hex(s, 2), Hex(s, 4));
					return true;
				}
				if(s.Length == 8)
				{
					color = Color.FromArgb(Hex(s, 0), Hex(s, 2), Hex(s, 4), Hex(s, 6));
					return true;
				}
			}
			catch
			{
			}
			return false;
		}

		private static byte Hex(string s, int i) => byte.Parse(s.Substring(i, 2), NumberStyles.HexNumber);

		public static string ToHex(Color c) =>
			c.A == 255 ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : $"#{c.A:x2}{c.R:x2}{c.G:x2}{c.B:x2}";

		public static Color WithOpacity(Color c, double percent) =>
			Color.FromArgb((byte)Math.Round(c.A * Math.Max(0, Math.Min(100, percent)) / 100.0), c.R, c.G, c.B);

		/// <summary>Checkerboard brush drawn behind colors so transparency is visible.</summary>
		public static Brush Checkerboard(bool dark)
		{
			var a = dark ? Color.FromRgb(0x3a, 0x3a, 0x3a) : Color.FromRgb(0xff, 0xff, 0xff);
			var b = dark ? Color.FromRgb(0x2a, 0x2a, 0x2a) : Color.FromRgb(0xdd, 0xdd, 0xdd);
			var group = new DrawingGroup();
			group.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new System.Windows.Rect(0, 0, 8, 8))));
			group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new System.Windows.Rect(0, 0, 4, 4))));
			group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, new RectangleGeometry(new System.Windows.Rect(4, 4, 4, 4))));
			var brush = new DrawingBrush(group)
			{
				TileMode = TileMode.Tile,
				Viewport = new System.Windows.Rect(0, 0, 8, 8),
				ViewportUnits = BrushMappingMode.Absolute,
			};
			brush.Freeze();
			return brush;
		}
	}
}
