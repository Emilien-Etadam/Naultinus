using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.ViewModel;
using Naultinus;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Naultinus.View
{
    public partial class FolderPortal : Window
    {
        private readonly FolderPortalViewModel viewModel;

        public FolderPortal(FolderPortalViewModel defaultModel)
        {
            InitializeComponent();
            DataContext = defaultModel;
            viewModel = defaultModel;
            Show();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); }
            catch (System.InvalidOperationException) { /* le bouton gauche n'est plus enfoncé : sans effet */ }
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

        private void OnExternalFileDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                e.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void OnExternalFileDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
                return;
            bool isCopy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            viewModel.ImportExplorerFileDrop(files, isCopy);
            e.Handled = true;
        }

        private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FolderPortalItem item)
            {
                e.Handled = true;
                ShellContextMenu.Show(item.FullPath, this);
                // Refresh after context menu closes (file may have been renamed, deleted, etc.)
                viewModel.RefreshCommand.Execute(null);
            }
        }

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            viewModel?.RefreshRecentSnapshots();
        }
    }
}
