using System;
using System.Collections.ObjectModel;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    [Serializable]
    public class CalDAVTaskList
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CalDAVUrl { get; set; } = string.Empty;
        public string CalDAVId { get; set; } = string.Empty;
        public string CalDAVEtag { get; set; } = string.Empty;
        public ObservableCollection<CalDAVTask> Tasks { get; set; } = new ObservableCollection<CalDAVTask>();
        
        public CalDAVTaskList() { }
        
        public CalDAVTaskList(string name, string displayName, string caldavUrl)
        {
            Name = name;
            DisplayName = displayName;
            CalDAVUrl = caldavUrl;
        }
    }
}