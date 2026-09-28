using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Naultinus.Model;

namespace Naultinus.Services
{
    public interface ICalendarCalDAVService
    {
        Task<List<CalDAVCalendarInfo>> GetCalendarListAsync();
        Task<List<CalendarEvent>> GetEventsAsync(string calendarHref, DateTime start, DateTime rangeEnd, string colorHex);
        Task<string?> CreateEventAsync(string calendarHref, string icalData);

        /// <summary>
        /// Supprime la ressource d'un seul événement. Un href de collection calendrier est refusé.
        /// </summary>
        Task DeleteEventAsync(string eventHref, string? etag, IReadOnlyList<string>? calendarCollectionHrefs);
    }
}
