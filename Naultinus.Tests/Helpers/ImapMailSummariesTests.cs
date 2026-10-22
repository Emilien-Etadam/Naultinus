using System;
using System.Collections.Generic;
using Naultinus.Helpers;
using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class ImapMailSummariesTests
    {
        [Theory]
        [InlineData("INBOX", true)]
        [InlineData("inbox", true)]
        [InlineData("Inbox", true)]
        [InlineData("Junk", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsInbox_MatchesCaseInsensitive(string? name, bool expected)
        {
            Assert.Equal(expected, ImapMailSummaries.IsInbox(name));
        }

        [Fact]
        public void FormatSender_LeavesShortUnchanged()
        {
            Assert.Equal("alice@example.test", ImapMailSummaries.FormatSender("alice@example.test"));
            Assert.Equal(string.Empty, ImapMailSummaries.FormatSender(null));
        }

        [Fact]
        public void FormatSender_TruncatesLongWithEllipsis()
        {
            string longFrom = new string('x', ImapMailSummaries.MaxSenderLength + 10);
            string formatted = ImapMailSummaries.FormatSender(longFrom);

            Assert.Equal(ImapMailSummaries.MaxSenderLength, formatted.Length);
            Assert.EndsWith("...", formatted, StringComparison.Ordinal);
            Assert.StartsWith(new string('x', ImapMailSummaries.MaxSenderLength - 3), formatted, StringComparison.Ordinal);
        }

        [Fact]
        public void ToDisplayItems_OrdersByDateDescending_AndCapsCount()
        {
            var snippets = new List<ImapEnvelopeSnippet>
            {
                new("old@test", "Old", new DateTime(2020, 1, 1)),
                new("new@test", "New", new DateTime(2024, 6, 1)),
                new("mid@test", "Mid", new DateTime(2022, 3, 1)),
            };

            List<MailSummaryItem> items = ImapMailSummaries.ToDisplayItems(snippets, maxCount: 2);

            Assert.Equal(2, items.Count);
            Assert.Equal("New", items[0].Subject);
            Assert.Equal("Mid", items[1].Subject);
        }

        [Fact]
        public void ToDisplayItems_MaxCountZero_ReturnsEmpty()
        {
            var snippets = new[] { new ImapEnvelopeSnippet("a", "s", DateTime.UtcNow) };
            Assert.Empty(ImapMailSummaries.ToDisplayItems(snippets, 0));
        }
    }
}
