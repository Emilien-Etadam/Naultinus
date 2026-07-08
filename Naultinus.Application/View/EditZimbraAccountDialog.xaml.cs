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
            if (Account == null)
                Account = new ZimbraAccount();
            Account.Email = email;
            Account.CalDAVBaseUrl = CalDAVUrlTextBox.Text?.Trim() ?? "";
            Account.ImapHost = ImapHostTextBox.Text?.Trim() ?? "";

            // Auto-complétion IMAP Host si vide (extrait du hostname CalDAV, uniquement si l'URL est valide).
            // Sans le TryCreate, une URL CalDAV invalide levait une UriFormatException non gérée qui
            // faisait échouer l'enregistrement du compte sans message pour l'utilisateur.
            if (string.IsNullOrWhiteSpace(Account.ImapHost)
                && !string.IsNullOrWhiteSpace(Account.CalDAVBaseUrl)
                && Uri.TryCreate(Account.CalDAVBaseUrl, UriKind.Absolute, out var caldavUri))
                Account.ImapHost = caldavUri.Host;

            if (!string.IsNullOrEmpty(PasswordBox.Password))
                Account.EncryptedPassword = CredentialEncryptor.Encrypt(PasswordBox.Password);
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
