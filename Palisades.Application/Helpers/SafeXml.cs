using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Palisades.Helpers
{
    /// <summary>
    /// Désérialisation XML durcie (CA5369) : DTD interdit et aucun résolveur d'entités externes,
    /// pour éviter les attaques XXE / déni de service par expansion d'entités sur les fichiers lus
    /// (état des palisades, comptes, paramètres, snapshots importés).
    /// </summary>
    public static class SafeXml
    {
        private static readonly XmlReaderSettings Settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        public static object? Deserialize(XmlSerializer serializer, Stream stream)
        {
            using var reader = XmlReader.Create(stream, Settings);
            return serializer.Deserialize(reader);
        }

        public static object? Deserialize(XmlSerializer serializer, TextReader textReader)
        {
            using var reader = XmlReader.Create(textReader, Settings);
            return serializer.Deserialize(reader);
        }
    }
}
