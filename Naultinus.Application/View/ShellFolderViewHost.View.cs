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

        /// <summary>
        /// Mode d'affichage pour le dossier courant. Le réglage est ensuite laissé au sac à dos
        /// du shell (pas de réimposition à chaque navigation).
        /// </summary>
        public void SetViewMode(uint viewMode, bool showColumnHeader)
        {
            _viewMode = viewMode;
            _showColumnHeader = showColumnHeader;
            ApplyViewMode();
            _lastKnownMode = viewMode;
            // Délégation au sac à dos : ne pas forcer ce mode sur les dossiers suivants.
            _viewMode = 0;
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
        /// Surveille le mode de la vue. Le menu de la vue change le mode sans prévenir personne, et
        /// comme il ne désactive pas le portail, un clic dedans ne déclenche pas non plus
        /// l'activation : sans cette veille, les titres de colonnes restent accrochés jusqu'au
        /// prochain clic extérieur. Le rythme est rapide quand le portail est au premier plan et
        /// lent ailleurs, pour ne pas réveiller la machine pour rien.
        /// </summary>
        public void StartModeWatch()
        {
            // Banc d'essai : permet de reproduire l'état bloqué, veille désactivée.
            if (Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_NOWATCH") == "1")
                return;

            // L'événement « vue créée » arrive parfois sur le fil COM : le ventilateur doit vivre
            // sur le fil d'interface, sinon il ne bat jamais.
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(StartModeWatch));
                return;
            }

            if (_modeWatch == null)
            {
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Tick += (_, _) => CheckModeChanged();
                _modeWatch = timer;
                NaultinusDiagnostics.Log("ShellFolderViewHost", "veille du mode de vue démarrée");
            }

            _modeWatch.Interval = TimeSpan.FromMilliseconds(400);
            if (!_modeWatch.IsEnabled)
                _modeWatch.Start();
        }

        /// <summary>Rythme lent : la veille continue, mais sans réveiller la machine en continu.</summary>
        public void SlowModeWatch()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(SlowModeWatch));
                return;
            }

            StartModeWatch();
            if (_modeWatch != null)
                _modeWatch.Interval = TimeSpan.FromMilliseconds(1500);
        }

        public void StopModeWatch()
        {
            if (_modeWatch == null)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(StopModeWatch));
                return;
            }

            _modeWatch.Stop();
            _modeWatch = null;
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

        private void TryRestoreSelectionAfterRefresh()
        {
            int index = _restoreSelectionIndex;
            _restoreSelectionIndex = -1;
            if (index < 0 || _browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                    folderView.SelectItem(index, ShellBrowserNative.SvsiKeyboardMove);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryRestoreSelectionAfterRefresh", ex);
            }
        }

        /// <summary>
        /// Index de l'élément marqué par la sélection (-1 si la vue n'est pas joignable). Sert à
        /// juger ce que coûte un rafraîchissement par renavigation.
        /// </summary>
        private int SelectionMarkedItem()
        {
            if (_browser is not IExplorerBrowser browser)
                return -2;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetSelectionMarkedItem(out int index);
                    return index;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SelectionMarkedItem", ex);
            }

            return -2;
        }

        /// <summary>Index de l'élément qui porte le focus dans la vue (-2 si la vue n'est pas joignable).</summary>
        private int FocusedItem()
        {
            if (_browser is not IExplorerBrowser browser)
                return -2;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetFocusedItem(out int index);
                    return index;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.FocusedItem", ex);
            }

            return -2;
        }


        private void CheckModeChanged()
        {
            try
            {
                uint mode = ReadViewMode();
                bool headerVisible = IsColumnHeaderVisible();
                bool layoutKnown = TryLayoutLooksLikeDetails(out bool layoutDetails);
                // Mode API figé (menu de la vue) alors que la grille n'est plus en détails, ou
                // en-tête encore visible hors détails : forcer la remise à l'heure.
                bool staleHeader = headerVisible && mode != ShellBrowserNative.FolderViewModes.Details;
                bool staleMode = layoutKnown && !layoutDetails && mode == ShellBrowserNative.FolderViewModes.Details;

                if (mode == _lastKnownMode && !staleHeader && !staleMode)
                    return;

                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "mode de la vue passé à " + mode
                    + " (dernier " + _lastKnownMode
                    + ", en-tête visible=" + headerVisible
                    + ", disposition détails=" + (layoutKnown ? layoutDetails.ToString() : "?") + ")");
                EnforceColumnHeader();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.CheckModeChanged", ex);
            }
        }

        /// <summary>
        /// Change le mode par la seule vue, sans réglages de dossier : c'est le chemin qu'emprunte le
        /// menu contextuel de la vue, et il laisse l'en-tête de colonnes en place.
        /// </summary>
        public void SetViewModeThroughViewOnly(uint viewMode)
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.SetCurrentViewMode(viewMode);
                    folderView.GetCurrentViewMode(out uint relu);
                    NaultinusDiagnostics.Log("ShellFolderViewHost", "mode demandé à la vue seule : " + viewMode + ", relu juste après " + relu);
                }
                else
                {
                    NaultinusDiagnostics.Log("ShellFolderViewHost", "mode demandé à la vue seule : aucune vue sous la main");
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SetViewModeThroughViewOnly", ex);
            }
        }

        /// <summary>
        /// Mode réellement affiché par la vue (0 si la vue n'est pas joignable).
        /// Préfère <c>IShellView::GetCurrentInfo</c> : plus fidèle après un changement par le menu.
        /// </summary>
        private uint ReadViewMode()
        {
            if (_browser is not IExplorerBrowser browser)
                return 0;

            try
            {
                Guid shellViewId = typeof(IShellView).GUID;
                browser.GetCurrentView(ref shellViewId, out object shellViewObj);
                if (shellViewObj is IShellView shellView)
                {
                    var settings = new ShellBrowserNative.FolderSettings();
                    if (shellView.GetCurrentInfo(ref settings) == SOk && settings.ViewMode != 0)
                        return (uint)settings.ViewMode;
                }

                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetCurrentViewMode(out uint mode);
                    return mode;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ReadViewMode", ex);
            }

            return 0;
        }

        /// <summary>La vue details affiche un <c>SysHeader32</c> ; s'il reste après un passage en icônes, bug.</summary>
        private bool IsColumnHeaderVisible()
        {
            if (_siteHwnd == IntPtr.Zero)
                return false;

            IntPtr header = IntPtr.Zero;
            EnumChildWindows(_siteHwnd, (h, _) =>
            {
                var sb = new System.Text.StringBuilder(64);
                _ = GetClassName(h, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "SysHeader32", StringComparison.Ordinal)
                    && IsWindowVisible(h))
                {
                    header = h;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return header != IntPtr.Zero;
        }

        /// <summary>
        /// Heuristique de disposition : deux premiers éléments sur la même colonne ⇒ détails/liste.
        /// Sert quand <c>GetCurrentViewMode</c> reste à « détails » après un changement par le menu.
        /// </summary>
        private bool TryLayoutLooksLikeDetails(out bool looksLikeDetails)
        {
            looksLikeDetails = false;
            if (_browser is not IExplorerBrowser browser)
                return false;

            IntPtr pidl0 = IntPtr.Zero;
            IntPtr pidl1 = IntPtr.Zero;
            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is not IFolderView folderView)
                    return false;

                folderView.ItemCount(ShellBrowserNative.SvgIoAllView, out int count);
                if (count < 2)
                    return false;

                folderView.Item(0, out pidl0);
                folderView.Item(1, out pidl1);
                if (pidl0 == IntPtr.Zero || pidl1 == IntPtr.Zero)
                    return false;

                folderView.GetItemPosition(pidl0, out ShellBrowserNative.POINT a);
                folderView.GetItemPosition(pidl1, out ShellBrowserNative.POINT b);
                looksLikeDetails = Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) > 2;
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryLayoutLooksLikeDetails", ex);
                return false;
            }
            finally
            {
                if (pidl0 != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl0);
                if (pidl1 != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl1);
            }
        }

        /// <summary>
        /// Remet l'en-tête de colonnes d'accord avec le mode affiché. À appeler quand on a une raison
        /// de croire que la vue a changé d'avis toute seule (menu contextuel de la vue).
        /// </summary>
        public void ReconcileViewMode()
        {
            if (_viewMode != 0)
            {
                ApplyViewMode();
                return;
            }

            // Repousser les réglages de dossier recharge la vue et efface la sélection : le faire à
            // chaque activation faisait perdre la sélection à chaque clic dans le corps (mesuré :
            // rectangle de focus sur l'élément 2, aucune ligne surlignée, F2 sans effet). On ne
            // corrige donc que si le mode a réellement bougé — la veille du mode s'en charge.
            uint mode = ReadViewMode();
            if (mode == _lastKnownMode)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ReconcileViewMode : mode " + mode + " inchangé, rien poussé");
                return;
            }

            EnforceColumnHeader();
        }

        /// <summary>
        /// Les titres de colonnes n'ont rien à faire dans un mode qui n'est pas « détails ». Mesuré :
        /// un mode changé par nos soins masque d'office les titres ; un mode changé par le menu de la
        /// vue, lui, laisse l'en-tête en place. On recopie donc les réglages selon le mode réel, en
        /// corrigeant le cas où l'API reste à « détails » alors que la disposition ne l'est plus.
        /// </summary>
        private void EnforceColumnHeader()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                uint mode = ReadViewMode();
                if (mode == 0)
                    return;

                bool showHeader = mode == ShellBrowserNative.FolderViewModes.Details;
                if (showHeader && TryLayoutLooksLikeDetails(out bool layoutDetails) && !layoutDetails)
                {
                    // Menu de la vue : mode API encore à 4, éléments déjà en grille.
                    showHeader = false;
                    mode = ShellBrowserNative.FolderViewModes.Icon;
                }

                var settings = new ShellBrowserNative.FolderSettings
                {
                    ViewMode = (int)mode,
                    Flags = showHeader ? 0u : ShellBrowserNative.FolderViewFlags.NoColumnHeader,
                };

                browser.SetFolderSettings(ref settings);
                _lastKnownMode = mode;
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "mode relevé dans la vue : " + mode + ", titres de colonnes : " + showHeader);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.EnforceColumnHeader", ex);
            }
        }

        /// <summary>
        /// Le mode passe par les réglages de dossier et par la vue courante : les deux sont
        /// nécessaires, <c>SetFolderSettings</c> seul ne repeint pas une vue déjà dessinée.
        /// </summary>
        private void ApplyViewMode()
        {
            if (_browser is not IExplorerBrowser browser || _viewMode == 0)
                return;

            try
            {
                var settings = new ShellBrowserNative.FolderSettings
                {
                    ViewMode = (int)_viewMode,
                    Flags = _showColumnHeader ? 0u : ShellBrowserNative.FolderViewFlags.NoColumnHeader,
                };

                browser.SetFolderSettings(ref settings);

                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view2);
                if (view2 is IFolderView2 folderView2
                    && folderView2.GetViewModeAndIconSize(out _, out int iconSize) == SOk)
                {
                    int size = iconSize > 0 ? iconSize : GetDesktopIconSize();
                    if (folderView2.SetViewModeAndIconSize((int)_viewMode, size) == SOk)
                    {
                        NaultinusDiagnostics.Log(
                            "ShellFolderViewHost",
                            "mode de vue demandé (IFolderView2) : " + _viewMode
                            + ", titres de colonnes : " + _showColumnHeader);
                        return;
                    }
                }

                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                    folderView.SetCurrentViewMode(_viewMode);

                NaultinusDiagnostics.Log("ShellFolderViewHost", "mode de vue demandé : " + _viewMode + ", titres de colonnes : " + _showColumnHeader);
                EnforceAutoArrange();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ApplyViewMode", ex);
            }
        }

        /// <summary>
        /// Empêche le réordonnancement manuel libre : le tri / l'arrangement automatique font foi.
        /// </summary>
        private void EnforceAutoArrange()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return;

                uint flags = ShellBrowserNative.FolderViewFlags.AutoArrange
                    | ShellBrowserNative.FolderViewFlags.SnapToGrid;
                folderView2.SetCurrentFolderFlags(flags, flags);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.EnforceAutoArrange", ex);
            }
        }

        #endregion
    }
}
