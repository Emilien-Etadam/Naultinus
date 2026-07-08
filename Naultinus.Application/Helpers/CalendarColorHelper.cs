using System;
using System.Collections.Generic;

namespace Naultinus.Helpers
{
    /// <summary>Palette et validation des couleurs par calendrier source.</summary>
    public static class CalendarColorHelper
    {
        public const string DefaultColor = "#708090";

        /// <summary>Palette fixe d'environ 8 couleurs distinctes et lisibles.</summary>
        public static IReadOnlyList<string> Palette { get; } = new[]
        {
            "#4A90D9",
            "#E74C3C",
            "#2ECC71",
            "#F39C12",
            "#9B59B6",
            "#1ABC9C",
            "#E67E22",
            "#3498DB",
        };

        /// <summary>Retourne une couleur de la palette (modulo) ou la couleur par défaut si l'index est invalide.</summary>
        public static string GetPaletteColor(int index)
        {
            if (index < 0 || Palette.Count == 0)
                return DefaultColor;

            return Palette[index % Palette.Count];
        }

        /// <summary>Vérifie qu'une chaîne est une couleur hexadécimale (#RGB, #RRGGBB ou #AARRGGBB).</summary>
        public static bool IsValidHexColor(string? colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
                return false;

            var hex = colorHex.Trim();
            if (!hex.StartsWith('#'))
                return false;

            var digits = hex.Substring(1);
            if (digits.Length is not (3 or 6 or 8))
                return false;

            foreach (var c in digits)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }

        /// <summary>Conserve une couleur persistée valide, sinon attribue une couleur de la palette.</summary>
        public static string ResolveColor(int paletteIndex, string? storedColor)
        {
            return IsValidHexColor(storedColor) ? storedColor!.Trim() : GetPaletteColor(paletteIndex);
        }

        /// <summary>Extrait un libellé court à partir du HREF CalDAV (dernier segment).</summary>
        public static string GetDisplayNameFromHref(string calendarHref)
        {
            if (string.IsNullOrWhiteSpace(calendarHref))
                return "?";

            var trimmed = calendarHref.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
        }
    }
}
