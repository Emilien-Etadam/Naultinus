using System.Windows;
using System.Windows.Forms;
using Naultinus.Helpers;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    public partial class EditFolderPortal : Window
    {
        public EditFolderPortal()
        {
            InitializeComponent();
        }

        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FolderPortalViewModel portal)
                return;

            using var dialog = new FolderBrowserDialog
            {
                Description = Properties.Strings.LabelRootPath,
                UseDescriptionForTitle = true,
                SelectedPath = portal.RootPath,
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string path = dialog.SelectedPath;
            if (string.IsNullOrEmpty(path) || !PortalFolderAccess.TryExists(path, out _))
                return;

            portal.ChangeRoot(path);
        }
    }
}
