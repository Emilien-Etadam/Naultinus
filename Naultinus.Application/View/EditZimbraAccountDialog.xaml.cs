using System;
using Naultinus.Properties;
using Naultinus.Helpers;
using Naultinus.Model;
using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    public partial class EditZimbraAccountDialog : Window
    {
        public ZimbraAccount? Account { get; private set; }

        public EditZimbraAccountDialog(ZimbraAccount? existing = null)
        {
            InitializeComponent();
            if (existing != null)
            {
                Account = existing;
                EmailTextBox.Text = existing.Email;
                CalDAVUrlTextBox.Text = existing.CalDAVBaseUrl;
                ImapHostTextBox.Text = existing.ImapHost;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show(Strings.AccountEmailRequired, Strings.AccountTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var caldavUrl = CalDAVUrlTextBox.Text?.Trim() ?? "";
            var imapHost = ImapHostTextBox.Text?.Trim() ?? "";

            // Auto-complétion IMAP si vide, seulement quand l'URL est absolue.
            // TryCreate évite une UriFormatException qui ferait échouer l'enregistrement sans message.
            if (string.IsNullOrWhiteSpace(imapHost)
                && !string.IsNullOrWhiteSpace(caldavUrl)
                && Uri.TryCreate(caldavUrl, UriKind.Absolute, out var caldavUri))
                imapHost = caldavUri.Host;

            string? encrypted = null;
            if (!string.IsNullOrEmpty(PasswordBox.Password))
            {
                encrypted = CredentialEncryptor.Encrypt(PasswordBox.Password);
                if (string.IsNullOrEmpty(encrypted) || string.Equals(encrypted, PasswordBox.Password, StringComparison.Ordinal))
                {
                    MessageBox.Show(Strings.SharedCalDavPasswordProtectFailed, Strings.AccountTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (Account == null)
                Account = new ZimbraAccount();
            Account.Email = email;
            Account.CalDAVBaseUrl = caldavUrl;
            Account.ImapHost = imapHost;
            if (encrypted != null)
                Account.EncryptedPassword = encrypted;

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
