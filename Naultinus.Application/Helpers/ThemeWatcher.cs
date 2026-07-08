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

            // Palette « VSS » — neutres chauds, accent bleu et couleurs sémantiques,
            // déclinée en variantes sombre / claire (cf. Design System VSS).
            // Surfaces
            resources["NaultinusWindowBrush"] = Brush(dark, "#1A1918", "#F5F4F2");      // --bg
            resources["NaultinusControlBrush"] = Brush(dark, "#212020", "#FFFFFF");     // --surface
            resources["NaultinusCardBrush"] = Brush(dark, "#282726", "#EFEDEA");        // --raised / --soft
            resources["NaultinusCardHoverBrush"] = Brush(dark, "#302F2D", "#E9E7E3");   // --hover
            resources["NaultinusCardPressedBrush"] = Brush(dark, "#3A3937", "#E3E1DD");
            resources["NaultinusHoverSubtleBrush"] = Brush(dark, "#282726", "#EFEDEA");
            resources["NaultinusHoverActiveBrush"] = Brush(dark, "#3D3B38", "#E3E1DD");
            resources["NaultinusBorderBrush"] = Brush(dark, "#3D3B38", "#E3E1DD");      // --border
            // Texte
            resources["NaultinusTextBrush"] = Brush(dark, "#D8D5D0", "#292724");        // --text
            resources["NaultinusSubtleBrush"] = Brush(dark, "#9A968F", "#6D6963");      // --muted
            // Accent & sélection
            resources["NaultinusAccentBrush"] = Brush(dark, "#4D9FEA", "#0A6CC2");      // --accent
            resources["NaultinusHighlightBrush"] = Brush(dark, "#4D9FEA", "#0A6CC2");
            resources["NaultinusHighlightTextBrush"] = new SolidColorBrush(Colors.White);
            resources["NaultinusSelectedBrush"] = Brush(dark, "#2E4D9FEA", "#E8F1FB");  // --accent-soft
            // Couleurs sémantiques
            resources["NaultinusErrorBrush"] = Brush(dark, "#EF8574", "#C53030");       // --red
            resources["NaultinusGreenBrush"] = Brush(dark, "#83CD7F", "#357F31");       // --green
            resources["NaultinusAmberBrush"] = Brush(dark, "#D3AF2A", "#A87908");       // --amber
            resources["NaultinusPurpleBrush"] = Brush(dark, "#C793C2", "#6B46B8");      // --purple
        }

        /// <summary>Crée un pinceau figé à partir de la couleur sombre ou claire (format #RRGGBB ou #AARRGGBB).</summary>
        private static SolidColorBrush Brush(bool dark, string darkHex, string lightHex)
        {
            var color = (Color)ColorConverter.ConvertFromString(dark ? darkHex : lightHex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
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
