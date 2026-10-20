using System.Windows;
using System.Windows.Controls;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    public partial class NaultinusAppearanceEditor : UserControl
    {
        public NaultinusAppearanceEditor()
        {
            InitializeComponent();
        }

        private ViewModelBase? Vm => DataContext as ViewModelBase;

        private void ClearHeader_Click(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.HeaderColor = string.Empty;
        }

        private void ClearBody_Click(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.BodyColor = string.Empty;
        }

        private void ClearTitle_Click(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.TitleColor = string.Empty;
        }

        private void ClearLabels_Click(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.LabelsColor = string.Empty;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            Vm?.ResetAppearance();
        }
    }
}
