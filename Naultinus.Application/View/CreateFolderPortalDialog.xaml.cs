using System.Windows;
using Naultinus.Properties;

namespace Naultinus.View
{
    public partial class CreateFolderPortalDialog : Window
    {
        public string SelectedPath { get; private set; } = "";
        public string PortalTitle { get; private set; } = "";

        public CreateFolderPortalDialog()
        {
            InitializeComponent();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = Strings.BrowseNaultinusFolderPickerDescription,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                PathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            string title = TitleTextBox.Text.Trim();
            string path = PathTextBox.Text.Trim();

            if (string.IsNullOrEmpty(title))
            {
                ValidationMessage.Text = Strings.BrowseNaultinusTitleRequired;
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                ValidationMessage.Text = Strings.BrowseNaultinusSelectFolder;
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            if (!System.IO.Directory.Exists(path))
            {
                ValidationMessage.Text = Strings.BrowseNaultinusFolderNotFound;
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            PortalTitle = title;
            SelectedPath = path;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
