using System;
using System.IO;
using Naultinus.Helpers;
using Naultinus.Model;

namespace Naultinus.Serialization
{
    /// <summary>
    /// Lecture et écriture du state.xml d'une naultinus (même sérialiseur que l'enregistrement de l'application).
    /// </summary>
    internal static class NaultinusStateFile
    {
        internal static void Write(string directory, NaultinusModelBase model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Le dossier d'enregistrement est requis.", nameof(directory));

            AppPaths.WriteAtomicText(
                Path.Combine(directory, "state.xml"),
                writer => NaultinusXmlSerialization.NaultinusModelSerializer.Serialize(writer, model));
        }

        internal static NaultinusModelBase? Read(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            var path = Path.Combine(directory, "state.xml");
            if (!File.Exists(path))
                return null;

            using var reader = new StreamReader(path);
            return SafeXml.Deserialize(NaultinusXmlSerialization.NaultinusModelSerializer, reader) as NaultinusModelBase;
        }
    }
}
