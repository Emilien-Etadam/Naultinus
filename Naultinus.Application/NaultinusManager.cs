using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Model;
using Naultinus.View;
using Naultinus.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Naultinus
{
    internal static class NaultinusManager
    {
        public static readonly Dictionary<string, Window> naultinus = new();

        public static void LoadNaultinus()
        {
            string saveDirectory = PDirectory.GetNaultinusDirectory();
            PDirectory.EnsureExists(saveDirectory);

            var loadedConcrete = new List<NaultinusModelBase>();

            foreach (string identifierDirname in Directory.GetDirectories(saveDirectory))
            {
                string stateFile = Path.Combine(identifierDirname, "state.xml");
                if (!File.Exists(stateFile))
                    continue;

                try
                {
                    using var reader = new StreamReader(stateFile);
                    var obj = SafeXml.Deserialize(ViewModelBase.SharedSerializer, reader);
                    if (obj is NaultinusModel legacy)
                    {
                        var concrete = NaultinusModelMigration.ToConcreteModel(legacy);
                        if (concrete.SchemaVersion == 0) concrete.SchemaVersion = 1;
                        loadedConcrete.Add(concrete);
                    }
                    else if (obj is NaultinusModelBase concrete)
                    {
                        if (concrete.SchemaVersion == 0) concrete.SchemaVersion = 1;
                        loadedConcrete.Add(concrete);
                    }
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.Log("NaultinusManager", "Failed to load " + stateFile, ex);
                }
            }

            var grouped = loadedConcrete.Where(m => !string.IsNullOrEmpty(m.GroupId)).ToList();
            var standalone = loadedConcrete.Where(m => string.IsNullOrEmpty(m.GroupId)).ToList();

            foreach (var g in grouped.GroupBy(m => m.GroupId!))
            {
                var groupId = g.Key;
                var ordered = g.OrderBy(m => m.TabOrder).ToList();
                var viewModels = new List<INaultinusViewModel>();
                foreach (var concrete in ordered)
                {
                    var vm = NaultinusFactory.CreateViewModel(concrete);
                    if (vm != null)
                        viewModels.Add(vm);
                }
                if (viewModels.Count == 0) continue;
                var group = new NaultinusGroup(groupId);
                foreach (var vm in viewModels)
                    group.AddMember(vm);
                var tabbed = new TabbedNaultinus(group);
                foreach (var vm in viewModels)
                    naultinus[vm.Identifier] = tabbed;
            }

            foreach (NaultinusModelBase concrete in standalone)
            {
                var vm = NaultinusFactory.CreateViewModel(concrete);
                if (vm == null) continue;
                var window = NaultinusFactory.CreateWindow(vm);
                naultinus.Add(concrete.Identifier, window);
            }
        }

        private static void RegisterViewModelInGroup(INaultinusViewModel vm, string? groupId, TabbedNaultinus? tabbedWindow)
        {
            if (!string.IsNullOrEmpty(groupId) && tabbedWindow?.DataContext is NaultinusGroup g)
            {
                g.AddMember(vm);
                naultinus[vm.Identifier] = tabbedWindow;
                vm.Save();
                g.SelectedMember = vm;
                return;
            }

            var window = NaultinusFactory.CreateWindow(vm);
            naultinus.Add(vm.Identifier, window);
            vm.Save();
            window.Show();
        }

        public static void CreateNaultinus(int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedNaultinus? tabbedWindow = null)
        {
            var model = new StandardNaultinusModel();
            if (x.HasValue) model.FenceX = x.Value;
            if (y.HasValue) model.FenceY = y.Value;
            if (width.HasValue) model.Width = width.Value;
            if (height.HasValue) model.Height = height.Value;
            if (!string.IsNullOrEmpty(groupId))
                model.GroupId = groupId;

            RegisterViewModelInGroup(new NaultinusViewModel(model), groupId, tabbedWindow);
        }

        public static void CreateFolderPortal(string rootPath, string title, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedNaultinus? tabbedWindow = null)
        {
            var model = new FolderPortalModel
            {
                Name = title,
                RootPath = rootPath,
                CurrentPath = rootPath
            };
            if (x.HasValue) model.FenceX = x.Value;
            if (y.HasValue) model.FenceY = y.Value;
            if (width.HasValue) model.Width = width.Value;
            if (height.HasValue) model.Height = height.Value;
            if (!string.IsNullOrEmpty(groupId))
                model.GroupId = groupId;

            RegisterViewModelInGroup(new FolderPortalViewModel(model), groupId, tabbedWindow);
        }

        public static void ShowCreateFolderPortalDialogForGroup(NaultinusGroup group, TabbedNaultinus tabbedWindow)
        {
            var dialog = new CreateFolderPortalDialog();
            SetOwnerSafe(dialog, tabbedWindow);
            if (dialog.ShowDialog() == true)
                CreateFolderPortal(dialog.SelectedPath, dialog.PortalTitle, group.X, group.Y, group.Width, group.Height, group.GroupId, tabbedWindow);
        }

        /// <summary>Ouvre le menu « + » : choix du type de naultinus à ajouter comme onglet.</summary>
        public static void RequestAddTab(Window hostWindow, FrameworkElement anchor)
        {
            var menu = new ContextMenu();
            void Add(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (_, _) =>
                {
                    menu.IsOpen = false;
                    action();
                };
                menu.Items.Add(item);
            }

            Add(Strings.AddTabShortcutNaultinus, () => AddTabFence(hostWindow));
            Add(Strings.AddTabBrowseNaultinus, () => AddTabFolderPortal(hostWindow));
            Add(Strings.AddTabTaskNaultinus, () => AddTabTaskNaultinus(hostWindow));
            Add(Strings.AddTabCalendarNaultinus, () => AddTabCalendarNaultinus(hostWindow));
            Add(Strings.AddTabMailNaultinus, () => AddTabMailNaultinus(hostWindow));

            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private static void AddTabFence(Window hostWindow)
        {
            if (hostWindow is TabbedNaultinus tabbed && tabbed.DataContext is NaultinusGroup g)
            {
                CreateNaultinus(g.X, g.Y, g.Width, g.Height, g.GroupId, tabbed);
                return;
            }

            if (hostWindow.DataContext is not INaultinusViewModel existing)
                return;

            var model = new StandardNaultinusModel
            {
                FenceX = existing.FenceX,
                FenceY = existing.FenceY,
                Width = existing.Width,
                Height = existing.Height,
            };
            var vm = new NaultinusViewModel(model);
            MergeStandaloneIntoTabbedWithNewMember(hostWindow, existing, vm);
        }

        // Squelette commun aux ajouts d'onglet passant par un dialogue de création : si l'hôte est déjà
        // un groupe à onglets, on délègue au dialogue « pour groupe » ; sinon on affiche le dialogue puis
        // on fusionne la naultinus autonome existante avec le nouveau membre construit à partir du dialogue.
        private static void AddTabWithDialog<TDialog>(
            Window hostWindow,
            Action<NaultinusGroup, TabbedNaultinus> showForGroup,
            Func<TDialog, INaultinusViewModel, INaultinusViewModel> buildViewModel)
            where TDialog : Window, new()
        {
            if (hostWindow is TabbedNaultinus tabbed && tabbed.DataContext is NaultinusGroup g)
            {
                showForGroup(g, tabbed);
                return;
            }

            var dialog = new TDialog();
            SetOwnerSafe(dialog, hostWindow);
            if (dialog.ShowDialog() != true) return;
            if (hostWindow.DataContext is not INaultinusViewModel existing) return;

            var vmNew = buildViewModel(dialog, existing);
            MergeStandaloneIntoTabbedWithNewMember(hostWindow, existing, vmNew);
        }

        private static void AddTabFolderPortal(Window hostWindow) =>
            AddTabWithDialog<CreateFolderPortalDialog>(hostWindow,
                (g, t) => ShowCreateFolderPortalDialogForGroup(g, t),
                (dialog, existing) => new FolderPortalViewModel(new FolderPortalModel
                {
                    Name = dialog.PortalTitle,
                    RootPath = dialog.SelectedPath,
                    CurrentPath = dialog.SelectedPath,
                    FenceX = existing.FenceX,
                    FenceY = existing.FenceY,
                    Width = existing.Width,
                    Height = existing.Height,
                }));

        private static void AddTabTaskNaultinus(Window hostWindow) =>
            AddTabWithDialog<CreateTaskNaultinusDialog>(hostWindow,
                (g, t) => ShowCreateTaskNaultinusDialog(g, t),
                (dialog, existing) => NaultinusFactory.CreateTaskViewModel(
                    dialog.CalDAVUrl, dialog.Username, dialog.Password,
                    dialog.SelectedTaskListIds ?? new List<string>(), dialog.NaultinusTitle,
                    existing.FenceX, existing.FenceY, existing.Width, existing.Height,
                    dialog.SelectedZimbraAccountId));

        private static void AddTabCalendarNaultinus(Window hostWindow) =>
            AddTabWithDialog<CreateCalendarNaultinusDialog>(hostWindow,
                (g, t) => ShowCreateCalendarNaultinusDialog(g, t),
                (dialog, existing) => NaultinusFactory.CreateCalendarViewModel(
                    dialog.CalDAVUrl, dialog.Username, dialog.Password, dialog.SelectedCalendarIds,
                    dialog.NaultinusTitle, dialog.ViewMode, dialog.DaysToShow,
                    existing.FenceX, existing.FenceY, existing.Width, existing.Height,
                    dialog.SelectedZimbraAccountId));

        private static void AddTabMailNaultinus(Window hostWindow) =>
            AddTabWithDialog<CreateMailNaultinusDialog>(hostWindow,
                (g, t) => ShowCreateMailNaultinusDialog(g, t),
                (dialog, existing) => NaultinusFactory.CreateMailViewModel(
                    dialog.ImapHost, dialog.ImapPort, dialog.Username, dialog.Password, dialog.SelectedFolders,
                    dialog.NaultinusTitle, dialog.DisplayMode, dialog.PollIntervalMinutes, dialog.WebmailUrl,
                    existing.FenceX, existing.FenceY, existing.Width, existing.Height,
                    dialog.SelectedZimbraAccountId));

        private static void MergeStandaloneIntoTabbedWithNewMember(Window hostWindow, INaultinusViewModel existing, INaultinusViewModel vmNew)
        {
            if (!naultinus.TryGetValue(existing.Identifier, out var oldWindow) || oldWindow != hostWindow)
                return;

            var gid = Guid.NewGuid().ToString();
            var group = new NaultinusGroup(gid);
            group.AddMember(existing);
            group.AddMember(vmNew);

            naultinus.Remove(existing.Identifier);
            oldWindow.Close();

            var tabbed = new TabbedNaultinus(group);
            foreach (var m in group.Members)
                naultinus[m.Identifier] = tabbed;
            group.SelectedMember = vmNew;
            vmNew.Save();
        }

        internal static void OpenEditDialog(INaultinusViewModel? vm)
        {
            if (vm == null) return;
            Window? owner = null;
            try { owner = GetWindow(vm.Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue affiché sans owner */ }
            switch (vm)
            {
                case NaultinusViewModel p:
                    new EditNaultinus { DataContext = p, Owner = owner }.ShowDialog();
                    break;
                case FolderPortalViewModel f:
                    new EditFolderPortal { DataContext = f, Owner = owner }.ShowDialog();
                    break;
                case TaskNaultinusViewModel t:
                    new EditTaskNaultinus(t) { Owner = owner }.ShowDialog();
                    break;
                case CalendarNaultinusViewModel c:
                    new EditCalendarNaultinus(c) { Owner = owner }.ShowDialog();
                    break;
            }
        }

        public static void CreateTaskNaultinus(string caldavUrl, string username, string password, List<string> taskListIds, string title, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedNaultinus? tabbedWindow = null, Guid? zimbraAccountId = null)
        {
            RegisterViewModelInGroup(NaultinusFactory.CreateTaskViewModel(caldavUrl, username, password, taskListIds, title, x, y, width, height, zimbraAccountId), groupId, tabbedWindow);
        }

        public static void ShowCreateFolderPortalDialog()
        {
            CreateFolderPortalDialog dialog = new();
            if (dialog.ShowDialog() == true)
            {
                CreateFolderPortal(dialog.SelectedPath, dialog.PortalTitle);
            }
        }

        public static void ShowCreateTaskNaultinusDialog(NaultinusGroup? intoGroup = null, TabbedNaultinus? tabbedWindow = null, Guid? preselectedZimbraAccountId = null)
        {
            var dialog = new CreateTaskNaultinusDialog(preselectedZimbraAccountId);
            SetOwnerSafe(dialog, tabbedWindow ?? Application.Current.MainWindow);
            if (dialog.ShowDialog() != true) return;
            var listIds = dialog.SelectedTaskListIds ?? new List<string>();
            if (intoGroup != null && tabbedWindow != null)
                CreateTaskNaultinus(dialog.CalDAVUrl, dialog.Username, dialog.Password, listIds, dialog.NaultinusTitle, intoGroup.X, intoGroup.Y, intoGroup.Width, intoGroup.Height, intoGroup.GroupId, tabbedWindow, dialog.SelectedZimbraAccountId);
            else
                CreateTaskNaultinus(dialog.CalDAVUrl, dialog.Username, dialog.Password, listIds, dialog.NaultinusTitle, zimbraAccountId: dialog.SelectedZimbraAccountId);
        }

        public static void CreateCalendarNaultinus(string caldavUrl, string username, string password, List<string> calendarIds, string title, CalendarViewMode viewMode, int daysToShow, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedNaultinus? tabbedWindow = null, Guid? zimbraAccountId = null)
        {
            RegisterViewModelInGroup(NaultinusFactory.CreateCalendarViewModel(caldavUrl, username, password, calendarIds, title, viewMode, daysToShow, x, y, width, height, zimbraAccountId), groupId, tabbedWindow);
        }

        public static void ShowCreateCalendarNaultinusDialog(NaultinusGroup? intoGroup = null, TabbedNaultinus? tabbedWindow = null, Guid? preselectedZimbraAccountId = null)
        {
            var dialog = new CreateCalendarNaultinusDialog(preselectedZimbraAccountId);
            SetOwnerSafe(dialog, tabbedWindow ?? Application.Current.MainWindow);
            if (dialog.ShowDialog() != true) return;
            if (intoGroup != null && tabbedWindow != null)
                CreateCalendarNaultinus(dialog.CalDAVUrl, dialog.Username, dialog.Password, dialog.SelectedCalendarIds, dialog.NaultinusTitle, dialog.ViewMode, dialog.DaysToShow, intoGroup.X, intoGroup.Y, intoGroup.Width, intoGroup.Height, intoGroup.GroupId, tabbedWindow, dialog.SelectedZimbraAccountId);
            else
                CreateCalendarNaultinus(dialog.CalDAVUrl, dialog.Username, dialog.Password, dialog.SelectedCalendarIds, dialog.NaultinusTitle, dialog.ViewMode, dialog.DaysToShow, zimbraAccountId: dialog.SelectedZimbraAccountId);
        }

        public static void CreateMailNaultinus(string imapHost, int imapPort, string username, string password, List<string> monitoredFolders, string title, MailDisplayMode displayMode, int pollIntervalMinutes, string? webmailUrl = null, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedNaultinus? tabbedWindow = null, Guid? zimbraAccountId = null)
        {
            RegisterViewModelInGroup(NaultinusFactory.CreateMailViewModel(imapHost, imapPort, username, password, monitoredFolders, title, displayMode, pollIntervalMinutes, webmailUrl, x, y, width, height, zimbraAccountId), groupId, tabbedWindow);
        }

        public static void ShowCreateMailNaultinusDialog(NaultinusGroup? intoGroup = null, TabbedNaultinus? tabbedWindow = null, Guid? preselectedZimbraAccountId = null)
        {
            var dialog = new CreateMailNaultinusDialog(preselectedZimbraAccountId);
            SetOwnerSafe(dialog, tabbedWindow ?? Application.Current.MainWindow);
            if (dialog.ShowDialog() != true) return;
            if (intoGroup != null && tabbedWindow != null)
                CreateMailNaultinus(dialog.ImapHost, dialog.ImapPort, dialog.Username, dialog.Password, dialog.SelectedFolders, dialog.NaultinusTitle, dialog.DisplayMode, dialog.PollIntervalMinutes, dialog.WebmailUrl, intoGroup.X, intoGroup.Y, intoGroup.Width, intoGroup.Height, intoGroup.GroupId, tabbedWindow, dialog.SelectedZimbraAccountId);
            else
                CreateMailNaultinus(dialog.ImapHost, dialog.ImapPort, dialog.Username, dialog.Password, dialog.SelectedFolders, dialog.NaultinusTitle, dialog.DisplayMode, dialog.PollIntervalMinutes, dialog.WebmailUrl, zimbraAccountId: dialog.SelectedZimbraAccountId);
        }

        public static void DeleteNaultinus(string identifier)
        {
            if (!naultinus.TryGetValue(identifier, out Window? window)) return;

            if (window is TabbedNaultinus tabbed && tabbed.DataContext is NaultinusGroup group)
            {
                var member = group.Members.FirstOrDefault(m => m.Identifier == identifier);
                if (member == null) return;
                (member as IDisposable)?.Dispose();
                member.Delete();
                group.RemoveMember(member);
                naultinus.Remove(identifier);

                if (group.Members.Count == 0)
                {
                    tabbed.Close();
                    return;
                }
                if (group.Members.Count == 1)
                {
                    var single = group.Members[0];
                    tabbed.Close();
                    var standalone = NaultinusFactory.CreateWindow(single);
                    naultinus[single.Identifier] = standalone;
                    standalone.Show();
                    single.GroupId = null;
                    return;
                }
                foreach (var m in group.Members)
                    naultinus[m.Identifier] = tabbed;
                return;
            }

            if (window.DataContext is INaultinusViewModel vm)
                vm.Delete();
            (window.DataContext as IDisposable)?.Dispose();
            window.Close();
            naultinus.Remove(identifier);
        }

        public static Window GetWindow(string identifier)
        {
            naultinus.TryGetValue(identifier, out Window? window);
            if (window == null)
            {
                throw new KeyNotFoundException(identifier);
            }
            return window;
        }

        public static StandardNaultinus GetNaultinus(string identifier)
        {
            var window = GetWindow(identifier);
            if (window is StandardNaultinus naultinus)
            {
                return naultinus;
            }
            throw new KeyNotFoundException(identifier);
        }

        /// <summary>Ferme toutes les naultinus et vide le dictionnaire (pour RestoreSnapshot).</summary>
        public static void CloseAllNaultinus()
        {
            var windows = naultinus.Values.Distinct().ToList();
            naultinus.Clear();
            foreach (var w in windows)
            {
                try
                {
                    if (w is TabbedNaultinus tabbed && tabbed.DataContext is NaultinusGroup group)
                    {
                        foreach (var member in group.Members)
                            (member as IDisposable)?.Dispose();
                    }
                    else
                    {
                        (w.DataContext as IDisposable)?.Dispose();
                    }
                    w.Close();
                }
                catch (Exception ex) { NaultinusDiagnostics.LogDebug("NaultinusManager.CloseAllNaultinus", ex); }
            }
        }

        /// <summary>Recalcule position/taille de toutes les naultinus (résolution changée).</summary>
        public static void ApplyRescale(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            const int minW = 200, minH = 100;
            var seen = new HashSet<string>();
            foreach (var kv in naultinus.ToList())
            {
                var window = kv.Value;
                if (window is View.TabbedNaultinus tabbed && tabbed.DataContext is NaultinusGroup group)
                {
                    foreach (var vm in group.Members)
                    {
                        if (seen.Add(vm.Identifier))
                            RescaleVm(vm, oldWidth, oldHeight, newWidth, newHeight, minW, minH);
                    }
                }
                else if (window.DataContext is INaultinusViewModel vm && seen.Add(vm.Identifier))
                {
                    RescaleVm(vm, oldWidth, oldHeight, newWidth, newHeight, minW, minH);
                }
            }
        }

        internal static void SetOwnerSafe(Window dialog, Window? owner)
        {
            try { dialog.Owner = owner; }
            catch (InvalidOperationException ex) { NaultinusDiagnostics.LogDebug("NaultinusManager.SetOwnerSafe", ex); }
        }

        private static void RescaleVm(INaultinusViewModel vm, int oldW, int oldH, int newW, int newH, int minW, int minH)
        {
            int x = (vm.FenceX * newW) / oldW;
            int y = (vm.FenceY * newH) / oldH;
            int w = Math.Max(minW, (vm.Width * newW) / oldW);
            int h = Math.Max(minH, (vm.Height * newH) / oldH);
            if (x + w > newW) x = Math.Max(0, newW - w);
            if (y + h > newH) y = Math.Max(0, newH - h);
            vm.FenceX = x;
            vm.FenceY = y;
            vm.Width = w;
            vm.Height = h;
        }
    }
}
