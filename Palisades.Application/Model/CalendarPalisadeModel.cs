using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Palisades.Helpers;

namespace Palisades.Model
{
    [XmlType(Namespace = "io.stouder")]
    public class CalendarPalisadeModel : PalisadeModelBase
    {
        public CalendarPalisadeModel()
        {
            Type = PalisadeType.CalendarPalisade;
        }

        /// <summary>Si défini, les credentials viennent de ZimbraAccountStore.</summary>
        public Guid? ZimbraAccountId { get; set; }
        public string CalDAVBaseUrl { get; set; } = string.Empty;
        public string CalDAVUsername { get; set; } = string.Empty;
        public string CalDAVPassword { get; set; } = string.Empty;
        public List<string> CalendarIds { get; set; } = new List<string>();

        /// <summary>Couleur hex par identifiant de calendrier (HREF CalDAV), pour un rendu stable.</summary>
        [XmlIgnore]
        public Dictionary<string, string> CalendarColors { get; set; } = new Dictionary<string, string>();

        [XmlArray("CalendarColors")]
        [XmlArrayItem("Entry")]
        public List<CalendarColorEntry> CalendarColorsList
        {
            get => CalendarColors
                .Select(pair => new CalendarColorEntry { CalendarId = pair.Key, Color = pair.Value })
                .ToList();
            set
            {
                CalendarColors = new Dictionary<string, string>();
                if (value == null)
                    return;

                foreach (var entry in value)
                {
                    if (string.IsNullOrWhiteSpace(entry.CalendarId))
                        continue;

                    CalendarColors[entry.CalendarId] = CalendarColorHelper.IsValidHexColor(entry.Color)
                        ? entry.Color.Trim()
                        : CalendarColorHelper.DefaultColor;
                }
            }
        }

        public CalendarViewMode ViewMode { get; set; } = CalendarViewMode.Agenda;
        public int DaysToShow { get; set; } = 7;
    }

    public enum CalendarViewMode
    {
        Agenda,
        Day,
        Week
    }
}
