using Naultinus.Model;
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

namespace Naultinus.Services
{
    public class CalDAVService : ICalDAVService, IDisposable
    {
        private readonly ICalDAVClient _client;
        private readonly CalendarSerializer _serializer = new CalendarSerializer();

        public CalDAVService(ICalDAVClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public void Dispose()
        {
            _client.Dispose();
            GC.SuppressFinalize(this);
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
                    if (calendar == null)
                        continue;
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
            var due = ReadOptionalCalDate(todo.Due);
            var completedDt = ReadOptionalCalDate(todo.Completed);
            var created = todo.Created is { } createdDt ? ReadCalDate(createdDt) : DateTime.UtcNow;
            var lastMod = todo.LastModified is { } modified ? ReadCalDate(modified) : DateTime.UtcNow;
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

        // Ical.Net expose CalDateTime.Value toujours en Unspecified, y compris pour un instant UTC (Z).
        // On rétablit le kind sans conversion : les composants sont déjà l'heure UTC. Une date flottante garde l'heure murale.
        private static DateTime ReadCalDate(CalDateTime value)
        {
            if (value.IsUtc)
                return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
            return value.Value;
        }

        private static DateTime? ReadOptionalCalDate(CalDateTime? value)
            => value is null ? null : ReadCalDate(value);

        // Échéance flottante ou journée entière : conserver l'heure murale.
        // Local (ex. DateTime.Today) devient Unspecified. Pas de ToUniversalTime : cela décalerait le jour.
        // Une valeur déjà UTC (Z) reste UTC.
        private static CalDateTime ToDueCalDateTime(DateTime due)
        {
            if (due.Kind == DateTimeKind.Local)
                due = DateTime.SpecifyKind(due, DateTimeKind.Unspecified);
            return new CalDateTime(due);
        }

        // CREATED, LAST-MODIFIED et COMPLETED sont des instants UTC (RFC 5545).
        // DateTime.Now est Local : on le convertit. Une valeur déjà UTC reste UTC.
        // Unspecified n'est pas réinterprété (pas de décalage).
        private static CalDateTime ToUtcCalDateTime(DateTime instant)
        {
            if (instant.Kind == DateTimeKind.Local)
                instant = instant.ToUniversalTime();
            return new CalDateTime(instant);
        }

        private static Todo BuildTodo(CalDAVTask task, string uid, DateTime lastModified) => new Todo
        {
            Summary = task.Title,
            Description = task.Description,
            Due = task.DueDate.HasValue ? ToDueCalDateTime(task.DueDate.Value) : null,
            Status = task.Completed ? "COMPLETED" : "NEEDS-ACTION",
            Completed = task.Completed ? ToUtcCalDateTime(task.CompletedDate ?? DateTime.Now) : null,
            Created = ToUtcCalDateTime(task.CreatedDate),
            LastModified = ToUtcCalDateTime(lastModified),
            Uid = uid
        };

        public async Task<CalDAVTask> CreateTaskAsync(string taskListHref, CalDAVTask task)
        {
            var uid = string.IsNullOrEmpty(task.Uid) ? Guid.NewGuid().ToString() : task.Uid;
            var calendar = new Calendar();
            calendar.Todos.Add(BuildTodo(task, uid, task.LastModified));
            var calendarData = _serializer.SerializeToString(calendar) ?? string.Empty;

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
            var calendarData = _serializer.SerializeToString(calendar) ?? string.Empty;

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
                    // Le serveur fait foi pour ce qui a déjà été synchronisé : une tâche portant un
                    // CalDAVId/Uid mais absente du serveur y a été supprimée (par un autre client) ; on
                    // ne la recrée pas, on la laisse tomber du cache local. Seule une tâche jamais
                    // synchronisée (sans CalDAVId ni Uid) est une vraie création locale à pousser.
                    bool alreadySynced = !string.IsNullOrEmpty(local.CalDAVId) || !string.IsNullOrEmpty(local.Uid);
                    if (alreadySynced)
                        continue;
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
