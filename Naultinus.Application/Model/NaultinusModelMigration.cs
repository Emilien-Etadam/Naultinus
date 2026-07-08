using System;

namespace Naultinus.Model
{
    /// <summary>
    /// Conversion d'un ancien NaultinusModel monolithique (state.xml sans xsi:type)
    /// vers les types concrets pour les ViewModels.
    /// </summary>
    public static class NaultinusModelMigration
    {
        public static NaultinusModelBase ToConcreteModel(NaultinusModel legacy)
        {
            ArgumentNullException.ThrowIfNull(legacy);

            switch (legacy.Type)
            {
                case NaultinusType.FolderPortal:
                    return ToFolderPortalModel(legacy);
                case NaultinusType.TaskNaultinus:
                    return ToTaskNaultinusModel(legacy);
                case NaultinusType.Standard:
                default:
                    return ToStandardNaultinusModel(legacy);
            }
        }

        public static StandardNaultinusModel ToStandardNaultinusModel(NaultinusModel legacy)
        {
            var m = new StandardNaultinusModel();
            CopyBase(legacy, m);
            m.Shortcuts = legacy.Shortcuts ?? new System.Collections.ObjectModel.ObservableCollection<Shortcut>();
            return m;
        }

        public static FolderPortalModel ToFolderPortalModel(NaultinusModel legacy)
        {
            var m = new FolderPortalModel();
            CopyBase(legacy, m);
            m.RootPath = legacy.RootPath ?? "";
            m.CurrentPath = legacy.CurrentPath ?? "";
            return m;
        }

        public static TaskNaultinusModel ToTaskNaultinusModel(NaultinusModel legacy)
        {
            var m = new TaskNaultinusModel();
            CopyBase(legacy, m);
            m.CalDAVUrl = legacy.CalDAVUrl ?? "";
            m.CalDAVUsername = legacy.CalDAVUsername ?? "";
            m.CalDAVPassword = legacy.CalDAVPassword ?? "";
            m.TaskListId = legacy.TaskListId ?? "";
            m.SyncIntervalMinutes = legacy.SyncIntervalMinutes;
            m.EnableLogging = legacy.EnableLogging;
            m.ShowCompletedTasks = legacy.ShowCompletedTasks;
            return m;
        }

        private static void CopyBase(NaultinusModel source, NaultinusModelBase target)
        {
            target.Identifier = source.Identifier;
            target.Name = source.Name;
            target.FenceX = source.FenceX;
            target.FenceY = source.FenceY;
            target.Width = source.Width;
            target.Height = source.Height;
            target.Type = source.Type;
        }
    }
}
