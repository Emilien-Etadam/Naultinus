using Naultinus.ViewModel;
using Naultinus;
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

            if (viewModel != null)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
                Loaded += (_, _) => ShellView.Navigate(viewModel.CurrentPath);
            }

            Activated += (_, _) => ShellView.FocusView();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FolderPortalViewModel.CurrentPath))
                ShellView.Navigate(viewModel.CurrentPath);
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
