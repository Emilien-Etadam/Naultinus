using Naultinus.Model;
using Naultinus.ViewModel;
using System.Windows;

namespace Naultinus.View
{
    public partial class EditCalendarNaultinus : Window
    {
        public EditCalendarNaultinus()
        {
            InitializeComponent();
        }

        public EditCalendarNaultinus(CalendarNaultinusViewModel viewModel) : this()
        {
            DataContext = viewModel;
            Loaded += (s, _) =>
            {
                ViewModeCombo.ItemsSource = new[] { CalendarViewMode.Agenda, CalendarViewMode.Day, CalendarViewMode.Week };
                if (DataContext is CalendarNaultinusViewModel vm)
                    ViewModeCombo.SelectedItem = vm.ViewMode;
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CalendarNaultinusViewModel vm)
            {
                if (ViewModeCombo.SelectedItem is CalendarViewMode mode)
                    vm.ViewMode = mode;
                vm.Save();
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
