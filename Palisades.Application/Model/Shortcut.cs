using System.IO;
using System.Xml.Serialization;

namespace Palisades.Model
{
    [XmlInclude(typeof(LnkShortcut))]
    [XmlInclude(typeof(UrlShortcut))]
    public abstract class Shortcut
    {
        private string name;
        private string iconPath;
        private string uriOrFileAction;

        public Shortcut() : this("", "", "")
        {

        }
        public Shortcut(string name, string iconPath, string uriOrFileAction)
        {
            this.name = name;
            this.iconPath = iconPath;
            this.uriOrFileAction = uriOrFileAction;
        }

        public string Name { get { return name; } set { name = value; } }

        public string IconPath { get { return iconPath; } set { iconPath = value; } }
        public string UriOrFileAction { get { return uriOrFileAction; } set { uriOrFileAction = value; } }

        public static string GetName(string filename) => Path.GetFileNameWithoutExtension(filename);
    }
}
