using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Services;

namespace Naultinus.View
{
    public partial class CreateTaskNaultinusDialog : Window
    {
        public string NaultinusTitle { get; set; } = Strings.DefaultTaskNaultinusTitle;
        public string CalDAVUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public List<string> SelectedTaskListIds { get; private set; } = new();

        /// <summary>Le compte n'est plus choisi par fenêtre. Les appelants reçoivent null.</summary>
        public Guid? SelectedZimbraAccountId => null;

        public bool IsLocalOnly { get; private set; }

        private readonly bool _sharedAccountConfigured;
        private List<CalDAVCalendarInfo>? _taskLists;

        public CreateTaskNaultinusDialog() : this(null) { }

        public CreateTaskNaultinusDialog(Guid? preselectedZimbraAccountId)
        {
            _ = preselectedZimbraAccountId;
            InitializeComponent();
            DataContext = this;
            _sharedAccountConfigured = SharedCalDavAccount.IsConfigured();
            AccountStatusText.Text = SharedCalDavAccount.DescribeStatus(AppSettingsStore.Load());
            if (!_sharedAccountConfigured)
            {
                LocalOnlyCheckBox.IsChecked = true;
                LocalOnlyCheckBox.IsEnabled = false;
            }

            ApplyLocalOnlyState();
        }

        private void LocalOnlyCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyLocalOnlyState();

        private void ApplyLocalOnlyState()
        {
            var localOnly = LocalOnlyCheckBox.IsChecked == true || !_sharedAccountConfigured;
            LoadListsButton.IsEnabled = !localOnly;
            TaskListsListBox.IsEnabled = !localOnly;
        }

        private async void LoadListsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsStore.Load();
            if (!SharedCalDavAccount.IsConfigured(settings))
            {
                MessageBox.Show(Strings.SharedCalDavMissing, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var client = new CalDAVClient(settings.CalDavBaseUrl, settings.CalDavUsername, SharedCalDavAccount.ReadPassword(settings));
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
            NaultinusTitle = string.IsNullOrWhiteSpace(NaultinusTitleTextBox.Text)
                ? Strings.DefaultTaskNaultinusTitle
                : NaultinusTitleTextBox.Text.Trim();
            CalDAVUrl = string.Empty;
            Username = string.Empty;
            Password = string.Empty;
            IsLocalOnly = LocalOnlyCheckBox.IsChecked == true || !_sharedAccountConfigured;
            if (IsLocalOnly)
            {
                SelectedTaskListIds = new List<string>();
                DialogResult = true;
                Close();
                return;
            }

            SelectedTaskListIds = TaskListsListBox.SelectedItems
                .Cast<CalDAVCalendarInfo>()
                .Select(c => c.Href)
                .ToList();

            if (SelectedTaskListIds.Count == 0)
            {
                MessageBox.Show(Strings.SelectTaskListOrLocal, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
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
