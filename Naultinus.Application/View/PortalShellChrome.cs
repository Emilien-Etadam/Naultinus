using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Naultinus.Helpers;

namespace Naultinus.View
{
    /// <summary>
    /// Couleurs de la vue dossier : le shell peint en blanc système, le portail suit le thème Naultinus.
    /// </summary>
    internal static class PortalShellChrome
    {
        private const int ListViewSetBackground = 0x1001;
        private const int ListViewSetTextColor = 0x1024;
        private const int ListViewSetTextBackground = 0x1026;
        private const uint RedrawInvalidate = 0x0001;
        private const uint RedrawErase = 0x0004;
        private const uint RedrawAllChildren = 0x0080;
        private static readonly object Sync = new();
        private static bool _darkModePrepared;

        internal static event Action? Changed;

        static PortalShellChrome()
        {
            try
            {
                SystemEvents.UserPreferenceChanged += (_, e) =>
                {
                    if (e.Category != UserPreferenceCategory.General)
                        return;

                    Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
                };
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("PortalShellChrome.Theme", ex);
            }
        }

        internal static int ColorRef(byte red, byte green, byte blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        internal static bool TryGetTheme(out int background, out int foreground)
        {
            background = ColorRef(0x21, 0x20, 0x20);
            foreground = ColorRef(0xD8, 0xD5, 0xD0);
            if (Application.Current == null)
                return true;

            if (Application.Current.TryFindResource("NaultinusControlBrush") is SolidColorBrush surface)
                background = ColorRef(surface.Color.R, surface.Color.G, surface.Color.B);
            if (Application.Current.TryFindResource("NaultinusTextBrush") is SolidColorBrush text)
                foreground = ColorRef(text.Color.R, text.Color.G, text.Color.B);
            return true;
        }

        internal static bool IsDark(int colorRef)
        {
            int red = colorRef & 0xFF;
            int green = (colorRef >> 8) & 0xFF;
            int blue = (colorRef >> 16) & 0xFF;
            return ((red * 299) + (green * 587) + (blue * 114)) / 1000 < 128;
        }

        internal static IntPtr CreateFill(int colorRef)
        {
            return CreateSolidBrush(colorRef);
        }

        internal static void DeleteFill(IntPtr brush)
        {
            if (brush != IntPtr.Zero)
                DeleteObject(brush);
        }

        internal static void Fill(IntPtr deviceContext, IntPtr window, IntPtr brush)
        {
            if (brush == IntPtr.Zero || deviceContext == IntPtr.Zero)
                return;
            if (!ExplorerBrowserInterop.GetClientRect(window, out ExplorerBrowserInterop.NativeRect rect))
                return;

            _ = FillRect(deviceContext, ref rect, brush);
        }

        internal static void Apply(IntPtr hostWindow, object? shellView)
        {
            if (hostWindow == IntPtr.Zero || !OperatingSystem.IsWindows())
                return;
            if (!TryGetTheme(out int background, out int foreground))
                return;

            PrepareDarkMode();
            bool dark = IsDark(background);
            string theme = dark ? "DarkMode_Explorer" : "Explorer";
            AllowDarkModeForWindow(hostWindow, dark);
            _ = SetWindowTheme(hostWindow, theme, null);

            if (shellView != null)
            {
                try
                {
                    ExplorerBrowserInterop.KeepHeaderOnlyInDetails(shellView);
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("PortalShellChrome.Header", ex);
                }

                try
                {
                    if (shellView is ExplorerBrowserInterop.IShellView view && view.GetWindow(out IntPtr viewWindow) == ExplorerBrowserInterop.Ok)
                    {
                        AllowDarkModeForWindow(viewWindow, dark);
                        _ = SetWindowTheme(viewWindow, theme, null);
                    }
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("PortalShellChrome.ViewTheme", ex);
                }
            }

            PaintChildren(hostWindow, background, foreground, theme, dark);
            RedrawWindow(hostWindow, IntPtr.Zero, IntPtr.Zero, RedrawInvalidate | RedrawErase | RedrawAllChildren);
        }

        private static void PrepareDarkMode()
        {
            lock (Sync)
            {
                if (_darkModePrepared)
                    return;
                _darkModePrepared = true;
            }

            try
            {
                _ = SetPreferredAppMode(1);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("PortalShellChrome.DarkMode", ex);
            }
        }

        private static void PaintChildren(IntPtr parent, int background, int foreground, string theme, bool dark)
        {
            EnumChildWindows(parent, (hwnd, _) =>
            {
                string name = ClassName(hwnd);
                if (name == "SysListView32")
                {
                    AllowDarkModeForWindow(hwnd, dark);
                    _ = SetWindowTheme(hwnd, theme, null);
                    SendMessage(hwnd, ListViewSetBackground, IntPtr.Zero, (IntPtr)background);
                    SendMessage(hwnd, ListViewSetTextColor, IntPtr.Zero, (IntPtr)foreground);
                    SendMessage(hwnd, ListViewSetTextBackground, IntPtr.Zero, (IntPtr)background);
                }
                else if (name == "SysHeader32" || name == "DirectUIHWND")
                {
                    AllowDarkModeForWindow(hwnd, dark);
                    _ = SetWindowTheme(hwnd, theme, null);
                }

                return true;
            }, IntPtr.Zero);
        }

        private static string ClassName(IntPtr window)
        {
            var buffer = new char[64];
            int length = GetClassNameW(window, buffer, buffer.Length);
            return length <= 0 ? string.Empty : new string(buffer, 0, length);
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr window, char[] className, int capacity);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subAppName, string? subIdList);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(IntPtr window, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int color);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr deviceContext, ref ExplorerBrowserInterop.NativeRect rect, IntPtr brush);
    }
}
