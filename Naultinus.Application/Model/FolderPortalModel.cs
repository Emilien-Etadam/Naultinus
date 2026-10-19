using System.Collections.Generic;
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

        /// <summary>Pile de navigation (chemins visités), persistée avec le portail.</summary>
        [XmlArray("NavigationHistory")]
        [XmlArrayItem("Path")]
        public List<string> NavigationHistory { get; set; } = new List<string>();

        /// <summary>Index courant dans <see cref="NavigationHistory"/> (-1 si vide).</summary>
        public int NavigationIndex { get; set; } = -1;
    }
}
