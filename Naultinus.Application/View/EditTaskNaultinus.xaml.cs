using Naultinus.Helpers;
using Naultinus.Services;
using Naultinus.ViewModel;
using System.Windows;

namespace Naultinus.View
{
    public partial class EditTaskNaultinus : Window
    {
        private readonly TaskNaultinusViewModel? _viewModel;

        public EditTaskNaultinus()
        {
            InitializeComponent();
        }

        public EditTaskNaultinus(TaskNaultinusViewModel viewModel) : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AccountHintText.Text = SharedCalDavAccount.DescribeStatus(AppSettingsStore.Load())
                + " "
                + Naultinus.Properties.Strings.SharedCalDavEditHint;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = _viewModel ?? (TaskNaultinusViewModel)DataContext;
            vm.Save();
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
