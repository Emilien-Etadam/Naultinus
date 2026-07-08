using System;
using System.Xml.Serialization;
using Naultinus.Model;

namespace Naultinus.Serialization
{
    /// <summary>
    /// Point unique pour le <see cref="XmlSerializer"/> des états naultinus (<c>state.xml</c>, snapshots).
    /// Tout nouveau type sérialisé sous <see cref="NaultinusModelBase"/> doit être ajouté ici et dans les attributs <c>[XmlInclude]</c> du modèle.
    /// </summary>
    public static class NaultinusXmlSerialization
    {
        /// <summary>Types additionnels pour le polymorphisme XML (héritiers de <see cref="NaultinusModelBase"/> et raccourcis).</summary>
        public static Type[] ExtraModelTypes { get; } =
        {
            typeof(NaultinusModel),
            typeof(StandardNaultinusModel),
            typeof(FolderPortalModel),
            typeof(TaskNaultinusModel),
            typeof(CalendarNaultinusModel),
            typeof(MailNaultinusModel),
            typeof(Shortcut),
            typeof(LnkShortcut),
            typeof(UrlShortcut),
        };

        /// <summary>Sérialiseur partagé : même instance pour ViewModelBase, LayoutSnapshotService et chargement initial.</summary>
        public static readonly XmlSerializer NaultinusModelSerializer = new(typeof(NaultinusModelBase), ExtraModelTypes);
    }
}
