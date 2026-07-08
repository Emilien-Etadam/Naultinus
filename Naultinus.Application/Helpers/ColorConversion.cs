using System.Windows.Media;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Conversion entre System.Drawing.Color et System.Windows.Media.Color
    /// pour utiliser System.Windows.Forms.ColorDialog (API reconnue Windows).
    /// </summary>
    internal static class ColorConversion
    {
        public static System.Drawing.Color ToDrawingColor(Color mediaColor)
        {
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        public static Color ToMediaColor(System.Drawing.Color drawingColor)
        {
            return Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);
        }
    }
}
