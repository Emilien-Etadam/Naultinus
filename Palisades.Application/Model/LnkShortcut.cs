using Palisades.Helpers;

namespace Palisades.Model
{
    public class LnkShortcut : Shortcut
    {
        public LnkShortcut() : base()
        {
        }
        public LnkShortcut(string name, string iconPath, string uriOrFileAction) : base(name, iconPath, uriOrFileAction)
        {
        }

        public static LnkShortcut? BuildFrom(string shortcut, string palisadeIdentifier)
        {
            string? targetPath = LnkReader.GetTargetPath(shortcut);
            if (string.IsNullOrEmpty(targetPath))
                return null;

            string name = Shortcut.GetName(shortcut);
            string iconPath = Palisades.Helpers.PDirectory.CreateIconPng(shortcut, palisadeIdentifier);

            return new LnkShortcut(name, iconPath, targetPath);
        }
    }
}
