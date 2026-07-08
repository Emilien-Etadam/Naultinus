using Palisades.Model;
using Palisades.Properties;
using Palisades.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace Palisades.Helpers
{
    /// <summary>Élément de ComboBox : compte Zimbra ou option « saisie manuelle ».</summary>
    public sealed class ZimbraAccountComboItem
    {
        public ZimbraAccount? Account { get; init; }

        public bool IsManualEntry => Account == null;

        public string DisplayName => Account?.Email ?? Strings.ZimbraManualEntry;
    }

    /// <summary>Initialisation et sélection de compte Zimbra dans les dialogues de création de palisade.</summary>
    public static class ZimbraAccountPickerHelper
    {
        public static void InitializeComboBox(ComboBox combo, Guid? preselectedAccountId = null)
        {
            var items = new List<ZimbraAccountComboItem> { new() };
            foreach (var acc in ZimbraAccountStore.Load())
                items.Add(new ZimbraAccountComboItem { Account = acc });

            combo.DisplayMemberPath = nameof(ZimbraAccountComboItem.DisplayName);
            combo.ItemsSource = items;

            if (preselectedAccountId is Guid id)
            {
                var match = items.FirstOrDefault(i => i.Account?.Id == id);
                combo.SelectedItem = match ?? items[0];
            }
            else
            {
                combo.SelectedIndex = 0;
            }
        }

        public static Guid? GetSelectedAccountId(ComboBox combo)
        {
            if (combo.SelectedItem is ZimbraAccountComboItem item && !item.IsManualEntry)
                return item.Account!.Id;
            return null;
        }

        public static ZimbraAccount? GetSelectedAccount(ComboBox combo)
        {
            if (combo.SelectedItem is ZimbraAccountComboItem item && !item.IsManualEntry)
                return item.Account;
            return null;
        }

        /// <summary>Mot de passe pour la découverte CalDAV/IMAP : compte sélectionné ou saisie manuelle.</summary>
        public static string GetDiscoveryPassword(ComboBox combo, PasswordBox passwordBox)
        {
            if (GetSelectedAccount(combo) is ZimbraAccount acc)
                return CredentialEncryptor.Decrypt(acc.EncryptedPassword ?? "");
            return passwordBox.Password;
        }

        public static void ApplyCalDavSelection(ComboBox combo, TextBox urlBox, TextBox usernameBox, PasswordBox passwordBox)
        {
            if (GetSelectedAccount(combo) is ZimbraAccount acc)
            {
                urlBox.Text = acc.CalDAVBaseUrl ?? string.Empty;
                usernameBox.Text = acc.Email ?? string.Empty;
                passwordBox.Password = string.Empty;
                urlBox.IsEnabled = false;
                usernameBox.IsEnabled = false;
                passwordBox.IsEnabled = false;
            }
            else
            {
                urlBox.IsEnabled = true;
                usernameBox.IsEnabled = true;
                passwordBox.IsEnabled = true;
            }
        }

        public static void ApplyMailSelection(ComboBox combo, TextBox hostBox, TextBox usernameBox, PasswordBox passwordBox)
        {
            if (GetSelectedAccount(combo) is ZimbraAccount acc)
            {
                hostBox.Text = !string.IsNullOrEmpty(acc.ImapHost) ? acc.ImapHost : acc.Server;
                usernameBox.Text = acc.Email ?? string.Empty;
                passwordBox.Password = string.Empty;
                hostBox.IsEnabled = false;
                usernameBox.IsEnabled = false;
                passwordBox.IsEnabled = false;
            }
            else
            {
                hostBox.IsEnabled = true;
                usernameBox.IsEnabled = true;
                passwordBox.IsEnabled = true;
            }
        }
    }
}
