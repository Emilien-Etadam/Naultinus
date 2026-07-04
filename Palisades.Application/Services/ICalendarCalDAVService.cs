using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Palisades.Model;

namespace Palisades.Services
{
    public interface ICalendarCalDAVService
    {
        Task<List<CalDAVCalendarInfo>> GetCalendarListAsync();
        Task<List<CalendarEvent>> GetEventsAsync(string calendarHref, DateTime start, DateTime rangeEnd);
        Task<string?> CreateEventAsync(string calendarHref, string icalData);
    }
}
