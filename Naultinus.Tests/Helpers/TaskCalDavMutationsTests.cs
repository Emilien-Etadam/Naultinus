using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class TaskCalDavMutationsTests
    {
        [Fact]
        public async Task TryCreateAsync_FillsIdsFromService()
        {
            var service = new FakeCalDav
            {
                OnCreate = (_, task) =>
                {
                    task.CalDAVId = "created.ics";
                    task.CalDAVEtag = "etag";
                    task.Uid = "uid-1";
                    return Task.FromResult(task);
                },
            };
            var task = new CalDAVTask("Titre");
            Assert.Null(await TaskCalDavMutations.TryCreateAsync(service, "/lists/1", task));
            Assert.Equal("created.ics", task.CalDAVId);
            Assert.Equal("etag", task.CalDAVEtag);
            Assert.Equal("uid-1", task.Uid);
        }

        [Fact]
        public async Task TryApplyEditAsync_RollsBackOnFailure()
        {
            var service = new FakeCalDav
            {
                OnUpdate = (_, _) => throw new InvalidOperationException("boom"),
            };
            var task = new CalDAVTask("Avant")
            {
                Description = "d0",
                DueDate = new DateTime(2026, 1, 1),
                CalDAVId = "x.ics",
                LastModified = new DateTime(2026, 1, 1),
            };
            var edited = new StoredLocalTask
            {
                Title = "Après",
                Description = "d1",
                DueDate = new DateTime(2026, 2, 2),
                LastModified = new DateTime(2026, 2, 2),
            };

            var error = await TaskCalDavMutations.TryApplyEditAsync(service, "/lists/1", task, edited);
            Assert.NotNull(error);
            Assert.Equal("Avant", task.Title);
            Assert.Equal("d0", task.Description);
            Assert.Equal(new DateTime(2026, 1, 1), task.DueDate);
        }

        private sealed class FakeCalDav : ICalDAVService
        {
            public Func<string, CalDAVTask, Task<CalDAVTask>>? OnCreate { get; set; }
            public Func<string, CalDAVTask, Task>? OnUpdate { get; set; }

            public Task<List<CalDAVTaskList>> GetTaskListsAsync() => Task.FromResult(new List<CalDAVTaskList>());
            public Task<List<CalDAVTask>> GetTasksAsync(string taskListHref) => Task.FromResult(new List<CalDAVTask>());
            public Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task)
                => OnCreate?.Invoke(taskListHref, task) ?? Task.FromResult(task);
            public Task UpdateTaskAsync(string taskListHref, CalDAVTask task)
                => OnUpdate?.Invoke(taskListHref, task) ?? Task.CompletedTask;
            public Task DeleteTaskAsync(string taskListHref, string taskId) => Task.CompletedTask;
            public Task<List<CalDAVTask>> SyncTasksAsync(string taskListHref, List<CalDAVTask> localTasks)
                => Task.FromResult(new List<CalDAVTask>(localTasks));
            public void Dispose() { }
        }
    }
}
