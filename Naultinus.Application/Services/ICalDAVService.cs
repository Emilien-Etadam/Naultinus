using System.Collections.Generic;
using System.Threading.Tasks;
using Naultinus.Model;

namespace Naultinus.Services
{
    public interface ICalDAVService
    {
        Task<List<CalDAVTaskList>> GetTaskListsAsync();
        Task<List<CalDAVTask>> GetTasksAsync(string taskListHref);
        Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task);
        Task UpdateTaskAsync(string taskListHref, CalDAVTask task);
        Task DeleteTaskAsync(string taskListHref, string taskId);
        Task<List<CalDAVTask>> SyncTasksAsync(string taskListHref, List<CalDAVTask> localTasks);
    }
}
