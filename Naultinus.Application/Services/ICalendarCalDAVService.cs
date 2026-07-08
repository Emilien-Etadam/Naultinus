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
    }
}
