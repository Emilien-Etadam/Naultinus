using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Naultinus.View
{
    public partial class ShellFolderViewHost : HwndHost
    {
        #region Vue

        private void TryCreateBrowser()
        {
            try
            {
                Guid clsid = ShellBrowserNative.ClsidExplorerBrowser;
                Guid iid = typeof(IExplorerBrowser).GUID;
                int hr = ShellBrowserNative.CoCreateInstance(ref clsid, IntPtr.Zero, ShellBrowserNative.ClsContext, ref iid, out object browser);
                if (hr != 0 || browser is not IExplorerBrowser explorerBrowser)
                {
                    NaultinusDiagnostics.Log("ShellFolderViewHost", $"Création de IExplorerBrowser impossible : 0x{hr:X8}");
                    return;
                }

                _browser = browser;

                // La zone cliente est encore à 1x1 ici : WPF dimensionne la fenêtre hôte juste après,
                // et SetRect prend le relais. Les réglages de vue sont laissés à NULL pour que le
                // shell applique ceux mémorisés pour le dossier, comme dans l'Explorateur.
                ShellBrowserNative.GetClientRect(_siteHwnd, out ShellBrowserNative.RECT rect);
                explorerBrowser.Initialize(_siteHwnd, ref rect, IntPtr.Zero);
                // Sac à dos dédié : mode, taille d'icônes et tri mémorisés par dossier (pas le magasin Explorer.exe).
                explorerBrowser.SetPropertyBag("Naultinus.FolderPortal");
                // Persistance activée (pas de EBO_NOPERSITVIEWSTATE / NoPersistViewState).
                explorerBrowser.SetOptions(ShellBrowserNative.ExplorerBrowserOptions.None);

                _initialized = true;
                AdviseEvents();
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Vue du shell créée.");
                if (!string.IsNullOrEmpty(_pendingPath))
                {
                    string pending = _pendingPath;
                    _pendingPath = "";
                    Navigate(pending);
                }
            }
            catch (Exception ex)
            {
                ReleaseBrowser();
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Initialisation de la vue du shell impossible.", ex);
            }
        }

        private void ReleaseBrowser()
        {
            if (_browser == null)
                return;

            try
            {
                if (_events != null && _eventsCookie != 0)
                    ((IExplorerBrowser)_browser).Unadvise(_eventsCookie);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.Unadvise", ex);
            }

            _eventsCookie = 0;
            _events = null;

            try
            {
                // Le contrat de IExplorerBrowser demande Destroy avant de relâcher l'objet :
                // sans cela, les fenêtres de la vue restent en vie.
                ((IExplorerBrowser)_browser).Destroy();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.Destroy", ex);
            }

            try
            {
                Marshal.ReleaseComObject(_browser);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ReleaseBrowser", ex);
            }

            _browser = null;
        }

        /// <summary>
        /// La vue hébergée ne suit pas la taille de sa fenêtre parente toute seule : on lui notifie
        /// la nouvelle zone cliente, puis on étire aussi ses fenêtres enfants, la fenêtre de gestion
        /// de l'hôte pouvant rester placée ailleurs.
        /// </summary>
        private void ResizeView()
        {
            if (_siteHwnd == IntPtr.Zero)
                return;

            if (!ShellBrowserNative.GetClientRect(_siteHwnd, out ShellBrowserNative.RECT rect))
                return;

            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);

            if (_browser is IExplorerBrowser explorerBrowser)
            {
                try
                {
                    explorerBrowser.SetRect(IntPtr.Zero, rect);
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SetRect", ex);
                }
            }

            IntPtr child = ShellBrowserNative.GetWindow(_siteHwnd, ShellBrowserNative.GwChild);
            while (child != IntPtr.Zero)
            {
                ShellBrowserNative.MoveWindow(child, 0, 0, width, height, true);
                child = ShellBrowserNative.GetWindow(child, ShellBrowserNative.GwHwndNext);
            }
        }

        private ShellBrowserNative.PropertyKey? _lastSortKey;
        private int _lastSortDirection = ShellBrowserNative.SortDirections.Ascending;

        /// <summary>
        /// Tri de la vue courante via <c>IFolderView2::SetSortColumns</c>. Un second appel sur le
        /// même critère inverse le sens. Persistance : sac à dos du shell.
        /// </summary>
        internal bool TrySetSort(PortalSortField field)
        {
            if (_browser is not IExplorerBrowser browser)
                return false;

            try
            {
                ShellBrowserNative.PropertyKey key = field switch
                {
                    PortalSortField.Type => ShellBrowserNative.SortPropertyKeys.ItemTypeText,
                    PortalSortField.Date => ShellBrowserNative.SortPropertyKeys.DateModified,
                    _ => ShellBrowserNative.SortPropertyKeys.ItemNameDisplay,
                };

                int direction;
                if (_lastSortKey.HasValue
                    && _lastSortKey.Value.FormatId == key.FormatId
                    && _lastSortKey.Value.PropertyId == key.PropertyId)
                {
                    direction = -_lastSortDirection;
                }
                else
                {
                    // Aligné sur la grille WPF : date = plus récents d'abord.
                    direction = field == PortalSortField.Date
                        ? ShellBrowserNative.SortDirections.Descending
                        : ShellBrowserNative.SortDirections.Ascending;
                }

                var column = new ShellBrowserNative.SortColumn
                {
                    PropertyKey = key,
                    Direction = direction,
                };

                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return false;

                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<ShellBrowserNative.SortColumn>());
                try
                {
                    Marshal.StructureToPtr(column, buffer, false);
                    folderView2.SetSortColumns(buffer, 1);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                _lastSortKey = key;
                _lastSortDirection = direction;
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "tri demandé : " + field + ", sens " + direction);
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TrySetSort", ex);
                return false;
            }
        }

        private const int MinIconSize = 16;
        private const int MaxIconSize = 256;
        private const int IconSizeStep = 16;

        /// <summary>
        /// Taille d'icônes du bureau (sac à dos Shell Bags\1\Desktop), repli 48 px.
        /// </summary>
        internal static int GetDesktopIconSize()
        {
            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Bags\1\Desktop");
                object? value = key?.GetValue("IconSize");
                if (value is int pixels && pixels >= MinIconSize && pixels <= MaxIconSize)
                    return pixels;
                if (value != null
                    && int.TryParse(value.ToString(), out int parsed)
                    && parsed >= MinIconSize
                    && parsed <= MaxIconSize)
                    return parsed;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.GetDesktopIconSize", ex);
            }

            return 48;
        }

        /// <summary>
        /// Ctrl+molette : ajuste la taille d'icônes via <c>IFolderView2</c>. En mode liste / détails /
        /// contenu, un zoom avant bascule en icônes ; un zoom arrière au minimum revient en détails.
        /// </summary>
        public bool TryAdjustIconSize(int wheelDelta)
        {
            if (wheelDelta == 0 || _browser is not IExplorerBrowser browser)
                return false;

            try
            {
                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return false;

                if (folderView2.GetViewModeAndIconSize(out int mode, out int size) != SOk)
                    return false;

                int steps = Math.Max(1, Math.Abs(wheelDelta) / 120);
                int delta = (wheelDelta > 0 ? IconSizeStep : -IconSizeStep) * steps;
                int newMode = mode;
                int newSize = size;

                bool listLike = mode == (int)ShellBrowserNative.FolderViewModes.Details
                    || mode == (int)ShellBrowserNative.FolderViewModes.List
                    || mode == (int)ShellBrowserNative.FolderViewModes.Content
                    || mode == (int)ShellBrowserNative.FolderViewModes.SmallIcon;

                if (listLike)
                {
                    if (delta <= 0)
                        return true; // déjà compact : ne pas avaler sans effet utile au-delà

                    newMode = (int)ShellBrowserNative.FolderViewModes.Icon;
                    int baseline = size > 0 ? size : GetDesktopIconSize();
                    newSize = Math.Clamp(baseline + delta, MinIconSize, MaxIconSize);
                }
                else
                {
                    newSize = size + delta;
                    if (newSize < MinIconSize)
                    {
                        newMode = (int)ShellBrowserNative.FolderViewModes.Details;
                        newSize = MinIconSize;
                    }
                    else
                    {
                        newSize = Math.Clamp(newSize, MinIconSize, MaxIconSize);
                    }
                }

                if (newMode == mode && newSize == size)
                    return true;

                if (folderView2.SetViewModeAndIconSize(newMode, newSize) != SOk)
                    return false;

                // Sac à dos : ne pas réimposer ce mode aux dossiers suivants.
                _viewMode = 0;
                _lastKnownMode = (uint)newMode;
                _showColumnHeader = newMode == (int)ShellBrowserNative.FolderViewModes.Details;
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "taille d'icônes : mode " + newMode + ", " + newSize + " px");
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryAdjustIconSize", ex);
                return false;
            }
        }

        /// <summary>
        /// Filtre les éléments affichés par libellé (sous-chaîne, insensible à la casse).
        /// Chaîne vide = tout afficher. Réappliqué après chaque navigation.
        /// </summary>
        public void SetNameFilter(string? query)
        {
            _nameFilter ??= new PortalFolderNameFilter();
            string next = (query ?? string.Empty).Trim();
            if (string.Equals(_nameFilter.Query, next, StringComparison.Ordinal))
                return;

            _nameFilter.Query = next;
            ApplyNameFilter(refresh: true);
        }

        private void ApplyNameFilter(bool refresh)
        {
            if (_browser == null || _nameFilter == null)
                return;

            try
            {
                if (!TryGetFolderFilterSite(out IFolderFilterSite site))
                {
                    NaultinusDiagnostics.LogDebug(
                        "ShellFolderViewHost.ApplyNameFilter : IFolderFilterSite indisponible");
                    return;
                }

                object? filter = string.IsNullOrEmpty(_nameFilter.Query) ? null : _nameFilter;
                site.SetFilter(filter);
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "filtre de nom : " + (filter == null ? "(aucun)" : _nameFilter.Query));

                if (refresh)
                    RefreshView();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ApplyNameFilter", ex);
            }
        }

        private bool TryGetFolderFilterSite(out IFolderFilterSite site)
        {
            site = null!;
            if (_browser == null)
                return false;

            if (TryQueryInterface(_browser, out site))
                return true;

            try
            {
                if (_browser is not IExplorerBrowser browser)
                    return false;

                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                return view != null && TryQueryInterface(view, out site);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryGetFolderFilterSite", ex);
                return false;
            }
        }

        private static bool TryQueryInterface<T>(object comObject, out T iface)
            where T : class
        {
            iface = null!;
            IntPtr unknown = Marshal.GetIUnknownForObject(comObject);
            try
            {
                Guid iid = typeof(T).GUID;
                int hr = Marshal.QueryInterface(unknown, in iid, out IntPtr pointer);
                if (hr != 0 || pointer == IntPtr.Zero)
                    return false;

                try
                {
                    iface = (T)Marshal.GetTypedObjectForIUnknown(pointer, typeof(T));
                    return iface != null;
                }
                finally
                {
                    Marshal.Release(pointer);
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        /// <summary>
        /// Redemande le contenu du dossier affiché. La vue du shell ne surveille pas le dossier à
        /// notre place (mesuré) : sans cet appel, un fichier créé pendant que le portail est ouvert
        /// n'apparaît pas.
        /// </summary>
        public void RefreshView()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                // Banc d'essai : « com » appelle IExplorerBrowser::Refresh, « navigate » (le défaut
                // retenu) redirige la vue vers le même dossier. Mesure : Refresh seul n'affiche pas
                // un fichier créé pendant la course ; la renavigation l'affiche. On restaure ensuite
                // l'index de sélection (best-effort) dans OnNavigationComplete.
                string how = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_REFRESH") ?? "navigate";
                if (!string.Equals(how, "com", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_displayedPath))
                {
                    int before = SelectionMarkedItem();
                    if (before < 0)
                        before = FocusedItem();
                    _restoreSelectionIndex = before >= 0 ? before : -1;
                    string saved = _displayedPath;
                    _displayedPath = "";
                    Navigate(saved);
                    NaultinusDiagnostics.LogDebug(
                        "ShellFolderViewHost.RefreshView : renavigation vers " + saved
                        + ", restauration prévue index " + _restoreSelectionIndex);
                    return;
                }

                browser.Refresh();
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.RefreshView : IExplorerBrowser::Refresh appelé — sans effet observable sur la liste");
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Rafraîchissement de la vue impossible", ex);
            }
        }

        #endregion
    }
}
