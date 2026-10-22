using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Naultinus.View
{
    public partial class ShellFolderViewHost : HwndHost
    {
        #region Navigation

        /// <summary>Dirige la vue vers un dossier. Sans effet tant que la vue n'est pas prête.</summary>
        public void Navigate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!_initialized)
            {
                _pendingPath = path;
                return;
            }

            IntPtr pidl = IntPtr.Zero;
            try
            {
                // La vue montre déjà ce dossier : le lui redemander jetterait le défilement et la
                // sélection. En revanche elle ne se rafraîchit pas toute seule (mesuré : cinq
                // secondes après la création d'un fichier, il n'était pas là) : on le lui demande.
                string target = Path.TrimEndingDirectorySeparator(path.Trim());
                if (string.Equals(target, _displayedPath, StringComparison.OrdinalIgnoreCase))
                {
                    NaultinusDiagnostics.LogDebug("ShellFolderViewHost.Navigate : dossier déjà affiché, rafraîchissement demandé (" + target + ")");
                    RefreshView();
                    return;
                }

                _navigatingFromApp = true;
                int hr = ShellBrowserNative.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero)
                {
                    _navigatingFromApp = false;
                    string message = PortalFolderAccess.DescribeShellParseFailure(path, hr);
                    NaultinusDiagnostics.Log(
                        "ShellFolderViewHost",
                        $"Chemin non résolu par le shell ({path}) : 0x{hr:X8}");
                    ShowUnavailable(message);
                    BrowseFailed?.Invoke(message);
                    return;
                }

                ((IExplorerBrowser)_browser!).BrowseToIDList(pidl, ShellBrowserNative.BrowseFlags.Absolute);
                _displayedPath = target;
            }
            catch (Exception ex)
            {
                _navigatingFromApp = false;
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Navigation impossible vers " + path, ex);
                string message = PortalFolderAccess.DescribeNavigationFailed(path);
                ShowUnavailable(message);
                BrowseFailed?.Invoke(message);
            }
            finally
            {
                // Le drapeau « initiée par l'application » ne se remet PAS ici : la notification de
                // navigation arrive après ce retour, et le remettre trop tôt faisait passer nos
                // navigations pour celles de la vue.
                if (pidl != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl);
            }
        }

        #endregion
    }
}
