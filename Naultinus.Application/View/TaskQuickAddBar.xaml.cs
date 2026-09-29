using Naultinus.ViewModel;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Naultinus.View
{
    /// <summary>
    /// Champ en bas de la liste de tâches : le titre saisi est créé avec Entrée.
    /// Le bouton « + Tâche » donne le focus à ce champ et n'insère pas de tâche.
    /// </summary>
    public partial class TaskQuickAddBar : UserControl
    {
        private TaskNaultinusViewModel? _subscribed;

        public TaskQuickAddBar()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Resubscribe();

        private void OnUnloaded(object sender, RoutedEventArgs e) => Unsubscribe();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Resubscribe();

        private void Resubscribe()
        {
            Unsubscribe();
            if (DataContext is not TaskNaultinusViewModel viewModel)
                return;
            viewModel.QuickAddFocusRequested += OnQuickAddFocusRequested;
            _subscribed = viewModel;
        }

        private void Unsubscribe()
        {
            if (_subscribed == null)
                return;
            _subscribed.QuickAddFocusRequested -= OnQuickAddFocusRequested;
            _subscribed = null;
        }

        private void OnQuickAddFocusRequested(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusTitleBox));
        }

        private void FocusTitleBox()
        {
            TitleBox.Focus();
            TitleBox.CaretIndex = TitleBox.Text?.Length ?? 0;
        }

        private void TitleBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
                return;
            e.Handled = true;
            if (DataContext is TaskNaultinusViewModel viewModel)
                viewModel.ConfirmQuickAddCommand.Execute(null);
        }
    }
}
