using Palisades.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ical.Net;
using Ical.Net.CalendarComponents;

namespace Palisades.Services
{
    /// <summary>
    /// Service CalDAV pour les calendriers (VEVENT). Utilise CalDAVClient pour le transport.
    /// </summary>
    public class CalendarCalDAVService : ICalendarCalDAVService, IDisposable
    {
        private readonly ICalDAVClient _client;

        public CalendarCalDAVService(ICalDAVClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        /// <summary>
        /// Découvre les collections de type calendrier (délègue à CalDAVClient.DiscoverCalendarsAsync).
        /// </summary>
        public async Task<List<CalDAVCalendarInfo>> GetCalendarListAsync()
        {
            return await _client.DiscoverCalendarsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Récupère les événements (VEVENT) dans la plage [start, end] pour un calendrier.
        /// calendarHref : HREF complet de la collection calendrier.
        /// </summary>
        public async Task<List<Model.CalendarEvent>> GetEventsAsync(string calendarHref, DateTime start, DateTime end)
        {
            var events = new List<Model.CalendarEvent>();
            var startUtc = start.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
            var endUtc = end.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
            var requestBody = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<c:calendar-query xmlns:c=""urn:ietf:params:xml:ns:caldav"" xmlns:d=""DAV:"">
    <d:prop><d:getetag></d:getetag><c:calendar-data></c:calendar-data></d:prop>
    <c:filter><c:comp-filter name=""VCALENDAR"">
        <c:comp-filter name=""VEVENT"">
            <c:time-range start=""{startUtc}"" end=""{endUtc}""/>
        </c:comp-filter>
    </c:comp-filter></c:filter>
</c:calendar-query>";

            var doc = await _client.ReportAsync(calendarHref, requestBody).ConfigureAwait(false);
            ParseEventsFromMultistatus(doc, calendarHref, "Calendar", "#708090", events);
            return events;
        }

        public async Task<string?> CreateEventAsync(string calendarHref, string icalData)
        {
            var uid = Guid.NewGuid().ToString();
            var resourceHref = calendarHref.TrimEnd('/') + "/" + uid + ".ics";
            return await _client.PutAsync(resourceHref, icalData).ConfigureAwait(false);
        }

        private static void ParseEventsFromMultistatus(XDocument xdoc, string calendarId, string calendarName, string defaultColorHex, List<Model.CalendarEvent> events)
        {
            var responses = CalDAVClient.ParseMultistatus(xdoc);
            foreach (var (href, props, _) in responses)
            {
                if (string.IsNullOrEmpty(href))
                    continue;
                if (!props.TryGetValue("getetag", out var etag))
                    etag = "";
                etag = etag.Trim('"');
                if (!props.TryGetValue("calendar-data", out var calendarData) || string.IsNullOrWhiteSpace(calendarData))
                    continue;
                try
                {
                    var calendar = Ical.Net.Calendar.Load(calendarData);
                    foreach (var evt in calendar.Events)
                        events.Add(MapIcalEventToCalendarEvent(evt, href, etag, calendarName, defaultColorHex));
                }
                catch
                {
                    /* ignorer un bloc calendar invalide */
                }
            }
        }

        private static Model.CalendarEvent MapIcalEventToCalendarEvent(Ical.Net.CalendarComponents.CalendarEvent evt, string caldavHref, string etag, string calendarName, string colorHex)
        {
            var start = evt.Start?.Value ?? DateTime.MinValue;
            var end = evt.End?.Value ?? evt.Start?.Value ?? DateTime.MinValue;
            return new Model.CalendarEvent
            {
                Uid = evt.Uid ?? "",
                Summary = evt.Summary ?? "",
                Description = evt.Description ?? "",
                DtStart = start,
                DtEnd = end,
                Location = evt.Location ?? "",
                IsAllDay = evt.IsAllDay,
                CalendarName = calendarName,
                Color = colorHex,
                CalDAVHref = caldavHref,
                ETag = etag
            };
        }
    }

    public class CalDAVCalendarInfo
    {
        public string CalendarId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Href { get; set; } = "";
        public List<string> SupportedComponents { get; set; } = new List<string>();
    }
}
