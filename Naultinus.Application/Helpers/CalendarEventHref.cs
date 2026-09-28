using System;
using System.Collections.Generic;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Décide si un href CalDAV désigne la ressource d'un seul événement,
    /// et non la collection calendrier.
    /// </summary>
    public static class CalendarEventHref
    {
        /// <summary>
        /// Retourne le href à supprimer quand il désigne une ressource enfant
        /// d'un des calendriers indiqués. Une collection, un href vide ou une
        /// adresse hors de ces calendriers est refusée.
        /// </summary>
        /// <param name="eventHref">Href de l'événement renvoyé par le serveur.</param>
        /// <param name="calendarCollectionHrefs">Href des collections affichées par le panneau.</param>
        /// <param name="resourceHref">Href d'origine, taillé, prêt pour DELETE.</param>
        /// <returns>Vrai si la suppression ne vise qu'une ressource événement.</returns>
        public static bool TryGetDeletableResource(
            string? eventHref,
            IReadOnlyList<string>? calendarCollectionHrefs,
            out string resourceHref)
        {
            resourceHref = string.Empty;
            if (string.IsNullOrWhiteSpace(eventHref) || calendarCollectionHrefs == null || calendarCollectionHrefs.Count == 0)
                return false;

            var eventPath = PathOf(eventHref);
            if (string.IsNullOrEmpty(eventPath))
                return false;

            var underCollection = false;
            foreach (var collectionHref in calendarCollectionHrefs)
            {
                if (string.IsNullOrWhiteSpace(collectionHref) || !SameOrigin(eventHref, collectionHref))
                    continue;

                var collectionPath = PathOf(collectionHref);
                if (string.IsNullOrEmpty(collectionPath))
                    continue;

                if (PathsEqual(eventPath, collectionPath))
                    return false;

                var prefix = collectionPath + "/";
                if (eventPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && eventPath.Length > prefix.Length)
                    underCollection = true;
            }

            if (!underCollection)
                return false;

            resourceHref = eventHref.Trim();
            return true;
        }

        /// <summary>Vrai si les deux href désignent la même ressource, une fois le chemin normalisé.</summary>
        public static bool SharesResource(string? leftHref, string? rightHref)
        {
            var left = PathOf(leftHref);
            var right = PathOf(rightHref);
            return left.Length > 0 && PathsEqual(left, right);
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool SameOrigin(string eventHref, string collectionHref)
        {
            var eventIsAbsolute = Uri.TryCreate(eventHref.Trim(), UriKind.Absolute, out var eventUri);
            var collectionIsAbsolute = Uri.TryCreate(collectionHref.Trim(), UriKind.Absolute, out var collectionUri);
            if (!eventIsAbsolute || !collectionIsAbsolute || eventUri == null || collectionUri == null)
                return true;

            return string.Equals(eventUri.Scheme, collectionUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(eventUri.IdnHost, collectionUri.IdnHost, StringComparison.OrdinalIgnoreCase)
                && eventUri.Port == collectionUri.Port;
        }

        private static string PathOf(string? href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return string.Empty;

            var trimmed = href.Trim();
            string path;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            {
                path = absolute.AbsolutePath;
            }
            else
            {
                var cut = trimmed.IndexOfAny(['?', '#']);
                path = cut >= 0 ? trimmed[..cut] : trimmed;
            }

            path = path.Replace('\\', '/');
            try
            {
                path = Uri.UnescapeDataString(path);
            }
            catch (UriFormatException)
            {
                // Un échappement invalide ne doit pas faire échouer le clic : on compare le chemin brut.
            }

            return path.TrimEnd('/');
        }
    }
}
