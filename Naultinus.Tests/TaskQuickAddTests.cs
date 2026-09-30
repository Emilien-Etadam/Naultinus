using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Naultinus.Converters;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests
{
    /// <summary>
    /// Création rapide : Entrée crée le titre saisi, une saisie vide ne fait rien,
    /// et « + Tâche » ne pose pas de tâche « Nouvelle tâche ».
    /// Sur une liste CalDAV, Entrée envoie aussi la tâche ; l'échec laisse CalDAVId vide.
    /// </summary>
    public class TaskQuickAddTests
    {
        [Fact]
        public void AddButton_DoesNotInsertAPlaceholder_AndRequestsFocus()
        {
            using var scope = NewScope(remote: false);
            var focused = false;
            scope.ViewModel.QuickAddFocusRequested += (_, _) => focused = true;

            scope.ViewModel.AddTaskCommand.Execute(null);

            Assert.True(focused);
            Assert.Empty(scope.ViewModel.Tasks);
            Assert.Empty(scope.Model.LocalTasks);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\r\n")]
        public void Enter_OnBlankTitle_DoesNothing(string title)
        {
            using var scope = NewScope(remote: false);
            scope.ViewModel.QuickAddTitle = title;

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            Assert.Empty(scope.ViewModel.Tasks);
            Assert.Empty(scope.Model.LocalTasks);
            Assert.Equal(title, scope.ViewModel.QuickAddTitle);
            Assert.Equal(string.Empty, scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public void Enter_Local_CreatesTrimmedTitle_AndClearsTheField()
        {
            using var scope = NewScope(remote: false);
            scope.ViewModel.QuickAddTitle = "  Acheter du pain  ";

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            var stored = Assert.Single(scope.Model.LocalTasks);
            var shown = Assert.Single(scope.ViewModel.Tasks);
            Assert.Equal("Acheter du pain", stored.Title);
            Assert.Equal("Acheter du pain", shown.Title);
            Assert.Equal(string.Empty, stored.Description);
            Assert.Null(stored.DueDate);
            Assert.Equal(string.Empty, scope.ViewModel.QuickAddTitle);
            Assert.Same(shown, scope.ViewModel.SelectedTask);
        }

        [Fact]
        public void Enter_AcceptsTheOldPlaceholder_WhenTheUserTypedIt()
        {
            using var scope = NewScope(remote: false);
            scope.ViewModel.QuickAddTitle = Strings.TaskNewTaskName;

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            Assert.Equal(Strings.TaskNewTaskName, Assert.Single(scope.Model.LocalTasks).Title);
        }

        [Fact]
        public void Enter_RejectsAnIllegalTitle_WithoutCreating()
        {
            using var scope = NewScope(remote: false);
            scope.ViewModel.QuickAddTitle = new string('a', LocalPlannerStore.MaxTitleLength + 1);

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            Assert.Empty(scope.Model.LocalTasks);
            Assert.Equal(new string('a', LocalPlannerStore.MaxTitleLength + 1), scope.ViewModel.QuickAddTitle);
            Assert.Equal(Strings.TitleTooLong, scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public void Enter_Local_DoesNotUpload()
        {
            using var scope = NewScope(remote: false);
            scope.ViewModel.QuickAddTitle = "Local seulement";

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            Assert.Equal(0, scope.Service.CreateCalls);
            Assert.True(scope.ViewModel.IsLocalMode);
            Assert.Equal(string.Empty, Assert.Single(scope.ViewModel.Tasks).CalDAVId);
        }

        [Fact]
        public void Enter_Remote_UploadsTheTypedTitle()
        {
            using var scope = NewScope(remote: true);
            scope.ViewModel.QuickAddTitle = "  Rappeler Léa  ";

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            var created = Assert.Single(scope.ViewModel.Tasks);
            Assert.Equal("Rappeler Léa", created.Title);
            Assert.Equal(string.Empty, created.Description);
            Assert.Null(created.DueDate);
            Assert.Equal("server.ics", created.CalDAVId);
            Assert.Equal("server-uid", created.Uid);
            Assert.Equal(1, scope.Service.CreateCalls);
            Assert.Equal(0, scope.Service.UpdateCalls);
            Assert.Equal("tasks", scope.Service.LastCreateHref);
            Assert.Empty(scope.Model.LocalTasks);
            Assert.Equal(string.Empty, scope.ViewModel.QuickAddTitle);
            Assert.Equal(string.Empty, scope.ViewModel.ErrorMessage);
            Assert.False(scope.ViewModel.IsLocalMode);
            Assert.Equal(Visibility.Collapsed, SaveButtonVisibility(created.CalDAVId));
        }

        [Fact]
        public void Enter_Remote_UploadFailure_KeepsTheTaskWithoutCalDavId()
        {
            using var scope = NewScope(remote: true);
            scope.Service.FailCreate = true;
            scope.ViewModel.QuickAddTitle = "À renvoyer";

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            var created = Assert.Single(scope.ViewModel.Tasks);
            Assert.Equal("À renvoyer", created.Title);
            Assert.Equal(string.Empty, created.CalDAVId);
            Assert.Equal(1, scope.Service.CreateCalls);
            Assert.Equal(0, scope.Service.UpdateCalls);
            Assert.Equal(string.Empty, scope.ViewModel.QuickAddTitle);
            Assert.Equal(
                string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.TaskSaveFailedFormat, "échec réseau"),
                scope.ViewModel.ErrorMessage);
            Assert.False(scope.ViewModel.IsLocalMode);
            Assert.Equal(Visibility.Visible, SaveButtonVisibility(created.CalDAVId));
        }

        [Fact]
        public void SaveButton_RetriesUpload_AfterFailure()
        {
            using var scope = NewScope(remote: true);
            scope.Service.FailCreate = true;
            scope.ViewModel.QuickAddTitle = "Reprise";
            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);
            var created = Assert.Single(scope.ViewModel.Tasks);
            Assert.Equal(string.Empty, created.CalDAVId);

            scope.Service.FailCreate = false;
            scope.ViewModel.SaveTaskCommand.Execute(created);

            Assert.Equal("server.ics", created.CalDAVId);
            Assert.Equal(2, scope.Service.CreateCalls);
            Assert.Equal(0, scope.Service.UpdateCalls);
            Assert.Equal(string.Empty, scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public void Enter_RemoteMultipleLists_UploadsOnlyTheSelectedTab()
        {
            using var scope = NewScope(remote: true);
            var first = new TaskTabItem { ListId = "list-a", DisplayName = "A" };
            var second = new TaskTabItem { ListId = "list-b", DisplayName = "B" };
            scope.ViewModel.TaskTabs.Add(first);
            scope.ViewModel.TaskTabs.Add(second);
            scope.ViewModel.SelectedTaskTab = second;
            scope.ViewModel.QuickAddTitle = "Dans B";

            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);

            Assert.Empty(scope.ViewModel.Tasks);
            Assert.Empty(first.Tasks);
            var created = Assert.Single(second.Tasks);
            Assert.Equal("Dans B", created.Title);
            Assert.Equal("server.ics", created.CalDAVId);
            Assert.Equal(1, scope.Service.CreateCalls);
            Assert.Equal("list-b", scope.Service.LastCreateHref);
        }

        private static Visibility SaveButtonVisibility(string calDavId)
        {
            var converter = new InverseEmptyStringToVisibilityConverter();
            return (Visibility)converter.Convert(calDavId, typeof(Visibility), null, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Scope NewScope(bool remote)
        {
            var model = new TaskNaultinusModel
            {
                Name = "quick-add-test",
                Width = 400,
                Height = 300,
                TaskListId = remote ? "tasks" : string.Empty,
            };
            var service = new CountingCalDavService();
            var viewModel = new TaskNaultinusViewModel(model, service, sharedAccountConfigured: remote, startBackgroundWork: false);
            return new Scope(model, viewModel, service);
        }

        private sealed class Scope : IDisposable
        {
            public Scope(TaskNaultinusModel model, TaskNaultinusViewModel viewModel, CountingCalDavService service)
            {
                Model = model;
                ViewModel = viewModel;
                Service = service;
            }

            public TaskNaultinusModel Model { get; }
            public TaskNaultinusViewModel ViewModel { get; }
            public CountingCalDavService Service { get; }

            public void Dispose()
            {
                var identifier = ViewModel.Identifier;
                ViewModel.Dispose();
                var directory = AppPaths.GetNaultinusDirectory(identifier);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class CountingCalDavService : ICalDAVService
        {
            public int CreateCalls { get; private set; }
            public int UpdateCalls { get; private set; }
            public string? LastCreateHref { get; private set; }
            public bool FailCreate { get; set; }

            public Task<List<CalDAVTaskList>> GetTaskListsAsync() => Task.FromResult(new List<CalDAVTaskList>());

            public Task<List<CalDAVTask>> GetTasksAsync(string taskListHref) => Task.FromResult(new List<CalDAVTask>());

            public Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task)
            {
                CreateCalls++;
                LastCreateHref = taskListHref;
                if (FailCreate)
                    return Task.FromException<CalDAVTask>(new InvalidOperationException("échec réseau"));

                return Task.FromResult(new CalDAVTask(task.Title)
                {
                    CalDAVId = "server.ics",
                    Uid = "server-uid",
                    CalDAVEtag = "etag-1",
                });
            }

            public Task UpdateTaskAsync(string taskListHref, CalDAVTask task)
            {
                UpdateCalls++;
                return Task.CompletedTask;
            }

            public Task DeleteTaskAsync(string taskListHref, string taskId) => Task.CompletedTask;

            public Task<List<CalDAVTask>> SyncTasksAsync(string taskListHref, List<CalDAVTask> localTasks) =>
                Task.FromResult(localTasks);
        }
    }
}
