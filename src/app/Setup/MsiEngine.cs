using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextShell.Setup
{
	public enum MsiOperation
	{
		Install,
		Upgrade,
		Repair,
		Uninstall,
	}

	/// <summary>A message Windows Installer wants shown to the user (error, warning, question).</summary>
	public sealed class MsiPrompt
	{
		public string Text;
		public bool IsError;
		public int Buttons; // MB_OK, MB_OKCANCEL, ... (low 4 bits of the message type)
	}

	/// <summary>
	/// Runs Windows Installer with our own UI (MsiSetExternalUIRecord) instead of the MSI's
	/// built-in dialogs. Install behavior is exactly the MSI's; only presentation changes.
	/// </summary>
	public sealed class MsiEngine
	{
		public const int ERROR_SUCCESS = 0;
		public const int ERROR_INSTALL_USEREXIT = 1602;
		public const int ERROR_INSTALL_FAILURE = 1603;
		public const int ERROR_SUCCESS_REBOOT_REQUIRED = 3010;
		public const int ERROR_SUCCESS_REBOOT_INITIATED = 1641;

		/// <summary>Progress 0..1 and a short status line. Called on the installer thread.</summary>
		public event Action<double, string> Progress;

		/// <summary>Ask the user; return IDOK/IDCANCEL/... Called on the installer thread.</summary>
		public Func<MsiPrompt, int> Prompt;

		public bool Quiet { get; set; }
		public string LogPath { get; set; }

		public volatile bool CancelRequested;
		public bool CanCancel { get; private set; } = true;

		// Keep the delegate alive while msi.dll holds a pointer to it.
		private InstallUIHandlerRecord _handler;

		private int _resets;
		private long _total, _done;
		private bool _forward = true;
		private double _reported;
		private string _status = "";

		/// <param name="package">Path of the extracted MSI (install, upgrade, repair).</param>
		/// <param name="productCode">Installed product code (repair, uninstall).</param>
		public int Run(MsiOperation op, string package, string productCode, string properties)
		{
			// No Windows Installer UI at all. In particular no "locate the package" prompt, which
			// would be invisible and hang: repairs always get the package from us instead.
			IntPtr none = IntPtr.Zero;
			MsiSetInternalUI(INSTALLUILEVEL_NONE, ref none);

			if(!string.IsNullOrEmpty(LogPath))
				MsiEnableLog(INSTALLLOGMODE_VERBOSE_ALL, LogPath, 0);

			_handler = Handler;
			MsiSetExternalUIRecord(_handler, MessageFilter, IntPtr.Zero, IntPtr.Zero);

			try
			{
				switch(op)
				{
					case MsiOperation.Install:
					case MsiOperation.Upgrade:
						return MsiInstallProduct(package, properties ?? "");
					case MsiOperation.Repair:
						// Windows caches the MSI without its embedded files, so a repair needs the
						// original package. Same build: reinstall from it ("v" re-caches it).
						// Same version, different build (different ProductCode): install over it,
						// which the MSI's upgrade rules turn into a clean replacement.
						if(string.Equals(PackageProductCode(package), productCode, StringComparison.OrdinalIgnoreCase))
							return MsiInstallProduct(package, ("REINSTALL=ALL REINSTALLMODE=vomus " + properties).Trim());
						return MsiInstallProduct(package, properties ?? "");
					case MsiOperation.Uninstall:
						return MsiConfigureProductEx(productCode, INSTALLLEVEL_DEFAULT, INSTALLSTATE_ABSENT, properties ?? "");
				}
				return ERROR_INSTALL_FAILURE;
			}
			finally
			{
				MsiSetExternalUIRecord(null, 0, IntPtr.Zero, IntPtr.Zero);
			}
		}

		private int Handler(IntPtr context, uint messageType, uint record)
		{
			uint type = messageType & 0xFF000000;
			try
			{
				switch(type)
				{
					case INSTALLMESSAGE_PROGRESS:
						OnProgress(record);
						return CancelRequested && CanCancel ? IDCANCEL : IDOK;

					case INSTALLMESSAGE_ACTIONSTART:
						OnActionStart(record);
						return CancelRequested && CanCancel ? IDCANCEL : IDOK;

					case INSTALLMESSAGE_FATALEXIT:
					case INSTALLMESSAGE_ERROR:
					case INSTALLMESSAGE_WARNING:
					case INSTALLMESSAGE_USER:
					case INSTALLMESSAGE_OUTOFDISKSPACE:
					{
						if(Quiet || Prompt == null || record == 0)
							return 0; // let Windows Installer apply its default, as with msiexec /qn
						var text = FormatRecord(record);
						if(string.IsNullOrWhiteSpace(text))
							return 0;
						return Prompt(new MsiPrompt
						{
							Text = text.Trim(),
							IsError = type != INSTALLMESSAGE_USER,
							Buttons = (int)(messageType & 0x0F),
						});
					}

					case INSTALLMESSAGE_FILESINUSE:
						// shell.dll is loaded by Explorer; the MSI's own custom action moves it aside,
						// so carry on rather than blocking on a files-in-use dialog.
						return IDIGNORE;

					case INSTALLMESSAGE_RMFILESINUSE:
						return IDOK;
				}
			}
			catch
			{
			}
			return 0;
		}

		private void OnActionStart(uint record)
		{
			var action = GetString(record, 1);
			var description = GetString(record, 2);

			if(action == "InstallFinalize" || action == "OnInstall" || action == "OnUninstall")
				CanCancel = false; // past the point where Windows Installer can roll back cleanly

			string status;
			if(!ActionText.TryGetValue(action ?? "", out status))
				status = string.IsNullOrWhiteSpace(description) ? null : description.TrimEnd('.') + "…";
			if(status != null)
			{
				_status = status;
				Progress?.Invoke(_reported, _status);
			}
		}

		private void OnProgress(uint record)
		{
			int field1 = MsiRecordGetInteger(record, 1);
			int field2 = MsiRecordGetInteger(record, 2);
			switch(field1)
			{
				case 0: // reset: field2 = total ticks, field3 = direction
					_resets++;
					_total = Math.Max(field2, 0);
					_forward = MsiRecordGetInteger(record, 3) == 0;
					_done = _forward ? 0 : _total;
					break;
				case 2: // report ticks
					if(_total > 0)
						_done += _forward ? field2 : -field2;
					break;
				case 3: // add to total
					_total += Math.Max(field2, 0);
					break;
				default:
					return;
			}

			double phase = _total > 0 ? Math.Max(0, Math.Min(1, (double)_done / _total)) : 0;
			// First reset is script generation (quick), the second is execution (the real work).
			double overall = _resets <= 1 ? phase * 0.15 : 0.15 + phase * 0.85;
			if(overall > _reported)
			{
				_reported = overall;
				Progress?.Invoke(_reported, _status);
			}
		}

		private static readonly Dictionary<string, string> ActionText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["CostInitialize"] = "Preparing…",
			["FileCost"] = "Preparing…",
			["CostFinalize"] = "Preparing…",
			["InstallValidate"] = "Checking your system…",
			["OnUpdate"] = "Preparing the update…",
			["InstallInitialize"] = "Starting…",
			["RemoveExistingProducts"] = "Removing the previous version…",
			["RemoveFiles"] = "Removing files…",
			["RemoveRegistryValues"] = "Cleaning up the registry…",
			["RemoveShortcuts"] = "Removing shortcuts…",
			["InstallFiles"] = "Copying files…",
			["CreateShortcuts"] = "Creating shortcuts…",
			["WriteRegistryValues"] = "Writing settings…",
			["RegisterProduct"] = "Registering ContextShell…",
			["PublishProduct"] = "Registering ContextShell…",
			["InstallFinalize"] = "Finishing…",
			["OnInstall"] = "Adding ContextShell to File Explorer…",
			["OnUninstall"] = "Removing ContextShell from File Explorer…",
			["Rollback"] = "Rolling back changes…",
		};

		private static string GetString(uint record, int field)
		{
			uint len = 0;
			var sb = new StringBuilder("");
			if(MsiRecordGetString(record, field, sb, ref len) == ERROR_MORE_DATA)
			{
				sb = new StringBuilder((int)++len);
				if(MsiRecordGetString(record, field, sb, ref len) == 0)
					return sb.ToString();
			}
			return sb.ToString();
		}

		private static string FormatRecord(uint record)
		{
			uint len = 0;
			var sb = new StringBuilder("");
			if(MsiFormatRecord(0, record, sb, ref len) == ERROR_MORE_DATA)
			{
				sb = new StringBuilder((int)++len);
				MsiFormatRecord(0, record, sb, ref len);
			}
			return sb.ToString();
		}

		// Installed product discovery

		public static List<string> RelatedProducts(string upgradeCode)
		{
			var list = new List<string>();
			var sb = new StringBuilder(39);
			for(int i = 0; MsiEnumRelatedProducts(upgradeCode, 0, i, sb) == 0; i++)
				list.Add(sb.ToString());
			return list;
		}

		public static string ProductInfo(string productCode, string property)
		{
			uint len = 0;
			var sb = new StringBuilder("");
			int rc = MsiGetProductInfo(productCode, property, sb, ref len);
			if(rc == ERROR_MORE_DATA || (rc == 0 && len > 0))
			{
				sb = new StringBuilder((int)++len);
				if(MsiGetProductInfo(productCode, property, sb, ref len) == 0)
					return sb.ToString();
			}
			return null;
		}

		/// <summary>Short, human explanation for a Windows Installer result code.</summary>
		public static string Describe(int code)
		{
			switch(code)
			{
				case ERROR_SUCCESS: return "Completed successfully.";
				case ERROR_INSTALL_USEREXIT: return "Setup was cancelled. No changes were made.";
				case 1618: return "Another installation is already running. Wait for it to finish, then try again.";
				case 1633: return "This installer doesn't support your version of Windows. Download the installer for your PC's architecture (x64, ARM64 or x86).";
				case 1638: return "Another version of ContextShell is already installed. Uninstall it from Settings > Apps, then try again.";
				case 1605: return "ContextShell isn't installed.";
				case 1612:
				case 1620: return "Windows Installer couldn't open the installation package. Download the installer again.";
				case 1625: return "A system policy prevents this installation. Ask your administrator.";
				case 1925: return "You need administrator rights to install ContextShell.";
				case 112: return "There isn't enough disk space on the selected drive.";
				case ERROR_INSTALL_FAILURE:
				default:
					return "Windows Installer reported an error and rolled back the changes. The setup log has the details.";
			}
		}

		// P/Invoke

		private delegate int InstallUIHandlerRecord(IntPtr context, uint messageType, uint record);

		private const int INSTALLUILEVEL_NONE = 2;
		private const int INSTALLLEVEL_DEFAULT = 0;
		private const int INSTALLSTATE_ABSENT = 2;
		private const int INSTALLSTATE_DEFAULT = 5;
		private const int ERROR_MORE_DATA = 234;

		private const uint INSTALLMESSAGE_FATALEXIT = 0x00000000;
		private const uint INSTALLMESSAGE_ERROR = 0x01000000;
		private const uint INSTALLMESSAGE_WARNING = 0x02000000;
		private const uint INSTALLMESSAGE_USER = 0x03000000;
		private const uint INSTALLMESSAGE_FILESINUSE = 0x05000000;
		private const uint INSTALLMESSAGE_OUTOFDISKSPACE = 0x07000000;
		private const uint INSTALLMESSAGE_ACTIONSTART = 0x08000000;
		private const uint INSTALLMESSAGE_PROGRESS = 0x0A000000;
		private const uint INSTALLMESSAGE_RMFILESINUSE = 0x19000000;

		private const uint MessageFilter =
			0x1 /*FATALEXIT*/ | 0x2 /*ERROR*/ | 0x4 /*WARNING*/ | 0x8 /*USER*/ | 0x20 /*FILESINUSE*/ |
			0x80 /*OUTOFDISKSPACE*/ | 0x100 /*ACTIONSTART*/ | 0x400 /*PROGRESS*/ | 0x2000000 /*RMFILESINUSE*/;

		// voicewarmupx: everything, verbose, including extra debug info.
		private const uint INSTALLLOGMODE_VERBOSE_ALL = 0x1FDF | 0x2000 /*EXTRADEBUG*/;

		public const int IDOK = 1, IDCANCEL = 2, IDABORT = 3, IDRETRY = 4, IDIGNORE = 5, IDYES = 6, IDNO = 7;

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiSetInternalUI(int level, ref IntPtr window);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiSetExternalUIRecord(InstallUIHandlerRecord handler, uint filter, IntPtr context, IntPtr previous);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiEnableLog(uint mode, string logFile, uint attributes);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiInstallProduct(string packagePath, string commandLine);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiConfigureProductEx(string product, int installLevel, int installState, string commandLine);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, StringBuilder productBuf);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiGetProductInfo(string product, string property, StringBuilder value, ref uint len);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiRecordGetInteger(uint record, int field);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiRecordGetString(uint record, int field, StringBuilder value, ref uint len);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiFormatRecord(uint install, uint record, StringBuilder result, ref uint len);

		/// <summary>ProductCode stored in an MSI's Property table.</summary>
		public static string PackageProductCode(string msiPath)
		{
			uint db = 0, view = 0, rec = 0;
			try
			{
				if(MsiOpenDatabase(msiPath, IntPtr.Zero, out db) != 0)
					return null;
				if(MsiDatabaseOpenView(db, "SELECT `Value` FROM `Property` WHERE `Property`='ProductCode'", out view) != 0
				   || MsiViewExecute(view, 0) != 0 || MsiViewFetch(view, out rec) != 0)
					return null;
				return GetString(rec, 1);
			}
			finally
			{
				if(rec != 0) MsiCloseHandle(rec);
				if(view != 0) MsiCloseHandle(view);
				if(db != 0) MsiCloseHandle(db);
			}
		}

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiOpenDatabase(string path, IntPtr persist, out uint handle);

		[DllImport("msi.dll", CharSet = CharSet.Unicode)]
		private static extern int MsiDatabaseOpenView(uint database, string query, out uint view);

		[DllImport("msi.dll")]
		private static extern int MsiViewExecute(uint view, uint record);

		[DllImport("msi.dll")]
		private static extern int MsiViewFetch(uint view, out uint record);

		[DllImport("msi.dll")]
		private static extern int MsiCloseHandle(uint handle);

		/// <summary>Write the embedded MSI to a temp folder. Returns its path, or null if not embedded.</summary>
		public static string ExtractPackage(string targetDir = null)
		{
			using(var stream = typeof(MsiEngine).Assembly.GetManifestResourceStream("ContextShell.msi"))
			{
				if(stream == null)
					return null;
				var dir = targetDir ?? Path.Combine(Path.GetTempPath(), "ContextShell-Setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
				Directory.CreateDirectory(dir);
				var path = Path.Combine(dir, "ContextShell.msi");
				using(var file = File.Create(path))
					stream.CopyTo(file);
				return path;
			}
		}
	}
}
