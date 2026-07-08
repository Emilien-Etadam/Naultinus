using System;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Détection / pré-remplissage pour configuration Zimbra OVH (Phase 7.3).
    /// À partir de l'adresse email, suggère serveur IMAP et URL CalDAV selon les conventions OVH.
    /// </summary>
    public static class ZimbraOvhDetection
    {
        /// <summary>
        /// Suggère l'hôte IMAP et l'URL de base CalDAV pour un email (conventions OVH Zimbra).
        /// IMAP OVH : ssl0.ovh.net fonctionne pour tous les domaines.
        /// CalDAV OVH : le host exact dépend du nœud Zimbra attribué au compte ; on ne peut pas le deviner.
        /// On laisse l'URL vide — l'utilisateur renseignera son URL réelle (ex: https://zimbra1.mail.ovh.net/dav/user@domain/).
        /// </summary>
        public static (string ImapHost, string CalDAVBaseUrl) SuggestFromEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return ("ssl0.ovh.net", "");

            email = email.Trim();
            string imapHost = "ssl0.ovh.net";
            string caldavBaseUrl = "";
            return (imapHost, caldavBaseUrl);
        }
    }
}
