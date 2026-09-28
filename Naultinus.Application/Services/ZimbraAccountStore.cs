using Naultinus.Helpers;
using Naultinus.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Naultinus.Services
{
    /// <summary>
    /// Persistance des comptes Zimbra (Phase 3.4). Fichier %LOCALAPPDATA%\Naultinus\accounts.xml.
    /// </summary>
    public static class ZimbraAccountStore
    {
        private static readonly XmlSerializer Serializer = new(typeof(List<ZimbraAccount>), new[] { typeof(ZimbraAccount) });
        private static readonly object SessionsGate = new();
        private static readonly Dictionary<string, AccountLoadSession> Sessions = new(StringComparer.OrdinalIgnoreCase);

        public static List<ZimbraAccount> Load()
        {
            return LoadMigrating(AppPaths.GetAccountsFilePath(), AppPaths.GetSettingsFilePath());
        }

        /// <summary>
        /// Lit les comptes, puis n'enregistre qu'une fois si une migration a réellement modifié
        /// les données. Un enregistrement impossible ne vide pas les secrets déjà lus.
        /// </summary>
        internal static List<ZimbraAccount> LoadMigrating(string accountsPath, string settingsPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(accountsPath);
            ArgumentException.ThrowIfNullOrEmpty(settingsPath);

            lock (SessionsGate)
            {
                var session = SessionFor(accountsPath);
                if (session.Pending != null)
                    return CopyAccounts(session.Pending);

                if (!TryRead(accountsPath, out var accounts))
                    return new List<ZimbraAccount>();

                if (session.MigrationDone)
                    return accounts;

                var settings = AppSettingsStore.LoadFrom(settingsPath);
                var absorbed = SharedCalDavAccount.TryAbsorbIntoList(settings, accounts);
                var stripped = SharedCalDavAccount.RemoveStoredUrlUserInfo(accounts);
                if (absorbed || stripped)
                {
                    try
                    {
                        SaveTo(accountsPath, accounts);
                    }
                    catch (Exception ex)
                    {
                        NaultinusDiagnostics.Log(
                            "ZimbraAccountStore",
                            "Enregistrement des comptes reporté : " + accountsPath,
                            ex);
                        session.Pending = CopyAccounts(accounts);
                        session.MigrationDone = true;
                        return accounts;
                    }
                }

                if (absorbed)
                {
                    try
                    {
                        AppSettingsStore.SaveTo(settingsPath, settings);
                    }
                    catch (Exception ex)
                    {
                        NaultinusDiagnostics.Log(
                            "ZimbraAccountStore",
                            "Ancien compte CalDAV conservé dans les paramètres : " + settingsPath,
                            ex);
                    }
                }

                session.MigrationDone = true;
                return accounts;
            }
        }

        /// <summary>Même lecture que <see cref="Load"/>, sans migration et sans toucher au profil.</summary>
        internal static List<ZimbraAccount> LoadFrom(string path)
        {
            return TryRead(path, out var accounts) ? accounts : new List<ZimbraAccount>();
        }

        /// <summary>False si le fichier existe mais ne se lit pas : on n'écrase pas un accounts.xml illisible.</summary>
        private static bool TryRead(string path, out List<ZimbraAccount> accounts)
        {
            accounts = new List<ZimbraAccount>();
            if (!File.Exists(path))
                return true;
            try
            {
                using var reader = new StreamReader(path);
                if (SafeXml.Deserialize(Serializer, reader) is List<ZimbraAccount> list)
                {
                    accounts = list;
                    return true;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ZimbraAccountStore", "Lecture des comptes impossible : " + path, ex);
            }

            return false;
        }

        public static void Save(List<ZimbraAccount> accounts)
        {
            var path = AppPaths.GetAccountsFilePath();
            lock (SessionsGate)
            {
                SaveTo(path, accounts);
                var session = SessionFor(path);
                session.Pending = null;
                session.MigrationDone = true;
            }
        }

        /// <summary>Même écriture que <see cref="Save"/>, vers un fichier explicite.</summary>
        internal static void SaveTo(string path, List<ZimbraAccount> accounts)
        {
            AppPaths.WriteAtomicText(path, writer => Serializer.Serialize(writer, accounts ?? new List<ZimbraAccount>()));
        }

        public static ZimbraAccount? GetById(Guid id)
        {
            return Load().Find(a => a.Id == id);
        }

        private static AccountLoadSession SessionFor(string path)
        {
            var key = Path.GetFullPath(path);
            if (!Sessions.TryGetValue(key, out var session))
            {
                session = new AccountLoadSession();
                Sessions[key] = session;
            }

            return session;
        }

        private static List<ZimbraAccount> CopyAccounts(List<ZimbraAccount> accounts)
        {
            var copy = new List<ZimbraAccount>(accounts.Count);
            foreach (var account in accounts)
            {
                if (account == null)
                    continue;
                copy.Add(new ZimbraAccount
                {
                    Id = account.Id,
                    Server = account.Server,
                    Email = account.Email,
                    EncryptedPassword = account.EncryptedPassword,
                    CalDAVBaseUrl = account.CalDAVBaseUrl,
                    ImapHost = account.ImapHost,
                    LastTestStatus = account.LastTestStatus,
                    UsedByCalendarsAndTasks = account.UsedByCalendarsAndTasks,
                });
            }

            return copy;
        }

        private sealed class AccountLoadSession
        {
            public bool MigrationDone { get; set; }

            public List<ZimbraAccount>? Pending { get; set; }
        }
    }
}
