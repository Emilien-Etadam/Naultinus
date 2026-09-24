using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Libellé de fichier tel que l'Explorateur : le DWORD
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt</c>
    /// masque l'extension des types connus seulement. 1 (ou valeur absente) = masquer, 0 = afficher.
    /// </summary>
    internal static class ExplorerFileNames
    {
        private const string HideFileExtKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private static readonly ConcurrentDictionary<string, bool> KnownExtensions = new(StringComparer.OrdinalIgnoreCase);
        private static int _hideKnownExtensions = -1;

        internal static string FormatShortcutLabel(string? storedName, string? uriOrFileAction, bool isUrlShortcut)
        {
            return FormatShortcutLabel(
                storedName,
                uriOrFileAction,
                isUrlShortcut,
                HideKnownExtensionsEnabled(),
                IsKnownExtension,
                File.Exists,
                Directory.Exists);
        }

        internal static string FormatShortcutLabel(
            string? storedName,
            string? uriOrFileAction,
            bool isUrlShortcut,
            bool hideKnownExtensions,
            Func<string, bool> isKnownExtension,
            Func<string, bool> fileExists,
            Func<string, bool> directoryExists)
        {
            storedName ??= string.Empty;
            string Display(string fileName) => ToDisplayName(fileName, hideKnownExtensions, isKnownExtension);

            if (!string.IsNullOrEmpty(uriOrFileAction))
            {
                bool isDirectory = directoryExists(uriOrFileAction);
                bool isFile = !isDirectory && fileExists(uriOrFileAction);
                if (isDirectory || isFile)
                {
                    string trimmed = uriOrFileAction.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string entryName = Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(entryName))
                        entryName = trimmed;

                    string comparable = isDirectory ? entryName : Path.GetFileNameWithoutExtension(entryName);
                    if (string.IsNullOrEmpty(storedName) || string.Equals(comparable, storedName, StringComparison.OrdinalIgnoreCase))
                        return Display(entryName);

                    if (!isUrlShortcut && storedName.Length > 0)
                        return Display(storedName + ".lnk");
                }
            }

            if (isUrlShortcut && storedName.Length > 0)
            {
                bool alreadyUrl = storedName.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
                return Display(alreadyUrl ? storedName : storedName + ".url");
            }

            return storedName;
        }

        internal static string ToDisplayName(string fileName, bool hideKnownExtensions, Func<string, bool> isKnownExtension)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName ?? string.Empty;

            string extension = Path.GetExtension(fileName);
            if (extension.Length < 2)
                return fileName;

            if (!hideKnownExtensions || !isKnownExtension(extension))
                return fileName;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            return string.IsNullOrEmpty(withoutExtension) ? fileName : withoutExtension;
        }

        internal static string ResolvePreviewPath(string? uriOrFileAction, string? iconPath)
        {
            return ResolvePreviewPath(uriOrFileAction, iconPath, File.Exists, RasterImageFiles.IsRasterPath);
        }

        internal static string ResolvePreviewPath(
            string? uriOrFileAction,
            string? iconPath,
            Func<string, bool> fileExists,
            Func<string, bool> isRasterPath)
        {
            if (!string.IsNullOrEmpty(uriOrFileAction) && fileExists(uriOrFileAction) && isRasterPath(uriOrFileAction))
                return uriOrFileAction;

            if (!string.IsNullOrEmpty(iconPath) && fileExists(iconPath))
                return iconPath;

            if (!string.IsNullOrEmpty(uriOrFileAction) && fileExists(uriOrFileAction))
                return uriOrFileAction;

            return iconPath ?? string.Empty;
        }

        internal static bool HideKnownExtensionsEnabled()
        {
            int cached = Volatile.Read(ref _hideKnownExtensions);
            if (cached >= 0)
                return cached == 1;

            bool hide = ReadHideFileExt();
            Volatile.Write(ref _hideKnownExtensions, hide ? 1 : 0);
            return hide;
        }

        internal static bool IsKnownExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            if (extension[0] != '.')
                extension = "." + extension;

            return KnownExtensions.GetOrAdd(extension, LookUpKnownExtension);
        }

        private static bool ReadHideFileExt()
        {
            if (!OperatingSystem.IsWindows())
                return true;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(HideFileExtKey);
                object? value = key?.GetValue("HideFileExt");
                return value switch
                {
                    int dword => dword != 0,
                    long qword => qword != 0,
                    _ => true,
                };
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ExplorerFileNames.HideFileExt", ex);
                return true;
            }
        }

        private static bool LookUpKnownExtension(string extension)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                using RegistryKey? key = Registry.ClassesRoot.OpenSubKey(extension);
                return key != null;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ExplorerFileNames.IsKnownExtension " + extension, ex);
                return false;
            }
        }
    }
}
