using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ContextShell.UI
{
	/// <summary>
	/// Applies the light/dark palette, follows the Windows app theme live, and gives
	/// windows a Mica backdrop (Windows 11 22H2+) or a solid themed background (Windows 10).
	/// </summary>
	public static class ThemeManager
	{
		private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

		/// <summary>Set to force a theme (screenshots, tests). Null follows Windows.</summary>
		public static bool? ForceDark { get; set; }

		public static bool IsDark { get; private set; }

		public static event EventHandler Changed;

		public static bool SystemUsesDarkApps()
		{
			try
			{
				using(var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
					return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
			}
			catch
			{
				return false;
			}
		}

		public static void Initialize(Application app)
		{
			Apply(app);
			SystemEvents.UserPreferenceChanged += (s, e) =>
			{
				if(e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
					app.Dispatcher.BeginInvoke(new Action(() => Apply(app)));
			};
			SystemParameters.StaticPropertyChanged += (s, e) =>
			{
				if(e.PropertyName == nameof(SystemParameters.HighContrast))
					app.Dispatcher.BeginInvoke(new Action(() => Apply(app)));
			};
		}

		public static void Apply(Application app)
		{
			IsDark = ForceDark ?? SystemUsesDarkApps();

			string palette = SystemParameters.HighContrast ? "HighContrast" : IsDark ? "Dark" : "Light";
			var dict = new ResourceDictionary
			{
				Source = new Uri($"pack://application:,,,/{typeof(ThemeManager).Assembly.GetName().Name};component/Common/Palette.{palette}.xaml")
			};

			var merged = app.Resources.MergedDictionaries;
			var old = merged.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Palette."));
			if(old != null)
				merged[merged.IndexOf(old)] = dict;
			else
				merged.Insert(0, dict);

			// With Mica the window background must be transparent so DWM can show through.
			if(UseMica)
				app.Resources["WindowBackgroundBrush"] = Brushes.Transparent;
			else
				app.Resources.Remove("WindowBackgroundBrush");

			foreach(Window w in app.Windows)
				ApplyWindow(w);

			Changed?.Invoke(null, EventArgs.Empty);
		}

		private static bool UseMica => Native.SupportsMica && !SystemParameters.HighContrast && ForceDark == null;

		/// <summary>Call from the window constructor.</summary>
		public static void Attach(Window window)
		{
			window.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
			window.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
			window.SourceInitialized += (s, e) => ApplyWindow(window);
		}

		private static void ApplyWindow(Window window)
		{
			var hwnd = new WindowInteropHelper(window).Handle;
			if(hwnd == IntPtr.Zero)
				return;

			int dark = IsDark && !SystemParameters.HighContrast ? 1 : 0;
			if(Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
				Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

			if(Native.IsWindows11)
			{
				int corner = Native.DWMWCP_ROUND;
				Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
			}

			var source = HwndSource.FromHwnd(hwnd);
			if(UseMica)
			{
				if(source?.CompositionTarget != null)
					source.CompositionTarget.BackgroundColor = Colors.Transparent;
				var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
				Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
				int backdrop = Native.DWMSBT_MAINWINDOW;
				Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
			}
			else if(Native.SupportsMica)
			{
				int backdrop = Native.DWMSBT_NONE;
				Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
			}
		}
	}
}
