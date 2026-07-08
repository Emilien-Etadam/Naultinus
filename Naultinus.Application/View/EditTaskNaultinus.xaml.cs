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
            if (DataContext is TaskNaultinusViewModel vm)
                PasswordBox.Password = vm.CalDAVPassword;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = _viewModel ?? (TaskNaultinusViewModel)DataContext;
            // Mettre à jour le mot de passe depuis le PasswordBox
            // Le setter de CalDAVPassword gère automatiquement le chiffrement
            vm.CalDAVPassword = PasswordBox.Password;
            
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