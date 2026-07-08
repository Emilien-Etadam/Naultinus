using System;

namespace Naultinus.Model
{
    public class CalendarEvent
    {
        public string Uid { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DtStart { get; set; }
        public DateTime DtEnd { get; set; }
        public string Location { get; set; } = string.Empty;
        public bool IsAllDay { get; set; }
        public string DayHeader { get; set; } = string.Empty;
        public bool IsToday { get; set; }

        public string DayHeaderDisplay => IsToday
            ? DayHeader + " — Aujourd'hui"
            : DayHeader;

        public string CalendarName { get; set; } = string.Empty;
        public string Color { get; set; } = "#708090";
        public string CalDAVHref { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;

        public string TimeDisplay => IsAllDay
            ? "Toute la journée"
            : DtStart.ToString("t") + " → " + DtEnd.ToString("t");
    }
}
