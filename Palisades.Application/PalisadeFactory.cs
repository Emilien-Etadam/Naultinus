using System;
using System.Collections.Generic;
using System.Windows;
using Palisades.Helpers;
using Palisades.Model;
using Palisades.Services;
using Palisades.View;
using Palisades.ViewModel;

namespace Palisades
{
    /// <summary>Crée les fenêtres WPF et les ViewModels de palisade (sans dépendre de <see cref="PalisadesManager"/> ni du dictionnaire de fenêtres).</summary>
    internal static class PalisadeFactory
    {
        public static Window CreateWindow(IPalisadeViewModel vm) => vm switch
        {
            PalisadeViewModel p => new Palisade(p),
            FolderPortalViewModel f => new FolderPortal(f),
            TaskPalisadeViewModel t => new TaskPalisade(t),
            CalendarPalisadeViewModel c => new CalendarPalisade(c),
            MailPalisadeViewModel m => new MailPalisade(m),
            _ => throw new NotSupportedException("No window for " + vm.GetType().Name),
        };

        public static IPalisadeViewModel? CreateViewModel(PalisadeModelBase concrete)
        {
            if (concrete is FolderPortalModel folderModel)
                return new FolderPortalViewModel(folderModel);
            if (concrete is TaskPalisadeModel taskModel)
            {
                string caldavUrl;
                string username;
                string password;
                if (taskModel.ZimbraAccountId is Guid accountId && ZimbraAccountStore.GetById(accountId) is ZimbraAccount account)
                {
                    caldavUrl = account.CalDAVBaseUrl ?? string.Empty;
                    username = account.Email ?? string.Empty;
                    password = CredentialEncryptor.Decrypt(account.EncryptedPassword ?? "");
                }
                else
                {
                    caldavUrl = taskModel.CalDAVUrl ?? string.Empty;
                    username = taskModel.CalDAVUsername ?? string.Empty;
                    password = CredentialEncryptor.Decrypt(taskModel.CalDAVPassword ?? "");
                }

                var client = new CalDAVClient(caldavUrl, username, password);
                var caldavService = new CalDAVService(client);
                return new TaskPalisadeViewModel(taskModel, caldavService);
            }

            if (concrete is CalendarPalisadeModel calModel)
            {
                string calUrl = calModel.CalDAVBaseUrl ?? "";
                string calUser = calModel.CalDAVUsername ?? "";
                string calPass = "";
                if (calModel.ZimbraAccountId is Guid zimbraId && ZimbraAccountStore.GetById(zimbraId) is ZimbraAccount zimbraAcc)
                {
                    calUrl = !string.IsNullOrEmpty(zimbraAcc.CalDAVBaseUrl) ? zimbraAcc.CalDAVBaseUrl : calUrl;
                    calUser = !string.IsNullOrEmpty(zimbraAcc.Email) ? zimbraAcc.Email : calUser;
                    calPass = CredentialEncryptor.Decrypt(zimbraAcc.EncryptedPassword ?? "");
                }
                else
                {
                    calPass = CredentialEncryptor.Decrypt(calModel.CalDAVPassword ?? "");
                }

                var calClient = new CalDAVClient(calUrl, calUser, calPass);
                var calService = new CalendarCalDAVService(calClient);
                return new CalendarPalisadeViewModel(calModel, calService);
            }

            if (concrete is MailPalisadeModel mailModel)
                return new MailPalisadeViewModel(mailModel);
            if (concrete is StandardPalisadeModel standardModel)
                return new PalisadeViewModel(standardModel);
            return null;
        }

        private static void ApplyPosition(PalisadeModelBase model, int? x, int? y)
        {
            if (x.HasValue) model.FenceX = x.Value;
            if (y.HasValue) model.FenceY = y.Value;
        }

        public static TaskPalisadeViewModel CreateTaskViewModel(string caldavUrl, string username, string password, List<string> taskListIds, string title, int? x, int? y, int? width, int? height)
        {
            taskListIds = taskListIds ?? new List<string>();
            var model = new TaskPalisadeModel
            {
                Name = title,
                CalDAVUrl = caldavUrl,
                CalDAVUsername = username,
                CalDAVPassword = CredentialEncryptor.Encrypt(password),
                TaskListIds = taskListIds,
                TaskListId = taskListIds.Count > 0 ? taskListIds[0] : string.Empty,
                Width = width ?? 600,
                Height = height ?? 400,
            };
            ApplyPosition(model, x, y);
            var client = new CalDAVClient(caldavUrl, username, password);
            var caldavService = new CalDAVService(client);
            return new TaskPalisadeViewModel(model, caldavService);
        }

        public static CalendarPalisadeViewModel CreateCalendarViewModel(string caldavUrl, string username, string password, List<string> calendarIds, string title, CalendarViewMode viewMode, int daysToShow, int? x, int? y, int? width, int? height)
        {
            var model = new CalendarPalisadeModel
            {
                Name = title,
                CalDAVBaseUrl = caldavUrl,
                CalDAVUsername = username,
                CalDAVPassword = CredentialEncryptor.Encrypt(password),
                CalendarIds = calendarIds ?? new List<string>(),
                ViewMode = viewMode,
                DaysToShow = daysToShow,
                Width = width ?? 500,
                Height = height ?? 400,
            };
            ApplyPosition(model, x, y);
            var client = new CalDAVClient(caldavUrl, username, password);
            var calendarService = new CalendarCalDAVService(client);
            return new CalendarPalisadeViewModel(model, calendarService);
        }

        public static MailPalisadeViewModel CreateMailViewModel(string imapHost, int imapPort, string username, string password, List<string> monitoredFolders, string title, MailDisplayMode displayMode, int pollIntervalMinutes, string? webmailUrl, int? x, int? y, int? width, int? height)
        {
            var model = new MailPalisadeModel
            {
                Name = title,
                ImapHost = imapHost,
                ImapPort = imapPort,
                ImapUsername = username,
                ImapPassword = CredentialEncryptor.Encrypt(password),
                MonitoredFolders = monitoredFolders ?? new List<string> { "INBOX" },
                DisplayMode = displayMode,
                PollIntervalMinutes = pollIntervalMinutes,
                WebmailUrl = webmailUrl,
                Width = width ?? 320,
                Height = height ?? 240,
            };
            ApplyPosition(model, x, y);
            return new MailPalisadeViewModel(model);
        }

        public static IImapMailService CreateImapMailService(MailPalisadeModel model)
        {
            string host;
            int port;
            string username;
            string password;
            if (model.ZimbraAccountId is Guid id && ZimbraAccountStore.GetById(id) is ZimbraAccount acc)
            {
                host = !string.IsNullOrEmpty(acc.ImapHost) ? acc.ImapHost : acc.Server;
                port = 993;
                username = acc.Email ?? "";
                password = CredentialEncryptor.Decrypt(acc.EncryptedPassword ?? "");
            }
            else
            {
                host = model.ImapHost;
                port = model.ImapPort > 0 ? model.ImapPort : 993;
                username = model.ImapUsername;
                password = CredentialEncryptor.Decrypt(model.ImapPassword ?? "");
            }

            return new ImapMailService(host, port, username, password);
        }
    }
}
