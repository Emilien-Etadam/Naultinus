using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;

namespace Naultinus.Helpers
{
    /// <summary>Chargement et fusion CalDAV des listes de tâches (sans UI / dispatcher).</summary>
    internal static class TaskCalDavOrchestrator
    {
        internal static string DisplayNameForList(string href)
        {
            if (string.IsNullOrEmpty(href))
                return Strings.TaskListDefaultName;
            var idx = href.TrimEnd('/').LastIndexOf('/');
            return idx >= 0 ? href.Substring(idx + 1).TrimEnd('/') : href;
        }

        internal static async Task<List<(string ListId, string DisplayName, List<CalDAVTask> Tasks)>> LoadListsAsync(
            ICalDAVService service,
            IReadOnlyList<string> listIds)
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(listIds);

            var result = new List<(string ListId, string DisplayName, List<CalDAVTask> Tasks)>(listIds.Count);
            foreach (var listId in listIds)
            {
                var tasks = await service.GetTasksAsync(listId).ConfigureAwait(false);
                result.Add((listId, DisplayNameForList(listId), tasks));
            }

            return result;
        }

        internal static Task<List<CalDAVTask>> SyncListAsync(
            ICalDAVService service,
            string listId,
            List<CalDAVTask> localSnapshot)
            => service.SyncTasksAsync(listId, localSnapshot);
    }
}
