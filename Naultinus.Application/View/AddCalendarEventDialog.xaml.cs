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

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var summary = SummaryTextBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(summary))
            {
                MessageBox.Show(Strings.SummaryRequired, Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDate = StartDatePicker?.SelectedDate ?? DateTime.Today;
            var endDate = EndDatePicker?.SelectedDate ?? DateTime.Today;
            var startTimeStr = StartTimeTextBox?.Text?.Trim() ?? "09:00";
            var endTimeStr = EndTimeTextBox?.Text?.Trim() ?? "10:00";
            var location = LocationTextBox?.Text?.Trim() ?? string.Empty;
            var isAllDay = IsAllDayCheckBox?.IsChecked == true;

            DateTime dtStart;
            DateTime dtEnd;

            if (isAllDay)
            {
                dtStart = startDate.Date;
                dtEnd = endDate.Date.AddDays(1);
            }
            else
            {
                if (!DateTime.TryParseExact(startTimeStr, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var st))
                    st = new DateTime(2000, 1, 1, 9, 0, 0);
                if (!DateTime.TryParseExact(endTimeStr, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var et))
                    et = new DateTime(2000, 1, 1, 10, 0, 0);
                dtStart = startDate.Date.Add(st.TimeOfDay);
                dtEnd = endDate.Date.Add(et.TimeOfDay);
                if (dtEnd <= dtStart)
                    dtEnd = dtStart.AddHours(1);
            }

            NewEvent = new CalendarEvent
            {
                Summary = summary,
                Description = string.Empty,
                Location = location,
                DtStart = dtStart,
                DtEnd = dtEnd,
                IsAllDay = isAllDay
            };

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
