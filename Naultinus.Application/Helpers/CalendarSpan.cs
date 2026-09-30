using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Nombre de jours affichés selon le mode.
    /// L'agenda conserve le nombre saisi. Le jour et la semaine ont une largeur fixe.
    /// </summary>
    internal static class CalendarSpan
    {
        public const int DefaultAgendaDays = 30;
        public const int DaySpan = 1;
        public const int WeekSpan = 7;

        public static int NormalizeAgendaDays(int days) => days > 0 ? days : DefaultAgendaDays;

        public static int DaysForMode(CalendarViewMode mode, int agendaDays) => mode switch
        {
            CalendarViewMode.Day => DaySpan,
            CalendarViewMode.Week => WeekSpan,
            CalendarViewMode.Agenda => NormalizeAgendaDays(agendaDays),
            _ => WeekSpan,
        };

        /// <summary>
        /// Vrai quand la valeur saisie est la préférence d'agenda.
        /// En jour ou en semaine, 1 et 7 sont la largeur du mode, pas une nouvelle préférence.
        /// </summary>
        public static bool IsAgendaPreference(CalendarViewMode mode, int days)
        {
            if (mode == CalendarViewMode.Agenda)
                return true;

            return days != DaysForMode(mode, days);
        }
    }
}
