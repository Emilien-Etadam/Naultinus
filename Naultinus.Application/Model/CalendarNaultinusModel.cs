using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Naultinus.Helpers;

namespace Naultinus.Model
{
    [XmlType(Namespace = "io.stouder")]
    public class CalendarNaultinusModel : NaultinusModelBase
    {
        public CalendarNaultinusModel()
        {
            Type = NaultinusType.CalendarNaultinus;
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

        // Type tableau (et non List) : sur une propriété collection dont le getter renvoie une
        // collection recalculée, XmlSerializer remplit la List du getter en place au lieu d'appeler
        // le setter — le dictionnaire n'était alors jamais reconstruit à la désérialisation. Avec un
        // tableau, XmlSerializer crée un nouvel objet et appelle bien le setter.
        [XmlArray("CalendarColors")]
        [XmlArrayItem("Entry")]
        public CalendarColorEntry[] CalendarColorsList
        {
            get => CalendarColors
                .Select(pair => new CalendarColorEntry { CalendarId = pair.Key, Color = pair.Value })
                .ToArray();
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
