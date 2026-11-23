using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SyncTwoFiles.App;

internal static class Native
{
    [DllImport("shell32.dll")]
    public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string id);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    private static bool? _packaged;

    // APPMODEL_ERROR_NO_PACKAGE (15700) = la app no corre dentro de un paquete MSIX.
    public static bool IsPackaged
    {
        get
        {
            if (_packaged is null)
            {
                try
                {
                    var len = 0;
                    _packaged = GetCurrentPackageFullName(ref len, null) != 15700;
                }
                catch { _packaged = false; }
            }
            return _packaged.Value;
        }
    }

    public static void ApplyDarkFrame(IntPtr hwnd)
    {
        var one = 1; DwmSetWindowAttribute(hwnd, 20, ref one, 4);
        var round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        var border = 0x003A3A3A; DwmSetWindowAttribute(hwnd, 34, ref border, 4);
    }
}

internal static class FolderPicker
{
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr hwndOwner);
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
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

    public static string? Pick(IntPtr owner, string title, string? initialFolder)
    {
        var dlg = (IFileOpenDialog)new FileOpenDialogRCW();
        dlg.GetOptions(out var opts);
        dlg.SetOptions(opts | 0x20 | 0x40 | 0x800); // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
        if (!string.IsNullOrEmpty(title)) dlg.SetTitle(title);
        if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
        {
            try
            {
                var g = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref g, out var item);
                dlg.SetFolder(item);
            }
            catch { }
        }
        if (dlg.Show(owner) != 0) return null;
        dlg.GetResult(out var result);
        result.GetDisplayName(0x80058000, out var p); // SIGDN_FILESYSPATH
        var path = Marshal.PtrToStringUni(p);
        Marshal.FreeCoTaskMem(p);
        return path;
    }
}
