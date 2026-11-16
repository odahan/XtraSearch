using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace XtraSearch;

internal static class ShellContextMenu
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7fff;
    private const uint RevealInExplorerCommand = 0x8000;
    private const uint MenuByPosition = 0x0400;
    private const uint ExtendedVerbs = 0x00000100;
    private const uint ReturnCommand = 0x0100;
    private const uint RightButton = 0x0002;
    private const uint Unicode = 0x00004000;
    private const uint ShiftDown = 0x10000000;
    private const uint PointInvoke = 0x20000000;
    private const uint ControlDown = 0x40000000;

    /// <summary>Shows the native file menu and handles the application command through the supplied callback.</summary>
    public static void Show(Window owner, string path, Point screenPosition, ModifierKeys modifiers, Action<string> revealInExplorer)
    {
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            throw new IOException("The search result is no longer a file.");

        var hwnd = new WindowInteropHelper(owner).Handle;
        var source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("The window handle is unavailable.");
        var folderId = typeof(IShellFolder).GUID;
        var menuId = typeof(IContextMenu).GUID;
        IntPtr itemId = IntPtr.Zero;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        IntPtr menu = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out itemId, 0, out _));
            Marshal.ThrowExceptionForHR(SHBindToParent(itemId, ref folderId, out folder, out var childId));
            Marshal.ThrowExceptionForHR(folder.GetUIObjectOf(hwnd, 1, [childId], ref menuId, IntPtr.Zero, out contextMenu));
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

            var queryFlags = modifiers.HasFlag(ModifierKeys.Shift) ? ExtendedVerbs : 0;
            Marshal.ThrowExceptionForHR(contextMenu.QueryContextMenu(menu, 0, FirstCommand, LastCommand, queryFlags));
            if (!InsertMenuW(menu, 0, MenuByPosition, RevealInExplorerCommand, "Show in File Explorer"))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var menu3 = contextMenu as IContextMenu3;
            var menu2 = menu3 == null ? contextMenu as IContextMenu2 : null;
            IntPtr MenuMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message != 0x0117 && message != 0x0120 &&
                    !((message == 0x002b || message == 0x002c) && wParam == IntPtr.Zero))
                    return IntPtr.Zero;

                if (menu3 != null && menu3.HandleMenuMsg2((uint)message, wParam, lParam, out var result) == 0)
                {
                    handled = true;
                    return result;
                }
                if (message != 0x0120 && menu2 != null && menu2.HandleMenuMsg((uint)message, wParam, lParam) == 0)
                    handled = true;
                return IntPtr.Zero;
            }

            HwndSourceHook hook = MenuMessage;
            uint selected;
            source.AddHook(hook);
            try
            {
                selected = TrackPopupMenuEx(menu, ReturnCommand | RightButton,
                    (int)screenPosition.X, (int)screenPosition.Y, hwnd, IntPtr.Zero);
            }
            finally { source.RemoveHook(hook); }

            if (selected == RevealInExplorerCommand)
            {
                revealInExplorer(path);
            }
            else if (selected is >= FirstCommand and <= LastCommand)
            {
                var invocationModifiers = Keyboard.Modifiers;
                var command = new InvokeCommandInfo
                {
                    Size = (uint)Marshal.SizeOf<InvokeCommandInfo>(),
                    Mask = Unicode | PointInvoke |
                        (invocationModifiers.HasFlag(ModifierKeys.Shift) ? ShiftDown : 0) |
                        (invocationModifiers.HasFlag(ModifierKeys.Control) ? ControlDown : 0),
                    Window = hwnd,
                    Verb = (IntPtr)(selected - FirstCommand),
                    VerbW = (IntPtr)(selected - FirstCommand),
                    Show = 1,
                    Point = new NativePoint { X = (int)screenPosition.X, Y = (int)screenPosition.Y }
                };
                Marshal.ThrowExceptionForHR(contextMenu.InvokeCommand(ref command));
            }
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
            if (folder != null) Marshal.ReleaseComObject(folder);
            if (itemId != IntPtr.Zero) Marshal.FreeCoTaskMem(itemId);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeCommandInfo
    {
        public uint Size;
        public uint Mask;
        public IntPtr Window;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
        public IntPtr Title;
        public IntPtr VerbW;
        public IntPtr ParametersW;
        public IntPtr DirectoryW;
        public IntPtr TitleW;
        public NativePoint Point;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName();
        void EnumObjects();
        void BindToObject();
        void BindToStorage();
        void CompareIDs();
        void CreateViewObject();
        void GetAttributesOf();
        [PreserveSig]
        int GetUIObjectOf(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] items,
            ref Guid interfaceId, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out IContextMenu contextMenu);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo info);
        void GetCommandString();
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        void QueryContextMenu();
        void InvokeCommand();
        void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        void QueryContextMenu();
        void InvokeCommand();
        void GetCommandString();
        void HandleMenuMsg();
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr itemId, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHBindToParent(IntPtr itemId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder, out IntPtr childId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenuW(IntPtr menu, uint position, uint flags, nuint command, string text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);
}
