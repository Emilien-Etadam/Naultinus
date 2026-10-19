using Naultinus.Helpers.Native;
using Naultinus.ViewModel;
using Naultinus;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

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
                if (useShellView)
                    return; // la vue hébergée traite ses propres touches
                if (PortalFolderInput.TryHandle(viewModel, this, e))
                    e.Handled = true;
            };
            Show();
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

            WpfGridView.Visibility = Visibility.Collapsed;
            ShellView.Visibility = Visibility.Visible;

            // WPF pré-traite tout le clavier du thread, y compris les touches destinées à la vue
            // hébergée. Faute d'élément focalisé, il y déclenche une navigation clavier qui rend le
            // focus à la fenêtre : le WM_KEYDOWN est avalé pendant le transfert et la vue ne reçoit
            // que le WM_KEYUP (mesuré sur le poste). Le focus logique reste donc sur l'hôte, qui le
            // transmet à la fenêtre de liste ; la navigation n'a nulle part où aller.
            System.Windows.Input.KeyboardNavigation.SetTabNavigation(ShellView, System.Windows.Input.KeyboardNavigationMode.None);
            System.Windows.Input.KeyboardNavigation.SetIsTabStop(ShellView, true);

            if (viewModel != null)
            {
                // La grille WPF est masquée : le modèle arrête de charger le dossier et ses icônes,
                // la vue du shell s'en occupe et se rafraîchit seule.
                viewModel.BodyRenderedByShell = true;
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
                ShellView.Navigated += ShellView_Navigated;
                Loaded += (_, _) => ShellView.Navigate(viewModel.CurrentPath);
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

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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
