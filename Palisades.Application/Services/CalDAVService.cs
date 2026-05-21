using Palisades.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace Palisades.Services
{
    public class CalDAVService : ICalDAVService
    {
        private readonly ICalDAVClient _client;
        private readonly CalendarSerializer _serializer = new CalendarSerializer();

        public CalDAVService(ICalDAVClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<List<CalDAVTaskList>> GetTaskListsAsync()
        {
            var calendars = await _client.DiscoverCalendarsAsync().ConfigureAwait(false);
            var taskLists = new List<CalDAVTaskList>();
            foreach (var c in calendars)
            {
                taskLists.Add(new CalDAVTaskList(c.CalendarId, c.DisplayName, c.Href));
            }
            return taskLists;
        }

        private static readonly string CalendarQueryVtodoBody = @"<?xml version=""1.0"" encoding=""utf-8""?>
<c:calendar-query xmlns:c=""urn:ietf:params:xml:ns:caldav"" xmlns:d=""DAV:"">
    <d:prop><d:getetag></d:getetag><c:calendar-data></c:calendar-data></d:prop>
    <c:filter><c:comp-filter name=""VCALENDAR"">
        <c:comp-filter name=""VTODO""></c:comp-filter>
    </c:comp-filter></c:filter>
</c:calendar-query>";

        public async Task<List<CalDAVTask>> GetTasksAsync(string taskListHref)
        {
            var doc = await _client.ReportAsync(taskListHref, CalendarQueryVtodoBody).ConfigureAwait(false);
            return ParseMultistatusCalendarData(doc);
        }

        private static List<CalDAVTask> ParseMultistatusCalendarData(XDocument xdoc)
        {
            var tasks = new List<CalDAVTask>();
            var responses = CalDAVClient.ParseMultistatus(xdoc);

            foreach (var (href, props, _) in responses)
            {
                if (string.IsNullOrEmpty(href))
                    continue;
                var caldavId = href.Contains('/') ? href.Substring(href.LastIndexOf('/') + 1) : href;
                if (!props.TryGetValue("getetag", out var etag))
                    etag = "";
                etag = etag.Trim('"');
                if (!props.TryGetValue("calendar-data", out var calendarData) || string.IsNullOrWhiteSpace(calendarData))
                    continue;

                try
                {
                    var calendar = Calendar.Load(calendarData);
                    foreach (var todo in calendar.Todos)
                        tasks.Add(MapTodoToCalDAVTask(todo, caldavId, etag));
                }
                catch
                {
                    /* ignorer un bloc calendar invalide */
                }
            }

            return tasks;
        }

        private static CalDAVTask MapTodoToCalDAVTask(Todo todo, string caldavId, string etag)
        {
            var due = todo.Due?.Value;
            var completedDt = todo.Completed?.Value;
            var created = todo.Created?.Value ?? DateTime.UtcNow;
            var lastMod = todo.LastModified?.Value ?? DateTime.UtcNow;
            return new CalDAVTask(todo.Summary ?? "")
            {
                Description = todo.Description ?? "",
                DueDate = due,
                Completed = string.Equals(todo.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase),
                CompletedDate = completedDt,
                CreatedDate = created,
                LastModified = lastMod,
                CalDAVId = caldavId,
                Uid = todo.Uid ?? "",
                CalDAVEtag = etag
            };
        }

        private static Todo BuildTodo(CalDAVTask task, string uid, DateTime lastModified) => new Todo
        {
            Summary = task.Title,
            Description = task.Description,
            Due = task.DueDate.HasValue ? new CalDateTime(task.DueDate.Value) : null,
            Status = task.Completed ? "COMPLETED" : "NEEDS-ACTION",
            Completed = task.Completed ? new CalDateTime(task.CompletedDate ?? DateTime.Now) : null,
            Created = new CalDateTime(task.CreatedDate),
            LastModified = new CalDateTime(lastModified),
            Uid = uid
        };

        public async Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task)
        {
            var uid = string.IsNullOrEmpty(task.Uid) ? Guid.NewGuid().ToString() : task.Uid;
            var calendar = new Calendar();
            calendar.Todos.Add(BuildTodo(task, uid, task.LastModified));
            var calendarData = _serializer.SerializeToString(calendar);

            var resourceHref = taskListHref.TrimEnd('/') + "/" + uid + ".ics";
            var createdEtag = await _client.PutAsync(resourceHref, calendarData).ConfigureAwait(false);

            task.CalDAVId = uid + ".ics";
            task.Uid = uid;
            task.CalDAVEtag = createdEtag ?? "created";
            return task;
        }

        public async Task UpdateTaskAsync(string taskListHref, CalDAVTask task)
        {
            var uid = !string.IsNullOrEmpty(task.Uid) ? task.Uid : (!string.IsNullOrEmpty(task.CalDAVId) ? Path.GetFileNameWithoutExtension(task.CalDAVId) : null) ?? Guid.NewGuid().ToString();
            var calendar = new Calendar();
            calendar.Todos.Add(BuildTodo(task, uid, DateTime.Now));
            var calendarData = _serializer.SerializeToString(calendar);

            var resourceHref = taskListHref.TrimEnd('/') + "/" + task.CalDAVId;
            var etag = await _client.PutAsync(resourceHref, calendarData, task.CalDAVEtag).ConfigureAwait(false);
            task.CalDAVEtag = etag ?? "updated";
        }

        public async Task DeleteTaskAsync(string taskListHref, string taskId)
        {
            var resourceHref = taskListHref.TrimEnd('/') + "/" + taskId;
            await _client.DeleteAsync(resourceHref).ConfigureAwait(false);
        }

        public async Task<List<CalDAVTask>> SyncTasksAsync(string taskListHref, List<CalDAVTask> localTasks)
        {
            var remoteTasks = await GetTasksAsync(taskListHref).ConfigureAwait(false);
            var remoteByUid = remoteTasks.Where(r => !string.IsNullOrEmpty(r.Uid)).ToDictionary(r => r.Uid);
            var remoteById = remoteTasks.Where(r => !string.IsNullOrEmpty(r.CalDAVId)).ToDictionary(r => r.CalDAVId);
            var merged = new List<CalDAVTask>();
            foreach (var local in localTasks)
            {
                CalDAVTask? remote = null;
                if (!string.IsNullOrEmpty(local.CalDAVId) && remoteById.TryGetValue(local.CalDAVId, out var byId))
                    remote = byId;
                if (remote == null && !string.IsNullOrEmpty(local.Uid) && remoteByUid.TryGetValue(local.Uid, out var byUid))
                    remote = byUid;
                if (remote == null)
                {
                    var created = await CreateTaskAsync(taskListHref, local).ConfigureAwait(false);
                    merged.Add(created);
                }
                else
                {
                    if (local.LastModified > remote.LastModified)
                        await UpdateTaskAsync(taskListHref, local).ConfigureAwait(false);
                    merged.Add(local.LastModified >= remote.LastModified ? local : remote);
                }
            }
            var mergedCalDAVIds = new HashSet<string>(merged.Where(m => !string.IsNullOrEmpty(m.CalDAVId)).Select(m => m.CalDAVId!));
            var mergedUids = new HashSet<string>(merged.Where(m => !string.IsNullOrEmpty(m.Uid)).Select(m => m.Uid!));
            foreach (var remote in remoteTasks)
            {
                bool alreadyMerged = (!string.IsNullOrEmpty(remote.CalDAVId) && mergedCalDAVIds.Contains(remote.CalDAVId))
                    || (!string.IsNullOrEmpty(remote.Uid) && mergedUids.Contains(remote.Uid));
                if (!alreadyMerged)
                    merged.Add(remote);
            }
            return merged;
        }
    }
}
