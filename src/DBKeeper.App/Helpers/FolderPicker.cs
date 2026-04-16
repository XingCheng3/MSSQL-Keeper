using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DBKeeper.App.Helpers;

/// <summary>
/// WPF 原生文件夹选择器 — 通过 IFileOpenDialog COM 接口实现，无需引用 WinForms
/// </summary>
public static class FolderPicker
{
    public static string? Show(string title = "选择文件夹", Window? owner = null)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM);
        dialog.SetTitle(title);

        var hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
        var hr = dialog.Show(hwnd);
        if (hr != 0) return null; // 用户取消

        dialog.GetResult(out var item);
        item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
        return path;
    }

    #region COM Interop

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(); // not used
        void SetFileTypeIndex(); // not used
        void GetFileTypeIndex(); // not used
        void Advise(); // not used
        void Unadvise(); // not used
        void SetOptions(FOS fos);
        void GetOptions(); // not used
        void SetDefaultFolder(); // not used
        void SetFolder(); // not used
        void GetFolder(); // not used
        void GetCurrentSelection(); // not used
        void SetFileName(); // not used
        void GetFileName(); // not used
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel(); // not used
        void SetFileNameLabel(); // not used
        void GetResult(out IShellItem ppsi);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(); // not used
        void GetParent(); // not used
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    }

    [Flags]
    private enum FOS : uint
    {
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

    #endregion
}
