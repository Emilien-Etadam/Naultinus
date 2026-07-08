using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    [XmlType(Namespace = "io.stouder")]
    public class TaskNaultinusModel : NaultinusModelBase
    {
        public TaskNaultinusModel()
        {
            Type = NaultinusType.TaskNaultinus;
        }

        /// <summary>Si défini, les credentials sont résolus depuis ZimbraAccountStore (Phase 3.4).</summary>
        public Guid? ZimbraAccountId { get; set; }

        public string CalDAVUrl { get; set; } = string.Empty;
        public string CalDAVUsername { get; set; } = string.Empty;
        public string CalDAVPassword { get; set; } = string.Empty;
        public string TaskListId { get; set; } = string.Empty;
        public List<string> TaskListIds { get; set; } = new();
        public int SyncIntervalMinutes { get; set; } = 5;
        public bool EnableLogging { get; set; }
        public bool ShowCompletedTasks { get; set; } = true;

        /// <summary>Clés locales (id:/caldav:/uid:) des tâches masquées dans l’interface — elles restent sur le serveur.</summary>
        public List<string> HiddenTaskKeys { get; set; } = new();
    }
}
