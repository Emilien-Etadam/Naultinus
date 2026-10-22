using System;
using System.Globalization;
using System.Threading.Tasks;
using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;

namespace Naultinus.Helpers
{
    /// <summary>Création / mise à jour CalDAV d'une tâche, avec messages d'erreur localisés.</summary>
    internal static class TaskCalDavMutations
    {
        /// <returns><c>null</c> si succès, sinon le message d'erreur.</returns>
        internal static async Task<string?> TryCreateAsync(ICalDAVService service, string listId, CalDAVTask task)
        {
            try
            {
                task.LastModified = DateTime.Now;
                var createdTask = await service.CreateTaskAsync(listId, task).ConfigureAwait(false);
                task.CalDAVId = createdTask.CalDAVId;
                task.CalDAVEtag = createdTask.CalDAVEtag;
                if (!string.IsNullOrEmpty(createdTask.Uid))
                    task.Uid = createdTask.Uid;
                return null;
            }
            catch (Exception ex)
            {
                return string.Format(CultureInfo.CurrentCulture, Strings.TaskSaveFailedFormat, ex.Message);
            }
        }

        /// <summary>Applique les champs édités puis pousse le serveur ; restaure l'ancien état en cas d'échec.</summary>
        /// <returns><c>null</c> si succès ou si la tâche n'a pas encore d'id serveur, sinon le message d'erreur.</returns>
        internal static async Task<string?> TryApplyEditAsync(
            ICalDAVService service,
            string listId,
            CalDAVTask task,
            StoredLocalTask edited)
        {
            var previousTitle = task.Title;
            var previousDescription = task.Description;
            var previousDue = task.DueDate;
            var previousModified = task.LastModified;

            task.Title = edited.Title ?? string.Empty;
            task.Description = edited.Description ?? string.Empty;
            task.DueDate = edited.DueDate;
            task.LastModified = edited.LastModified == default ? DateTime.Now : edited.LastModified;

            if (string.IsNullOrEmpty(task.CalDAVId))
                return null;

            try
            {
                await service.UpdateTaskAsync(listId, task).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                task.Title = previousTitle;
                task.Description = previousDescription;
                task.DueDate = previousDue;
                task.LastModified = previousModified;
                return string.Format(CultureInfo.CurrentCulture, Strings.TaskUpdateFailedFormat, ex.Message);
            }
        }
    }
}
