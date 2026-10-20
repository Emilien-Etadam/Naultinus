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

        /// <summary>Filtre de nom du portail (sous-chaîne), conservé entre deux exécutions.</summary>
        public string FilterQuery { get; set; } = "";

        /// <summary>Double-clic (ouvrir / entrer dans un dossier).</summary>
        public bool AllowDoubleClick { get; set; } = true;

        /// <summary>Glisser des éléments hors du portail.</summary>
        public bool AllowDragOut { get; set; } = true;

        /// <summary>Déposer des fichiers dans le portail.</summary>
        public bool AllowDrop { get; set; } = true;

        /// <summary>Menu contextuel (éléments et vide).</summary>
        public bool AllowContextMenu { get; set; } = true;

        /// <summary>Clavier dans le corps (hors raccourcis du bandeau).</summary>
        public bool AllowKeyboard { get; set; } = true;

        /// <summary>Masquer la flèche de raccourci sur les icônes (grille WPF ; DefView reste natif).</summary>
        public bool HideShortcutOverlay { get; set; }

        /// <summary>Masquer les superpositions de synchro cloud (grille WPF ; DefView reste natif).</summary>
        public bool HideSyncOverlay { get; set; }
    }
}
