using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using Naultinus.ViewModel;
using Naultinus;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Naultinus.View
{
    public partial class FolderPortal : Window
    {
        private readonly FolderPortalViewModel viewModel;
        private readonly bool useShellView;

        public FolderPortal(FolderPortalViewModel defaultModel)
        {
            InitializeComponent();
            DataContext = defaultModel;
            viewModel = defaultModel;

            // Corps rendu par la vue d'éléments du shell (rendu de l'Explorateur) : mode expérimental,
            // voir docs/PORTAL_EXPLORER_PARITY.md. La classe de fenêtre hôte doit être disponible
            // avant la construction de la fenêtre, sinon on garde l'affichage WPF.
            useShellView = ShellFolderViewHost.IsRequested() && ShellFolderViewHost.TryPrepare();
            if (useShellView)
                ConfigureShellView();

            PreviewKeyDown += (_, e) =>
            {
                // Raccourcis du bandeau : même sous vue shell (Alt+← / Alt+Home), car DirectUIHWND
                // ne les traduit pas en navigation Naultinus.
                if (PortalFolderInput.TryHandleBannerKeys(viewModel, e))
                {
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    FocusPortalFilterBox();
                    e.Handled = true;
                    return;
                }

                if (useShellView)
                    return; // le reste du clavier est pour la vue hébergée
                if (!viewModel.AllowKeyboard)
                    return;
                if (PortalFolderInput.TryHandle(viewModel, this, e))
                    e.Handled = true;
            };

            if (useShellView)
            {
                ViewModeMenu.Visibility = Visibility.Visible;
                SortMenu.Visibility = Visibility.Visible;
                viewModel.ShellSortRequested += OnShellSortRequested;
                Closed += (_, _) => viewModel.ShellSortRequested -= OnShellSortRequested;
                ComponentDispatcher.ThreadFilterMessage += OnShellBannerFilterMessage;
                Closed += (_, _) => ComponentDispatcher.ThreadFilterMessage -= OnShellBannerFilterMessage;
                PreviewMouseWheel += OnShellViewPreviewMouseWheel;
            }

            Show();
        }

        private void ViewModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!useShellView || sender is not MenuItem item || item.Tag is not string tag
                || !uint.TryParse(tag, out uint mode))
                return;

            bool showHeader = mode == ShellBrowserNative.FolderViewModes.Details;
            ShellView.SetViewMode(mode, showHeader);
        }

        private void OnShellSortRequested(PortalSortField field)
        {
            ShellView.TrySetSort(field);
        }

        private void FocusPortalFilterBox()
        {
            if (Header.Child is PortalPathBar bar)
                bar.FocusFilter();
        }

        private void OnShellViewPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!useShellView || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;

            if (ShellView.TryAdjustIconSize(e.Delta))
                e.Handled = true;
        }

        /// <summary>
        /// Persiste largeur/hauteur et force <c>SetRect</c> sur la vue : le binding TwoWay ne suffit
        /// pas toujours pendant un drag de bordure avec un HwndHost.
        /// </summary>
        private void OnShellViewWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || !useShellView)
                return;

            int w = (int)Math.Round(ActualWidth);
            int h = (int)Math.Round(ActualHeight);
            if (w > 0 && w != viewModel.Width)
                viewModel.Width = w;
            if (h > 0 && h != viewModel.Height)
                viewModel.Height = h;
        }

        /// <summary>
        /// Alt+← / Alt+Home n'arrivent pas toujours en <c>PreviewKeyDown</c> quand le focus Win32
        /// est dans la vue : on les prend dans le filtre de messages du thread.
        /// </summary>
        private void OnShellBannerFilterMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
        {
            if (handled || !useShellView)
                return;

            const int wmMouseWheel = 0x020A;
            if (msg.message == wmMouseWheel)
            {
                // Quand le focus est dans DirectUIHWND, la molette n'arrive pas en PreviewMouseWheel.
                long wp = msg.wParam.ToInt64();
                int keys = unchecked((int)(wp & 0xffff));
                if ((keys & 0x0008) == 0) // MK_CONTROL
                    return;

                short delta = unchecked((short)((wp >> 16) & 0xffff));
                if (ShellView.TryAdjustIconSize(delta))
                    handled = true;
                return;
            }

            const int wmKeyDown = 0x0100;
            const int wmSysKeyDown = 0x0104;
            if (msg.message != wmKeyDown && msg.message != wmSysKeyDown)
                return;

            int vk = unchecked((int)(msg.wParam.ToInt64() & 0xffff));
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl && vk == 0x46) // VK_F — focus filtre (sinon DefView mange Ctrl+F)
            {
                Dispatcher.BeginInvoke(new Action(FocusPortalFilterBox));
                handled = true;
                return;
            }

            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt
                || msg.message == wmSysKeyDown;

            if (!alt)
                return;

            if (vk == 0x24) // VK_HOME
            {
                viewModel.NavigateToRootCommand.Execute(null);
                handled = true;
            }
            else if (vk == 0x25 && viewModel.CanNavigateBack) // VK_LEFT
            {
                viewModel.NavigateBackCommand.Execute(null);
                handled = true;
            }
        }

        /// <summary>
        /// Une fenêtre WPF transparente ne peut pas afficher de contenu Win32 : le mode vue du shell
        /// reprend donc un fond opaque (les teintes du thème) et masque la grille WPF.
        /// </summary>
        private void ConfigureShellView()
        {
            AllowsTransparency = false;
            Background = TryFindResource("NaultinusWindowBrush") as Brush
                ?? SystemColors.WindowBrush;

            // L'HwndHost de la vue shell mange les hits souris sur toute sa surface : sans marge,
            // il recouvre la bande de redimensionnement de WindowChrome (mesuré : impossible de
            // saisir les bords gauche / droit / bas). On laisse 6 px de WPF autour.
            const double resizeBand = 6;
            ShellView.Margin = new Thickness(resizeBand);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(resizeBand),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                NonClientFrameEdges = NonClientFrameEdges.None,
                UseAeroCaptionButtons = false,
            });

            WpfGridView.Visibility = Visibility.Collapsed;
            ShellView.Visibility = Visibility.Visible;

            SizeChanged += OnShellViewWindowSizeChanged;

            // WPF pré-traite tout le clavier du thread (HwndSource.OnPreprocessMessage). Sans
            // DirectionalNavigation=None, ↑/↓ déclenchent KeyboardNavigation.Navigate : le focus
            // Win32 quitte DirectUIHWND pendant le KEYDOWN (mesuré : seule le KEYUP arrive à la
            // vue ; pile = KeyboardNavigation → UIElement.Focus). TabNavigation seul ne suffit pas.
            KeyboardNavigation.SetTabNavigation(ShellView, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(ShellView, KeyboardNavigationMode.None);
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
            KeyboardNavigation.SetIsTabStop(ShellView, true);

            if (viewModel != null)
            {
                // La grille WPF est masquée : le modèle arrête de charger le dossier et ses icônes,
                // la vue du shell s'en occupe et se rafraîchit seule.
                viewModel.BodyRenderedByShell = true;
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
                ShellView.Navigated += ShellView_Navigated;
                ShellView.BrowseFailed += ShellView_BrowseFailed;
                ShellView.BrowseSucceeded += ShellView_BrowseSucceeded;
                ShellView.EmptyBackgroundDoubleClick += ShellView_EmptyBackgroundDoubleClick;
                viewModel.ShellFolderNeedsRefresh += ShellView_NeedsRefresh;
                Closed += (_, _) => viewModel.ShellFolderNeedsRefresh -= ShellView_NeedsRefresh;
                ApplyShellInteractionPolicy();
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(FolderPortalViewModel.AllowDoubleClick)
                        or nameof(FolderPortalViewModel.AllowKeyboard)
                        or nameof(FolderPortalViewModel.AllowContextMenu)
                        or nameof(FolderPortalViewModel.AllowDrop)
                        or nameof(FolderPortalViewModel.AllowDragOut))
                        ApplyShellInteractionPolicy();
                };
                Loaded += (_, _) =>
                {
                    ShellView.SetNameFilter(viewModel.FilterQuery);
                    ShellView.Navigate(viewModel.CurrentPath);
                };
            }

            // La vue peut changer de mode par son propre menu, et l'en-tête de colonnes reste alors
            // accroché : on le remet d'accord avec le mode à chaque activation du portail. Le focus
            // clavier doit finir sur la fenêtre de liste, sinon F2 et Suppr ne répondent pas : appelé
            // pendant l'activation, il était écrasé par la restauration du focus côté WPF et ne
            // marchait qu'au deuxième clic — il est donc différé derrière elle.
            Activated += (_, _) =>
            {
                ShellView.ReconcileViewMode();
                ShellView.StartModeWatch();
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        System.Windows.Input.Keyboard.Focus(ShellView);
                        ShellView.FocusView();
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
            Deactivated += (_, _) => ShellView.SlowModeWatch();

            if (Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_DIAGMODE") == "cycle"
                || string.Equals(Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_DIAGMODE"), "menu", StringComparison.OrdinalIgnoreCase))
                StartModeCycleForDiagnostics();

        }

        /// <summary>
        /// Diagnostic réservé aux bancs d'essai : enchaîne les modes de vue pour mesurer ce que
        /// chacun fait des titres de colonnes (NAULTINUS_SHELLVIEW_DIAGMODE=cycle).
        /// </summary>
        private void StartModeCycleForDiagnostics()
        {
            (uint Mode, bool Header, string Nom)[] steps =
            {
                (ShellBrowserNative.FolderViewModes.Details, true, "details"),
                (ShellBrowserNative.FolderViewModes.Thumbnail, true, "vignettes"),
                (ShellBrowserNative.FolderViewModes.List, true, "liste"),
            };

            int index = 0;
            bool throughView = string.Equals(
                Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_DIAGMODE"),
                "menu",
                StringComparison.OrdinalIgnoreCase);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            timer.Tick += (_, _) =>
            {
                (uint Mode, bool Header, string Nom) step = steps[index % steps.Length];
                if (throughView)
                {
                    // Chemin du menu de la vue : la vue change le mode sans que les réglages du
                    // dossier bougent. C'est l'état où les éléments partent en icônes et où l'en-tête
                    // reste accroché.
                    ShellView.SetViewModeThroughViewOnly(step.Mode);
                }
                else
                {
                    ShellView.SetViewMode(step.Mode, step.Header);
                }

                Naultinus.Helpers.NaultinusDiagnostics.Log("Diagnostic.mode", "mode = " + step.Nom + (throughView ? " (par la vue seule)" : string.Empty));
                index++;
            };

            timer.Start();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FolderPortalViewModel.FilterQuery))
            {
                ShellView.SetNameFilter(viewModel.FilterQuery);
                return;
            }

            if (e.PropertyName == nameof(FolderPortalViewModel.ErrorMessage)
                && !string.IsNullOrEmpty(viewModel.ErrorMessage))
            {
                ShellView.ShowUnavailable(viewModel.ErrorMessage);
                return;
            }

            if (e.PropertyName != nameof(FolderPortalViewModel.CurrentPath))
                return;

            // Un chemin qui vient de la vue n'a pas besoin d'être renvoyé à la vue.
            if (viewModel.IsAdoptingShellPath)
                return;

            ShellView.Navigate(viewModel.CurrentPath);
        }

        /// <summary>
        /// La vue a navigué seule (double-clic sur un dossier, retour arrière) : on aligne l'état,
        /// la barre de chemin et la flèche « remonter ».
        /// </summary>
        private void ShellView_Navigated(string path)
        {
            viewModel.AdoptShellPath(path);
        }

        private void ShellView_BrowseFailed(string message)
        {
            viewModel.ErrorMessage = message;
        }

        private void ShellView_BrowseSucceeded()
        {
            if (!string.IsNullOrEmpty(viewModel.ErrorMessage))
                viewModel.ErrorMessage = "";
        }

        private void ShellView_NeedsRefresh()
        {
            ShellView.RefreshView();
        }

        private void ApplyShellInteractionPolicy()
        {
            ShellView.AllowDoubleClick = viewModel.AllowDoubleClick;
            ShellView.AllowKeyboard = viewModel.AllowKeyboard;
            ShellView.AllowContextMenu = viewModel.AllowContextMenu;
            ShellView.AllowShellDrop = viewModel.AllowDrop;
            ShellView.AllowShellDragOut = viewModel.AllowDragOut;
            ShellView.ApplyInteractionPolicy();
        }

        private void ShellView_EmptyBackgroundDoubleClick(bool control)
        {
            if (!viewModel.AllowDoubleClick)
                return;
            if (control)
                viewModel.NavigateToRootCommand.Execute(null);
            else
                viewModel.OpenInExplorerCommand.Execute(null);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (!viewModel.AllowDoubleClick)
                {
                    e.Handled = true;
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    viewModel.NavigateToRootCommand.Execute(null);
                else
                    viewModel.OpenInExplorerCommand.Execute(null);
                e.Handled = true;
                return;
            }

            base.OnMouseLeftButtonDown(e);
            try { DragMove(); }
            catch (System.InvalidOperationException) { /* le bouton gauche n'est plus enfoncé : sans effet */ }
        }

        private void Header_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            BannerContextMenu.Open(Header, e);
        }

        private void TitleBarMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (Header.ContextMenu is ContextMenu cm)
            {
                cm.PlacementTarget = sender as UIElement;
                cm.Placement = PlacementMode.Bottom;
                cm.IsOpen = true;
            }

            e.Handled = true;
        }

        private void AddFolderPortalTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.FrameworkElement anchor)
                NaultinusManager.RequestAddTab(this, anchor);
            e.Handled = true;
        }

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            viewModel?.RefreshRecentSnapshots();
        }
    }
}
