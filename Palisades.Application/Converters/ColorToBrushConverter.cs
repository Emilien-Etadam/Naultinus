using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Palisades.Helpers;

namespace Palisades.Converters
{
    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (TryParseColor(value, out var color))
                return new SolidColorBrush(color);

            return new SolidColorBrush(ParseHexOrDefault(CalendarColorHelper.DefaultColor));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color;
            return ParseHexOrDefault(CalendarColorHelper.DefaultColor);
        }

        private static bool TryParseColor(object? value, out Color color)
        {
            color = default;
            if (value is Color typedColor)
            {
                color = typedColor;
                return true;
            }

            if (value is string colorHex && CalendarColorHelper.IsValidHexColor(colorHex))
            {
                color = ParseHexOrDefault(colorHex.Trim());
                return true;
            }

            return false;
        }

        private static Color ParseHexOrDefault(string colorHex)
        {
            try
            {
                var converted = ColorConverter.ConvertFromString(colorHex);
                if (converted is Color color)
                    return color;
            }
            catch
            {
                /* repli sur la couleur par défaut */
            }

            return (Color)ColorConverter.ConvertFromString(CalendarColorHelper.DefaultColor)!;
        }
    }
}
