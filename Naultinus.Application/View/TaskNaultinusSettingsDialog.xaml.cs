using Naultinus.ViewModel;
using System.Windows;

namespace Naultinus.View
{
    public partial class TaskNaultinusSettingsDialog : Window
    {
        private readonly TaskNaultinusViewModel? _viewModel;

        public TaskNaultinusSettingsDialog()
        {
            InitializeComponent();
        }

        public TaskNaultinusSettingsDialog(TaskNaultinusViewModel viewModel) : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            SyncIntervalSlider.Value = 5;
            EnableLoggingCheckBox.IsChecked = false;
            ShowCompletedCheckBox.IsChecked = true;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Issue ouverte : persistance des réglages (intervalle, journalisation, tâches terminées)
            // non implémentée — le modèle TaskNaultinusModel n'expose pas encore ces champs.
            // Le dialogue affiche les valeurs par défaut et se ferme sans modifier le comportement.

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
