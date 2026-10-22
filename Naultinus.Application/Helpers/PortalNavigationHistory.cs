using System.Collections.Generic;
using System.IO;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>Pile d'historique de navigation du portail (troncature de la branche « avant »).</summary>
    internal static class PortalNavigationHistory
    {
        internal static void Push(FolderPortalModel model, string path)
        {
            string normalized = Path.TrimEndingDirectorySeparator(path.Trim());
            List<string> hist = model.NavigationHistory;
            int idx = model.NavigationIndex;

            if (idx >= 0 && idx < hist.Count && PortalPathGuard.AreSame(hist[idx], normalized))
                return;

            if (idx >= 0 && idx < hist.Count - 1)
                hist.RemoveRange(idx + 1, hist.Count - idx - 1);

            hist.Add(normalized);
            model.NavigationIndex = hist.Count - 1;
        }

        internal static void SeedFromCurrentPath(FolderPortalModel model)
        {
            if (string.IsNullOrEmpty(model.CurrentPath))
                return;

            List<string> hist = model.NavigationHistory;
            if (hist.Count == 0)
            {
                hist.Add(Path.TrimEndingDirectorySeparator(model.CurrentPath.Trim()));
                model.NavigationIndex = 0;
                return;
            }

            if (model.NavigationIndex < 0 || model.NavigationIndex >= hist.Count)
                model.NavigationIndex = hist.Count - 1;
        }
    }
}
