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

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                var next = value ?? string.Empty;
                if (_title == next)
                    return;
                _title = next;
                OnPropertyChanged();
            }
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set
            {
                var next = value ?? string.Empty;
                if (_description == next)
                    return;
                _description = next;
                OnPropertyChanged();
            }
        }

        private DateTime? _dueDate;
        public DateTime? DueDate
        {
            get => _dueDate;
            set
            {
                if (_dueDate == value)
                    return;
                _dueDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DueDateDisplay));
                OnPropertyChanged(nameof(DueDateColor));
            }
        }
        private bool _completed;
        public bool Completed
        {
            get => _completed;
            set
            {
                if (_completed == value)
                    return;
                _completed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DueDateColor));
            }
        }

        private DateTime? _completedDate;
        public DateTime? CompletedDate
        {
            get => _completedDate;
            set
            {
                if (_completedDate == value)
                    return;
                _completedDate = value;
                OnPropertyChanged();
            }
        }

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