using Naultinus.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Naultinus.View
{
    public partial class TaskNaultinus : Window
    {
        private readonly TaskNaultinusViewModel _viewModel;
        
        public TaskNaultinus(TaskNaultinusViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            Show();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsHeaderInteractiveSource(e.OriginalSource as DependencyObject))
                return;

            try { DragMove(); }
            catch (System.InvalidOperationException) { /* le bouton gauche n'est plus enfoncé : sans effet */ }
            e.Handled = true;
        }

        private static bool IsHeaderInteractiveSource(DependencyObject? source)
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

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            _viewModel?.RefreshRecentSnapshots();
        }
    }
}