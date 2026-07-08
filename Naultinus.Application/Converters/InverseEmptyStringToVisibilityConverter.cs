using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Naultinus.Converters
{
    /// <summary>Visible lorsque la chaîne est vide ou absente (ex. tâche pas encore créée sur le serveur).</summary>
    public class InverseEmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && !string.IsNullOrEmpty(str))
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
