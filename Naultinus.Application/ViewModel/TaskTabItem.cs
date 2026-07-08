using System.Collections.ObjectModel;
using Naultinus.Model;

namespace Naultinus.ViewModel
{
    public class TaskTabItem
    {
        public string ListId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ObservableCollection<CalDAVTask> Tasks { get; } = new();
    }
}
