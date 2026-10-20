using Naultinus.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Naultinus.View
{
    public partial class StandardNaultinus : Window
    {
        private readonly NaultinusViewModel viewModel;
        public StandardNaultinus(NaultinusViewModel defaultModel)
        {
            InitializeComponent();
            DataContext = defaultModel;
            viewModel = defaultModel;
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

        private void TitleBarMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (Header.ContextMenu is ContextMenu cm)
            {
                cm.PlacementTarget = sender as UIElement;
                cm.Placement = PlacementMode.Bottom;
                cm.IsOpen = true;
            }
            e.Handled = true;
        }

        private void AddFolderPortalTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement anchor)
                NaultinusManager.RequestAddTab(this, anchor);
            e.Handled = true;
        }

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (viewModel != null)
                viewModel.RefreshRecentSnapshots();
        }

        private void OnExternalFileDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnExternalFileDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || DataContext is not ViewModel.NaultinusViewModel vm)
                return;
            foreach (var filePath in files)
            {
                if (string.IsNullOrEmpty(filePath)) continue;
                vm.TryAddShortcutFromExternalPath(filePath);
            }

            e.Handled = true;
        }
    }
}
