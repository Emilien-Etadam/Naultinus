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
        #region Notifications de la vue

        /// <summary>
        /// Déclare notre récepteur d'événements auprès de l'hôte de vue. Le pointeur COM rendu par
        /// <c>GetComInterfaceForObject</c> porte notre propre référence : on la rend tout de suite,
        /// l'hôte garde la sienne tant que le cookie court.
        /// </summary>
        private void AdviseEvents()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                var sink = new ExplorerBrowserEventsSink();
                sink.ViewCreated += OnViewCreated;
                sink.NavigationComplete += OnNavigationComplete;
                sink.NavigationFailed += OnNavigationFailed;

                IntPtr sinkPointer = Marshal.GetComInterfaceForObject(sink, typeof(IExplorerBrowserEvents));
                try
                {
                    browser.Advise(sinkPointer, out _eventsCookie);
                }
                finally
                {
                    Marshal.Release(sinkPointer);
                }

                _events = sink;
            }
            catch (Exception ex)
            {
                // Sans notifications, la vue fonctionne : seule la barre de chemin ne se resynchronise pas.
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.AdviseEvents", ex);
            }
        }

        private void OnViewCreated()
        {
            NaultinusDiagnostics.Log("ShellFolderViewHost", "vue créée par l'hôte de navigation.");
            ApplyViewTheme();
            ApplyViewMode();
            EnforceAutoArrange();
            ApplyNameFilter(refresh: false);
            StartModeWatch();
        }

        private void OnNavigationFailed(IntPtr pidlFolder)
        {
            // Une navigation ratée ne rend jamais la notification de fin : sans ceci, le drapeau
            // resterait levé et la navigation suivante passerait pour la nôtre.
            _navigatingFromApp = false;
            string? attempted = _pendingPath;
            if (string.IsNullOrEmpty(attempted))
                attempted = _displayedPath;
            string message = PortalFolderAccess.DescribeNavigationFailed(attempted);
            NaultinusDiagnostics.Log("ShellFolderViewHost", "notification d'échec de navigation reçue");
            ShowUnavailable(message);
            BrowseFailed?.Invoke(message);
        }

        /// <summary>Texte de vue vide + oubli du chemin affiché pour forcer une vraie renavigation.</summary>
        public void ShowUnavailable(string message)
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                browser.SetEmptyText(string.IsNullOrEmpty(message) ? " " : message);
                _displayedPath = "";
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ShowUnavailable", ex);
            }
        }

        private void OnNavigationComplete(IntPtr pidlFolder)
        {
            // Le drapeau ne se lit qu'ici : la notification arrive après le retour de
            // BrowseToIDList, et le remettre à faux trop tôt faisait passer nos propres navigations
            // pour celles de la vue.
            bool initiatedByApp = _navigatingFromApp;
            _navigatingFromApp = false;

            // Trace de diagnostic : prouve que l'hôte de vue nous rappelle bien, y compris pour une
            // navigation qu'il a faite tout seul (double-clic sur un dossier).
            NaultinusDiagnostics.Log("ShellFolderViewHost", "notification de navigation reçue (initiée par l'application : " + initiatedByApp + ")");

            // La navigation recrée des fenêtres : elles naissent avec le thème par défaut (clair).
            _themeApplied = false;
            ApplyViewTheme();

            // Le shell rétablit le mode mémorisé pour ce dossier : on redemande le nôtre.
            ReconcileViewMode();
            // Le filtre n'est pas dans le sac à dos de vue : on le réapplique à chaque dossier.
            ApplyNameFilter(refresh: false);

            string? path = CurrentFolderPath();
            NaultinusDiagnostics.Log("ShellFolderViewHost", "dossier réellement affiché : " + (path ?? "(élément hors système de fichiers)"));
            if (!string.IsNullOrEmpty(path))
                _displayedPath = Path.TrimEndingDirectorySeparator(path);

            try
            {
                if (_browser is IExplorerBrowser browser)
                    browser.SetEmptyText(" ");
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.OnNavigationComplete.SetEmptyText", ex);
            }

            BrowseSucceeded?.Invoke();
            TryRestoreSelectionAfterRefresh();
            ApplyDropPolicy();

            if (initiatedByApp)
                return;

            if (!string.IsNullOrEmpty(path))
                Navigated?.Invoke(path);
        }

        /// <summary>
        /// Applique dépôt / glisser sortant selon la politique du portail. À rappeler après
        /// navigation (les cibles OLE sont recréées) et quand l'utilisateur change les bascules.
        /// </summary>
        public void ApplyInteractionPolicy()
        {
            if (!AllowShellDragOut)
                _trackingPotentialDrag = false;

            ApplyDropPolicy();
        }

        private void ApplyDropPolicy()
        {
            if (_siteHwnd == IntPtr.Zero)
                return;

            if (!AllowShellDrop)
            {
                foreach (IntPtr hwnd in EnumDropTargetWindows())
                    _ = RevokeDragDrop(hwnd);
                _dropTargetsRevoked = true;
                return;
            }

            if (!_dropTargetsRevoked)
                return;

            // RevokeDragDrop est irréversible pour la vue courante : on renavigue pour que le shell
            // réenregistre ses IDropTarget (API documentée OleInitialize / RegisterDragDrop).
            _dropTargetsRevoked = false;
            string path = _displayedPath;
            if (!string.IsNullOrEmpty(path))
                Navigate(path);
        }

        private IntPtr[] EnumDropTargetWindows()
        {
            var list = new System.Collections.Generic.List<IntPtr>();
            if (_siteHwnd == IntPtr.Zero)
                return list.ToArray();

            EnumChildWindows(_siteHwnd, (h, _) =>
            {
                var sb = new System.Text.StringBuilder(64);
                _ = GetClassName(h, sb, sb.Capacity);
                string name = sb.ToString();
                if (name is "SHELLDLL_DefView" or "DirectUIHWND" or "ExplorerBrowserControl")
                    list.Add(h);
                return true;
            }, IntPtr.Zero);

            return list.ToArray();
        }

        /// <summary>
        /// Empêche le démarrage d'un glisser OLE : dès que le seuil système est franchi, on relâche
        /// la capture souris pour que DefView n'appelle pas DoDragDrop.
        /// </summary>
        private bool TrySuppressDragOut(ref MSG msg)
        {
            if (msg.message == WmLButtonDown)
            {
                _trackingPotentialDrag = true;
                UnpackMessagePoint(msg.lParam, out _dragStartX, out _dragStartY);
                return false;
            }

            if (msg.message == WmLButtonUp || msg.message == WmLButtonDblClk)
            {
                _trackingPotentialDrag = false;
                return false;
            }

            if (msg.message != WmMouseMove || !_trackingPotentialDrag)
                return false;

            // Bouton gauche toujours enfoncé (MK_LBUTTON = 0x0001).
            if ((msg.wParam.ToInt64() & 0x0001) == 0)
            {
                _trackingPotentialDrag = false;
                return false;
            }

            UnpackMessagePoint(msg.lParam, out int x, out int y);
            int thresholdX = Math.Max(1, GetSystemMetrics(SmCxDrag));
            int thresholdY = Math.Max(1, GetSystemMetrics(SmCyDrag));
            if (Math.Abs(x - _dragStartX) < thresholdX && Math.Abs(y - _dragStartY) < thresholdY)
                return false;

            _trackingPotentialDrag = false;
            _ = ReleaseCapture();
            return true;
        }

        private static void UnpackMessagePoint(IntPtr lParam, out int x, out int y)
        {
            int packed = unchecked((int)(lParam.ToInt64() & 0xffffffff));
            x = (short)(packed & 0xffff);
            y = (short)((packed >> 16) & 0xffff);
        }

        private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General)
                return;

            _themeApplied = false;
            Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyViewTheme));
        }

        /// <summary>
        /// Le shell ne rend ses vues en sombre que si l'on demande le sous-thème « Explorer » à
        /// chacune de ses fenêtres. Sans cela, le corps du portail reste blanc dans une application
        /// sombre, ce qui est pire que l'inverse.
        /// </summary>
        private void ApplyViewTheme()
        {
            if (_siteHwnd == IntPtr.Zero || _themeApplied)
                return;

            _themeApplied = true;

            // Pas de SetWindowTheme ici : mesuré sur le poste, la vue de dossier suit déjà le thème
            // (sombre ou clair) déclaré par Windows pour les applications. Lui imposer le sous-thème
            // « Explorer » ne la noircissait pas davantage mais écrasait les brosses de la sélection
            // et du survol : les lignes restaient cliquables et sélectionnables, sans aucun retour
            // visuel. Le thème de la vue ne se pilote donc pas depuis Naultinus.
            NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ApplyViewTheme : thème laissé à Windows");
        }

        /// <summary>
        /// Lit le dossier réellement affiché par la vue : vue courante → dossier → nom complet.
        /// Retourne null pour un élément hors système de fichiers (Corbeille, Réseau…).
        /// </summary>
        private string? CurrentFolderPath()
        {
            if (_browser is not IExplorerBrowser browser)
                return null;

            IntPtr namePointer = IntPtr.Zero;
            object? folder = null;
            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is not IFolderView folderView)
                    return null;

                Guid shellItemId = typeof(IShellItem).GUID;
                folderView.GetFolder(ref shellItemId, out folder);
                if (folder is not IShellItem item)
                    return null;

                item.GetDisplayName(ShellBrowserNative.SigdnFilePath, out namePointer);
                return namePointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(namePointer);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.CurrentFolderPath", ex);
                return null;
            }
            finally
            {
                if (namePointer != IntPtr.Zero)
                    ShellBrowserNative.CoTaskMemFree(namePointer);
                if (folder != null)
                    Marshal.ReleaseComObject(folder);
            }
        }

        #endregion
    }
}
