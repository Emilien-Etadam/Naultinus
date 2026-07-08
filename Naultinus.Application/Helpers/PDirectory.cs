using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Naultinus.Helpers
{
    internal static class PDirectory
    {
        internal static string GetAppDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PEnv.IsDev() ? "NaultinusDev" : "Naultinus");
        }

        internal static string GetNaultinusDirectory()
        {
            return Path.Combine(GetAppDirectory(), "saved");
        }

        internal static string GetNaultinusDirectory(string identifier)
        {
            return Path.Combine(GetNaultinusDirectory(), identifier);
        }

        internal static string GetNaultinusIconsDirectory(string identifier)
        {
            return Path.Combine(GetNaultinusDirectory(identifier), "icons");
        }

        internal static string GetNaultinusImportedDirectory(string identifier)
        {
            return Path.Combine(GetNaultinusDirectory(identifier), "imported");
        }

        internal static string GetAccountsFilePath()
        {
            return Path.Combine(GetAppDirectory(), "accounts.xml");
        }

        internal static string GetSnapshotsDirectory()
        {
            return Path.Combine(GetAppDirectory(), "snapshots");
        }

        internal static string GetSettingsFilePath()
        {
            return Path.Combine(GetAppDirectory(), "settings.xml");
        }

        internal static void EnsureExists(string directory)
        {
            DirectoryInfo infos = new(directory);
            if (!infos.Exists)
            {
                infos.Create();
            }
        }

        /// <summary>
        /// Écrit un fichier de façon atomique : écriture dans un fichier temporaire puis remplacement
        /// du fichier cible. Évite de corrompre le fichier existant si l'écriture est interrompue
        /// (disque plein, plantage). Le contenu est produit par <paramref name="writeContent"/>.
        /// </summary>
        internal static void WriteAtomic(string path, Action<Stream> writeContent)
        {
            EnsureExists(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writeContent(fs);
                fs.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        /// <summary>
        /// Variante texte de <see cref="WriteAtomic"/> : fournit un <see cref="TextWriter"/> UTF-8
        /// (même encodage qu'un StreamWriter par défaut), pour préserver le format de sortie existant.
        /// </summary>
        internal static void WriteAtomicText(string path, Action<TextWriter> writeContent)
        {
            WriteAtomic(path, stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writeContent(writer);
            });
        }

        internal static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        internal static IEnumerable<string> DesktopRootPaths()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(d)) yield return d;
            string cd = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(cd)) yield return cd;
        }

        internal static bool IsUnderDesktop(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (var root in DesktopRootPaths())
                {
                    string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (fullPath.Length > fullRoot.Length &&
                        fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("PDirectory.IsUnderDesktop: chemin invalide", ex); }
            return false;
        }

        internal static string AllocateUniqueFilePath(string sourceFile, string destDirectory)
        {
            EnsureExists(destDirectory);
            string name = Path.GetFileName(sourceFile);
            string dest = Path.Combine(destDirectory, name);
            string ext = Path.GetExtension(name);
            string baseName = Path.GetFileNameWithoutExtension(name);
            int c = 1;
            while (File.Exists(dest) || Directory.Exists(dest))
                dest = Path.Combine(destDirectory, $"{baseName} ({c++}){ext}");
            return dest;
        }

        internal static string AllocateUniqueDirectoryPath(string sourceDir, string destParent)
        {
            EnsureExists(destParent);
            string name = new DirectoryInfo(sourceDir).Name;
            string dest = Path.Combine(destParent, name);
            int c = 1;
            while (Directory.Exists(dest) || File.Exists(dest))
                dest = Path.Combine(destParent, $"{name} ({c++})");
            return dest;
        }

        internal static string CreateIconPng(string filePath, string naultinusIdentifier)
        {
            using Bitmap? icon = IconExtractor.GetFileImageFromPath(filePath, Native.IconSizeEnum.LargeIcon48);
            if (icon == null) return string.Empty;
            string iconDir = GetNaultinusIconsDirectory(naultinusIdentifier);
            EnsureExists(iconDir);
            string iconPath = Path.Combine(iconDir, Guid.NewGuid().ToString() + ".png");
            using FileStream fs = new(iconPath, FileMode.Create);
            icon.Save(fs, ImageFormat.Png);
            return iconPath;
        }

        internal static void MoveRobust(string source, string dest, bool isDirectory)
        {
            if (isDirectory)
            {
                try { Directory.Move(source, dest); }
                catch (IOException) { CopyDirectory(source, dest); Directory.Delete(source, recursive: true); }
            }
            else
            {
                try { File.Move(source, dest); }
                catch (IOException) { File.Copy(source, dest, overwrite: false); File.Delete(source); }
            }
        }
    }
}
