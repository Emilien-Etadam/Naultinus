using System.Windows;

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
                Description = "Select the folder to show in this browse naultinus",
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
                ValidationMessage.Text = "Please enter a title.";
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                ValidationMessage.Text = "Please select a folder.";
                ValidationMessage.Visibility = Visibility.Visible;
                return;
            }

            if (!System.IO.Directory.Exists(path))
            {
                ValidationMessage.Text = "The selected folder does not exist.";
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
