using System;

namespace Palisades.Model
{
    /// <summary>
    /// Conversion d'un ancien PalisadeModel monolithique (state.xml sans xsi:type)
    /// vers les types concrets pour les ViewModels.
    /// </summary>
    public static class PalisadeModelMigration
    {
        public static PalisadeModelBase ToConcreteModel(PalisadeModel legacy)
        {
            ArgumentNullException.ThrowIfNull(legacy);

            switch (legacy.Type)
            {
                case PalisadeType.FolderPortal:
                    return ToFolderPortalModel(legacy);
                case PalisadeType.TaskPalisade:
                    return ToTaskPalisadeModel(legacy);
                case PalisadeType.Standard:
                default:
                    return ToStandardPalisadeModel(legacy);
            }
        }

        public static StandardPalisadeModel ToStandardPalisadeModel(PalisadeModel legacy)
        {
            var m = new StandardPalisadeModel();
            CopyBase(legacy, m);
            m.Shortcuts = legacy.Shortcuts ?? new System.Collections.ObjectModel.ObservableCollection<Shortcut>();
            return m;
        }

        public static FolderPortalModel ToFolderPortalModel(PalisadeModel legacy)
        {
            var m = new FolderPortalModel();
            CopyBase(legacy, m);
            m.RootPath = legacy.RootPath ?? "";
            m.CurrentPath = legacy.CurrentPath ?? "";
            return m;
        }

        public static TaskPalisadeModel ToTaskPalisadeModel(PalisadeModel legacy)
        {
            var m = new TaskPalisadeModel();
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

        private static void CopyBase(PalisadeModel source, PalisadeModelBase target)
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
