using System;
using System.Windows;
using Naultinus.Properties;

namespace Naultinus.View
{
    public partial class SaveSnapshotDialog : Window
    {
        public string SnapshotName { get; private set; } = "";

        public SaveSnapshotDialog()
        {
            InitializeComponent();
            NameTextBox.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.SaveLayoutDefaultNameFormat, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            NameTextBox.SelectAll();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(Strings.SaveLayoutNameRequired, Strings.SaveLayoutTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SnapshotName = name;
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
