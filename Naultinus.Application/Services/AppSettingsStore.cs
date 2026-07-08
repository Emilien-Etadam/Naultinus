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
            var path = AppPaths.GetSettingsFilePath();
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

        public static void Save(AppSettings settings)
        {
            var path = AppPaths.GetSettingsFilePath();
            AppPaths.WriteAtomicText(path, writer => Serializer.Serialize(writer, settings ?? new AppSettings()));
        }
    }
}