using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Naultinus.Model;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests.Models
{
    public class MailNaultinusModelSerializationTests
    {
        [Fact]
        public void MailNaultinusModel_RoundTrip_PreservesFields()
        {
            var model = new MailNaultinusModel
            {
                Name = "Mail Test",
                ImapHost = "ssl0.ovh.net",
                ImapPort = 993,
                ImapUsername = "user@domain.com",
                ImapPassword = "encrypted",
                MonitoredFolders = new List<string> { "INBOX", "Sent" },
                DisplayMode = MailDisplayMode.CountAndSubjects,
                PollIntervalMinutes = 5,
                WebmailUrl = "https://webmail.example.com"
            };

            using var writer = new StringWriter();
            ViewModelBase.SharedSerializer.Serialize(writer, model);
            using var reader = new StringReader(writer.ToString());
            var d = (MailNaultinusModel)ViewModelBase.SharedSerializer.Deserialize(reader)!;

            Assert.Equal(model.Name, d.Name);
            Assert.Equal(model.ImapHost, d.ImapHost);
            Assert.Equal(model.ImapPort, d.ImapPort);
            Assert.Equal(model.ImapUsername, d.ImapUsername);
            Assert.Equal(model.ImapPassword, d.ImapPassword);
            Assert.Equal(2, d.MonitoredFolders.Count);
            Assert.Equal("INBOX", d.MonitoredFolders[0]);
            Assert.Equal("Sent", d.MonitoredFolders[1]);
            Assert.Equal(MailDisplayMode.CountAndSubjects, d.DisplayMode);
            Assert.Equal(5, d.PollIntervalMinutes);
            Assert.Equal(model.WebmailUrl, d.WebmailUrl);
        }

        [Fact]
        public void MailNaultinusModel_EmptyFolders_RoundTrip()
        {
            var model = new MailNaultinusModel { Name = "Empty", MonitoredFolders = new List<string>() };
            using var writer = new StringWriter();
            ViewModelBase.SharedSerializer.Serialize(writer, model);
            using var reader = new StringReader(writer.ToString());
            var d = (MailNaultinusModel)ViewModelBase.SharedSerializer.Deserialize(reader)!;
            Assert.NotNull(d.MonitoredFolders);
        }
    }
}
