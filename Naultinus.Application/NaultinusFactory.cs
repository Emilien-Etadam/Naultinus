using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Serialization;
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
            _ => throw new NotSupportedException("Aucune fenêtre pour " + vm.GetType().Name),
        };

        public static INaultinusViewModel? CreateViewModel(NaultinusModelBase concrete)
        {
            if (concrete is FolderPortalModel folderModel)
                return new FolderPortalViewModel(folderModel);
            if (concrete is TaskNaultinusModel taskModel)
            {
                ForgetWindowCalDavSecrets(taskModel);
                return new TaskNaultinusViewModel(taskModel, new CalDAVService(CreateSharedCalDavClient()));
            }

            if (concrete is CalendarNaultinusModel calModel)
            {
                ForgetWindowCalDavSecrets(calModel);
                return new CalendarNaultinusViewModel(calModel, new CalendarCalDAVService(CreateSharedCalDavClient()));
            }

            if (concrete is MailNaultinusModel mailModel)
                return new MailNaultinusViewModel(mailModel);
            if (concrete is StandardNaultinusModel standardModel)
                return new NaultinusViewModel(standardModel);
            return null;
        }

        /// <summary>
        /// Une fois le compte de la liste en place, retire les identifiants CalDAV du state.xml.
        /// Ils ne sont jamais recopiés : retirer le compte ne le fait pas revenir au prochain démarrage.
        /// </summary>
        private static void ForgetWindowCalDavSecrets(TaskNaultinusModel model)
        {
            if (!SharedCalDavAccount.IsConfigured())
                return;
            if (model.ZimbraAccountId == null
                && string.IsNullOrEmpty(model.CalDAVUrl)
                && string.IsNullOrEmpty(model.CalDAVUsername)
                && string.IsNullOrEmpty(model.CalDAVPassword))
                return;

            model.ZimbraAccountId = null;
            model.CalDAVUrl = string.Empty;
            model.CalDAVUsername = string.Empty;
            model.CalDAVPassword = string.Empty;
            WriteStateWithoutWindowSecrets(model);
        }

        private static void ForgetWindowCalDavSecrets(CalendarNaultinusModel model)
        {
            if (!SharedCalDavAccount.IsConfigured())
                return;
            if (model.ZimbraAccountId == null
                && string.IsNullOrEmpty(model.CalDAVBaseUrl)
                && string.IsNullOrEmpty(model.CalDAVUsername)
                && string.IsNullOrEmpty(model.CalDAVPassword))
                return;

            model.ZimbraAccountId = null;
            model.CalDAVBaseUrl = string.Empty;
            model.CalDAVUsername = string.Empty;
            model.CalDAVPassword = string.Empty;
            WriteStateWithoutWindowSecrets(model);
        }

        private static void WriteStateWithoutWindowSecrets(NaultinusModelBase model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Identifier))
                    return;
                var directory = AppPaths.GetNaultinusDirectory(model.Identifier);
                if (!Directory.Exists(directory))
                    return;
                NaultinusStateFile.Write(directory, model);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("NaultinusFactory", "Retrait des identifiants CalDAV de fenêtre impossible", ex);
            }
        }

        private static CalDAVClient CreateSharedCalDavClient()
        {
            var account = SharedCalDavAccount.GetMarked();
            return new CalDAVClient(
                account?.CalDAVBaseUrl ?? string.Empty,
                account?.Email ?? string.Empty,
                SharedCalDavAccount.ReadPassword(account));
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
            // Les identifiants reçus ne sont pas recopiés : le client lit le compte CalDAV partagé.
            _ = caldavUrl;
            _ = username;
            _ = password;
            _ = zimbraAccountId;
            var model = new TaskNaultinusModel
            {
                Name = title,
                TaskListIds = taskListIds,
                TaskListId = taskListIds.Count > 0 ? taskListIds[0] : string.Empty,
            };
            ApplySize(model, x, y, width, height, 600, 400);
            return new TaskNaultinusViewModel(model, new CalDAVService(CreateSharedCalDavClient()));
        }

        public static CalendarNaultinusViewModel CreateCalendarViewModel(string caldavUrl, string username, string password, List<string> calendarIds, string title, CalendarViewMode viewMode, int daysToShow, int? x, int? y, int? width, int? height, Guid? zimbraAccountId = null)
        {
            _ = caldavUrl;
            _ = username;
            _ = password;
            _ = zimbraAccountId;
            var model = new CalendarNaultinusModel
            {
                Name = title,
                CalendarIds = calendarIds ?? new List<string>(),
                ViewMode = viewMode,
                DaysToShow = daysToShow,
            };
            ApplySize(model, x, y, width, height, 500, 400);
            return new CalendarNaultinusViewModel(model, new CalendarCalDAVService(CreateSharedCalDavClient()));
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
