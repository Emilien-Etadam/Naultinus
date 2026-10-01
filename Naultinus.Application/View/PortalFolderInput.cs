using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Touches de la grille, en fenêtre seule et en onglet. Entrée et Espace restent aux boutons du bandeau.
    /// </summary>
    internal static class PortalFolderInput
    {
        internal static bool TryHandle(FolderPortalViewModel portal, Window owner, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox)
                return false;
            if (e.OriginalSource is Button && e.Key is Key.Enter or Key.Space)
                return false;

            ModifierKeys modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                portal.CopySelection();
                return true;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.X)
            {
                portal.CutSelection();
                return true;
            }

            if (modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                portal.PasteFromClipboardCommand.Execute(null);
                return true;
            }

            if (modifiers != ModifierKeys.None)
                return false;

            if (e.Key == Key.Enter)
            {
                portal.OpenSelection();
                return true;
            }

            if (e.Key == Key.Back)
            {
                portal.NavigateBackCommand.Execute(null);
                return true;
            }

            if (e.Key == Key.F2)
            {
                Rename(portal, owner);
                return true;
            }

            if (e.Key == Key.Delete)
            {
                ConfirmRecycle(portal, owner);
                return true;
            }

            return false;
        }

        private static void Rename(FolderPortalViewModel portal, Window owner)
        {
            FolderPortalItem? item = portal.SelectedItems.Count == 1 ? portal.SelectedItems[0] : null;
            if (item == null)
                return;

            var dialog = new RenameSnapshotInputDialog
            {
                CurrentName = item.Name,
                PromptLabel = Strings.PortalRenamePrompt,
                Title = Strings.PortalRenameTitle,
                Owner = owner,
            };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.NewName))
                return;

            portal.TryRename(item, dialog.NewName);
        }

        private static void ConfirmRecycle(FolderPortalViewModel portal, Window owner)
        {
            int count = portal.SelectedItems.Count;
            if (count == 0)
                return;

            string message = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.PortalRecycleConfirmFormat, count);
            if (MessageBox.Show(owner, message, Strings.PortalRecycleTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;

            portal.RecycleSelection();
        }
    }
}
