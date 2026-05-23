using Palisades.Helpers;
using Palisades.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Palisades.Services
{
    /// <summary>
    /// Persistance des comptes Zimbra (Phase 3.4). Fichier %LOCALAPPDATA%\Palisades\accounts.xml.
    /// </summary>
    public static class ZimbraAccountStore
    {
        private static readonly XmlSerializer Serializer = new(typeof(List<ZimbraAccount>), new[] { typeof(ZimbraAccount) });

        public static List<ZimbraAccount> Load()
        {
            var path = PDirectory.GetAccountsFilePath();
            if (!File.Exists(path))
                return new List<ZimbraAccount>();
            try
            {
                using var reader = new StreamReader(path);
                if (Serializer.Deserialize(reader) is List<ZimbraAccount> list)
                    return list;
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("ZimbraAccountStore", "Lecture des comptes impossible : " + path, ex);
            }

            return new List<ZimbraAccount>();
        }

        public static void Save(List<ZimbraAccount> accounts)
        {
            var path = PDirectory.GetAccountsFilePath();
            PDirectory.EnsureExists(Path.GetDirectoryName(path)!);
            using var writer = new StreamWriter(path);
            Serializer.Serialize(writer, accounts ?? new List<ZimbraAccount>());
        }

        public static ZimbraAccount? GetById(Guid id)
        {
            return Load().Find(a => a.Id == id);
        }
    }
}
