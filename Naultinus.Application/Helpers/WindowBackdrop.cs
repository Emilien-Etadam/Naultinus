using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Naultinus.Helpers
{
    public static class WindowBackdrop
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(WindowBackdrop),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window && (bool)e.NewValue)
            {
                if (window.IsLoaded)
                    Apply(window);
                else
                    window.Loaded += (_, _) => Apply(window);
            }
        }

        private static void Apply(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Le mode sombre du chrome DWM (attr. 20) est supporté dès Windows 10 1809.
            int useDark = IsSystemDarkMode() ? 1 : 0;
            TrySetAttribute(hwnd, 20, useDark, "dark-mode");

            // Mica (attr. 38) et la préférence de coins (attr. 33) sont spécifiques à Windows 11
            // (build >= 22000). Sous Windows 10 ces appels échouent en silence, et laisser le fond
            // transparent rendrait la fenêtre illisible (aucun backdrop dessous). On ne rend donc la
            // fenêtre transparente que là où Mica est réellement appliqué.
            if (IsWindows11OrGreater())
            {
                TrySetAttribute(hwnd, 38, 2, "mica");    // DWMSBT_MAINWINDOW
                TrySetAttribute(hwnd, 33, 1, "corner");  // DWMWCP_DONOTROUND
                window.Background = Brushes.Transparent;
            }
        }

        // Applique un attribut DWM et journalise si l'appel échoue, au lieu d'ignorer le HRESULT.
        private static void TrySetAttribute(IntPtr hwnd, int attribute, int value, string label)
        {
            int hr = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            if (hr != 0)
                NaultinusDiagnostics.LogDebug($"WindowBackdrop: DwmSetWindowAttribute({label}) a échoué (0x{hr:X8})");
        }

        private static bool IsWindows11OrGreater()
        {
            var v = Environment.OSVersion.Version;
            return v.Major >= 10 && v.Build >= 22000;
        }

        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("WindowBackdrop.IsSystemDarkMode", ex); return false; }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
