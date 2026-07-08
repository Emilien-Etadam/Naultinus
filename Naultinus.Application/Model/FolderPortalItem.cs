namespace Naultinus.Model
{
    public class FolderPortalItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public string IconPath { get; set; }

        public FolderPortalItem(string name, string fullPath, bool isDirectory, string iconPath)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            IconPath = iconPath;
        }
    }
}
