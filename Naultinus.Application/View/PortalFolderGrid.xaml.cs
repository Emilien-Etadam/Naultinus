using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Grille WPF du portail. Pas de fenêtre shell : les clics restent dans cette fenêtre.
    /// </summary>
    public partial class PortalFolderGrid : UserControl
    {
        private bool _deferSingleSelect;
        private Point _pressPosition;
        private FolderPortalItem? _pressedItem;

        public PortalFolderGrid()
        {
            InitializeComponent();
        }

        private FolderPortalViewModel? Portal => DataContext as FolderPortalViewModel;

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (FindItem(e.OriginalSource as DependencyObject) != null)
                return;
            if (e.ClickCount == 2 && Portal is FolderPortalViewModel portal)
            {
                if (!portal.AllowDoubleClick)
                {
                    e.Handled = true;
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    portal.NavigateToRootCommand.Execute(null);
                else
                    portal.OpenInExplorerCommand.Execute(null);
                e.Handled = true;
                return;
            }

            Portal?.ClearSelection();
        }

        private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderPortalItem item || Portal is not FolderPortalViewModel portal)
                return;

            Focus();
            _pressPosition = e.GetPosition(this);
            _pressedItem = item;
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (!control && !shift && item.IsSelected)
            {
                _deferSingleSelect = true;
                if (e.ClickCount == 2)
                {
                    if (!portal.AllowDoubleClick)
                    {
                        e.Handled = true;
                        return;
                    }

                    portal.NavigateIntoFolderCommand.Execute(item);
                    e.Handled = true;
                }

                return;
            }

            _deferSingleSelect = false;
            portal.SelectItem(item, control, shift);
            if (e.ClickCount == 2)
            {
                if (!portal.AllowDoubleClick)
                {
                    e.Handled = true;
                    return;
                }

                portal.NavigateIntoFolderCommand.Execute(item);
                e.Handled = true;
            }
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_deferSingleSelect || _pressedItem == null || Portal is not FolderPortalViewModel portal)
                return;

            Point position = e.GetPosition(this);
            _deferSingleSelect = false;
            if (System.Math.Abs(position.X - _pressPosition.X) >= SystemParameters.MinimumHorizontalDragDistance
                || System.Math.Abs(position.Y - _pressPosition.Y) >= SystemParameters.MinimumVerticalDragDistance)
                return;

            portal.SelectItem(_pressedItem, control: false, shift: false);
        }

        private void Item_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            e.Handled = true;
        }

        private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FolderPortalItem item || Portal is not FolderPortalViewModel portal)
                return;

            e.Handled = true;
            if (!portal.AllowContextMenu)
                return;

            if (!item.IsSelected)
                portal.SelectItem(item, control: false, shift: false);

            Window? owner = Window.GetWindow(this);
            if (owner != null)
            {
                string[] paths = portal.SelectedItems.Select(selected => selected.FullPath).ToArray();
                ShellContextMenu.Show(paths, owner);
            }

            portal.RefreshCommand.Execute(null);
        }

        private void Background_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindItem(e.OriginalSource as DependencyObject) != null)
                return;
            if (Portal is not FolderPortalViewModel portal || string.IsNullOrEmpty(portal.CurrentPath))
                return;

            e.Handled = true;
            if (!portal.AllowContextMenu)
                return;

            Window? owner = Window.GetWindow(this);
            if (owner != null)
                ShellContextMenu.ShowBackground(portal.CurrentPath, owner);
        }

        private void OnExternalFileDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            if (Portal is { AllowDrop: false })
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            bool isCopy = PortalDragDropKeys.IsCopy(e.KeyStates);
            e.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnExternalFileDrop(object sender, DragEventArgs e)
        {
            if (Portal is not FolderPortalViewModel portal || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            if (!portal.AllowDrop)
            {
                e.Handled = true;
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
                return;

            bool isCopy = PortalDragDropKeys.IsCopy(e.KeyStates);
            FolderPortalItem? over = FindItem(e.OriginalSource as DependencyObject);
            string? into = over is { IsDirectory: true } ? over.FullPath : null;
            portal.ImportExplorerFileDrop(files, isCopy, into);
            e.Handled = true;
        }

        private static FolderPortalItem? FindItem(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is FrameworkElement element && element.DataContext is FolderPortalItem item)
                    return item;
                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }
    }
}
