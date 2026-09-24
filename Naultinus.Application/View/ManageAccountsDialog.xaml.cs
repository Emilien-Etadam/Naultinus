using Naultinus.Properties;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    public partial class ManageAccountsDialog : Window
    {
        private List<ZimbraAccount> _accounts = new List<ZimbraAccount>();

        public ManageAccountsDialog()
        {
            InitializeComponent();
            LoadAccounts();
        }

        private void ShowAccountInForm(ZimbraAccount? account)
        {
            CalDavUrlTextBox.Text = account?.CalDAVBaseUrl ?? string.Empty;
            CalDavUsernameTextBox.Text = account?.Email ?? string.Empty;
            CalDavPasswordBox.Password = string.Empty;
            CalDavStatusText.Text = SharedCalDavAccount.DescribeStatus(SharedCalDavAccount.FindMarked(_accounts));
        }

        private void SaveCalDavButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = AccountsListBox.SelectedItem as ZimbraAccount;
            var target = selected ?? new ZimbraAccount();
            if (!SharedCalDavAccount.TryApplyToAccount(target, CalDavUrlTextBox.Text, CalDavUsernameTextBox.Text, CalDavPasswordBox.Password, out var error))
            {
                MessageBox.Show(SharedCalDavAccount.Describe(error), Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selected == null)
                _accounts.Add(target);
            SharedCalDavAccount.MarkExclusive(_accounts, target.Id);
            SaveAccounts();
            LoadAccounts(target.Id);
            MessageBox.Show(Strings.SharedCalDavSaved, Strings.AccountTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearCalDavButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Strings.ConfirmClearCalDav, Strings.ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            SharedCalDavAccount.UnmarkAll(_accounts);
            SaveAccounts();
            LoadAccounts(selectMarked: false);
            MessageBox.Show(Strings.SharedCalDavCleared, Strings.AccountTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UseZimbraForCalDavButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is not ZimbraAccount acc)
                return;
            if (!SharedCalDavAccount.IsUsable(acc, out var error))
            {
                MessageBox.Show(SharedCalDavAccount.Describe(error), Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SharedCalDavAccount.MarkExclusive(_accounts, acc.Id);
            SaveAccounts();
            LoadAccounts(acc.Id);
            MessageBox.Show(Strings.SharedCalDavSaved, Strings.AccountTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadAccounts(Guid? selectId = null, bool selectMarked = true)
        {
            _accounts = ZimbraAccountStore.Load();
            AccountsListBox.ItemsSource = null;
            AccountsListBox.ItemsSource = _accounts;
            ZimbraAccount? select = null;
            if (selectId is Guid id)
                select = _accounts.Find(a => a.Id == id);
            else if (selectMarked)
                select = _accounts.Find(a => a.UsedByCalendarsAndTasks);
            AccountsListBox.SelectedItem = select;
            if (AccountsListBox.SelectedItem == null)
                ShowAccountInForm(null);
        }

        private void SaveAccounts()
        {
            ZimbraAccountStore.Save(_accounts);
        }

        private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = AccountsListBox.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            TestButton.IsEnabled = hasSelection;
            CreateNaultinusButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            UseZimbraForCalDavButton.IsEnabled = hasSelection;
            if (AccountsListBox.SelectedItem is ZimbraAccount acc)
                ShowAccountInForm(acc);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new EditZimbraAccountDialog();
            if (dialog.ShowDialog() == true && dialog.Account != null)
            {
                _accounts.Add(dialog.Account);
                SaveAccounts();
                LoadAccounts(dialog.Account.Id);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is not ZimbraAccount acc) return;
            var dialog = new EditZimbraAccountDialog(acc);
            if (dialog.ShowDialog() == true && dialog.Account != null)
            {
                SaveAccounts();
                LoadAccounts(acc.Id);
            }
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is not ZimbraAccount acc) return;
            var password = CredentialEncryptor.Decrypt(acc.EncryptedPassword ?? "");
            acc.LastTestStatus = Strings.AccountTestingStatus;

            try
            {
                // Test CalDAV
                if (!string.IsNullOrEmpty(acc.CalDAVBaseUrl))
                {
                    using var client = new CalDAVClient(acc.CalDAVBaseUrl, acc.Email ?? "", password);
                    var caldav = new Services.CalDAVService(client);
                    await caldav.GetTaskListsAsync();
                }
                // Test IMAP si l'hôte est renseigné
                if (!string.IsNullOrEmpty(acc.ImapHost) || !string.IsNullOrEmpty(acc.Server))
                {
                    var host = !string.IsNullOrEmpty(acc.ImapHost) ? acc.ImapHost : acc.Server;
                    var imap = new ImapMailService(host, 993, acc.Email, password);
                    await imap.ConnectAsync();
                    imap.Disconnect();
                }
                acc.LastTestStatus = Strings.AccountTestOk;
            }
            catch (System.Exception ex)
            {
                acc.LastTestStatus = string.Format(CultureInfo.CurrentCulture, Strings.AccountTestFailedFormat, ex.Message);
            }

            SaveAccounts();
            LoadAccounts(acc.Id);
        }

        private void CreateNaultinusButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is not ZimbraAccount acc) return;

            var menu = new ContextMenu();
            menu.Items.Add(CreateNaultinusMenuItem(Strings.MenuNewCalendarNaultinus, () =>
                NaultinusManager.ShowCreateCalendarNaultinusDialog(preselectedZimbraAccountId: acc.Id)));
            menu.Items.Add(CreateNaultinusMenuItem(Strings.MenuNewMailNaultinus, () =>
                NaultinusManager.ShowCreateMailNaultinusDialog(preselectedZimbraAccountId: acc.Id)));
            menu.Items.Add(CreateNaultinusMenuItem(Strings.MenuNewTaskNaultinus, () =>
                NaultinusManager.ShowCreateTaskNaultinusDialog(preselectedZimbraAccountId: acc.Id)));

            if (sender is Button btn)
                menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        private static MenuItem CreateNaultinusMenuItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is not ZimbraAccount acc) return;
            if (MessageBox.Show(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.DeleteAccountFormat, acc.Email), Strings.ConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _accounts.Remove(acc);
            SaveAccounts();
            LoadAccounts();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
