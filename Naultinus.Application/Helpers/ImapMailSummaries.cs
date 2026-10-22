using System;
using System.Collections.Generic;
using System.Linq;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Projection pure des enveloppes IMAP vers <see cref="MailSummaryItem"/>
    /// (troncature expéditeur, tri par date). Isolée pour rester testable hors MailKit.
    /// </summary>
    internal static class ImapMailSummaries
    {
        internal const int MaxSenderLength = 50;

        internal static bool IsInbox(string? folderName)
            => string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase);

        internal static string FormatSender(string? from)
        {
            string sender = from ?? string.Empty;
            if (sender.Length <= MaxSenderLength)
                return sender;
            return string.Concat(sender.AsSpan(0, MaxSenderLength - 3), "...");
        }

        /// <summary>
        /// Construit la liste d'affichage : tri date décroissante, plafonnée à <paramref name="maxCount"/>.
        /// </summary>
        internal static List<MailSummaryItem> ToDisplayItems(
            IEnumerable<ImapEnvelopeSnippet> snippets,
            int maxCount)
        {
            if (maxCount <= 0)
                return new List<MailSummaryItem>();

            return snippets
                .OrderByDescending(s => s.Date)
                .Take(maxCount)
                .Select(s => new MailSummaryItem
                {
                    Sender = FormatSender(s.From),
                    Subject = s.Subject ?? string.Empty,
                    Date = s.Date,
                })
                .ToList();
        }
    }

    /// <summary>Enveloppe minimale pour les tests et le service IMAP (évite de mocker MailKit).</summary>
    internal readonly struct ImapEnvelopeSnippet
    {
        internal ImapEnvelopeSnippet(string? from, string? subject, DateTime date)
        {
            From = from;
            Subject = subject;
            Date = date;
        }

        internal string? From { get; }
        internal string? Subject { get; }
        internal DateTime Date { get; }
    }
}
