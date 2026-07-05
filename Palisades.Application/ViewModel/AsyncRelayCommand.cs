using Palisades.Helpers;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Palisades.ViewModel
{
    /// <summary>
    /// Commande asynchrone : encapsule un <see cref="Func{Task}"/> en l'attendant et en capturant
    /// ses exceptions. Contrairement à un lambda <c>async</c> passé à <see cref="RelayCommand"/>
    /// (qui devient un <c>async void</c> dont les exceptions s'échappent hors pile), les erreurs
    /// sont ici journalisées au lieu de faire planter l'application. La sémantique de
    /// <see cref="CanExecute"/>/<see cref="Execute"/> reste identique à <see cref="RelayCommand"/>.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public async void Execute(object? parameter)
        {
            try
            {
                await _execute();
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("AsyncRelayCommand", "Commande asynchrone en échec.", ex);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Variante paramétrée de <see cref="AsyncRelayCommand"/> (miroir de <see cref="RelayCommand{T}"/>).</summary>
    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public AsyncRelayCommand(Func<T, Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public async void Execute(object? parameter)
        {
            if (parameter is not T typed)
                return;
            try
            {
                await _execute(typed);
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("AsyncRelayCommand", "Commande asynchrone en échec.", ex);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
