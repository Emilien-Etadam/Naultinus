using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    public partial class ManageSnapshotsDialog : Window
    {
        private List<LayoutSnapshot> _snapshots = new();

        public ManageSnapshotsDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshList();
        }

        private void RefreshList()
        {
            _snapshots = LayoutSnapshotService.ListSnapshots();
            SnapshotsListBox.ItemsSource = _snapshots.ToList();
        }

        private void SnapshotsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Optional: enable/disable row buttons based on selection
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var id = (string)((Button)sender).Tag;
            if (string.IsNullOrEmpty(id)) return;
            if (MessageBox.Show(Strings.RestoreLayoutConfirm, Strings.RestoreLayoutTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            LayoutSnapshotService.RestoreSnapshot(id);
            DialogResult = true;
            Close();
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            var id = (string)((Button)sender).Tag;
            if (string.IsNullOrEmpty(id)) return;
            var snap = _snapshots.FirstOrDefault(s => s.Id == id);
            if (snap == null) return;
            var input = new RenameSnapshotInputDialog { CurrentName = snap.Name, PromptLabel = Strings.DialogLayoutNameLabel };
            if (input.ShowDialog() != true) return;
            var newName = input.NewName?.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            LayoutSnapshotService.RenameSnapshot(id, newName);
            RefreshList();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var id = (string)((Button)sender).Tag;
            if (string.IsNullOrEmpty(id)) return;
            if (MessageBox.Show(Strings.DeleteLayoutConfirm, Strings.DeleteLayoutTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            LayoutSnapshotService.DeleteSnapshot(id);
            RefreshList();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = SnapshotsListBox.SelectedItem as LayoutSnapshot;
            if (selected == null)
            {
                MessageBox.Show(Strings.ExportSelectLayout, Strings.ExportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = Strings.ExportFolderDescription,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (LayoutSnapshotService.ExportSnapshot(selected.Id, dlg.SelectedPath))
                MessageBox.Show(Strings.ExportSuccess, Strings.ExportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(Strings.ExportFailed, Strings.ExportTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = Strings.ImportFolderDescription,
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var newId = LayoutSnapshotService.ImportSnapshot(dlg.SelectedPath);
            if (newId != null)
            {
                RefreshList();
                MessageBox.Show(Strings.ImportSuccess, Strings.ImportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
                MessageBox.Show(Strings.ImportNoSnapshot, Strings.ImportTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
