using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DAW.Services;

/// <summary>
/// WPF-friendly folder picker using the modern IFileDialog COM interface.
/// No dependency on Windows Forms.
/// </summary>
public static class FolderPicker
{
    public static string? ShowDialog(string? title = null, string? initialDirectory = null)
    {
        var dialog = (IFileDialog)new FileOpenDialogRCW();
        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

        if (!string.IsNullOrEmpty(title))
            dialog.SetTitle(title);

        if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
        {
            var riid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"); // IShellItem
            SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref riid, out var folder);
            if (folder != null)
                dialog.SetFolder(folder);
        }

        var hwnd = Application.Current?.MainWindow is Window w
            ? new WindowInteropHelper(w).Handle
            : IntPtr.Zero;

        var hr = dialog.Show(hwnd);
        if (hr != 0) return null; // cancelled or error

        dialog.GetResult(out var item);
        item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
        return path;
    }

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr hwnd);
        void SetFileTypes();
        void SetFileTypeIndex();
        void GetFileTypeIndex();
        void Advise();
        void Unadvise();
        void SetOptions(uint fos);
        void GetOptions(out uint fos);
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
        void AddPlace(IShellItem psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes();
        void Compare();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
