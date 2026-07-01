using Palisades.ViewModel;
using System.Windows;

namespace Palisades.View
{
    public partial class TaskPalisade : Window
    {
        private readonly TaskPalisadeViewModel _viewModel;
        
        public TaskPalisade(TaskPalisadeViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            Show();
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); }
            catch (System.InvalidOperationException) { /* le bouton gauche n'est plus enfoncé : sans effet */ }
        }

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            _viewModel?.RefreshRecentSnapshots();
        }
    }
}