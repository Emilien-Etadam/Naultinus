using System;
using System.Globalization;
using System.IO;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Serialization;
using Xunit;

namespace Naultinus.Tests
{
    public class LocalPlannerStoreTests
    {
        private const string PasswordMarker = "mot-de-passe-UNIQUE-naultinus-9f3a";

        [Fact]
        public void UsesRemoteCalendars_DependsOnSharedAccount_NotOnWidgetUrl()
        {
            var model = new CalendarNaultinusModel
            {
                CalDAVBaseUrl = "https://legacy.example/dav/",
                CalDAVUsername = "ancienne-fenetre",
                CalendarIds = new() { "/dav/cal" },
            };

            Assert.False(LocalPlannerStore.UsesRemoteCalendars(model, sharedAccountConfigured: false));
            Assert.True(LocalPlannerStore.UsesRemoteCalendars(model, sharedAccountConfigured: true));
            Assert.False(LocalPlannerStore.UsesRemoteCalendars(new CalendarNaultinusModel(), sharedAccountConfigured: true));
        }

        [Fact]
        public void UsesRemoteTasks_IgnoresPerWindowCredentials()
        {
            var withListOnly = new TaskNaultinusModel { TaskListId = "Tasks" };
            var withUrlOnly = new TaskNaultinusModel { CalDAVUrl = "https://legacy.example/dav/", CalDAVUsername = "user" };

            Assert.False(LocalPlannerStore.UsesRemoteTasks(withListOnly, sharedAccountConfigured: false));
            Assert.True(LocalPlannerStore.UsesRemoteTasks(withListOnly, sharedAccountConfigured: true));
            Assert.False(LocalPlannerStore.UsesRemoteTasks(withUrlOnly, sharedAccountConfigured: true));
        }

