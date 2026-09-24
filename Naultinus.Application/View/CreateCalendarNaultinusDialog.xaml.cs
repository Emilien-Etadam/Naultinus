using System;
using System.Collections.Generic;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    public partial class CreateCalendarNaultinusDialog : Window
    {
        public string NaultinusTitle { get; set; } = Strings.CalendarDefaultName;
        public string CalDAVUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public List<string> SelectedCalendarIds { get; private set; } = new List<string>();
        public CalendarViewMode ViewMode { get; set; } = CalendarViewMode.Agenda;
        public int DaysToShow { get; set; } = 7;

        /// <summary>Le compte n'est plus choisi par fenêtre. Les appelants reçoivent null.</summary>
        public Guid? SelectedZimbraAccountId => null;

        public bool IsLocalOnly { get; private set; }

        private readonly bool _sharedAccountConfigured;
        private List<CalDAVCalendarInfo>? _calendarList;

        public CreateCalendarNaultinusDialog() : this(null) { }

        public CreateCalendarNaultinusDialog(Guid? preselectedZimbraAccountId)
        {
            // Le compte CalDAV est celui des paramètres, pas celui présélectionné pour cette fenêtre.
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
            LoadCalendarsButton.IsEnabled = !localOnly;
            CalendarsListBox.IsEnabled = !localOnly;
        }

        private async void LoadCalendarsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsStore.Load();
            if (!SharedCalDavAccount.IsConfigured(settings))
            {
                MessageBox.Show(Strings.SharedCalDavMissing, Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var client = new CalDAVClient(settings.CalDavBaseUrl, settings.CalDavUsername, SharedCalDavAccount.ReadPassword(settings));
                var service = new CalendarCalDAVService(client);
                _calendarList = await service.GetCalendarListAsync();
                CalendarsListBox.ItemsSource = _calendarList;
                CalendarsListBox.SelectedItems.Clear();
                if (_calendarList.Count == 0)
                    MessageBox.Show(Strings.CaldavNoCalendar, Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.CaldavLoadCalendarFailedFormat, ex.Message), Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CalendarsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedCalendarIds = CalendarsListBox.SelectedItems.Cast<CalDAVCalendarInfo>().Select(c => c.Href).ToList();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            NaultinusTitle = string.IsNullOrWhiteSpace(NaultinusTitleTextBox.Text)
                ? Strings.CalendarDefaultName
                : NaultinusTitleTextBox.Text.Trim();
            CalDAVUrl = string.Empty;
            Username = string.Empty;
            Password = string.Empty;
            IsLocalOnly = LocalOnlyCheckBox.IsChecked == true || !_sharedAccountConfigured;
            if (IsLocalOnly)
            {
                SelectedCalendarIds = new List<string>();
                DialogResult = true;
                Close();
                return;
            }

            if (CalendarsListBox.SelectedItems.Count > 0)
                SelectedCalendarIds = CalendarsListBox.SelectedItems.Cast<CalDAVCalendarInfo>().Select(c => c.Href).ToList();
            else
                SelectedCalendarIds = new List<string>();

            if (SelectedCalendarIds.Count == 0)
            {
                MessageBox.Show(Strings.SelectCalendarOrLocal, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
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
