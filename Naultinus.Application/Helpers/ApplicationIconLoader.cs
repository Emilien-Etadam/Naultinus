using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Fournit l'icône d'application déjà livrée avec Naultinus, avec un handle qui nous appartient.
    /// </summary>
    /// <remarks>
    /// La publication est un exécutable unique : <c>Ressources/icon.ico</c> n'est pas à côté du processus.
    /// L'icône est la ressource PE de <c>Naultinus.exe</c>. Le menu ne doit pas recevoir
    /// <see cref="SystemIcons.Application"/> : ce handle partagé (<c>LoadIcon</c>) n'est pas l'icône
    /// de l'application, et le libérer casserait l'icône statique.
    /// </remarks>
    internal static class ApplicationIconLoader
    {
        private const int FallbackIconSizePx = 32;

        internal static Icon? TryCreateOwnedIcon()
        {
            Icon? icon = TryFromExecutable() ?? TryFromSidecarFile();
            if (icon != null)
                return icon;

            return TryCloneStockIcon();
        }

        private static Icon? TryFromExecutable()
        {
            string? exePath = Environment.ProcessPath;
            string? assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(assemblyName))
                return null;

            if (!string.Equals(Path.GetFileName(exePath), assemblyName + ".exe", StringComparison.OrdinalIgnoreCase))
                return null;

            return TryShellIcon(exePath);
        }

        private static Icon? TryFromSidecarFile()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Ressources", "icon.ico");
            Icon? icon = TryShellIcon(iconPath);
            if (icon != null)
                return icon;

            return TryManagedIconFile(iconPath);
        }

        private static Icon? TryShellIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            return TryCall(() => Icon.ExtractIcon(path, 0, smallIcon: true))
                ?? TryCall(() => Icon.ExtractIcon(path, 0, FallbackIconSizePx))
                ?? TryCall(() => Icon.ExtractAssociatedIcon(path));
        }

        private static Icon? TryManagedIconFile(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                using var loaded = new Icon(path);
                var copy = (Icon)loaded.Clone();
                if (HasLiveHandle(copy))
                    return copy;

                copy.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("TrayIcon: constructeur Icon sur le fichier", ex);
                return null;
            }
        }

        private static Icon? TryCloneStockIcon()
        {
            try
            {
                // Ne pas rendre le handle statique : NotifyIcon ne doit pas le détruire.
                var copy = (Icon)SystemIcons.Application.Clone();
                if (HasLiveHandle(copy))
                    return copy;

                copy.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("TrayIcon: copie de l'icône système", ex);
                return null;
            }
        }

        private static Icon? TryCall(Func<Icon?> load)
        {
            try
            {
                Icon? icon = load();
                if (icon == null)
                    return null;

                if (HasLiveHandle(icon))
                    return icon;

                icon.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("TrayIcon: extraction de l'icône", ex);
                return null;
            }
        }

        private static bool HasLiveHandle(Icon icon)
        {
            try
            {
                return icon.Handle != IntPtr.Zero;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }
}
