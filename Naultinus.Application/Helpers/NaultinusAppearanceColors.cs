using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Couleurs d'apparence des naultinus (#RRGGBB), à la manière des teintes Fences :
    /// couleur de fond + opacité séparée ; chaînes vides = thème courant.
    /// </summary>
    internal static class NaultinusAppearanceColors
    {
        /// <summary>Sans teinte personnalisée : fond à 100 % (thème / Mica).</summary>
        public const int DefaultBodyOpacityPercent = 100;

        /// <summary>Opacité proposée au premier choix d'une couleur de fond (effet Fences).</summary>
        public const int PreferredTintOpacityPercent = 80;

        public const int MinOpacityPercent = 0;
        public const int MaxOpacityPercent = 100;

        /// <summary>Teinte utilisée quand l'utilisateur baisse l'opacité sans choisir de couleur.</summary>
        public const string DefaultTintRgbHex = "#000000";

        public static bool TryParseRgb(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            string text = hex.Trim();
            if (text.StartsWith('#') && text.Length == 7
                && byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
                && byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
                && byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                color = Color.FromRgb(r, g, b);
                return true;
            }

            return false;
        }

        public static string ToRgbHex(Color color)
            => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);

        public static int ClampOpacityPercent(int percent)
            => Math.Clamp(percent, MinOpacityPercent, MaxOpacityPercent);

        public static byte OpacityToAlpha(int opacityPercent, bool forceOpaque)
        {
            if (forceOpaque)
                return 255;
            int clamped = ClampOpacityPercent(opacityPercent);
            return (byte)Math.Round(255.0 * clamped / 100.0);
        }

        public static SolidColorBrush? TryCreateBrush(string? rgbHex, int opacityPercent, bool forceOpaque)
        {
            if (!TryParseRgb(rgbHex, out Color rgb))
                return null;

            var brush = new SolidColorBrush(Color.FromArgb(
                OpacityToAlpha(opacityPercent, forceOpaque),
                rgb.R,
                rgb.G,
                rgb.B));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Pinceau RGB opaque (A=255). L'opacité visuelle doit passer par
        /// la propriété Opacity de l'élément UI : l'alpha des brushes est peu fiable
        /// sur les fenêtres WPF AllowsTransparency (souvent 0 % ou 100 %).
        /// </summary>
        public static SolidColorBrush? TryCreateOpaqueBrush(string? rgbHex)
            => TryCreateBrush(rgbHex, opacityPercent: 100, forceOpaque: true);

        public static double PercentToOpacity(int opacityPercent)
            => ClampOpacityPercent(opacityPercent) / 100.0;

        public static Brush ThemeBrush(string resourceKey, Brush fallback)
        {
            if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
                return brush;
            return fallback;
        }

        /// <summary>
        /// Fond quasiment invisible mais hit-testable. Sur fenêtre <c>AllowsTransparency</c>,
        /// <see cref="Brushes.Transparent"/> ne reçoit souvent plus les clics (DragMove cassé).
        /// </summary>
        // public : référencé depuis le XAML (PortalPathBar / en-têtes).
        public static SolidColorBrush HitTestableClear { get; } = CreateHitTestableClear();

        private static SolidColorBrush CreateHitTestableClear()
        {
            var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            brush.Freeze();
            return brush;
        }
    }
}
