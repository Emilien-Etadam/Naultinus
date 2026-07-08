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
            if (!File.Exists(path))
                return new List<ZimbraAccount>();
            try
            {
                using var reader = new StreamReader(path);
                if (SafeXml.Deserialize(Serializer, reader) is List<ZimbraAccount> list)
                    return list;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ZimbraAccountStore", "Lecture des comptes impossible : " + path, ex);
            }

            return new List<ZimbraAccount>();
        }

        public static void Save(List<ZimbraAccount> accounts)
        {
            var path = AppPaths.GetAccountsFilePath();
            AppPaths.WriteAtomicText(path, writer => Serializer.Serialize(writer, accounts ?? new List<ZimbraAccount>()));
        }

        public static ZimbraAccount? GetById(Guid id)
        {
            return Load().Find(a => a.Id == id);
        }
    }
}
