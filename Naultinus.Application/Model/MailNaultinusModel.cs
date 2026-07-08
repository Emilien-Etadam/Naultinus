using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    [XmlType(Namespace = "io.stouder")]
    public class MailNaultinusModel : NaultinusModelBase
    {
        public MailNaultinusModel()
        {
            Type = NaultinusType.MailNaultinus;
        }

        public Guid? ZimbraAccountId { get; set; }
        public string ImapHost { get; set; } = string.Empty;
        public int ImapPort { get; set; } = 993;
        public string ImapUsername { get; set; } = string.Empty;
        public string ImapPassword { get; set; } = string.Empty;
        public List<string> MonitoredFolders { get; set; } = new List<string>();
        public MailDisplayMode DisplayMode { get; set; } = MailDisplayMode.CountOnly;
        public int MaxSubjectsShown { get; set; } = 5;
        public int PollIntervalMinutes { get; set; } = 3;
        public string? WebmailUrl { get; set; }
    }

    public enum MailDisplayMode
    {
        CountOnly,
        CountAndSubjects
    }
}
