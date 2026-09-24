using System;
using System.IO;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Extensions décodées comme une image par PathToImageConverter, et non comme une icône shell.
    /// </summary>
    internal static class RasterImageFiles
    {
        private static readonly string[] Extensions =
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".wdp", ".jxr",
        };

        internal static bool IsRasterPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }
    }
}
