using System;
using System.Collections.Generic;
using System.IO;
using Naultinus.Model;
using Naultinus.Serialization;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests.Serialization
{
    public class NaultinusXmlSerializationTests
    {
        [Fact]
        public void SharedSerializer_UsesCentralNaultinusModelSerializer()
        {
            Assert.Same(NaultinusXmlSerialization.NaultinusModelSerializer, ViewModelBase.SharedSerializer);
        }

        [Fact]
        public void StandardNaultinusModel_WithLnkAndUrlShortcuts_RoundTripsThroughCentralSerializer()
        {
            var model = new StandardNaultinusModel
            {
                Name = "StandardName",
                FenceX = 12,
                FenceY = 34,
                Width = 640,
                Height = 480,
                GroupId = "grp-a",
                TabOrder = 2,
            };
            model.Shortcuts.Add(new LnkShortcut("App", @"C:\icons\app.ico", @"C:\app.exe"));
            model.Shortcuts.Add(new UrlShortcut("Docs", @"D:\icons\doc.png", "https://example.com/docs"));

            var roundTrip = RoundTrip(model);

            Assert.Equal(model.SchemaVersion, roundTrip.SchemaVersion);
            Assert.Equal(model.Name, roundTrip.Name);
            Assert.Equal(model.FenceX, roundTrip.FenceX);
            Assert.Equal(model.FenceY, roundTrip.FenceY);
            Assert.Equal(model.Width, roundTrip.Width);
            Assert.Equal(model.Height, roundTrip.Height);
            Assert.Equal(model.GroupId, roundTrip.GroupId);
            Assert.Equal(model.TabOrder, roundTrip.TabOrder);
            Assert.Equal(2, roundTrip.Shortcuts.Count);

            var lnk = Assert.IsType<LnkShortcut>(roundTrip.Shortcuts[0]);
            Assert.Equal("App", lnk.Name);
            Assert.Equal(@"C:\icons\app.ico", lnk.IconPath);
            Assert.Equal(@"C:\app.exe", lnk.UriOrFileAction);

            var url = Assert.IsType<UrlShortcut>(roundTrip.Shortcuts[1]);
            Assert.Equal("Docs", url.Name);
            Assert.Equal(@"D:\icons\doc.png", url.IconPath);
            Assert.Equal("https://example.com/docs", url.UriOrFileAction);
        }

        [Fact]
        public void FolderPortalModel_RoundTripsThroughCentralSerializer()
        {
            var model = new FolderPortalModel
            {
                Name = "Browse",
                RootPath = @"D:\Roots\Share",
                CurrentPath = @"D:\Roots\Share\Projects",
                FenceX = 10,
                FenceY = 20,
                Width = 900,
                Height = 500,
                GroupId = "g-fp",
                TabOrder = 3,
            };

            var roundTrip = RoundTrip(model);

            Assert.Equal(model.SchemaVersion, roundTrip.SchemaVersion);
            Assert.Equal(model.Name, roundTrip.Name);
            Assert.Equal(model.RootPath, roundTrip.RootPath);
            Assert.Equal(model.CurrentPath, roundTrip.CurrentPath);
            Assert.Equal(model.FenceX, roundTrip.FenceX);
            Assert.Equal(model.FenceY, roundTrip.FenceY);
            Assert.Equal(model.Width, roundTrip.Width);
            Assert.Equal(model.Height, roundTrip.Height);
            Assert.Equal(model.GroupId, roundTrip.GroupId);
            Assert.Equal(model.TabOrder, roundTrip.TabOrder);
            Assert.Equal(NaultinusType.FolderPortal, roundTrip.Type);
        }

        [Fact]
        public void TaskNaultinusModel_RoundTripsThroughCentralSerializer()
        {
            var zid = Guid.Parse("a1b2c3d4-e5f6-4789-a012-3456789abcde");
            var model = new TaskNaultinusModel
            {
                Name = "Tasks",
                FenceX = 1,
                FenceY = 2,
                Width = 700,
                Height = 600,
                GroupId = "tg",
                TabOrder = 0,
                ZimbraAccountId = zid,
                CalDAVUrl = "https://mail.example.com/dav/",
                CalDAVUsername = "user@example.com",
                CalDAVPassword = "secret",
                TaskListId = "list-primary",
                TaskListIds = new List<string> { "a1", "b2" },
                SyncIntervalMinutes = 7,
                EnableLogging = true,
                ShowCompletedTasks = false,
            };

            var roundTrip = RoundTrip(model);

            Assert.Equal(model.SchemaVersion, roundTrip.SchemaVersion);
            Assert.Equal(model.Name, roundTrip.Name);
            Assert.Equal(model.FenceX, roundTrip.FenceX);
            Assert.Equal(model.FenceY, roundTrip.FenceY);
            Assert.Equal(model.Width, roundTrip.Width);
            Assert.Equal(model.Height, roundTrip.Height);
            Assert.Equal(model.GroupId, roundTrip.GroupId);
            Assert.Equal(model.TabOrder, roundTrip.TabOrder);
            Assert.Equal(zid, roundTrip.ZimbraAccountId);
            Assert.Equal(model.CalDAVUrl, roundTrip.CalDAVUrl);
            Assert.Equal(model.CalDAVUsername, roundTrip.CalDAVUsername);
            Assert.Equal(model.CalDAVPassword, roundTrip.CalDAVPassword);
            Assert.Equal(model.TaskListId, roundTrip.TaskListId);
            Assert.Equal(model.TaskListIds, roundTrip.TaskListIds);
            Assert.Equal(model.SyncIntervalMinutes, roundTrip.SyncIntervalMinutes);
            Assert.Equal(model.EnableLogging, roundTrip.EnableLogging);
            Assert.Equal(model.ShowCompletedTasks, roundTrip.ShowCompletedTasks);
            Assert.Equal(NaultinusType.TaskNaultinus, roundTrip.Type);
        }

        [Fact]
        public void CalendarNaultinusModel_RoundTripsThroughCentralSerializer()
        {
            var model = new CalendarNaultinusModel
            {
                Name = "Cal",
                FenceX = 5,
                FenceY = 6,
                Width = 400,
                Height = 300,
                GroupId = "cg",
                TabOrder = 1,
                ZimbraAccountId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                CalDAVBaseUrl = "https://cal.example.com/",
                CalDAVUsername = "caluser",
                CalDAVPassword = "pwd",
                CalendarIds = new List<string> { "cal-1", "cal-2" },
                ViewMode = CalendarViewMode.Week,
                DaysToShow = 14,
            };

            var roundTrip = RoundTrip(model);

            Assert.Equal(model.SchemaVersion, roundTrip.SchemaVersion);
            Assert.Equal(model.Name, roundTrip.Name);
            Assert.Equal(model.FenceX, roundTrip.FenceX);
            Assert.Equal(model.FenceY, roundTrip.FenceY);
            Assert.Equal(model.Width, roundTrip.Width);
            Assert.Equal(model.Height, roundTrip.Height);
            Assert.Equal(model.GroupId, roundTrip.GroupId);
            Assert.Equal(model.TabOrder, roundTrip.TabOrder);
            Assert.Equal(model.ZimbraAccountId, roundTrip.ZimbraAccountId);
            Assert.Equal(model.CalDAVBaseUrl, roundTrip.CalDAVBaseUrl);
            Assert.Equal(model.CalDAVUsername, roundTrip.CalDAVUsername);
            Assert.Equal(model.CalDAVPassword, roundTrip.CalDAVPassword);
            Assert.Equal(model.CalendarIds, roundTrip.CalendarIds);
            Assert.Equal(model.ViewMode, roundTrip.ViewMode);
            Assert.Equal(model.DaysToShow, roundTrip.DaysToShow);
            Assert.Equal(NaultinusType.CalendarNaultinus, roundTrip.Type);
        }

        [Fact]
        public void MailNaultinusModel_RoundTripsThroughCentralSerializer()
        {
            var model = new MailNaultinusModel
            {
                Name = "Mail",
                FenceX = 0,
                FenceY = 0,
                Width = 500,
                Height = 400,
                GroupId = "mg",
                TabOrder = 4,
                ZimbraAccountId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                ImapHost = "imap.example.com",
                ImapPort = 143,
                ImapUsername = "imapuser",
                ImapPassword = "imapsecret",
                MonitoredFolders = new List<string> { "INBOX", "Sent" },
                DisplayMode = MailDisplayMode.CountAndSubjects,
                MaxSubjectsShown = 10,
                PollIntervalMinutes = 5,
                WebmailUrl = "https://webmail.example.com/",
            };

            var roundTrip = RoundTrip(model);

            Assert.Equal(model.SchemaVersion, roundTrip.SchemaVersion);
            Assert.Equal(model.Name, roundTrip.Name);
            Assert.Equal(model.FenceX, roundTrip.FenceX);
            Assert.Equal(model.FenceY, roundTrip.FenceY);
            Assert.Equal(model.Width, roundTrip.Width);
            Assert.Equal(model.Height, roundTrip.Height);
            Assert.Equal(model.GroupId, roundTrip.GroupId);
            Assert.Equal(model.TabOrder, roundTrip.TabOrder);
            Assert.Equal(model.ZimbraAccountId, roundTrip.ZimbraAccountId);
            Assert.Equal(model.ImapHost, roundTrip.ImapHost);
            Assert.Equal(model.ImapPort, roundTrip.ImapPort);
            Assert.Equal(model.ImapUsername, roundTrip.ImapUsername);
            Assert.Equal(model.ImapPassword, roundTrip.ImapPassword);
            Assert.Equal(model.MonitoredFolders, roundTrip.MonitoredFolders);
            Assert.Equal(model.DisplayMode, roundTrip.DisplayMode);
            Assert.Equal(model.MaxSubjectsShown, roundTrip.MaxSubjectsShown);
            Assert.Equal(model.PollIntervalMinutes, roundTrip.PollIntervalMinutes);
            Assert.Equal(model.WebmailUrl, roundTrip.WebmailUrl);
            Assert.Equal(NaultinusType.MailNaultinus, roundTrip.Type);
        }

        private static T RoundTrip<T>(T model)
            where T : NaultinusModelBase
        {
            using var writer = new StringWriter();
            NaultinusXmlSerialization.NaultinusModelSerializer.Serialize(writer, model);
            using var reader = new StringReader(writer.ToString());
            return (T)NaultinusXmlSerialization.NaultinusModelSerializer.Deserialize(reader)!;
        }
    }
}
