using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>Énumération filtrée / triée d'un dossier pour la grille WPF du portail.</summary>
    internal static class PortalFolderListing
    {
        internal static List<FolderPortalItem> Enumerate(
            string path,
            string filter,
            string iconsDir,
            bool hideShortcutOverlay,
            bool hideSyncOverlay,
            PortalSortField sortField,
            out long elapsedMilliseconds)
        {
            var found = new List<FolderPortalItem>();
            var loadingStarted = Stopwatch.StartNew();

            foreach (string dir in Directory.GetDirectories(path))
            {
                if (IsHiddenOrSystemEntry(dir))
                    continue;
                string dirName = Path.GetFileName(dir);
                if (!MatchesFilter(dirName, filter))
                    continue;
                string iconPath = AppPaths.GetOrCreateIcon(
                    dir, "folder_", iconsDir, hideShortcutOverlay, hideSyncOverlay);
                found.Add(new FolderPortalItem(dirName, dir, true, iconPath) { LastWriteUtc = LastWriteUtc(dir) });
            }

            foreach (string file in Directory.GetFiles(path))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith("~$", StringComparison.Ordinal) || IsHiddenOrSystemEntry(file))
                    continue;
                if (!MatchesFilter(fileName, filter))
                    continue;
                string iconPath = AppPaths.GetOrCreateIcon(
                    file, "file_", iconsDir, hideShortcutOverlay, hideSyncOverlay);
                found.Add(new FolderPortalItem(fileName, file, false, iconPath) { LastWriteUtc = LastWriteUtc(file) });
            }

            elapsedMilliseconds = loadingStarted.ElapsedMilliseconds;
            return new List<FolderPortalItem>(PortalItemSort.Order(found, sortField));
        }

        internal static bool MatchesFilter(string name, string filter)
            => string.IsNullOrEmpty(filter)
                || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        internal static bool IsHiddenOrSystemEntry(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("FolderPortal.IsHiddenOrSystemEntry", ex);
                return false;
            }
        }

        private static DateTime LastWriteUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("FolderPortal.LastWriteUtc", ex);
                return DateTime.MinValue;
            }
        }
    }
}
