using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Naultinus.Helpers
{
    public class ShellContextMenu
    {
        #region COM Interfaces

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F4-0000-0000-C000-000000000046")]
        private interface IContextMenu2
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCFCE0A0-EC17-11d0-8D10-00A0C90F2719")]
        private interface IContextMenu3
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

            [PreserveSig]
            int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

            [PreserveSig]
            int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr result);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

            [PreserveSig]
            int EnumObjects(IntPtr hwndOwner, uint grfFlags, out IntPtr ppenumIDList);

            [PreserveSig]
            int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

            [PreserveSig]
            int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);

            [PreserveSig]
            int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

            [PreserveSig]
            int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);

            [PreserveSig]
            int SetNameOf(IntPtr hwndOwner, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
        }

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CMINVOKECOMMANDINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            [MarshalAs(UnmanagedType.LPStr)]
            public string? lpParameters;
            [MarshalAs(UnmanagedType.LPStr)]
            public string? lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        #region P/Invoke

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        #endregion

        #region Constants

        private const uint CMF_NORMAL = 0x00000000;
        private const uint CMF_EXPLORE = 0x00000004;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_LEFTALIGN = 0x0000;
        private const int SW_SHOWNORMAL = 1;
        private const uint WM_INITMENUPOPUP = 0x0117;
        private const uint WM_DRAWITEM = 0x002B;
        private const uint WM_MEASUREITEM = 0x002C;
        private const uint WM_MENUCHAR = 0x0120;

        private const uint CMD_FIRST = 1;
        private const uint CMD_LAST = 30000;

        #endregion

        [ThreadStatic]
        private static IContextMenu2? _contextMenu2;
        [ThreadStatic]
        private static IContextMenu3? _contextMenu3;

        public static void Show(string path, Window owner)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return;

            string? parentPath = Path.GetDirectoryName(path);
            if (parentPath == null) return;

            string childName = Path.GetFileName(path);

            if (SHGetDesktopFolder(out IShellFolder desktopFolder) != 0)
                return;

            IntPtr parentPidl = IntPtr.Zero;
            IntPtr childPidl = IntPtr.Zero;
            IntPtr hMenu = IntPtr.Zero;
            IShellFolder? parentFolder = null;
            IContextMenu? contextMenu = null;
            HwndSource? hwndSource = null;

            try
            {
                // Get parent folder's PIDL
                uint eaten = 0;
                uint attrs = 0;
                if (desktopFolder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, parentPath, out eaten, out parentPidl, ref attrs) != 0)
                    return;

                // Bind to parent folder
                Guid iidShellFolder = typeof(IShellFolder).GUID;
                if (desktopFolder.BindToObject(parentPidl, IntPtr.Zero, ref iidShellFolder, out IntPtr parentFolderPtr) != 0)
                    return;
                parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentFolderPtr);
                Marshal.Release(parentFolderPtr);

                // Get child PIDL
                eaten = 0;
                attrs = 0;
                if (parentFolder.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, childName, out eaten, out childPidl, ref attrs) != 0)
                    return;

                // Get IContextMenu
                Guid iidContextMenu = typeof(IContextMenu).GUID;
                IntPtr[] pidls = new IntPtr[] { childPidl };
                if (parentFolder.GetUIObjectOf(IntPtr.Zero, 1, pidls, ref iidContextMenu, IntPtr.Zero, out IntPtr contextMenuPtr) != 0)
                    return;
                contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
                Marshal.Release(contextMenuPtr);

                // Try to get IContextMenu2/3 for owner-drawn menu items
                _contextMenu3 = contextMenu as IContextMenu3;
                _contextMenu2 = contextMenu as IContextMenu2;

                // Create and populate popup menu
                hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return;

                contextMenu.QueryContextMenu(hMenu, 0, CMD_FIRST, CMD_LAST, CMF_NORMAL | CMF_EXPLORE);

                // Hook window proc for owner-drawn items
                IntPtr hwnd = new WindowInteropHelper(owner).Handle;
                hwndSource = HwndSource.FromHwnd(hwnd);
                hwndSource?.AddHook(WndProc);

                // Show menu at cursor position
                GetCursorPos(out POINT pt);
                uint cmd = (uint)TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN, pt.X, pt.Y, hwnd, IntPtr.Zero);

                if (cmd >= CMD_FIRST)
                {
                    CMINVOKECOMMANDINFO invoke = new()
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                        fMask = 0,
                        hwnd = hwnd,
                        lpVerb = (IntPtr)(cmd - CMD_FIRST),
                        lpParameters = null,
                        lpDirectory = null,
                        nShow = SW_SHOWNORMAL,
                        dwHotKey = 0,
                        hIcon = IntPtr.Zero
                    };
                    contextMenu.InvokeCommand(ref invoke);
                }
            }
            catch
            {
                // Silently handle errors - context menu is best-effort
            }
            finally
            {
                if (hwndSource != null)
                    hwndSource.RemoveHook(WndProc);
                _contextMenu2 = null;
                _contextMenu3 = null;

                if (hMenu != IntPtr.Zero)
                    DestroyMenu(hMenu);
                if (childPidl != IntPtr.Zero)
                    CoTaskMemFree(childPidl);
                if (parentPidl != IntPtr.Zero)
                    CoTaskMemFree(parentPidl);

                if (contextMenu != null)
                    Marshal.FinalReleaseComObject(contextMenu);
                if (parentFolder != null)
                    Marshal.FinalReleaseComObject(parentFolder);
                Marshal.FinalReleaseComObject(desktopFolder);
            }
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_contextMenu3 != null)
            {
                if (msg == (int)WM_MENUCHAR)
                {
                    if (_contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out IntPtr result) == 0)
                    {
                        handled = true;
                        return result;
                    }
                }

                if (msg == (int)WM_DRAWITEM || msg == (int)WM_MEASUREITEM || msg == (int)WM_INITMENUPOPUP)
                {
                    if (_contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out _) == 0)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                }
            }
            else if (_contextMenu2 != null)
            {
                if (msg == (int)WM_DRAWITEM || msg == (int)WM_MEASUREITEM || msg == (int)WM_INITMENUPOPUP)
                {
                    if (_contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                }
            }

            return IntPtr.Zero;
        }
    }
}
