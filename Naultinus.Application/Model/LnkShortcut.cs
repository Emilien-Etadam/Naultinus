using Naultinus.Helpers;

namespace Naultinus.Model
{
    public class LnkShortcut : Shortcut
    {
        public LnkShortcut() : base()
        {
        }
        public LnkShortcut(string name, string iconPath, string uriOrFileAction) : base(name, iconPath, uriOrFileAction)
        {
        }

        public static LnkShortcut? BuildFrom(string shortcut, string naultinusIdentifier)
        {
            string? targetPath = LnkReader.GetTargetPath(shortcut);
            if (string.IsNullOrEmpty(targetPath))
                return null;

            string name = Shortcut.GetName(shortcut);
            string iconPath = Naultinus.Helpers.PDirectory.CreateIconPng(shortcut, naultinusIdentifier);

            return new LnkShortcut(name, iconPath, targetPath);
        }
    }
}
