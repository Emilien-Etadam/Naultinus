using Naultinus.Helpers;
using Naultinus.ViewModel;
using System;
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

        /// <summary>Ouvre le dialogue avec les valeurs déjà enregistrées sur le modèle.</summary>
        public TaskNaultinusSettingsDialog(TaskNaultinusViewModel viewModel) : this()
        {
            ArgumentNullException.ThrowIfNull(viewModel);
            _viewModel = viewModel;
            DataContext = viewModel;
            LoadPersistedValues(viewModel);
        }

        private void LoadPersistedValues(TaskNaultinusViewModel viewModel)
        {
            // Le curseur ne couvre que la plage valide. Une valeur déjà stockée hors plage
            // est affichée à la borne ; Annuler ne réécrit pas le modèle.
            SyncIntervalSlider.Minimum = TaskNaultinusSettings.MinSyncIntervalMinutes;
            SyncIntervalSlider.Maximum = TaskNaultinusSettings.MaxSyncIntervalMinutes;

            int interval = viewModel.SyncIntervalMinutes;
            if (interval < TaskNaultinusSettings.MinSyncIntervalMinutes)
                interval = TaskNaultinusSettings.MinSyncIntervalMinutes;
            else if (interval > TaskNaultinusSettings.MaxSyncIntervalMinutes)
                interval = TaskNaultinusSettings.MaxSyncIntervalMinutes;

            SyncIntervalSlider.Value = interval;
            EnableLoggingCheckBox.IsChecked = viewModel.EnableLogging;
            ShowCompletedCheckBox.IsChecked = viewModel.ShowCompletedTasks;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Enregistrer écrit les trois réglages. Un intervalle invalide laisse le dialogue
            // ouvert et ne modifie pas le modèle. Annuler ne passe pas par cette méthode.
            var viewModel = _viewModel ?? DataContext as TaskNaultinusViewModel;
            if (viewModel == null)
                return;

            int interval = (int)Math.Round(SyncIntervalSlider.Value, MidpointRounding.AwayFromZero);
            bool enableLogging = EnableLoggingCheckBox.IsChecked == true;
            bool showCompleted = ShowCompletedCheckBox.IsChecked == true;

            if (!viewModel.TryApplySettings(interval, enableLogging, showCompleted))
                return;

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
