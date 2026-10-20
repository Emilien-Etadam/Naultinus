using System.Windows;
using System.Windows.Controls;
using Naultinus.Helpers;

namespace Naultinus.View
{
    /// <summary>
    /// Bandeau unique du portail : menu, navigation, fil d'Ariane, filtre, nouvel onglet.
    /// En fenêtre groupée, menu et + sont déjà sur la barre d'onglets : désactiver
    /// <see cref="ShowWindowChromeButtons"/>.
    /// </summary>
    public partial class PortalPathBar : UserControl
    {
        public static readonly DependencyProperty ShowWindowChromeButtonsProperty =
            DependencyProperty.Register(
                nameof(ShowWindowChromeButtons),
                typeof(bool),
                typeof(PortalPathBar),
                new PropertyMetadata(true));

        public PortalPathBar()
        {
            InitializeComponent();
            // Transparent pur ignore les clics sur fenêtre layered ; A=1 permet DragMove sur le vide.
            Background = NaultinusAppearanceColors.HitTestableClear;
        }

        /// <summary>
        /// Affiche ☰ et + (fenêtre autonome). False dans <see cref="TabbedNaultinus"/>
        /// où ces actions sont déjà sur la barre d'onglets.
        /// </summary>
        public bool ShowWindowChromeButtons
        {
            get => (bool)GetValue(ShowWindowChromeButtonsProperty);
            set => SetValue(ShowWindowChromeButtonsProperty, value);
        }

        /// <summary>Clic sur le bouton menu (☰).</summary>
        public event RoutedEventHandler? MenuClick;

        /// <summary>Clic sur le bouton nouvel onglet (+).</summary>
        public event RoutedEventHandler? AddTabClick;

        /// <summary>Place le focus dans la zone de filtre (Ctrl+F).</summary>
        public void FocusFilter()
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e) => MenuClick?.Invoke(sender, e);

        private void AddTabButton_Click(object sender, RoutedEventArgs e) => AddTabClick?.Invoke(sender, e);
    }
}
