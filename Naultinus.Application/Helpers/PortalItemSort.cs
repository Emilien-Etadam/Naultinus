using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    internal enum PortalSortField
    {
        Name,
        Type,
        Date,
    }

    /// <summary>
    /// Tri de la grille du portail : dossiers d'abord, puis le critère demandé.
    /// La date met les plus récents en tête.
    /// </summary>
    internal static class PortalItemSort
    {
        internal static IReadOnlyList<FolderPortalItem> Order(IEnumerable<FolderPortalItem> items, PortalSortField field)
        {
            return Order(
                items,
                field,
                item => item.Name,
                item => item.IsDirectory,
                item => item.IsDirectory ? string.Empty : Path.GetExtension(item.Name),
                item => item.LastWriteUtc);
        }

        internal static IReadOnlyList<T> Order<T>(
            IEnumerable<T> items,
            PortalSortField field,
            Func<T, string> name,
            Func<T, bool> isDirectory,
            Func<T, string> extension,
            Func<T, DateTime> lastWriteUtc)
        {
            IOrderedEnumerable<T> grouped = items.OrderBy(item => isDirectory(item) ? 0 : 1);
            IOrderedEnumerable<T> ordered = field switch
            {
                PortalSortField.Type => grouped
                    .ThenBy(extension, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(name, StringComparer.OrdinalIgnoreCase),
                PortalSortField.Date => grouped
                    .ThenByDescending(lastWriteUtc)
                    .ThenBy(name, StringComparer.OrdinalIgnoreCase),
                _ => grouped.ThenBy(name, StringComparer.OrdinalIgnoreCase),
            };
            return ordered.ToList();
        }
    }
}
