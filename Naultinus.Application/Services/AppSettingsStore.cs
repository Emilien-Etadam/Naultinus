using Naultinus.Helpers;
using Naultinus.Model;
using System;
using System.IO;
using System.Xml.Serialization;

namespace Naultinus.Services
{
    /// <summary>
    /// Persistance des paramètres globaux (Phase 10.2). Fichier %LOCALAPPDATA%\Naultinus\settings.xml.
    /// </summary>
    public static class AppSettingsStore
    {
        private static readonly XmlSerializer Serializer = new(typeof(AppSettings));

        public static AppSettings Load()
        {
            return LoadFrom(AppPaths.GetSettingsFilePath());
        }

        public static void Save(AppSettings settings)
        {
            SaveTo(AppPaths.GetSettingsFilePath(), settings);
        }

        /// <summary>Même lecture que <see cref="Load"/>, vers un fichier explicite (tests, sans toucher au profil).</summary>
        internal static AppSettings LoadFrom(string path)
        {
            if (!File.Exists(path))
                return new AppSettings();
            try
            {
                using var reader = new StreamReader(path);
                if (SafeXml.Deserialize(Serializer, reader) is AppSettings settings)
                    return settings;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("AppSettingsStore", "Impossible de charger les paramètres : " + path, ex);
            }

            return new AppSettings();
        }

        /// <summary>Même écriture que <see cref="Save"/>, vers un fichier explicite.</summary>
        internal static void SaveTo(string path, AppSettings settings)
        {
            AppPaths.WriteAtomicText(path, writer => Serializer.Serialize(writer, settings ?? new AppSettings()));
        }
    }
}