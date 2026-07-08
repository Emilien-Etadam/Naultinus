using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    [Serializable]
    public class CalDAVTask : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public bool Completed { get; set; }
        public DateTime? CompletedDate { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;
        /// <summary>Chemin/href serveur (ex. nom fichier .ics).</summary>
        private string _calDAVId = string.Empty;
        public string CalDAVId
        {
            get => _calDAVId;
            set
            {
                if (_calDAVId == value) return;
                _calDAVId = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
        /// <summary>Uid iCalendar (identifiant interne du VTODO).</summary>
        public string Uid { get; set; } = string.Empty;
        public string CalDAVEtag { get; set; } = string.Empty;

        [XmlIgnore]
        public string DueDateDisplay => DueDate.HasValue
            ? DueDate.Value.ToString("ddd dd MMM")
            : string.Empty;

        [XmlIgnore]
        public string DueDateColor
        {
            get
            {
                if (!DueDate.HasValue || Completed) return "#A0A0A0";
                if (DueDate.Value.Date < DateTime.Today) return "#FF6B6B";
                if (DueDate.Value.Date == DateTime.Today) return "#60CDFF";
                return "#A0A0A0";
            }
        }
        
        public CalDAVTask() { }
        
        public CalDAVTask(string title)
        {
            Title = title;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}