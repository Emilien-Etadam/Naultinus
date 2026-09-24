using Naultinus.Model;
using Naultinus.Properties;
using System;
using System.Globalization;
using System.Windows;

namespace Naultinus.View
{
    public partial class AddCalendarEventDialog : Window
    {
        public AddCalendarEventDialog()
        {
            InitializeComponent();
            Summary = string.Empty;
            StartDate = DateTime.Today;
            EndDate = DateTime.Today;
            StartTime = "09:00";
            EndTime = "10:00";
            Location = string.Empty;
            IsAllDay = false;
            DataContext = this;
        }

        public string Summary { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public string Location { get; set; }
        public bool IsAllDay { get; set; }

        public CalendarEvent? NewEvent { get; private set; }

        /// <summary>Brouillon validé, utilisé pour l'enregistrement local.</summary>
        public StoredCalendarEvent? Draft { get; private set; }

        private string? _existingUid;

        public AddCalendarEventDialog(CalendarEvent existing)
            : this()
        {
            _existingUid = existing.Uid;
            SummaryTextBox.Text = existing.Summary ?? string.Empty;
            StartDatePicker.SelectedDate = existing.DtStart.Date;
            if (existing.IsAllDay)
            {
                var inclusiveEnd = existing.DtEnd.Date;
                if (existing.DtEnd.TimeOfDay == TimeSpan.Zero && inclusiveEnd > existing.DtStart.Date)
                    inclusiveEnd = inclusiveEnd.AddDays(-1);
                EndDatePicker.SelectedDate = inclusiveEnd;
            }
            else
            {
                EndDatePicker.SelectedDate = existing.DtEnd.Date;
            }

            StartTimeTextBox.Text = existing.DtStart.ToString("HH:mm", CultureInfo.InvariantCulture);
            EndTimeTextBox.Text = existing.DtEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
            LocationTextBox.Text = existing.Location ?? string.Empty;
            IsAllDayCheckBox.IsChecked = existing.IsAllDay;
            Title = Strings.WindowTitleEditEvent;
            CreateButton.Content = Strings.ButtonSave;
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Naultinus.Helpers.LocalPlannerStore.TryBuildEvent(
                SummaryTextBox?.Text,
                LocationTextBox?.Text,
                string.Empty,
                StartDatePicker?.SelectedDate,
                EndDatePicker?.SelectedDate,
                StartTimeTextBox?.Text,
                EndTimeTextBox?.Text,
                IsAllDayCheckBox?.IsChecked == true,
                _existingUid,
                out var draft,
                out var error))
            {
                MessageBox.Show(Naultinus.Helpers.LocalPlannerStore.Describe(error), Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Draft = draft;
            NewEvent = Naultinus.Helpers.LocalPlannerStore.ToCalendarEvent(draft!);
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
