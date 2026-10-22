using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Naultinus.Helpers
{
    /// <summary>Hit-test de l'en-tête : boutons / champs restent cliquables, le vide lance DragMove.</summary>
    public static class HeaderChromeInput
    {
        public static bool IsInteractiveSource(DependencyObject? source)
        {
            for (DependencyObject? current = source; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or TextBox or ComboBox)
                    return true;
                if (current is Window)
                    return false;
            }

            return false;
        }
    }
}
