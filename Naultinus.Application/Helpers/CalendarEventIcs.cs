using System;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>Sérialisation iCalendar d'un nouvel événement pour un PUT CalDAV.</summary>
    internal static class CalendarEventIcs
    {
        internal static string BuildNewEvent(CalendarEvent evt, CalendarSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(evt);
            ArgumentNullException.ThrowIfNull(serializer);

            var calendar = new Ical.Net.Calendar();
            var dtStart = evt.IsAllDay
                ? new CalDateTime(evt.DtStart.Year, evt.DtStart.Month, evt.DtStart.Day)
                : new CalDateTime(evt.DtStart);
            var dtEnd = evt.IsAllDay
                ? new CalDateTime(evt.DtEnd.Year, evt.DtEnd.Month, evt.DtEnd.Day)
                : new CalDateTime(evt.DtEnd);
            calendar.Events.Add(new Ical.Net.CalendarComponents.CalendarEvent
            {
                Uid = Guid.NewGuid().ToString(),
                Summary = evt.Summary,
                Description = evt.Description ?? "",
                Location = evt.Location ?? "",
                DtStart = dtStart,
                DtEnd = dtEnd,
            });
            return serializer.SerializeToString(calendar) ?? "";
        }
    }
}
