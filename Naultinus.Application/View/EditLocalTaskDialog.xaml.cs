using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Properties;
using System;
using System.Windows;

namespace Naultinus.View
{
    /// <summary>Saisie d'une tâche locale (titre, description, échéance). Aucun appel réseau.</summary>
    public partial class EditLocalTaskDialog : Window
    {
        private readonly CalDAVTask? _existing;

        public StoredLocalTask? Result { get; private set; }

        public EditLocalTaskDialog(CalDAVTask? existing)
        {
            InitializeComponent();
            _existing = existing;
            if (existing == null)
            {
                Title = Strings.WindowTitleAddTask;
                SaveButton.Content = Strings.ButtonCreate;
                return;
            }

            Title = Strings.WindowTitleEditTask;
            TitleTextBox.Text = existing.Title ?? string.Empty;
            DescriptionTextBox.Text = existing.Description ?? string.Empty;
            DueDatePicker.SelectedDate = existing.DueDate?.Date;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!LocalPlannerStore.TryBuildTask(
                TitleTextBox.Text,
                DescriptionTextBox.Text,
                DueDatePicker.SelectedDate,
                _existing?.Id,
                _existing?.CreatedDate ?? default,
                _existing?.Completed ?? false,
                _existing?.CompletedDate,
                out var task,
                out var error))
            {
                MessageBox.Show(LocalPlannerStore.Describe(error), Strings.ValidationTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = task;
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
