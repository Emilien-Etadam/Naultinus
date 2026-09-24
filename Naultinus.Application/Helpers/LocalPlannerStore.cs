using Naultinus.Model;
using Naultinus.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Naultinus.Helpers
{
    /// <summary>Erreur de validation d'un événement ou d'une tâche locale.</summary>
    public enum LocalEntryError
    {
        None = 0,
        TitleRequired,
        TitleTooLong,
        TextTooLong,
        InvalidText,
        DateMissing,
        DateOutOfRange,
        EndNotAfterStart,
        TimeInvalid,
        NotFound,
    }

    /// <summary>
    /// Création, modification et suppression des événements et tâches stockés dans le modèle
    /// (state.xml), sans réseau. La synchro CalDAV reste décidée à part, selon le compte partagé.
    /// </summary>
    public static class LocalPlannerStore
    {
        public const int MaxTitleLength = 200;
        public const int MaxLocationLength = 300;
        public const int MaxDescriptionLength = 4000;

        public static readonly DateTime MinDate = new DateTime(1970, 1, 1);
        public static readonly DateTime MaxDate = new DateTime(2100, 12, 31, 23, 59, 59);

        /// <summary>
        /// Vrai si la fenêtre a des calendriers distants et qu'un compte CalDAV partagé est configuré.
        /// Les identifiants éventuellement copiés sur la fenêtre ne comptent pas comme un compte.
        /// </summary>
        public static bool UsesRemoteCalendars(CalendarNaultinusModel model, bool sharedAccountConfigured)
        {
            ArgumentNullException.ThrowIfNull(model);
            return sharedAccountConfigured && HasAny(model.CalendarIds);
        }

        /// <summary>
        /// Vrai si la fenêtre a une liste de tâches distante et qu'un compte CalDAV partagé est configuré.
        /// </summary>
        public static bool UsesRemoteTasks(TaskNaultinusModel model, bool sharedAccountConfigured)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (!sharedAccountConfigured)
                return false;
            if (HasAny(model.TaskListIds))
                return true;
            return !string.IsNullOrWhiteSpace(model.TaskListId);
        }

        public static string Describe(LocalEntryError error)
        {
            return error switch
            {
                LocalEntryError.TitleRequired => Strings.SummaryRequired,
                LocalEntryError.TitleTooLong => Strings.TitleTooLong,
                LocalEntryError.TextTooLong => Strings.TextTooLong,
                LocalEntryError.InvalidText => Strings.InvalidTextCharacters,
                LocalEntryError.DateMissing => Strings.DateMissing,
                LocalEntryError.DateOutOfRange => Strings.DateOutOfRange,
                LocalEntryError.EndNotAfterStart => Strings.EndNotAfterStart,
                LocalEntryError.TimeInvalid => Strings.TimeInvalid,
                LocalEntryError.NotFound => Strings.EntryNotFound,
                _ => string.Empty,
            };
        }

        public static bool TryBuildEvent(
            string? summary,
            string? location,
            string? description,
            DateTime? startDate,
            DateTime? endDate,
            string? startTimeText,
            string? endTimeText,
            bool isAllDay,
            string? existingUid,
            out StoredCalendarEvent? evt,
            out LocalEntryError error)
        {
            evt = null;
            if (!TryNormalizeTitle(summary, out var title, out error))
                return false;
            if (!TryNormalizeOptional(location, MaxLocationLength, out var normalizedLocation, out error))
                return false;
            if (!TryNormalizeOptional(description, MaxDescriptionLength, out var normalizedDescription, out error))
                return false;
            if (startDate == null || endDate == null)
            {
                error = LocalEntryError.DateMissing;
                return false;
            }

            if (!IsCalendarDateInRange(startDate.Value) || !IsCalendarDateInRange(endDate.Value))
            {
                error = LocalEntryError.DateOutOfRange;
                return false;
            }

            DateTime dtStart;
            DateTime dtEnd;
            if (isAllDay)
            {
                dtStart = startDate.Value.Date;
                dtEnd = endDate.Value.Date.AddDays(1);
            }
            else
            {
                if (!TryParseClock(startTimeText, out var startClock) || !TryParseClock(endTimeText, out var endClock))
                {
                    error = LocalEntryError.TimeInvalid;
                    return false;
                }

                dtStart = startDate.Value.Date.Add(startClock);
                dtEnd = endDate.Value.Date.Add(endClock);
            }

            if (dtEnd <= dtStart)
            {
                error = LocalEntryError.EndNotAfterStart;
                return false;
            }

            if (dtStart < MinDate || dtEnd > MaxDate.Date.AddDays(1))
            {
                error = LocalEntryError.DateOutOfRange;
                return false;
            }

            evt = new StoredCalendarEvent
            {
                Uid = string.IsNullOrWhiteSpace(existingUid) ? Guid.NewGuid().ToString("D") : existingUid.Trim(),
                Summary = title,
                Location = normalizedLocation,
                Description = normalizedDescription,
                DtStart = dtStart,
                DtEnd = dtEnd,
                IsAllDay = isAllDay,
            };
            error = LocalEntryError.None;
            return true;
        }

        public static bool TryAddEvent(CalendarNaultinusModel model, StoredCalendarEvent evt, out LocalEntryError error)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (!TryValidateStoredEvent(evt, out error))
                return false;

            model.LocalEvents ??= new List<StoredCalendarEvent>();
            model.LocalEvents.Add(Clone(evt));
            return true;
        }

        public static bool TryUpdateEvent(CalendarNaultinusModel model, StoredCalendarEvent evt, out LocalEntryError error)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (!TryValidateStoredEvent(evt, out error))
                return false;

            var list = model.LocalEvents;
            if (list == null)
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            var index = list.FindIndex(item => string.Equals(item.Uid, evt.Uid, StringComparison.Ordinal));
            if (index < 0)
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            list[index] = Clone(evt);
            return true;
        }

        public static bool TryRemoveEvent(CalendarNaultinusModel model, string? uid, out LocalEntryError error)
        {
            ArgumentNullException.ThrowIfNull(model);
            var list = model.LocalEvents;
            if (list == null || string.IsNullOrWhiteSpace(uid))
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            var removed = list.RemoveAll(item => string.Equals(item.Uid, uid, StringComparison.Ordinal));
            if (removed == 0)
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            error = LocalEntryError.None;
            return true;
        }

        /// <summary>Événements qui chevauchent [rangeStart, rangeEnd).</summary>
        public static IReadOnlyList<StoredCalendarEvent> EventsOverlapping(CalendarNaultinusModel model, DateTime rangeStart, DateTime rangeEnd)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model.LocalEvents == null || model.LocalEvents.Count == 0)
                return Array.Empty<StoredCalendarEvent>();

            return model.LocalEvents
                .Where(evt => evt.DtEnd > rangeStart && evt.DtStart < rangeEnd)
                .OrderBy(evt => evt.DtStart)
                .ToList();
        }

        public static CalendarEvent ToCalendarEvent(StoredCalendarEvent evt)
        {
            ArgumentNullException.ThrowIfNull(evt);
            return new CalendarEvent
            {
                Uid = evt.Uid ?? string.Empty,
                Summary = evt.Summary ?? string.Empty,
                Description = evt.Description ?? string.Empty,
                DtStart = evt.DtStart,
                DtEnd = evt.DtEnd,
                Location = evt.Location ?? string.Empty,
                IsAllDay = evt.IsAllDay,
                Color = CalendarColorHelper.DefaultColor,
            };
        }

        public static bool TryBuildTask(
            string? title,
            string? description,
            DateTime? dueDate,
            string? existingId,
            DateTime createdDate,
            bool completed,
            DateTime? completedDate,
            out StoredLocalTask? task,
            out LocalEntryError error)
        {
            task = null;
            if (!TryNormalizeTitle(title, out var normalizedTitle, out error))
                return false;
            if (!TryNormalizeOptional(description, MaxDescriptionLength, out var normalizedDescription, out error))
                return false;
            if (dueDate.HasValue && !IsInstantInRange(dueDate.Value))
            {
                error = LocalEntryError.DateOutOfRange;
                return false;
            }

            var now = DateTime.Now;
            task = new StoredLocalTask
            {
                Id = string.IsNullOrWhiteSpace(existingId) ? Guid.NewGuid().ToString("D") : existingId.Trim(),
                Title = normalizedTitle,
                Description = normalizedDescription,
                DueDate = dueDate,
                Completed = completed,
                CompletedDate = completed ? completedDate ?? now : null,
                CreatedDate = createdDate == default ? now : createdDate,
                LastModified = now,
            };
            error = LocalEntryError.None;
            return true;
        }

        public static bool TryAddTask(TaskNaultinusModel model, StoredLocalTask task, out LocalEntryError error)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (!TryValidateStoredTask(task, out error))
                return false;

            model.LocalTasks ??= new List<StoredLocalTask>();
            model.LocalTasks.Add(Clone(task));
            return true;
        }

        public static bool TryUpdateTask(TaskNaultinusModel model, StoredLocalTask task, out LocalEntryError error)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (!TryValidateStoredTask(task, out error))
                return false;

            var list = model.LocalTasks;
            if (list == null)
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            var index = list.FindIndex(item => string.Equals(item.Id, task.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            list[index] = Clone(task);
            return true;
        }

        public static bool TryRemoveTask(TaskNaultinusModel model, string? id, out LocalEntryError error)
        {
            ArgumentNullException.ThrowIfNull(model);
            var list = model.LocalTasks;
            if (list == null || string.IsNullOrWhiteSpace(id))
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            var removed = list.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (removed == 0)
            {
                error = LocalEntryError.NotFound;
                return false;
            }

            error = LocalEntryError.None;
            return true;
        }

        public static CalDAVTask ToUiTask(StoredLocalTask task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return new CalDAVTask(task.Title ?? string.Empty)
            {
                Id = string.IsNullOrEmpty(task.Id) ? Guid.NewGuid().ToString("D") : task.Id,
                Description = task.Description ?? string.Empty,
                DueDate = task.DueDate,
                Completed = task.Completed,
                CompletedDate = task.CompletedDate,
                CreatedDate = task.CreatedDate == default ? DateTime.Now : task.CreatedDate,
                LastModified = task.LastModified == default ? DateTime.Now : task.LastModified,
            };
        }

        public static StoredLocalTask ToStoredTask(CalDAVTask task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return new StoredLocalTask
            {
                Id = string.IsNullOrEmpty(task.Id) ? Guid.NewGuid().ToString("D") : task.Id,
                Title = task.Title ?? string.Empty,
                Description = task.Description ?? string.Empty,
                DueDate = task.DueDate,
                Completed = task.Completed,
                CompletedDate = task.Completed ? task.CompletedDate : null,
                CreatedDate = task.CreatedDate,
                LastModified = task.LastModified,
            };
        }

        private static bool TryValidateStoredEvent(StoredCalendarEvent? evt, out LocalEntryError error)
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.Uid))
            {
                error = LocalEntryError.InvalidText;
                return false;
            }

            if (!TryNormalizeTitle(evt.Summary, out _, out error))
                return false;
            if (evt.DtEnd <= evt.DtStart)
            {
                error = LocalEntryError.EndNotAfterStart;
                return false;
            }

            if (evt.DtStart < MinDate || evt.DtEnd > MaxDate.Date.AddDays(1))
            {
                error = LocalEntryError.DateOutOfRange;
                return false;
            }

            error = LocalEntryError.None;
            return true;
        }

        private static bool TryValidateStoredTask(StoredLocalTask? task, out LocalEntryError error)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.Id))
            {
                error = LocalEntryError.InvalidText;
                return false;
            }

            if (!TryNormalizeTitle(task.Title, out _, out error))
                return false;
            if (task.DueDate.HasValue && !IsInstantInRange(task.DueDate.Value))
            {
                error = LocalEntryError.DateOutOfRange;
                return false;
            }

            error = LocalEntryError.None;
            return true;
        }

        private static bool TryNormalizeTitle(string? value, out string normalized, out LocalEntryError error)
        {
            normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                error = LocalEntryError.TitleRequired;
                return false;
            }

            if (normalized.Length > MaxTitleLength)
            {
                error = LocalEntryError.TitleTooLong;
                return false;
            }

            if (!IsLegalSingleLine(normalized))
            {
                error = LocalEntryError.InvalidText;
                return false;
            }

            error = LocalEntryError.None;
            return true;
        }

        private static bool TryNormalizeOptional(string? value, int maxLength, out string normalized, out LocalEntryError error)
        {
            normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > maxLength)
            {
                error = LocalEntryError.TextTooLong;
                return false;
            }

            if (!IsLegalXmlText(normalized))
            {
                error = LocalEntryError.InvalidText;
                return false;
            }

            error = LocalEntryError.None;
            return true;
        }

        private static bool TryParseClock(string? text, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!DateTime.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return false;
            time = parsed.TimeOfDay;
            return true;
        }

        private static bool IsCalendarDateInRange(DateTime value)
        {
            var date = value.Date;
            return date >= MinDate.Date && date <= MaxDate.Date;
        }

        private static bool IsInstantInRange(DateTime value)
        {
            return value >= MinDate && value <= MaxDate;
        }

        private static bool IsLegalSingleLine(string value)
        {
            foreach (var c in value)
            {
                if (c == '\r' || c == '\n' || c == '\t' || char.IsControl(c))
                    return false;
                if (c == '\uFFFE' || c == '\uFFFF')
                    return false;
            }

            return true;
        }

        private static bool IsLegalXmlText(string value)
        {
            foreach (var c in value)
            {
                if (c == '\t' || c == '\n' || c == '\r')
                    continue;
                if (char.IsControl(c) || c == '\uFFFE' || c == '\uFFFF')
                    return false;
            }

            return true;
        }

        private static bool HasAny(List<string>? ids)
        {
            if (ids == null)
                return false;
            foreach (var id in ids)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    return true;
            }

            return false;
        }

        private static StoredCalendarEvent Clone(StoredCalendarEvent evt)
        {
            return new StoredCalendarEvent
            {
                Uid = evt.Uid,
                Summary = evt.Summary,
                Description = evt.Description,
                DtStart = evt.DtStart,
                DtEnd = evt.DtEnd,
                Location = evt.Location,
                IsAllDay = evt.IsAllDay,
            };
        }

        private static StoredLocalTask Clone(StoredLocalTask task)
        {
            return new StoredLocalTask
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                DueDate = task.DueDate,
                Completed = task.Completed,
                CompletedDate = task.CompletedDate,
                CreatedDate = task.CreatedDate,
                LastModified = task.LastModified,
            };
        }
    }
}
