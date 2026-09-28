using System;
using System.Collections.Generic;
using System.Linq;

namespace ContextShell.Settings.Themes
{
	public enum FieldKind
	{
		Choice,      // dropdown of fixed values
		Toggle,      // true/false
		Number,      // slider + number box
		Color,       // color with optional dark-mode variant
		Text,        // free text (font name, glyph font)
		Font,        // font family picker
		Spacing,     // [left, right, top, bottom]
		ColorList,   // [c1, c2, c3]
		Numbers,     // fixed-length number array (gradient geometry)
		Stops,       // gradient stops [[offset, color, opacity], ...]
	}

	public sealed class ThemeField
	{
		public string Path;
		public string Label;
		public string Help;
		public FieldKind Kind;
		public double Min, Max = 100, Step = 1;
		public (string Value, string Label)[] Choices;
		public string[] Parts; // labels for Numbers / ColorList / Spacing
		public bool AllowsAuto;
	}

	public sealed class ThemeGroup
	{
		public string Title;
		public string Glyph;
		public List<ThemeField> Fields = new List<ThemeField>();
	}

	/// <summary>
	/// Every theme option from docs/configuration/themes.html, grouped the way the docs are.
	/// The editor builds its UI from this list, so new options only need a line here.
	/// </summary>
	public static class ThemeSchema
	{
		// Declared before Groups: static initializers run in order.
		private static readonly string[] Spacing4 = { "Left", "Right", "Top", "Bottom" };

		public static readonly List<ThemeGroup> Groups = Build();

		private static readonly Dictionary<string, int> _order =
			Groups.SelectMany(g => g.Fields).Select((f, i) => (f.Path, i)).ToDictionary(x => x.Path, x => x.i, StringComparer.OrdinalIgnoreCase);

		public static int Order(string path) => _order.TryGetValue(path, out var i) ? i : int.MaxValue;

		public static ThemeField Find(string path) => Groups.SelectMany(g => g.Fields).FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

		private static ThemeField Color(string path, string label, string help = null) =>
			new ThemeField { Path = path, Label = label, Help = help, Kind = FieldKind.Color, AllowsAuto = true };

		private static ThemeField Num(string path, string label, double min, double max, string help = null, bool auto = false) =>
			new ThemeField { Path = path, Label = label, Help = help, Kind = FieldKind.Number, Min = min, Max = max, AllowsAuto = auto };

		private static ThemeField Toggle(string path, string label, string help = null) =>
			new ThemeField { Path = path, Label = label, Help = help, Kind = FieldKind.Toggle };

		private static ThemeField Choice(string path, string label, string help, params (string, string)[] choices) =>
			new ThemeField { Path = path, Label = label, Help = help, Kind = FieldKind.Choice, Choices = choices };

		private static ThemeField Spacing(string path, string label, string help = null) =>
			new ThemeField { Path = path, Label = label, Help = help, Kind = FieldKind.Spacing, Parts = Spacing4, Min = 0, Max = 40 };

		private static IEnumerable<ThemeField> ColorStates(string prefix, string what) => new[]
		{
			Color(prefix + ".normal", what),
			Color(prefix + ".select", what + " (hover)"),
			Color(prefix + ".normal.disabled", what + " (disabled)"),
			Color(prefix + ".select.disabled", what + " (disabled, hover)"),
		};

