using System;
using System.ComponentModel;
using Naultinus.Helpers;

namespace Naultinus.Model
{
    public class FolderPortalItem : INotifyPropertyChanged
    {
        private bool _selected;

        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public string IconPath { get; set; }
        public DateTime LastWriteUtc { get; set; }

        public FolderPortalItem(string name, string fullPath, bool isDirectory, string iconPath)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            IconPath = iconPath;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Nom affiché : extension connue masquée, comme dans l'Explorateur.</summary>
        public string DisplayName => ExplorerFileNames.ToDisplayName(
            Name,
            ExplorerFileNames.HideKnownExtensionsEnabled(),
            ExplorerFileNames.IsKnownExtension);

        /// <summary>Photo réelle si le fichier est une image, sinon l'icône en cache.</summary>
        public string PreviewPath => ExplorerFileNames.ResolvePreviewPath(IsDirectory ? null : FullPath, IconPath);

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
