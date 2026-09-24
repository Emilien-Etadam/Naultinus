using System.Xml.Serialization;

namespace Naultinus.Model
{
    /// <summary>
    /// Configuration globale de l'application (Phase 10.2).
    /// </summary>
    public class AppSettings
    {
        public TabStyle DefaultTabStyle { get; set; } = TabStyle.Flat;

        /// <summary>URL HTTPS du compte CalDAV unique (calendriers et tâches). Le courriel ne lit pas ce champ.</summary>
        public string CalDavBaseUrl { get; set; } = string.Empty;

        /// <summary>Identifiant du compte CalDAV unique.</summary>
        public string CalDavUsername { get; set; } = string.Empty;

        /// <summary>Mot de passe CalDAV chiffré (DPAPI). Jamais le mot de passe en clair.</summary>
        public string CalDavEncryptedPassword { get; set; } = string.Empty;
    }
}