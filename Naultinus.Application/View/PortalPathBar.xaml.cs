using System.Windows;
using System.Windows.Controls;

namespace Naultinus.View
{
    /// <summary>
    /// Bandeau unique du portail : menu, navigation, fil d'Ariane, filtre, nouvel onglet.
    /// </summary>
    public partial class PortalPathBar : UserControl
    {
        public PortalPathBar()
        {
            InitializeComponent();
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
