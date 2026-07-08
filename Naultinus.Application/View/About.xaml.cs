using System.Windows;

namespace Naultinus.View
{
    /// <summary>
    /// Logique d'interaction pour About.xaml
    /// </summary>
    public partial class About : Window
    {
        public About()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}