using System;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Décide si la liste agenda doit repartir du jour local.
    /// Le nombre de jours affichés n'est pas concerné : seule l'ancre bouge, et seulement en mode agenda.
    /// </summary>
    internal static class AgendaAnchor
    {
        public static DateTime LocalDate(DateTime localNow) => localNow.Date;

        /// <summary>
        /// Vrai quand le jour local a changé et que l'ancre agenda n'est plus ce jour.
        /// Semaine et jour ne sont pas recalés : ils gardent leur date de départ.
        /// </summary>
        public static bool ShouldRealign(CalendarViewMode mode, DateTime anchor, DateTime observedLocalDate, DateTime localNow)
        {
            var today = localNow.Date;
            return mode == CalendarViewMode.Agenda
                && observedLocalDate.Date != today
                && anchor.Date != today;
        }
    }
}
