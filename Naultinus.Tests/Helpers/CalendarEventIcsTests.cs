using System;
using Ical.Net.Serialization;
using Naultinus.Helpers;
using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class CalendarEventIcsTests
    {
        [Fact]
        public void BuildNewEvent_ContainsSummaryAndUid()
        {
            var ics = CalendarEventIcs.BuildNewEvent(
                new CalendarEvent
                {
                    Summary = "Réunion",
                    Description = "desc",
                    Location = "Paris",
                    DtStart = new DateTime(2026, 10, 22, 10, 0, 0),
                    DtEnd = new DateTime(2026, 10, 22, 11, 0, 0),
                    IsAllDay = false,
                },
                new CalendarSerializer());

            Assert.Contains("BEGIN:VEVENT", ics, StringComparison.Ordinal);
            Assert.Contains("SUMMARY:Réunion", ics, StringComparison.Ordinal);
            Assert.Contains("UID:", ics, StringComparison.Ordinal);
        }
    }
}
