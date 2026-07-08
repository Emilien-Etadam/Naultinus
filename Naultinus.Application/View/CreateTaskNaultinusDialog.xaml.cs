using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Services;

namespace Naultinus.View
{
    public partial class CreateTaskNaultinusDialog : Window
    {
        public string NaultinusTitle { get; set; } = "Task naultinus";
        public string CalDAVUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public List<string> SelectedTaskListIds { get; private set; } = new();
        public Guid? SelectedZimbraAccountId => ZimbraAccountPickerHelper.GetSelectedAccountId(ZimbraAccountCombo);

        private List<CalDAVCalendarInfo>? _taskLists;

        public CreateTaskNaultinusDialog() : this(null) { }

        public CreateTaskNaultinusDialog(Guid? preselectedZimbraAccountId)
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
            ZimbraAccountPickerHelper.ApplyCalDavSelection(ZimbraAccountCombo, CalDAVUrlTextBox, UsernameTextBox, PasswordBox);
            CalDAVUrl = CalDAVUrlTextBox.Text;
            Username = UsernameTextBox.Text;
            Password = string.Empty;
        }

        private async void LoadListsButton_Click(object sender, RoutedEventArgs e)
        {
            var url = CalDAVUrlTextBox.Text?.Trim() ?? "";
            var user = UsernameTextBox.Text?.Trim() ?? "";
            Password = ZimbraAccountPickerHelper.GetDiscoveryPassword(ZimbraAccountCombo, PasswordBox);

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(user))
            {
                MessageBox.Show(Strings.CaldavEnterUrlUser, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(Strings.CaldavHttpsRequired, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var client = new CalDAVClient(url, user, Password);
                var allCalendars = await client.DiscoverCalendarsAsync();
                _taskLists = allCalendars
                    .Where(c => c.SupportedComponents.Contains("VTODO", StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (_taskLists.Count == 0)
                {
                    MessageBox.Show(Strings.TaskNoVtodo, Strings.TaskListsTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    TaskListsListBox.ItemsSource = null;
                    return;
                }

                TaskListsListBox.ItemsSource = _taskLists;
                TaskListsListBox.SelectedItems.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.TaskLoadFailedFormat, ex.Message), Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (ZimbraAccountPickerHelper.GetSelectedAccount(ZimbraAccountCombo) == null)
                Password = PasswordBox.Password;
            else
                Password = string.Empty;

            NaultinusTitle = NaultinusTitleTextBox.Text;
            CalDAVUrl = CalDAVUrlTextBox.Text?.Trim() ?? "";
            Username = UsernameTextBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(CalDAVUrl))
            {
                MessageBox.Show(Strings.CaldavEnterServerUrl, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!CalDAVUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(Strings.CaldavHttpsRequired, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show(Strings.MailEnterUsername, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedTaskListIds = TaskListsListBox.SelectedItems
                .Cast<CalDAVCalendarInfo>()
                .Select(c => c.Href)
                .ToList();

            if (SelectedTaskListIds.Count == 0 && _taskLists != null && _taskLists.Count > 0)
            {
                MessageBox.Show(Strings.TaskSelectList, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
