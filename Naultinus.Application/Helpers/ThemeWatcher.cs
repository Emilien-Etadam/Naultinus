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

            // ── Jetons « VSS » (Design System) — valeurs exactes du kit, variantes sombre / claire.
            Color bg = C(dark ? "#1A1918" : "#F5F4F2");   // --bg
            Color surface = C(dark ? "#212020" : "#FFFFFF");   // --surface (panneaux)
            Color raised = C(dark ? "#282726" : "#FFFFFF");   // --raised (éléments en relief)
            Color soft = C(dark ? "#302F2D" : "#EFEDEA");   // --soft (fonds de chips / listes)
            Color control = C(dark ? "#2E2D2B" : "#F1EFEC");   // --control (base des champs)
            Color hover = C(dark ? "#0FFBF7F5" : "#E9E7E3");   // --hover
            Color border = C(dark ? "#3D3B38" : "#E3E1DD");   // --border
            Color text = C(dark ? "#D8D5D0" : "#292724");   // --text
            Color muted = C(dark ? "#9A968F" : "#6D6963");   // --muted
            Color accent = C(dark ? "#4D9FEA" : "#0A6CC2");   // --accent
            Color accentSoft = C(dark ? "#2E4D9FEA" : "#E8F1FB");   // --accent-soft
            Color focus = C(dark ? "#734D9FEA" : "#660A6CC2");   // --focus-ring
            Color red = C(dark ? "#EF8574" : "#C53030");   // --red
            Color redSoft = C(dark ? "#24EF8574" : "#FBE9E9");   // --red-soft
            Color green = C(dark ? "#83CD7F" : "#357F31");   // --green
            Color greenSoft = C(dark ? "#2483CD7F" : "#E7F3E7");   // --green-soft
            Color amber = C(dark ? "#D3AF2A" : "#A87908");   // --amber
            Color amberSoft = C(dark ? "#21D3AF2A" : "#FAF3DF");   // --amber-soft
            Color purple = C(dark ? "#C793C2" : "#6B46B8");   // --purple
            Color purpleSoft = C(dark ? "#24C793C2" : "#F2EDFB");   // --purple-soft
            Color white = Colors.White, black = Colors.Black;

            // Champ « en creux » : --control assombri (comme color-mix(control 96%, #000) du kit).
            Color field = Mix(control, black, 0.04);

            // ── Surfaces
            resources["NaultinusWindowBrush"] = Solid(bg);
            resources["NaultinusControlBrush"] = Solid(surface);
            resources["NaultinusCardBrush"] = Solid(raised);
            resources["NaultinusSoftBrush"] = Solid(soft);
            resources["NaultinusFieldBrush"] = Solid(field);
            resources["NaultinusCardHoverBrush"] = Solid(hover);
            resources["NaultinusHoverSubtleBrush"] = Solid(hover);
            resources["NaultinusCardPressedBrush"] = Solid(soft);
            resources["NaultinusHoverActiveBrush"] = Solid(soft);
            resources["NaultinusBorderBrush"] = Solid(border);
            // ── Texte
            resources["NaultinusTextBrush"] = Solid(text);
            resources["NaultinusSubtleBrush"] = Solid(muted);
            // ── Accent, sélection, focus
            resources["NaultinusAccentBrush"] = Solid(accent);
            resources["NaultinusHighlightBrush"] = Solid(accent);
            resources["NaultinusHighlightTextBrush"] = Solid(white);
            resources["NaultinusSelectedBrush"] = Solid(accentSoft);
            resources["NaultinusFocusBrush"] = Solid(focus);
            // ── Couleurs sémantiques + variantes « soft »
            resources["NaultinusErrorBrush"] = Solid(red);
            resources["NaultinusErrorSoftBrush"] = Solid(redSoft);
            resources["NaultinusGreenBrush"] = Solid(green);
            resources["NaultinusGreenSoftBrush"] = Solid(greenSoft);
            resources["NaultinusAmberBrush"] = Solid(amber);
            resources["NaultinusAmberSoftBrush"] = Solid(amberSoft);
            resources["NaultinusPurpleBrush"] = Solid(purple);
            resources["NaultinusPurpleSoftBrush"] = Solid(purpleSoft);

            // ── Recettes « matière usinée » : dégradés verticaux + reliefs (color-mix du kit).
            // Bouton primaire (recette n°1)
            resources["NaultinusPrimaryButtonBrush"] = VGrad(
                (Mix(accent, white, 0.10), 0.0), (accent, 0.55), (Mix(accent, black, 0.08), 1.0));
            resources["NaultinusPrimaryButtonBorderBrush"] = Solid(Mix(accent, black, 0.30));
            // Bouton secondaire / en-têtes de carte (surface --raised « usinée »)
            resources["NaultinusRaisedGradientBrush"] = VGrad(
                (Mix(raised, white, 0.08), 0.0), (Mix(raised, black, 0.04), 1.0));
            resources["NaultinusSecondaryButtonBorderBrush"] = Solid(Argb(0x73, muted));
            // Bande d'onglets (dégradé léger)
            resources["NaultinusTabBarBrush"] = VGrad(
                (Mix(raised, white, 0.06), 0.0), (Mix(raised, black, 0.03), 1.0));
            // Bouton danger plein
            resources["NaultinusDangerSolidBrush"] = VGrad(
                (Mix(red, white, 0.10), 0.0), (red, 0.55), (Mix(red, black, 0.08), 1.0));
            resources["NaultinusDangerBorderBrush"] = Solid(Mix(red, black, 0.30));
            // Reflet supérieur « inset » réutilisable (inset 0 1px 0 rgba(255,255,255,.x)).
            resources["NaultinusSheenBrush"] = VGrad(
                (Argb(0x2E, white), 0.0), (Argb(0x00, white), 0.5), (Argb(0x00, white), 1.0));
        }

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        /// <summary>Mélange linéaire de deux couleurs (t = proportion de <paramref name="b"/>), à la color-mix().</summary>
        private static Color Mix(Color a, Color b, double t)
        {
            byte Ch(byte x, byte y) => (byte)Math.Round(x * (1 - t) + y * t);
            return Color.FromArgb(Ch(a.A, b.A), Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B));
        }

        private static Color Argb(byte alpha, Color c) => Color.FromArgb(alpha, c.R, c.G, c.B);

        private static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>Dégradé vertical (180°) figé, comme les recettes « usinées » du kit.</summary>
        private static LinearGradientBrush VGrad(params (Color color, double offset)[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var s in stops)
                g.GradientStops.Add(new GradientStop(s.color, s.offset));
            g.Freeze();
            return g;
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
