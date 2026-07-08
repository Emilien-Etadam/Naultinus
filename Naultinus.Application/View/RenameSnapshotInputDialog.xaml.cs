using System.Windows;
using Naultinus.Properties;

namespace Naultinus.View
{
    public partial class RenameSnapshotInputDialog : Window
    {
        public string? NewName { get; private set; }
        public string CurrentName { get; set; } = "";
        public string PromptLabel { get; set; } = Strings.DialogLayoutNameLabel;

        public RenameSnapshotInputDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => NameTextBox.Text = CurrentName;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            NewName = NameTextBox.Text?.Trim();
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
