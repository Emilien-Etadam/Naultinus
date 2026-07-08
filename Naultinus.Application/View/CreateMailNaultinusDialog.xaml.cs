using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    public partial class CreateMailNaultinusDialog : Window
    {
        public string NaultinusTitle { get; set; } = "Mail naultinus";
        public string ImapHost { get; set; } = string.Empty;
        public int ImapPort { get; set; } = 993;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public MailDisplayMode DisplayMode { get; set; } = MailDisplayMode.CountOnly;
        public int PollIntervalMinutes { get; set; } = 3;
        public List<string> SelectedFolders { get; private set; } = new List<string>();
        public string? WebmailUrl { get; set; }
        public Guid? SelectedZimbraAccountId => ZimbraAccountPickerHelper.GetSelectedAccountId(ZimbraAccountCombo);

        public CreateMailNaultinusDialog() : this(null) { }

        public CreateMailNaultinusDialog(Guid? preselectedZimbraAccountId)
        {
            InitializeComponent();
            DataContext = this;
            ZimbraAccountPickerHelper.InitializeComboBox(ZimbraAccountCombo, preselectedZimbraAccountId);
            ApplyZimbraAccountSelection();
        }

        private void ZimbraAccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ApplyZimbraAccountSelection();

        private void ApplyZimbraAccountSelection()
        {
            ZimbraAccountPickerHelper.ApplyMailSelection(ZimbraAccountCombo, ImapHostTextBox, UsernameTextBox, PasswordBox);
            ImapHost = ImapHostTextBox.Text;
            Username = UsernameTextBox.Text;
            Password = string.Empty;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb && ZimbraAccountPickerHelper.GetSelectedAccount(ZimbraAccountCombo) == null)
                Password = pb.Password;
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            ImapHost = ImapHostTextBox.Text?.Trim() ?? "";
            Username = UsernameTextBox.Text?.Trim() ?? "";
            Password = ZimbraAccountPickerHelper.GetDiscoveryPassword(ZimbraAccountCombo, PasswordBox);
            if (string.IsNullOrWhiteSpace(ImapHost) || string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show(Strings.MailEnterHostUser, Strings.MailTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var service = new ImapMailService(ImapHost, ImapPort, Username, Password);
                await service.ConnectAsync();
                var folders = await service.GetFolderNamesAsync();
                folders.Sort(StringComparer.OrdinalIgnoreCase);
                FoldersListBox.ItemsSource = folders;
                FoldersListBox.SelectedItems.Clear();
                if (folders.Contains("INBOX"))
                    FoldersListBox.SelectedItems.Add("INBOX");
                service.Disconnect();
                MessageBox.Show(Strings.MailConnectionOk, Strings.MailTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.MailConnectionFailedFormat, ex.Message), Strings.MailTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (ZimbraAccountPickerHelper.GetSelectedAccount(ZimbraAccountCombo) == null)
                Password = PasswordBox.Password;
            else
                Password = string.Empty;

            NaultinusTitle = NaultinusTitleTextBox.Text;
            ImapHost = ImapHostTextBox.Text;
            Username = UsernameTextBox.Text;

            var host = ImapHostTextBox.Text?.Trim() ?? "";
            var user = UsernameTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(Strings.MailEnterImapHost, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(user))
            {
                MessageBox.Show(Strings.MailEnterUsername, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DisplayMode = DisplayModeCombo.SelectedIndex == 1 ? MailDisplayMode.CountAndSubjects : MailDisplayMode.CountOnly;
            SelectedFolders = FoldersListBox.SelectedItems.Cast<string>().ToList();
            if (SelectedFolders.Count == 0)
                SelectedFolders.Add("INBOX");
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
