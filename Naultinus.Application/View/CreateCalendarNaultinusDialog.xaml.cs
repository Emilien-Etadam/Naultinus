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
        public Guid? SelectedZimbraAccountId => ZimbraAccountPickerHelper.GetSelectedAccountId(ZimbraAccountCombo);

        private List<CalDAVCalendarInfo>? _calendarList;

        public CreateCalendarNaultinusDialog() : this(null) { }

        public CreateCalendarNaultinusDialog(Guid? preselectedZimbraAccountId)
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

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb && ZimbraAccountPickerHelper.GetSelectedAccount(ZimbraAccountCombo) == null)
                Password = pb.Password;
        }

        private async void LoadCalendarsButton_Click(object sender, RoutedEventArgs e)
        {
            CalDAVUrl = CalDAVUrlTextBox.Text?.Trim() ?? "";
            Username = UsernameTextBox.Text?.Trim() ?? "";
            Password = ZimbraAccountPickerHelper.GetDiscoveryPassword(ZimbraAccountCombo, PasswordBox);
            if (string.IsNullOrWhiteSpace(CalDAVUrl) || string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show(Strings.CaldavEnterUrlUser, Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                using var client = new CalDAVClient(CalDAVUrl, Username, Password);
                var service = new CalendarCalDAVService(client);
                _calendarList = await service.GetCalendarListAsync();
                CalendarsListBox.ItemsSource = _calendarList;
                CalendarsListBox.SelectedItems.Clear();
                if (_calendarList.Count == 0)
                    MessageBox.Show(Strings.CaldavNoCalendar, Strings.CalendarTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
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
            if (ZimbraAccountPickerHelper.GetSelectedAccount(ZimbraAccountCombo) == null)
                Password = PasswordBox.Password;
            else
                Password = string.Empty;

            if (CalendarsListBox.SelectedItems.Count > 0)
                SelectedCalendarIds = CalendarsListBox.SelectedItems.Cast<CalDAVCalendarInfo>().Select(c => c.Href).ToList();
            var url = CalDAVUrlTextBox.Text?.Trim() ?? "";
            var user = UsernameTextBox.Text?.Trim() ?? "";
            CalDAVUrl = url;
            Username = user;
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(Strings.CaldavEnterBaseUrl, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(Strings.CaldavHttpsRequired, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(user))
            {
                MessageBox.Show(Strings.MailEnterUsername, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
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
