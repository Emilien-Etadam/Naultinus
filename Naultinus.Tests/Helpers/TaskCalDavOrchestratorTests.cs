using System.Collections.Generic;
using System.Threading.Tasks;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class TaskCalDavOrchestratorTests
    {
        [Fact]
        public void DisplayNameForList_UsesLastSegment()
        {
            Assert.Equal("Tasks", TaskCalDavOrchestrator.DisplayNameForList("/dav/u/Tasks/"));
        }

        [Fact]
        public async Task LoadListsAsync_ReturnsTasksPerList()
        {
            var service = new FakeCalDav();
            var loaded = await TaskCalDavOrchestrator.LoadListsAsync(
                service,
                new[] { "/lists/a", "/lists/b" });

            Assert.Equal(2, loaded.Count);
            Assert.Equal("/lists/a", loaded[0].ListId);
            Assert.Equal("a", loaded[0].DisplayName);
            Assert.Single(loaded[0].Tasks);
            Assert.Equal("ta", loaded[0].Tasks[0].Title);
            Assert.Equal("tb", loaded[1].Tasks[0].Title);
        }

        private sealed class FakeCalDav : ICalDAVService
        {
            public Task<List<CalDAVTaskList>> GetTaskListsAsync() => Task.FromResult(new List<CalDAVTaskList>());
            public Task<List<CalDAVTask>> GetTasksAsync(string taskListHref)
            {
                var title = taskListHref.Contains("/a", System.StringComparison.Ordinal) ? "ta" : "tb";
                return Task.FromResult(new List<CalDAVTask> { new CalDAVTask(title) });
            }

            public Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task) => Task.FromResult(task);
            public Task UpdateTaskAsync(string taskListHref, CalDAVTask task) => Task.CompletedTask;
            public Task DeleteTaskAsync(string taskListHref, string taskId) => Task.CompletedTask;
            public Task<List<CalDAVTask>> SyncTasksAsync(string taskListHref, List<CalDAVTask> localTasks)
                => Task.FromResult(new List<CalDAVTask>(localTasks));
            public void Dispose() { }
        }
    }
}