		private static List<ThemeGroup> Build()
		{
			var groups = new List<ThemeGroup>();

			groups.Add(new ThemeGroup
			{
				Title = "General",
				Glyph = "",
				Fields =
				{
					Choice("name", "Base style", "The built-in style every other option starts from.",
						// Quoted: the engine reads theme.name as a string.
						("\"auto\"", "Auto"), ("\"modern\"", "Modern"), ("\"classic\"", "Classic"), ("\"white\"", "White"), ("\"black\"", "Black")),
					Choice("view", "Density", "Row height of menu items.",
						("auto", "Auto"), ("view.compact", "Compact"), ("view.small", "Small"), ("view.medium", "Medium"),
						("view.large", "Large"), ("view.wide", "Wide")),
					Choice("dark", "Color mode", "Auto follows the Windows app mode.",
						("auto", "Follow Windows"), ("default", "Windows default"), ("true", "Always dark"), ("false", "Always light")),
				}
			});

			groups.Add(new ThemeGroup
			{
				Title = "Background",
				Glyph = "",
				Fields =
				{
					Color("background.color", "Color"),
					Num("background.opacity", "Opacity", 0, 100, "100 is solid. Lower values let the effect show through.", auto: true),
					Choice("background.effect", "Effect", "Transparency effects must be on in Windows Settings for blur, acrylic and mica.",
						("auto", "Auto"), ("0", "None (solid)"), ("1", "Transparent"), ("2", "Blur"), ("3", "Acrylic"), ("4", "Mica"), ("5", "Mica alt (tabbed)")),
					Color("background.tintcolor", "Effect tint", "Tint of the blur or acrylic effect."),
					Toggle("background.gradient.enabled", "Gradient overlay"),
					new ThemeField
					{
						Path = "background.gradient.linear", Label = "Linear gradient", Kind = FieldKind.Numbers,
						Parts = new[] { "x1", "x2", "y1", "y2" }, Min = 0, Max = 100,
						Help = "Start and end points in percent of the menu. 0, 0, 0, 100 runs top to bottom.",
					},
					new ThemeField
					{
						Path = "background.gradient.radial", Label = "Radial gradient", Kind = FieldKind.Numbers,
						Parts = new[] { "cx", "cy", "r", "fx", "fy" }, Min = 0, Max = 200,
						Help = "Center, radius and focal point in percent. Use either linear or radial.",
					},
					new ThemeField
					{
						Path = "background.gradient.stop", Label = "Gradient stops", Kind = FieldKind.Stops,
						Help = "Each stop has an offset (0 to 1), a color and an opacity (0 to 100).",
					},
				}
			});

			var item = new ThemeGroup { Title = "Items", Glyph = "" };
			item.Fields.Add(Num("item.opacity", "Opacity", 0, 100, "Opacity of item backgrounds."));
			item.Fields.Add(Num("item.radius", "Corner radius", 0, 3));
			item.Fields.Add(Choice("item.prefix", "Icon column", "Space reserved for icons and check marks.",
				("auto", "Auto"), ("0", "Hide"), ("1", "Show"), ("2", "Ignore")));
			item.Fields.AddRange(ColorStates("item.text", "Text"));
			item.Fields.AddRange(ColorStates("item.back", "Background"));
			item.Fields.AddRange(ColorStates("item.border", "Border"));
			item.Fields.Add(Spacing("item.padding", "Padding"));
			item.Fields.Add(Spacing("item.margin", "Margin"));
			groups.Add(item);

			groups.Add(new ThemeGroup
			{
				Title = "Border",
				Glyph = "",
				Fields =
				{
					Toggle("border.enabled", "Show border"),
					Num("border.size", "Width", 0, 10),
					Color("border.color", "Color"),
					Num("border.opacity", "Opacity", 0, 100),
					Num("border.radius", "Corner radius", 0, 20),
					Spacing("border.padding", "Padding", "Space between the border and the items."),
				}
			});

			groups.Add(new ThemeGroup
			{
				Title = "Shadow",
				Glyph = "",
				Fields =
				{
					Toggle("shadow.enabled", "Show shadow"),
					Num("shadow.size", "Size", 0, 30),
					Color("shadow.color", "Color"),
					Num("shadow.opacity", "Opacity", 0, 100),
					Num("shadow.offset", "Offset", 0, 30),
				}
			});

			groups.Add(new ThemeGroup
			{
				Title = "Font",
				Glyph = "",
				Fields =
				{
					new ThemeField { Path = "font.name", Label = "Font", Kind = FieldKind.Font },
					Num("font.size", "Size", 6, 32),
					Choice("font.weight", "Weight", null,
						("1", "Thin"), ("2", "Extra light"), ("3", "Light"), ("4", "Regular"), ("5", "Medium"),
						("6", "Semibold"), ("7", "Bold"), ("8", "Extra bold"), ("9", "Black")),
					Toggle("font.italic", "Italic"),
				}
			});

			groups.Add(new ThemeGroup
			{
				Title = "Separators",
				Glyph = "",
				Fields =
				{
					Num("separator.size", "Thickness", 0, 40),
					Color("separator.color", "Color"),
					Num("separator.opacity", "Opacity", 0, 100),
					Spacing("separator.margin", "Margin"),
				}
			});

			var symbol = new ThemeGroup { Title = "Symbols", Glyph = "" };
			symbol.Fields.AddRange(ColorStates("symbol", "All symbols"));
			symbol.Fields.AddRange(ColorStates("symbol.chevron", "Submenu arrow"));
			symbol.Fields.AddRange(ColorStates("symbol.checkmark", "Check mark"));
			symbol.Fields.AddRange(ColorStates("symbol.bullet", "Bullet"));
			groups.Add(symbol);

			groups.Add(new ThemeGroup
			{
				Title = "Icons",
				Glyph = "",
				Fields =
				{
					Toggle("image.enabled", "Show icons"),
					new ThemeField { Path = "image.color", Label = "Glyph colors", Kind = FieldKind.ColorList, Parts = new[] { "Primary", "Secondary", "Accent" } },
					Num("image.size", "Icon size", 8, 48),
					Num("image.gap", "Gap", 0, 40, "Space between the icon and the text."),
					new ThemeField { Path = "image.glyph", Label = "Glyph font", Kind = FieldKind.Font, Help = "Font used for built-in glyph icons." },
					Toggle("image.scale", "Scale with DPI"),
					Choice("image.align", "Check marks", null,
						("0", "Check mark only"), ("1", "Icon only"), ("2", "Icon and check mark")),
				}
			});

			groups.Add(new ThemeGroup
			{
				Title = "Layout",
				Glyph = "",
				Fields =
				{
					Toggle("layout.rtl", "Right-to-left", "For Arabic, Hebrew and other right-to-left languages."),
					Num("layout.popup", "Submenu offset", -20, 20, "Shifts submenus horizontally."),
				}
			});

			return groups;
		}
	}
}
