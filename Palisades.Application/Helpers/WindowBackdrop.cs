using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Palisades.Helpers
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
            DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));

            // Mica (attr. 38) et la préférence de coins (attr. 33) sont spécifiques à Windows 11
            // (build >= 22000). Sous Windows 10 ces appels échouent en silence, et laisser le fond
            // transparent rendrait la fenêtre illisible (aucun backdrop dessous). On ne rend donc la
            // fenêtre transparente que là où Mica est réellement appliqué.
            if (IsWindows11OrGreater())
            {
                int mica = 2; // DWMSBT_MAINWINDOW = Mica
                DwmSetWindowAttribute(hwnd, 38, ref mica, sizeof(int));

                // DWMWCP_DONOTROUND : sans cela, Windows 11 trace ombre + liseré autour des fenêtres sans chrome / transparentes.
                int corner = 1; // DWMWCP_DONOTROUND (voir DWM_WINDOW_CORNER_PREFERENCE)
                DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

                window.Background = Brushes.Transparent;
            }
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
            catch { return false; }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
