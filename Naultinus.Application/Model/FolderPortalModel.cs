using System.Xml.Serialization;

namespace Naultinus.Model
{
    [XmlType(Namespace = "io.stouder")]
    public class FolderPortalModel : NaultinusModelBase
    {
        private string _rootPath = "";
        private string _currentPath = "";

        public FolderPortalModel()
        {
            Type = NaultinusType.FolderPortal;
        }

        public string RootPath { get => _rootPath; set => _rootPath = value ?? ""; }
        public string CurrentPath { get => _currentPath; set => _currentPath = value ?? ""; }
    }
}
