using System;
using System.Collections.Generic;
using System.Windows;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.View;
using Naultinus.ViewModel;

namespace Naultinus
{
    /// <summary>Crée les fenêtres WPF et les ViewModels de naultinus (sans dépendre de <see cref="NaultinusManager"/> ni du dictionnaire de fenêtres).</summary>
    internal static class NaultinusFactory
    {
        public static Window CreateWindow(INaultinusViewModel vm) => vm switch
        {
            NaultinusViewModel p => new StandardNaultinus(p),
            FolderPortalViewModel f => new FolderPortal(f),
            TaskNaultinusViewModel t => new TaskNaultinus(t),
            CalendarNaultinusViewModel c => new CalendarNaultinus(c),
            MailNaultinusViewModel m => new MailNaultinus(m),
            _ => throw new NotSupportedException("No window for " + vm.GetType().Name),
        };

        public static INaultinusViewModel? CreateViewModel(NaultinusModelBase concrete)
        {
            if (concrete is FolderPortalModel folderModel)
                return new FolderPortalViewModel(folderModel);
            if (concrete is TaskNaultinusModel taskModel)
            {
                var (caldavUrl, username, password) = ResolveCalDAVCredentials(taskModel.ZimbraAccountId, taskModel.CalDAVUrl, taskModel.CalDAVUsername, taskModel.CalDAVPassword);
                var client = new CalDAVClient(caldavUrl, username, password);
                return new TaskNaultinusViewModel(taskModel, new CalDAVService(client));
            }

            if (concrete is CalendarNaultinusModel calModel)
            {
                var (calUrl, calUser, calPass) = ResolveCalDAVCredentials(calModel.ZimbraAccountId, calModel.CalDAVBaseUrl, calModel.CalDAVUsername, calModel.CalDAVPassword);
                return new CalendarNaultinusViewModel(calModel, new CalendarCalDAVService(new CalDAVClient(calUrl, calUser, calPass)));
            }

            if (concrete is MailNaultinusModel mailModel)
                return new MailNaultinusViewModel(mailModel);
            if (concrete is StandardNaultinusModel standardModel)
                return new NaultinusViewModel(standardModel);
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

        private static void ApplySize(NaultinusModelBase model, int? x, int? y, int? width, int? height, int defW, int defH)
        {
            if (x.HasValue) model.FenceX = x.Value;
            if (y.HasValue) model.FenceY = y.Value;
            model.Width = width ?? defW;
            model.Height = height ?? defH;
        }

        public static TaskNaultinusViewModel CreateTaskViewModel(string caldavUrl, string username, string password, List<string> taskListIds, string title, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            taskListIds = taskListIds ?? new List<string>();
            var model = new TaskNaultinusModel
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
            return new TaskNaultinusViewModel(model, caldavService);
        }

        public static CalendarNaultinusViewModel CreateCalendarViewModel(string caldavUrl, string username, string password, List<string> calendarIds, string title, CalendarViewMode viewMode, int daysToShow, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            var model = new CalendarNaultinusModel
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
            return new CalendarNaultinusViewModel(model, calendarService);
        }

        public static MailNaultinusViewModel CreateMailViewModel(string imapHost, int imapPort, string username, string password, List<string> monitoredFolders, string title, MailDisplayMode displayMode, int pollIntervalMinutes, string? webmailUrl, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            var model = new MailNaultinusModel
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
            return new MailNaultinusViewModel(model);
        }

        public static IImapMailService CreateImapMailService(MailNaultinusModel model)
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
