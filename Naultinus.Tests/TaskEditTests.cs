using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests
{
    /// <summary>
    /// Le crayon ouvre la même saisie en local et sur une liste CalDAV.
    /// Confirmer une tâche déjà sur le serveur appelle UpdateTaskAsync, pas une seconde création.
    /// Annuler n'appelle pas <see cref="TaskNaultinusViewModel.CommitTaskEditAsync"/>.
    /// </summary>
    public class TaskEditTests
    {
        [Fact]
        public async Task LocalEdit_SavesLocally_WithoutNetwork()
        {
            using var scope = NewScope(remote: false);
            scope.ViewModel.QuickAddTitle = "Pain";
            scope.ViewModel.ConfirmQuickAddCommand.Execute(null);
            var shown = Assert.Single(scope.ViewModel.Tasks);
            var due = new DateTime(2026, 10, 2);

            Assert.True(LocalPlannerStore.TryBuildTask("Pain complet", "boulangerie", due, shown.Id, shown.CreatedDate, false, null, out var edited, out _));
            await scope.ViewModel.CommitTaskEditAsync(shown, edited!);

            var stored = Assert.Single(scope.Model.LocalTasks);
            Assert.Equal("Pain complet", stored.Title);
            Assert.Equal("boulangerie", stored.Description);
            Assert.Equal(due, stored.DueDate);
            Assert.Equal("Pain complet", Assert.Single(scope.ViewModel.Tasks).Title);
            Assert.Equal(0, scope.Service.CreateCalls);
            Assert.Equal(0, scope.Service.UpdateCalls);
        }

        [Fact]
        public async Task RemoteEdit_UpdatesThatTask_AndDoesNotCreateAnother()
        {
            using var scope = NewScope(remote: true);
            var task = new CalDAVTask("Ancien")
            {
                CalDAVId = "deja.ics",
                Uid = "uid-deja",
                CalDAVEtag = "etag-0",
                Description = "avant",
            };
            scope.ViewModel.Tasks.Add(task);
            var due = new DateTime(2026, 11, 3);

            Assert.True(LocalPlannerStore.TryBuildTask("Nouveau", "après", due, task.Id, task.CreatedDate, false, null, out var edited, out _));
            await scope.ViewModel.CommitTaskEditAsync(task, edited!);

            Assert.Same(task, Assert.Single(scope.ViewModel.Tasks));
            Assert.Equal("Nouveau", task.Title);
            Assert.Equal("après", task.Description);
            Assert.Equal(due, task.DueDate);
            Assert.Equal("deja.ics", task.CalDAVId);
            Assert.Equal("uid-deja", task.Uid);
            Assert.Equal(0, scope.Service.CreateCalls);
            Assert.Equal(1, scope.Service.UpdateCalls);
            Assert.Equal("tasks", scope.Service.LastUpdateHref);
            Assert.Same(task, scope.Service.LastUpdated);
            Assert.Equal(string.Empty, scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public async Task RemoteEdit_OnTheSelectedTab_UpdatesThatList()
        {
            using var scope = NewScope(remote: true);
            var first = new TaskTabItem { ListId = "list-a", DisplayName = "A" };
            var second = new TaskTabItem { ListId = "list-b", DisplayName = "B" };
            scope.ViewModel.TaskTabs.Add(first);
            scope.ViewModel.TaskTabs.Add(second);
            scope.ViewModel.SelectedTaskTab = second;
            var task = new CalDAVTask("Dans B") { CalDAVId = "b.ics", Uid = "uid-b" };
            second.Tasks.Add(task);

            Assert.True(LocalPlannerStore.TryBuildTask("Dans B, modifié", string.Empty, null, task.Id, task.CreatedDate, false, null, out var edited, out _));
            await scope.ViewModel.CommitTaskEditAsync(task, edited!);

            Assert.Equal("Dans B, modifié", task.Title);
            Assert.Equal(0, scope.Service.CreateCalls);
            Assert.Equal(1, scope.Service.UpdateCalls);
            Assert.Equal("list-b", scope.Service.LastUpdateHref);
            Assert.Empty(first.Tasks);
        }

        [Fact]
        public async Task RemoteEdit_WhenUpdateFails_RestoresThePreviousFields()
        {
            using var scope = NewScope(remote: true);
            var due = new DateTime(2026, 5, 1);
            var task = new CalDAVTask("Stable")
            {
                CalDAVId = "stable.ics",
                Uid = "uid-stable",
                Description = "reste",
                DueDate = due,
            };
            scope.ViewModel.Tasks.Add(task);
            scope.Service.FailUpdate = true;

            Assert.True(LocalPlannerStore.TryBuildTask("Changé", "autre", new DateTime(2026, 6, 1), task.Id, task.CreatedDate, false, null, out var edited, out _));
            await scope.ViewModel.CommitTaskEditAsync(task, edited!);

            Assert.Equal("Stable", task.Title);
            Assert.Equal("reste", task.Description);
            Assert.Equal(due, task.DueDate);
            Assert.Equal("stable.ics", task.CalDAVId);
            Assert.Equal(0, scope.Service.CreateCalls);
            Assert.Equal(1, scope.Service.UpdateCalls);
            Assert.Equal(
                string.Format(CultureInfo.CurrentCulture, Strings.TaskUpdateFailedFormat, "échec mise à jour"),
                scope.ViewModel.ErrorMessage);
        }

        [Fact]
        public async Task RemoteEdit_WithoutServerId_KeepsLocalFields_AndDoesNotCreate()
        {
            using var scope = NewScope(remote: true);
            var task = new CalDAVTask("Pas encore") { Description = "brouillon" };
            scope.ViewModel.Tasks.Add(task);

            Assert.True(LocalPlannerStore.TryBuildTask("Pas encore, précisé", "note", null, task.Id, task.CreatedDate, false, null, out var edited, out _));
            await scope.ViewModel.CommitTaskEditAsync(task, edited!);

            Assert.Equal("Pas encore, précisé", task.Title);
            Assert.Equal("note", task.Description);
            Assert.Equal(string.Empty, task.CalDAVId);
            Assert.Equal(0, scope.Service.CreateCalls);
            Assert.Equal(0, scope.Service.UpdateCalls);
        }

        private static Scope NewScope(bool remote)
        {
            var model = new TaskNaultinusModel
            {
                Name = "task-edit-test",
                Width = 400,
                Height = 300,
                TaskListId = remote ? "tasks" : string.Empty,
            };
            var service = new RecordingCalDavService();
            var viewModel = new TaskNaultinusViewModel(model, service, sharedAccountConfigured: remote, startBackgroundWork: false);
            return new Scope(model, viewModel, service);
        }

        private sealed class Scope : IDisposable
        {
            public Scope(TaskNaultinusModel model, TaskNaultinusViewModel viewModel, RecordingCalDavService service)
            {
                Model = model;
                ViewModel = viewModel;
                Service = service;
            }

            public TaskNaultinusModel Model { get; }
            public TaskNaultinusViewModel ViewModel { get; }
            public RecordingCalDavService Service { get; }

            public void Dispose()
            {
                var identifier = ViewModel.Identifier;
                ViewModel.Dispose();
                var directory = AppPaths.GetNaultinusDirectory(identifier);
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private sealed class RecordingCalDavService : ICalDAVService
        {
            public int CreateCalls { get; private set; }
            public int UpdateCalls { get; private set; }
            public string? LastUpdateHref { get; private set; }
            public CalDAVTask? LastUpdated { get; private set; }
            public bool FailUpdate { get; set; }

            public Task<List<CalDAVTaskList>> GetTaskListsAsync() => Task.FromResult(new List<CalDAVTaskList>());

            public Task<List<CalDAVTask>> GetTasksAsync(string taskListHref) => Task.FromResult(new List<CalDAVTask>());

            public Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task)
            {
                CreateCalls++;
                return Task.FromResult(task);
            }

            public Task UpdateTaskAsync(string taskListHref, CalDAVTask task)
            {
                UpdateCalls++;
                LastUpdateHref = taskListHref;
                LastUpdated = task;
                if (FailUpdate)
                    return Task.FromException(new InvalidOperationException("échec mise à jour"));
                task.CalDAVEtag = "etag-2";
                return Task.CompletedTask;
            }

            public Task DeleteTaskAsync(string taskListHref, string taskId) => Task.CompletedTask;

            public Task<List<CalDAVTask>> SyncTasksAsync(string taskListHref, List<CalDAVTask> localTasks) =>
                Task.FromResult(localTasks);
        }
    }
}
