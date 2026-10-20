using System;
using System.Globalization;
using System.IO;
using Naultinus.Properties;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Messages d'accès dossier : disparition, droits, partage réseau — sans planter.
    /// </summary>
    internal static class PortalFolderAccess
    {
        /// <summary>
        /// HRESULT fréquents de <c>SHParseDisplayName</c> / Win32 (FACILITY_WIN32).
        /// </summary>
        private const uint Win32AccessDenied = 0x80070005;
        private const uint Win32PathNotFound = 0x80070003;
        private const uint Win32FileNotFound = 0x80070002;
        private const uint Win32BadNetPath = 0x80070035;
        private const uint Win32NetworkUnreachable = 0x800704CF;
        private const uint Win32BadNetResp = 0x8007003A;
        private const uint Win32NetNameDeleted = 0x80070040;

        /// <summary>Chemin d'espace de noms shell (recherche, known folder), pas un dossier FS.</summary>
        internal static bool IsShellNamespacePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string t = path.Trim();
            return t.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("::{", StringComparison.Ordinal);
        }

        /// <summary>
        /// Teste l'existence sans laisser remonter une exception réseau.
        /// Les chemins <c>search-ms:</c> / <c>shell:</c> passent par <c>SHParseDisplayName</c>.
        /// </summary>
        internal static bool TryExists(string path, out string? errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, Strings.FolderNotFoundFormat, path ?? "");
                return false;
            }

            if (IsShellNamespacePath(path))
            {
                if (OperatingSystem.IsWindows()
                    && Native.ShellBrowserNative.SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0
                    && pidl != IntPtr.Zero)
                {
                    Native.ShellBrowserNative.ILFree(pidl);
                    return true;
                }

                errorMessage = string.Format(CultureInfo.CurrentCulture, Strings.PortalNavigationFailedFormat, path);
                return false;
            }

            try
            {
                if (Directory.Exists(path))
                    return true;

                errorMessage = DescribeMissing(path);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, Strings.AccessDeniedFormat, path);
                return false;
            }
            catch (IOException)
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, Strings.PortalFolderUnreachableFormat, path);
                return false;
            }
        }

        internal static string DescribeMissing(string path)
        {
            if (IsUnc(path))
                return string.Format(CultureInfo.CurrentCulture, Strings.PortalFolderUnreachableFormat, path);
            return string.Format(CultureInfo.CurrentCulture, Strings.PortalFolderGoneFormat, path);
        }

        internal static string DescribeShellParseFailure(string path, int hresult)
        {
            uint code = unchecked((uint)hresult);
            if (code == Win32AccessDenied)
                return string.Format(CultureInfo.CurrentCulture, Strings.AccessDeniedFormat, path);
            if (code == Win32BadNetPath
                || code == Win32NetworkUnreachable
                || code == Win32BadNetResp
                || code == Win32NetNameDeleted
                || IsUnc(path))
                return string.Format(CultureInfo.CurrentCulture, Strings.PortalFolderUnreachableFormat, path);
            if (code == Win32PathNotFound || code == Win32FileNotFound)
                return string.Format(CultureInfo.CurrentCulture, Strings.PortalFolderGoneFormat, path);

            return string.Format(CultureInfo.CurrentCulture, Strings.PortalNavigationFailedFormat, path);
        }

        internal static string DescribeNavigationFailed(string? path)
        {
            string display = string.IsNullOrEmpty(path) ? "…" : path;
            if (IsUnc(display))
                return string.Format(CultureInfo.CurrentCulture, Strings.PortalFolderUnreachableFormat, display);
            return string.Format(CultureInfo.CurrentCulture, Strings.PortalNavigationFailedFormat, display);
        }

        private static bool IsUnc(string path) =>
            path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal);
    }
}
