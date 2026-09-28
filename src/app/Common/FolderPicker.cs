using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ContextShell.UI
{
	/// <summary>The modern Windows folder picker (IFileOpenDialog with FOS_PICKFOLDERS).</summary>
	public static class FolderPicker
	{
		public static string Show(Window owner, string title, string initialFolder)
		{
			var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
			try
			{
				dialog.GetOptions(out var options);
				dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
				dialog.SetTitle(title);

				if(!string.IsNullOrEmpty(initialFolder))
				{
					var folder = initialFolder;
					while(!string.IsNullOrEmpty(folder) && !System.IO.Directory.Exists(folder))
						folder = System.IO.Path.GetDirectoryName(folder);
					if(!string.IsNullOrEmpty(folder)
					   && SHCreateItemFromParsingName(folder, IntPtr.Zero, typeof(IShellItem).GUID, out var item) == 0)
						dialog.SetFolder(item);
				}

				var hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
				if(dialog.Show(hwnd) != 0)
					return null;

				dialog.GetResult(out var result);
				result.GetDisplayName(SIGDN_FILESYSPATH, out var path);
				return path;
			}
			catch
			{
				return null;
			}
			finally
			{
				Marshal.ReleaseComObject(dialog);
			}
		}

		private const uint FOS_PICKFOLDERS = 0x20;
		private const uint FOS_FORCEFILESYSTEM = 0x40;
		private const uint FOS_PATHMUSTEXIST = 0x800;
		private const uint SIGDN_FILESYSPATH = 0x80058000;

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
		private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

		[ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
		private class FileOpenDialogRCW
		{
		}

		[ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFileOpenDialog
		{
			[PreserveSig] int Show(IntPtr parent);
			void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
			void SetFileTypeIndex(uint iFileType);
			void GetFileTypeIndex(out uint piFileType);
			void Advise(IntPtr pfde, out uint pdwCookie);
			void Unadvise(uint dwCookie);
			void SetOptions(uint fos);
			void GetOptions(out uint pfos);
			void SetDefaultFolder(IShellItem psi);
			void SetFolder(IShellItem psi);
			void GetFolder(out IShellItem ppsi);
			void GetCurrentSelection(out IShellItem ppsi);
			void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
			void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
			void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
			void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
			void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
			void GetResult(out IShellItem ppsi);
			void AddPlace(IShellItem psi, int alignment);
			void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
			void Close(int hr);
			void SetClientGuid(ref Guid guid);
			void ClearClientData();
			void SetFilter(IntPtr pFilter);
			void GetResults(out IntPtr ppenum);
			void GetSelectedItems(out IntPtr ppsai);
		}

		[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItem
		{
			void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
			void GetParent(out IShellItem ppsi);
			void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
			void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
			void Compare(IShellItem psi, uint hint, out int piOrder);
		}
	}
}
