using System;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    /// <summary>
    /// Compte Zimbra centralisé (Phase 3.4). Credentials chiffrés DPAPI.
    /// Les naultinus CalDAV / Calendrier / Mail référencent cet account par Id.
    /// </summary>
    public class ZimbraAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Server { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        /// <summary>Mot de passe chiffré via DPAPI (ProtectedData).</summary>
        public string EncryptedPassword { get; set; } = string.Empty;
        /// <summary>URL de base CalDAV, ex. https://server/dav/user@domain/</summary>
        public string CalDAVBaseUrl { get; set; } = string.Empty;
        /// <summary>Hôte IMAP pour Mail Naultinus (ex. ssl0.ovh.net).</summary>
        public string ImapHost { get; set; } = string.Empty;
        /// <summary>Statut du dernier test (affiché dans ManageAccountsDialog).</summary>
        public string? LastTestStatus { get; set; }
    }
}