        [Fact]
        public void Events_AddUpdateDelete_RoundTripStateXml()
        {
            var directory = CreateTempDirectory();
            try
            {
                var model = new CalendarNaultinusModel { Name = "Local" };
                var start = new DateTime(2026, 9, 24, 9, 0, 0);
                var end = start.AddHours(1);
                Assert.True(LocalPlannerStore.TryBuildEvent("Réunion", "Bureau", "Notes", start, end, "09:00", "10:00", false, null, out var draft, out var error));
                Assert.Equal(LocalEntryError.None, error);
                Assert.True(LocalPlannerStore.TryAddEvent(model, draft!, out error));

                Assert.True(LocalPlannerStore.TryBuildEvent("Réunion déplacée", "Bureau", "Notes", start, end.AddHours(1), "09:00", "11:00", false, draft!.Uid, out var updated, out error));
                Assert.True(LocalPlannerStore.TryUpdateEvent(model, updated!, out error));

                NaultinusStateFile.Write(directory, model);
                var loaded = Assert.IsType<CalendarNaultinusModel>(NaultinusStateFile.Read(directory));
                var storedEvent = Assert.Single(loaded.LocalEvents);
                Assert.Equal("Réunion déplacée", storedEvent.Summary);
                Assert.Equal(draft.Uid, storedEvent.Uid);
                Assert.Equal(start, storedEvent.DtStart);
                Assert.Equal(end.AddHours(1), storedEvent.DtEnd);
                Assert.DoesNotContain(PasswordMarker, File.ReadAllText(Path.Combine(directory, "state.xml")));

                Assert.True(LocalPlannerStore.TryRemoveEvent(loaded, draft.Uid, out error));
                Assert.Empty(loaded.LocalEvents);
                Assert.False(LocalPlannerStore.TryRemoveEvent(loaded, draft.Uid, out error));
                Assert.Equal(LocalEntryError.NotFound, error);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void EventsOverlapping_KeepsOnlyTheVisibleRange()
        {
            var model = new CalendarNaultinusModel();
            var day = new DateTime(2026, 9, 24);
            AddEvent(model, "Matin", day.AddHours(10), day.AddHours(11));
            AddEvent(model, "Soir", day.AddHours(18), day.AddHours(19));

            var visible = LocalPlannerStore.EventsOverlapping(model, day.AddHours(11), day.AddHours(18));
            Assert.Empty(visible);

            visible = LocalPlannerStore.EventsOverlapping(model, day.AddHours(10).AddMinutes(30), day.AddHours(10).AddMinutes(45));
            Assert.Single(visible);
            Assert.Equal("Matin", visible[0].Summary);
        }

        [Theory]
        [InlineData("", "09:00", "10:00", false, LocalEntryError.TitleRequired)]
        [InlineData("   ", "09:00", "10:00", false, LocalEntryError.TitleRequired)]
        [InlineData("Ok", "9h30", "10:00", false, LocalEntryError.TimeInvalid)]
        [InlineData("Ok", "10:00", "09:00", false, LocalEntryError.EndNotAfterStart)]
        public void TryBuildEvent_RejectsInvalidInput(string title, string startTime, string endTime, bool allDay, LocalEntryError expected)
        {
            var day = new DateTime(2026, 9, 24);
            var built = LocalPlannerStore.TryBuildEvent(title, "", "", day, day, startTime, endTime, allDay, null, out _, out var error);
            Assert.False(built);
            Assert.Equal(expected, error);
            Assert.False(string.IsNullOrWhiteSpace(LocalPlannerStore.Describe(error)));
        }

        [Fact]
        public void TryBuildEvent_RejectsTitleTooLong_AndDateOutOfRange()
        {
            var day = new DateTime(2026, 9, 24);
            var longTitle = new string('a', LocalPlannerStore.MaxTitleLength + 1);
            Assert.False(LocalPlannerStore.TryBuildEvent(longTitle, "", "", day, day, "09:00", "10:00", false, null, out _, out var error));
            Assert.Equal(LocalEntryError.TitleTooLong, error);

            var ancient = new DateTime(1960, 1, 1);
            Assert.False(LocalPlannerStore.TryBuildEvent("Ok", "", "", ancient, ancient, "09:00", "10:00", false, null, out _, out error));
            Assert.Equal(LocalEntryError.DateOutOfRange, error);
        }

        [Fact]
        public void Tasks_AddUpdateDelete_RoundTripStateXml()
        {
            var directory = CreateTempDirectory();
            try
            {
                var model = new TaskNaultinusModel { Name = "Local" };
                var due = new DateTime(2026, 10, 1);
                Assert.True(LocalPlannerStore.TryBuildTask("Acheter du pain", "Boulangerie", due, null, default, false, null, out var created, out var error));
                Assert.True(LocalPlannerStore.TryAddTask(model, created!, out error));

                Assert.True(LocalPlannerStore.TryBuildTask("Acheter du pain complet", "Boulangerie", due, created!.Id, created.CreatedDate, true, created.CreatedDate, out var updated, out error));
                Assert.True(LocalPlannerStore.TryUpdateTask(model, updated!, out error));

                NaultinusStateFile.Write(directory, model);
                var loaded = Assert.IsType<TaskNaultinusModel>(NaultinusStateFile.Read(directory));
                var storedTask = Assert.Single(loaded.LocalTasks);
                Assert.Equal("Acheter du pain complet", storedTask.Title);
                Assert.True(storedTask.Completed);
                Assert.Equal(due, storedTask.DueDate);
                Assert.Equal(created.Id, storedTask.Id);

                var ui = LocalPlannerStore.ToUiTask(storedTask);
                Assert.Equal(created.Id, ui.Id);
                Assert.True(string.IsNullOrEmpty(ui.CalDAVId));

                Assert.True(LocalPlannerStore.TryRemoveTask(loaded, created.Id, out error));
                Assert.Empty(loaded.LocalTasks);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void TryBuildTask_RejectsEmptyTitle_AndDueDateOutOfRange()
        {
            Assert.False(LocalPlannerStore.TryBuildTask("  ", "", null, null, default, false, null, out _, out var error));
            Assert.Equal(LocalEntryError.TitleRequired, error);

            Assert.False(LocalPlannerStore.TryBuildTask("Ok", "", new DateTime(1800, 1, 1), null, default, false, null, out _, out error));
            Assert.Equal(LocalEntryError.DateOutOfRange, error);

            Assert.True(LocalPlannerStore.TryBuildTask("Sans échéance", "", null, null, default, false, null, out var task, out error));
            Assert.Null(task!.DueDate);
        }

        private static void AddEvent(CalendarNaultinusModel model, string title, DateTime start, DateTime end)
        {
            Assert.True(LocalPlannerStore.TryBuildEvent(title, "", "", start, end, start.ToString("HH:mm", CultureInfo.InvariantCulture), end.ToString("HH:mm", CultureInfo.InvariantCulture), false, null, out var draft, out _));
            Assert.True(LocalPlannerStore.TryAddEvent(model, draft!, out _));
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "naultinus-local-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
