using System;
using System.Collections.Generic;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Découpe le chemin courant du portail en segments, de la racine jusqu'au dossier affiché.
    /// </summary>
    internal static class PortalBreadcrumb
    {
        internal static IReadOnlyList<PortalPathSegment> Build(string? rootPath, string? currentPath)
        {
            if (!PortalPathGuard.TryNormalize(rootPath, out string root)
                || !PortalPathGuard.TryNormalize(currentPath, out string current)
                || !PortalPathGuard.IsInsideRoot(root, current))
            {
                return Array.Empty<PortalPathSegment>();
            }

            var segments = new List<PortalPathSegment>
            {
                new PortalPathSegment(LabelOf(root), root),
            };
            if (PortalPathGuard.AreSame(root, current))
                return segments;

            string prefix = root[root.Length - 1] == '\\' ? root : root + "\\";
            string relative = current.Substring(prefix.Length);
            string accumulated = root[root.Length - 1] == '\\' ? root.TrimEnd('\\') : root;
            string[] parts = relative.Split('\\');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                    continue;

                accumulated = accumulated + "\\" + parts[i];
                segments.Add(new PortalPathSegment(parts[i], accumulated));
            }

            return segments;
        }

        private static string LabelOf(string normalized)
        {
            if (normalized.Length > 0 && normalized[normalized.Length - 1] == '\\')
                return normalized;

            int slash = normalized.LastIndexOf('\\');
            if (slash < 0 || slash >= normalized.Length - 1)
                return normalized;

            return normalized.Substring(slash + 1);
        }
    }
}
