using Naultinus.Properties;
using System;
using System.Globalization;
using System.Text;

namespace Naultinus.Services
{
    /// <summary>
    /// Texte affiché quand une requête CalDAV HTTP échoue.
    /// Le corps n'est repris que s'il s'agit d'une courte erreur texte ou CalDAV.
    /// Une page HTML (proxy 502, par exemple) n'est jamais recopiée dans le panneau.
    /// </summary>
    internal static class CalDavHttpFailure
    {
        /// <summary>
        /// Au-delà, le corps n'est plus une courte ligne de statut : on ne l'affiche pas.
        /// </summary>
        private const int MaxShortStatusLineLength = 180;

        private static readonly string[] HtmlTagNames =
        {
            "html", "head", "body", "title", "meta", "script", "style",
            "div", "center", "table", "h1", "br", "hr", "p",
        };

        internal static string Format(string method, int statusCode, string? reasonPhrase, string? responseBody, string? mediaType)
        {
            var status = FormatStatus(statusCode, reasonPhrase);
            switch (ClassifyBody(responseBody, mediaType, out var detail))
            {
                case BodyKind.Plain:
                    return string.Format(CultureInfo.CurrentCulture, Strings.CaldavHttpFailedFormat, method, status, detail);
                case BodyKind.Hidden:
                    return string.Format(CultureInfo.CurrentCulture, Strings.CaldavServerUnavailableFormat, method, status);
                default:
                    return string.Format(CultureInfo.CurrentCulture, Strings.CaldavHttpFailedFormat, method, status, string.Empty).TrimEnd();
            }
        }

        private static string FormatStatus(int statusCode, string? reasonPhrase)
        {
            var code = statusCode.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(reasonPhrase))
                return code;

            var reason = CollapseToMax(reasonPhrase, 80);
            if (string.IsNullOrEmpty(reason) || LooksLikeHtml(reason) || reason.Contains('<'))
                return code;

            return code + " " + reason;
        }

        private static BodyKind ClassifyBody(string? responseBody, string? mediaType, out string detail)
        {
            detail = string.Empty;
            if (IsHtmlMediaType(mediaType))
                return BodyKind.Hidden;
            if (string.IsNullOrWhiteSpace(responseBody))
                return BodyKind.None;
            if (LooksLikeHtml(responseBody))
                return BodyKind.Hidden;

            var collapsed = CollapseToMax(responseBody, MaxShortStatusLineLength);
            if (collapsed == null)
                return BodyKind.Hidden;
            if (collapsed.Length == 0)
                return BodyKind.None;

            detail = collapsed;
            return BodyKind.Plain;
        }

        private static bool IsHtmlMediaType(string? mediaType)
        {
            return !string.IsNullOrWhiteSpace(mediaType)
                && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeHtml(string text)
        {
            var span = text.AsSpan().TrimStart();
            if (span.Length > 0 && span[0] == '\uFEFF')
                span = span.Slice(1).TrimStart();
            if (span.IsEmpty)
                return false;
            if (span.Length > 4096)
                span = span.Slice(0, 4096);

            var sample = span.ToString();
            if (sample.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var tag in HtmlTagNames)
            {
                if (ContainsHtmlTag(sample, tag))
                    return true;
            }

            return false;
        }

        private static bool ContainsHtmlTag(string sample, string tagName)
        {
            var needle = "<" + tagName;
            var index = 0;
            while (index < sample.Length)
            {
                var found = sample.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    return false;
                var after = found + needle.Length;
                if (after >= sample.Length || !char.IsLetterOrDigit(sample[after]))
                    return true;
                index = after;
            }

            return false;
        }

        /// <summary>
        /// Replie les blancs en une seule ligne. Retourne null si le texte dépasse <paramref name="maxLength"/>.
        /// </summary>
        private static string? CollapseToMax(string text, int maxLength)
        {
            var sb = new StringBuilder(Math.Min(text.Length, maxLength));
            var pendingSpace = false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                {
                    if (sb.Length > 0)
                        pendingSpace = true;
                    continue;
                }

                if (pendingSpace)
                {
                    if (sb.Length >= maxLength)
                        return null;
                    sb.Append(' ');
                    pendingSpace = false;
                }

                if (sb.Length >= maxLength)
                    return null;
                sb.Append(ch);
            }

            return sb.ToString();
        }

        private enum BodyKind
        {
            None,
            Plain,
            Hidden,
        }
    }
}
