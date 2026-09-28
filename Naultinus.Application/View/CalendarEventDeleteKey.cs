using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Suppr supprime l'événement de calendrier sélectionné.
    /// Les panneaux de raccourcis ne passent pas par ici.
    /// </summary>
    public static class CalendarEventDeleteKey
    {
        /// <summary>
        /// Consomme Suppr quand un événement est sélectionné et que le focus n'est pas dans un champ de saisie.
        /// La suppression reste celle de <see cref="CalendarNaultinusViewModel.DeleteEventCommand"/> :
        /// confirmation, puis retrait local ou DELETE CalDAV de cette seule ressource.
        /// </summary>
        /// <param name="calendar">Panneau calendrier actif. Null si l'onglet courant n'est pas un calendrier.</param>
        /// <param name="e">Touche en cours.</param>
        /// <returns>Vrai si la touche a lancé la suppression.</returns>
        public static bool TryHandle(CalendarNaultinusViewModel? calendar, KeyEventArgs e)
        {
            if (calendar == null || e.Key != Key.Delete || e.IsRepeat)
                return false;

            if (IsTextInput(e.OriginalSource as DependencyObject))
                return false;

            var selected = calendar.SelectedEvent;
            if (selected == null)
                return false;

            var command = calendar.DeleteEventCommand;
            if (!command.CanExecute(selected))
                return false;

            command.Execute(selected);
            return true;
        }

        private static bool IsTextInput(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is TextBox or RichTextBox or PasswordBox)
                    return true;

                source = Parent(source);
            }

            return false;
        }

        private static DependencyObject? Parent(DependencyObject child)
        {
            if (child is Visual or System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(child);

            return LogicalTreeHelper.GetParent(child);
        }
    }
}
