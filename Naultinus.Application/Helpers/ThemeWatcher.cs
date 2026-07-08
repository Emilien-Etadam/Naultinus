using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media;

namespace Naultinus.Helpers
{
    public static class ThemeWatcher
    {
        private static ResourceDictionary? _resources;

        static ThemeWatcher()
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && _resources != null)
                    Application.Current?.Dispatcher.Invoke(() => Apply(_resources));
            };
        }

        public static void Apply(ResourceDictionary resources)
        {
            _resources = resources;
            bool dark = IsDarkMode();
            resources["NaultinusWindowBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            resources["NaultinusControlBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Color.FromRgb(0xFF, 0xFF, 0xFF));
            resources["NaultinusTextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            resources["NaultinusSubtleBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xA0, 0xA0, 0xA0) : Color.FromRgb(0x60, 0x60, 0x60));
            resources["NaultinusAccentBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
            resources["NaultinusHighlightBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x00, 0x78, 0xD4) : Color.FromRgb(0x00, 0x78, 0xD4));
            resources["NaultinusHighlightTextBrush"] = new SolidColorBrush(Colors.White);
            resources["NaultinusErrorBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0xC4, 0x23, 0x23));
            resources["NaultinusBorderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x40, 0x40, 0x40) : Color.FromRgb(0xD0, 0xD0, 0xD0));
        }

        private static bool IsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("ThemeWatcher.IsDarkMode", ex); return false; }
        }
    }
}
