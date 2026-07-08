using System;
using System.Xml.Serialization;

namespace Naultinus.Model
{
    /// <summary>
    /// Modèle de base commun à toutes les naultinus. Les sous-types sont utilisés pour la sérialisation polymorphe.
    /// </summary>
    [XmlInclude(typeof(NaultinusModel))]
    [XmlInclude(typeof(StandardNaultinusModel))]
    [XmlInclude(typeof(FolderPortalModel))]
    [XmlInclude(typeof(TaskNaultinusModel))]
    [XmlInclude(typeof(CalendarNaultinusModel))]
    [XmlInclude(typeof(MailNaultinusModel))]
    [XmlRoot(Namespace = "io.stouder", ElementName = "NaultinusModel")]
    public abstract class NaultinusModelBase
    {
        // SchemaVersion history:
        // 1 - Initial versioned format (v0.5.x). Corresponds to all models prior to explicit versioning.
        // When adding version 2: add migration logic in NaultinusModelMigration.
        private int _schemaVersion = 1;

        private string _identifier;
        private string _name;
        private int _fenceX;
        private int _fenceY;
        private int _width;
        private int _height;
        private NaultinusType _type;

        protected NaultinusModelBase()
        {
            _identifier = Guid.NewGuid().ToString();
            _name = "No name";
            _width = 800;
            _height = 450;
            _type = NaultinusType.Standard;
        }

        public string Identifier { get => _identifier; set => _identifier = value; }
        public string Name { get => _name; set => _name = value; }
        public int FenceX { get => _fenceX; set => _fenceX = value; }
        public int FenceY { get => _fenceY; set => _fenceY = value; }
        public int Width { get => _width; set => _width = value; }
        public int Height { get => _height; set => _height = value; }
        public NaultinusType Type { get => _type; set => _type = value; }
        /// <summary>Groupe d'onglets (Phase 10.2). Null = naultinus autonome.</summary>
        public string? GroupId { get; set; }
        /// <summary>Position de l'onglet dans le groupe (défaut 0).</summary>
        public int TabOrder { get; set; }

        /// <summary>Version du schéma XML pour state.xml (rétrocompatibilité : anciens fichiers sans attribut → 0, traité comme 1 au chargement).</summary>
        public int SchemaVersion { get => _schemaVersion; set => _schemaVersion = value; }
    }
}
