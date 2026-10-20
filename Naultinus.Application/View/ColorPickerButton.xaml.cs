using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Naultinus.Helpers;

namespace Naultinus.View
{
    /// <summary>Carré cliquable qui ouvre le <see cref="System.Windows.Forms.ColorDialog"/> Windows.</summary>
    public partial class ColorPickerButton : Button
    {
        public static readonly DependencyProperty ColorHexProperty =
            DependencyProperty.Register(
                nameof(ColorHex),
                typeof(string),
                typeof(ColorPickerButton),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnColorHexChanged));

        public ColorPickerButton()
        {
            InitializeComponent();
            UpdateSwatch(ColorHex);
        }

        public string ColorHex
        {
            get => (string)GetValue(ColorHexProperty);
            set => SetValue(ColorHexProperty, value);
        }

        private static void OnColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerButton button)
                button.UpdateSwatch(e.NewValue as string);
        }

        private void UpdateSwatch(string? hex)
        {
            if (NaultinusAppearanceColors.TryParseRgb(hex, out Color color))
                Background = new SolidColorBrush(color);
            else
                Background = Brushes.Transparent;
        }

        private void OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = true,
            };

            if (NaultinusAppearanceColors.TryParseRgb(ColorHex, out Color current))
                dialog.Color = ColorConversion.ToDrawingColor(current);

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            ColorHex = NaultinusAppearanceColors.ToRgbHex(ColorConversion.ToMediaColor(dialog.Color));
        }
    }
}
