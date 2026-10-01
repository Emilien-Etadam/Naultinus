using System;
using System.Runtime.InteropServices;

namespace Naultinus.View
{
    /// <summary>
    /// Contrats COM de la vue dossier Windows (<c>IExplorerBrowser</c>).
    /// L'ordre des méthodes est celui de la vtable : ne pas le réarranger.
    /// </summary>
    internal static class ExplorerBrowserInterop
    {
        internal const int Ok = 0;
        internal const int NoInterface = unchecked((int)0x80004002);
        internal const int AccessDenied = unchecked((int)0x80070005);
        internal const int Fail = unchecked((int)0x80004005);

        /// <summary>SBSP_ABSOLUTE, sans historique de navigation du shell.</summary>
        internal const uint BrowseNoHistory = 0x00000080 | 0x00100000 | 0x08000000;

        internal const uint PaneForceOff = 0x00020002;
        internal const int ChildWindowStyle = 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000;
        internal const int ShowCommand = 5;
        internal const int HideCommand = 0;

        /// <summary>WS_POPUP | WS_CLIPCHILDREN | WS_CLIPSIBLINGS. Pas de WS_EX_LAYERED : un enfant HWND y serait invisible.</summary>
        internal const int PopupClipStyle = unchecked((int)0x80000000) | 0x02000000 | 0x04000000;

        /// <summary>WS_EX_TOOLWINDOW : hors de la barre des tâches et d'Alt+Tab.</summary>
        internal const int ToolWindowExtendedStyle = 0x00000080;

        internal const int OwnerWindowIndex = -8;
        internal const int WindowPosChanging = 0x0046;
        internal const int WindowPosChanged = 0x0047;
        internal const uint SwpNoZOrder = 0x0004;
        internal const uint SwpNoActivate = 0x0010;
        internal const uint SwpShowWindow = 0x0040;
        internal const uint SwpNoCopyBits = 0x0100;

        internal static readonly Guid PaneVisibilityId = new("E07010EC-BC17-44C0-97B0-46C7C95B9EDC");

        internal static readonly Guid NavPane = new("CB316B22-25F7-42B8-8A09-540D23A43C2F");
        internal static readonly Guid CommandsPane = new("D9745868-CA5F-4A76-91CD-F5A129FBB076");
        internal static readonly Guid CommandsOrganizePane = new("72E81700-E3EC-4660-BF24-3C3B7B648806");
        internal static readonly Guid CommandsViewPane = new("21F7C32D-EEAA-439B-BB51-37B96FD6A943");
        internal static readonly Guid DetailsPane = new("43ABF98B-89B8-472D-B9CE-E69B8229F019");
        internal static readonly Guid PreviewPane = new("893C63D1-45C8-4D17-BE19-223BE71BE365");
        internal static readonly Guid QueryPane = new("65BCDE4F-4F07-4F27-83A7-1AFCA4DF7DDD");
        internal static readonly Guid AdvancedQueryPane = new("B4E9DB8B-34BA-4C39-B5CC-16A1BD2C411C");
        internal static readonly Guid StatusBarPane = new("65FE56CE-5CFE-4BC4-AD8A-7AE3FE7E8F7C");
        internal static readonly Guid RibbonPane = new("D27524A8-C9F2-4834-A106-DF8889FD4F37");

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowPos
        {
            public IntPtr Hwnd;
            public IntPtr InsertAfter;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal sealed class FolderSettings
        {
#pragma warning disable SA1401 // Disposition binaire de FOLDERSETTINGS : le marshaller COM lit des champs, pas des propriétés.
            public FolderViewMode ViewMode;
            public int Options;
#pragma warning restore SA1401
        }

        internal enum FolderViewMode
        {
            Auto = -1,
        }

        [Flags]
        internal enum ExplorerBrowserOptions
        {
            NoTravelLog = 0x00000008,
            NoBorder = 0x00000040,
        }

        [ComImport]
        [Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB")]
        [ClassInterface(ClassInterfaceType.None)]
        internal class ExplorerBrowserClass
        {
        }

        [ComImport]
        [Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IExplorerBrowser
        {
            [PreserveSig]
            int Initialize(IntPtr parentWindow, [In] ref NativeRect bounds, [In] FolderSettings settings);

            [PreserveSig]
            int Destroy();

            [PreserveSig]
            int SetRect([In, Out] ref IntPtr deferWindowPos, NativeRect bounds);

            [PreserveSig]
            int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string propertyBag);

            [PreserveSig]
            int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string emptyText);

            [PreserveSig]
            int SetFolderSettings([In] FolderSettings settings);

            [PreserveSig]
            int Advise(IntPtr events, out uint cookie);

            [PreserveSig]
            int Unadvise(uint cookie);

            [PreserveSig]
            int SetOptions(ExplorerBrowserOptions options);

            [PreserveSig]
            int GetOptions(out ExplorerBrowserOptions options);

            [PreserveSig]
            int BrowseToIDList(IntPtr pidl, uint flags);

            [PreserveSig]
            int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object target, uint flags);

            [PreserveSig]
            int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object source, int flags);

            [PreserveSig]
            int RemoveAll();

            [PreserveSig]
            int GetCurrentView(ref Guid interfaceId, out IntPtr view);
        }

        [ComImport]
        [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid serviceId, ref Guid interfaceId, out IntPtr instance);
        }

        [ComImport]
        [Guid("E07010EC-BC17-44C0-97B0-46C7C95B9EDC")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IExplorerPaneVisibility
        {
            [PreserveSig]
            int GetPaneState(ref Guid paneId, out uint state);
        }

        [ComImport]
        [Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IExplorerBrowserEvents
        {
            [PreserveSig]
            int OnNavigationPending(IntPtr folderPidl);

            [PreserveSig]
            int OnViewCreated([MarshalAs(UnmanagedType.IUnknown)] object shellView);

            [PreserveSig]
            int OnNavigationComplete(IntPtr folderPidl);

            [PreserveSig]
            int OnNavigationFailed(IntPtr folderPidl);
        }

        [ComImport]
        [Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IObjectWithSite
        {
            [PreserveSig]
            int SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);

            [PreserveSig]
            int GetSite(ref Guid interfaceId, out IntPtr instance);
        }

        [ComImport]
        [Guid("000214E3-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellView
        {
            [PreserveSig]
            int GetWindow(out IntPtr window);

            [PreserveSig]
            int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);

            [PreserveSig]
            int TranslateAccelerator(IntPtr message);

            [PreserveSig]
            int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);

            [PreserveSig]
            int UIActivate(uint state);

            [PreserveSig]
            int Refresh();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowExW(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        internal static void SetOwner(IntPtr window, IntPtr owner)
        {
            if (IntPtr.Size == 8)
                _ = SetWindowLongPtrW(window, OwnerWindowIndex, owner);
            else
                _ = SetWindowLongW(window, OwnerWindowIndex, owner.ToInt32());
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandleW(string? moduleName);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLongW(IntPtr window, int index, int newValue);
    }
}
