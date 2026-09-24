using System;

namespace Naultinus.Model
{
    /// <summary>
    /// Tâche enregistrée dans le state.xml de la fenêtre, sans serveur CalDAV.
    /// </summary>
    public class StoredLocalTask
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public bool Completed { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
    }
}
