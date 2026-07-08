using System.Collections.ObjectModel;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    [XmlType(Namespace = "io.stouder")]
    public class StandardNaultinusModel : NaultinusModelBase
    {
        private ObservableCollection<Shortcut> _shortcuts = new();

        public StandardNaultinusModel()
        {
            Type = NaultinusType.Standard;
        }

        [XmlArrayItem(typeof(LnkShortcut))]
        [XmlArrayItem(typeof(UrlShortcut))]
        public ObservableCollection<Shortcut> Shortcuts { get => _shortcuts; set => _shortcuts = value; }
    }
}
