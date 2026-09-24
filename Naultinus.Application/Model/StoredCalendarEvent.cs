using System;

namespace Naultinus.Model
{
    /// <summary>
    /// Événement enregistré dans le state.xml du calendrier, sans serveur CalDAV.
    /// </summary>
    public class StoredCalendarEvent
    {
        public string Uid { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime DtStart { get; set; }
        public DateTime DtEnd { get; set; }
        public string Location { get; set; } = string.Empty;
        public bool IsAllDay { get; set; }
    }
}
