using System;
using System.Runtime.InteropServices;

namespace ContextShell.UI
{
	internal static class Native
	{
		[StructLayout(LayoutKind.Sequential)]
		public struct MARGINS
		{
			public int Left, Right, Top, Bottom;
		}

		public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
		public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
		public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
		public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

		public const int DWMSBT_NONE = 1;
		public const int DWMSBT_MAINWINDOW = 2; // Mica
		public const int DWMWCP_ROUND = 2;

		[DllImport("dwmapi.dll")]
		public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

		[DllImport("dwmapi.dll")]
		public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

		[DllImport("ntdll.dll")]
		private static extern int RtlGetVersion(ref OSVERSIONINFOEX info);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct OSVERSIONINFOEX
		{
			public int dwOSVersionInfoSize;
			public int dwMajorVersion;
			public int dwMinorVersion;
			public int dwBuildNumber;
			public int dwPlatformId;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string szCSDVersion;
			public ushort wServicePackMajor, wServicePackMinor, wSuiteMask;
			public byte wProductType, wReserved;
		}

		private static int? _build;

		/// <summary>Real Windows build number (Environment.OSVersion lies without a manifest entry).</summary>
		public static int WindowsBuild
		{
			get
			{
				if(_build == null)
				{
					var info = new OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX)) };
					_build = RtlGetVersion(ref info) == 0 ? info.dwBuildNumber : Environment.OSVersion.Version.Build;
				}
				return _build.Value;
			}
		}

		public static bool IsWindows11 => WindowsBuild >= 22000;

		/// <summary>Mica via DWMWA_SYSTEMBACKDROP_TYPE is available from Windows 11 22H2.</summary>
		public static bool SupportsMica => WindowsBuild >= 22621;
	}
}
