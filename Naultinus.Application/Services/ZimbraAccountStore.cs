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

        public static List<ZimbraAccount> Load()
        {
            var path = AppPaths.GetAccountsFilePath();
            if (!TryRead(path, out var accounts))
                return new List<ZimbraAccount>();

            var settings = AppSettingsStore.Load();
            if (SharedCalDavAccount.TryAbsorbIntoList(settings, accounts))
            {
                Save(accounts);
                AppSettingsStore.Save(settings);
            }

            return accounts;
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
            SaveTo(AppPaths.GetAccountsFilePath(), accounts);
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
    }
}
