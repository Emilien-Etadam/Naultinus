using System.Xml.Serialization;

namespace Naultinus.Model
{
    /// <summary>
    /// Configuration globale de l'application (Phase 10.2).
    /// </summary>
    public class AppSettings
    {
        public TabStyle DefaultTabStyle { get; set; } = TabStyle.Flat;
    }
}