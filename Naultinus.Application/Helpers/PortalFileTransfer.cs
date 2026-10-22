using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Naultinus.Properties;

namespace Naultinus.Helpers
{
    /// <summary>Copie / déplacement de chemins vers un dossier du portail (collision → « nom (n) »).</summary>
    internal static class PortalFileTransfer
    {
        /// <summary>
        /// Importe <paramref name="sourcePaths"/> dans <paramref name="targetDir"/> s'il est sous <paramref name="rootPath"/>.
        /// </summary>
        /// <returns>Dernier message d'erreur, ou <c>null</c> si tout s'est bien passé (ou rien à faire).</returns>
        internal static string? TryImport(
            string rootPath,
            string targetDir,
            IEnumerable<string> sourcePaths,
            bool isCopy)
        {
            if (string.IsNullOrEmpty(targetDir)
                || !Directory.Exists(targetDir)
                || !PortalPathGuard.IsAllowed(rootPath, targetDir))
                return null;

            string? lastError = null;
            foreach (string sourcePath in sourcePaths)
            {
                try
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = ResolveUniqueDestination(targetDir, fileName);

                    if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (Directory.Exists(sourcePath))
                    {
                        if (isCopy)
                            AppPaths.CopyDirectory(sourcePath, destPath);
                        else
                            Directory.Move(sourcePath, destPath);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        if (isCopy)
                            File.Copy(sourcePath, destPath);
                        else
                            File.Move(sourcePath, destPath);
                    }
                }
                catch (Exception ex)
                {
                    lastError = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.FileOperationFailedFormat,
                        isCopy ? Strings.CopyVerb : Strings.MoveVerb,
                        Path.GetFileName(sourcePath),
                        ex.Message);
                }
            }

            return lastError;
        }

        internal static string ResolveUniqueDestination(string targetDir, string fileName)
        {
            string destPath = Path.Combine(targetDir, fileName);
            if (!File.Exists(destPath) && !Directory.Exists(destPath))
                return destPath;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            do
            {
                destPath = Path.Combine(targetDir, $"{nameWithoutExt} ({counter}){ext}");
                counter++;
            }
            while (File.Exists(destPath) || Directory.Exists(destPath));

            return destPath;
        }
    }
}
