using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Naultinus.Model;
using Naultinus.Services;

namespace Naultinus.Helpers
{
    /// <summary>Agrégation d'événements CalDAV sur une plage visible.</summary>
    internal static class CalendarEventQuery
    {
        internal static async Task<List<CalendarEvent>> FetchVisibleAsync(
            ICalendarCalDAVService service,
            IReadOnlyList<string> calendarIds,
            IReadOnlyDictionary<string, string> colors,
            DateTime rangeStart,
            DateTime rangeEnd)
        {
            var allEvents = new List<CalendarEvent>();
            for (var i = 0; i < calendarIds.Count; i++)
            {
                var calId = calendarIds[i];
                var color = colors[calId];
                var list = await service.GetEventsAsync(calId, rangeStart, rangeEnd, color).ConfigureAwait(false);
                allEvents.AddRange(list);
            }

            return allEvents
                .Where(e => e.DtEnd > rangeStart && e.DtStart < rangeEnd)
                .OrderBy(e => AgendaAnchor.DisplayDay(e.DtStart, rangeStart))
                .ThenBy(e => e.DtStart)
                .ToList();
        }
    }
}
