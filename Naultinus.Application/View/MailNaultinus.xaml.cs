using Naultinus.ViewModel;
using System.Windows;
using System.Windows.Input;

namespace Naultinus.View
{
    public partial class MailNaultinus : Window
    {
        public MailNaultinus(MailNaultinusViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Show();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnMouseLeftButtonDown(e);
            try { DragMove(); }
            catch (System.InvalidOperationException) { /* le bouton gauche n'est plus enfoncé : sans effet */ }
        }

        private void LayoutsSubmenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModelBase)?.RefreshRecentSnapshots();
        }
    }
}
