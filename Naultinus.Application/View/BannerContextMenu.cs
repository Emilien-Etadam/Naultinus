using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Rouvre le menu du bandeau. Un clic droit sur un bouton ou le chemin ne l'atteint plus tout seul.
    /// </summary>
    internal static class BannerContextMenu
    {
        internal static void Open(FrameworkElement header, MouseButtonEventArgs e)
        {
            if (e.Handled || e.ChangedButton != MouseButton.Right || header.ContextMenu is not ContextMenu menu)
                return;
            if (IsTabButton(e.OriginalSource as DependencyObject))
                return;

            menu.PlacementTarget = e.OriginalSource as UIElement ?? header;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static bool IsTabButton(DependencyObject? source)
        {
            Button? button = FindAncestor<Button>(source);
            if (button == null || FindAncestor<ItemsControl>(button) == null)
                return false;

            return button.DataContext is INaultinusViewModel;
        }

        private static T? FindAncestor<T>(DependencyObject? child)
            where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T found)
                    return found;
                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }
    }
}
