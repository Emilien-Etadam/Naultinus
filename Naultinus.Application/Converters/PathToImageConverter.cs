using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Naultinus.Helpers;
using Naultinus.Helpers.Native;

namespace Naultinus.Converters
{
    public class PathToImageConverter : IValueConverter
    {
        private static readonly string[] RasterExtensions =
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".ico", ".wdp", ".jxr",
        };

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(RasterExtensions, ext) >= 0)
                {
                    var raster = LoadBitmapImage(path);
                    if (raster != null)
                    {
                        return raster;
                    }
                }

                return LoadShellIcon(path);
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage? LoadBitmapImage(string path)
        {
            try
            {
                var image = new BitmapImage();
                using (var stream = File.OpenRead(path))
                {
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                }

                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource? LoadShellIcon(string path)
        {
            using Bitmap? icon = IconExtractor.GetFileImageFromPath(path, IconSizeEnum.LargeIcon48);
            if (icon == null)
            {
                return null;
            }

            IntPtr hBitmap = icon.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr ho);

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
