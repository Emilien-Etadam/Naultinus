using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Naultinus.View
{
    public partial class ShellFolderViewHost : HwndHost
    {
        #region Mode d'affichage

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
