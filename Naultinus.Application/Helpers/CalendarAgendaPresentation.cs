using System;
using System.Collections.Generic;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>En-têtes de jour et pastilles de légende pour la liste agenda.</summary>
    internal static class CalendarAgendaPresentation
    {
        internal static void DecorateDayHeaders(IList<CalendarEvent> ordered, DateTime rangeStart)
        {
            DateTime? prevDate = null;
            foreach (var evt in ordered)
            {
                var evtDate = AgendaAnchor.DisplayDay(evt.DtStart, rangeStart);
                evt.IsToday = evtDate == DateTime.Today;
                if (evtDate != prevDate)
                {
                    evt.DayHeader = evtDate.ToString("ddd dd MMM");
                    prevDate = evtDate;
                }
                else
                {
                    evt.DayHeader = string.Empty;
                }
            }
        }

        internal static List<(string DisplayName, string ColorHex)> BuildLegendEntries(
            IReadOnlyList<string>? calendarIds,
            IReadOnlyDictionary<string, string> colors)
        {
            var result = new List<(string DisplayName, string ColorHex)>();
            if (calendarIds == null)
                return result;

            foreach (var calendarId in calendarIds)
            {
                if (!colors.TryGetValue(calendarId, out var color))
                    continue;
                result.Add((CalendarColorHelper.GetDisplayNameFromHref(calendarId), color));
            }

            return result;
        }
    }
}
