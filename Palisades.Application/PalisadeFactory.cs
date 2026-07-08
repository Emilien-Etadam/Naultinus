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
                var (caldavUrl, username, password) = ResolveCalDAVCredentials(taskModel.ZimbraAccountId, taskModel.CalDAVUrl, taskModel.CalDAVUsername, taskModel.CalDAVPassword);
                var client = new CalDAVClient(caldavUrl, username, password);
                return new TaskPalisadeViewModel(taskModel, new CalDAVService(client));
            }

            if (concrete is CalendarPalisadeModel calModel)
            {
                var (calUrl, calUser, calPass) = ResolveCalDAVCredentials(calModel.ZimbraAccountId, calModel.CalDAVBaseUrl, calModel.CalDAVUsername, calModel.CalDAVPassword);
                return new CalendarPalisadeViewModel(calModel, new CalendarCalDAVService(new CalDAVClient(calUrl, calUser, calPass)));
            }

            if (concrete is MailPalisadeModel mailModel)
                return new MailPalisadeViewModel(mailModel);
            if (concrete is StandardPalisadeModel standardModel)
                return new PalisadeViewModel(standardModel);
            return null;
        }

        private static (string Url, string Username, string Password) ResolveCalDAVCredentials(Guid? zimbraAccountId, string? modelUrl, string? modelUser, string? modelEncPass)
        {
            if (zimbraAccountId is Guid id && ZimbraAccountStore.GetById(id) is ZimbraAccount acc)
            {
                string url = !string.IsNullOrEmpty(acc.CalDAVBaseUrl) ? acc.CalDAVBaseUrl : (modelUrl ?? "");
                string user = !string.IsNullOrEmpty(acc.Email) ? acc.Email : (modelUser ?? "");
                return (url, user, CredentialEncryptor.Decrypt(acc.EncryptedPassword ?? ""));
            }
            return (modelUrl ?? "", modelUser ?? "", CredentialEncryptor.Decrypt(modelEncPass ?? ""));
        }

        private static void ApplySize(PalisadeModelBase model, int? x, int? y, int? width, int? height, int defW, int defH)
        {
            if (x.HasValue) model.FenceX = x.Value;
            if (y.HasValue) model.FenceY = y.Value;
            model.Width = width ?? defW;
            model.Height = height ?? defH;
        }

        public static TaskPalisadeViewModel CreateTaskViewModel(string caldavUrl, string username, string password, List<string> taskListIds, string title, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            taskListIds = taskListIds ?? new List<string>();
            var model = new TaskPalisadeModel
            {
                Name = title,
                CalDAVUrl = caldavUrl,
                CalDAVUsername = username,
                CalDAVPassword = zimbraAccountId.HasValue ? string.Empty : CredentialEncryptor.Encrypt(password),
                ZimbraAccountId = zimbraAccountId,
                TaskListIds = taskListIds,
                TaskListId = taskListIds.Count > 0 ? taskListIds[0] : string.Empty,
            };
            ApplySize(model, x, y, width, height, 600, 400);
            var (url, user, pass) = ResolveCalDAVCredentials(zimbraAccountId, caldavUrl, username, model.CalDAVPassword);
            var client = new CalDAVClient(url, user, pass);
            var caldavService = new CalDAVService(client);
            return new TaskPalisadeViewModel(model, caldavService);
        }

        public static CalendarPalisadeViewModel CreateCalendarViewModel(string caldavUrl, string username, string password, List<string> calendarIds, string title, CalendarViewMode viewMode, int daysToShow, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            var model = new CalendarPalisadeModel
            {
                Name = title,
                CalDAVBaseUrl = caldavUrl,
                CalDAVUsername = username,
                CalDAVPassword = zimbraAccountId.HasValue ? string.Empty : CredentialEncryptor.Encrypt(password),
                ZimbraAccountId = zimbraAccountId,
                CalendarIds = calendarIds ?? new List<string>(),
                ViewMode = viewMode,
                DaysToShow = daysToShow,
            };
            ApplySize(model, x, y, width, height, 500, 400);
            var (url, user, pass) = ResolveCalDAVCredentials(zimbraAccountId, caldavUrl, username, model.CalDAVPassword);
            var client = new CalDAVClient(url, user, pass);
            var calendarService = new CalendarCalDAVService(client);
            return new CalendarPalisadeViewModel(model, calendarService);
        }

        public static MailPalisadeViewModel CreateMailViewModel(string imapHost, int imapPort, string username, string password, List<string> monitoredFolders, string title, MailDisplayMode displayMode, int pollIntervalMinutes, string? webmailUrl, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            var model = new MailPalisadeModel
            {
                Name = title,
                ImapHost = imapHost,
                ImapPort = imapPort,
                ImapUsername = username,
                ImapPassword = zimbraAccountId.HasValue ? string.Empty : CredentialEncryptor.Encrypt(password),
                ZimbraAccountId = zimbraAccountId,
                MonitoredFolders = monitoredFolders ?? new List<string> { "INBOX" },
                DisplayMode = displayMode,
                PollIntervalMinutes = pollIntervalMinutes,
                WebmailUrl = webmailUrl,
            };
            ApplySize(model, x, y, width, height, 320, 240);
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
