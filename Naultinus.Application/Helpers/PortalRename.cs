using System;
using System.IO;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Nom cible d'un renommage dans le dossier courant. Null si le nom est vide, illégal, inchangé, ou déjà pris.
    /// </summary>
    internal static class PortalRename
    {
        internal static string? Target(string? directory, string? currentName, string? requestedName, Func<string, bool> exists)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(requestedName))
                return null;

            string name = requestedName.Trim();
            if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;
            if (string.Equals(name, currentName, StringComparison.Ordinal))
                return null;

            string source = Path.Combine(directory, currentName);
            string target = Path.Combine(directory, name);
            if (exists(target) && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                return null;

            return target;
        }
    }
}
