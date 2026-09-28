using System.Xml.Serialization;

namespace Naultinus.Model
{
    /// <summary>
    /// Configuration globale de l'application (Phase 10.2).
    /// </summary>
    public class AppSettings
    {
        public TabStyle DefaultTabStyle { get; set; } = TabStyle.Flat;

        /// <summary>
        /// Ancienne URL CalDAV. Lue une fois pour créer la ligne dans accounts.xml, puis effacée.
        /// Les calendriers lisent la ligne marquée, pas ce champ.
        /// </summary>
        public string CalDavBaseUrl { get; set; } = string.Empty;

        /// <summary>Ancien identifiant CalDAV, même cycle que <see cref="CalDavBaseUrl"/>.</summary>
        public string CalDavUsername { get; set; } = string.Empty;

        /// <summary>Ancien blob CalDAV. Copié vers la ligne, jamais déchiffré ici, puis effacé.</summary>
        public string CalDavEncryptedPassword { get; set; } = string.Empty;
    }
}