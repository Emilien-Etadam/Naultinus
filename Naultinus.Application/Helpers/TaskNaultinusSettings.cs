using Naultinus.Model;
using System;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Réglages du dialogue des tâches : intervalle de synchro, journalisation, tâches terminées.
    /// La plage d'intervalle est celle du curseur du dialogue (1 à 60 minutes).
    /// </summary>
    public static class TaskNaultinusSettings
    {
        public const int MinSyncIntervalMinutes = 1;
        public const int MaxSyncIntervalMinutes = 60;

        /// <summary>
        /// Écrit les trois réglages sur le modèle si l'intervalle est dans la plage autorisée.
        /// Sinon le modèle n'est pas modifié : aucun champ n'est écrit.
        /// </summary>
        public static bool TryApply(TaskNaultinusModel model, int syncIntervalMinutes, bool enableLogging, bool showCompletedTasks)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (syncIntervalMinutes < MinSyncIntervalMinutes || syncIntervalMinutes > MaxSyncIntervalMinutes)
                return false;

            model.SyncIntervalMinutes = syncIntervalMinutes;
            model.EnableLogging = enableLogging;
            model.ShowCompletedTasks = showCompletedTasks;
            return true;
        }
    }
}
