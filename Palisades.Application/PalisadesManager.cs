using Palisades.Helpers;
using Palisades.Properties;
using Palisades.Model;
using Palisades.View;
using Palisades.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Palisades
{
    internal static class PalisadesManager
    {
        public static readonly Dictionary<string, Window> palisades = new();

        public static void LoadPalisades()
        {
            string saveDirectory = PDirectory.GetPalisadesDirectory();
            PDirectory.EnsureExists(saveDirectory);

            var loadedConcrete = new List<PalisadeModelBase>();

            foreach (string identifierDirname in Directory.GetDirectories(saveDirectory))
            {
                string stateFile = Path.Combine(identifierDirname, "state.xml");
                if (!File.Exists(stateFile))
                    continue;

                try
                {
                    using var reader = new StreamReader(stateFile);
                    var obj = SafeXml.Deserialize(ViewModelBase.SharedSerializer, reader);
                    if (obj is PalisadeModel legacy)
                    {
                        var concrete = PalisadeModelMigration.ToConcreteModel(legacy);
                        if (concrete.SchemaVersion == 0) concrete.SchemaVersion = 1;
                        loadedConcrete.Add(concrete);
                    }
                    else if (obj is PalisadeModelBase concrete)
                    {
                        if (concrete.SchemaVersion == 0) concrete.SchemaVersion = 1;
                        loadedConcrete.Add(concrete);
                    }
                }
                catch (Exception ex)
                {
                    PalisadeDiagnostics.Log("PalisadesManager", "Failed to load " + stateFile, ex);
                }
            }

            var grouped = loadedConcrete.Where(m => !string.IsNullOrEmpty(m.GroupId)).ToList();
            var standalone = loadedConcrete.Where(m => string.IsNullOrEmpty(m.GroupId)).ToList();

            foreach (var g in grouped.GroupBy(m => m.GroupId!))
            {
                var groupId = g.Key;
                var ordered = g.OrderBy(m => m.TabOrder).ToList();
                var viewModels = new List<IPalisadeViewModel>();
                foreach (var concrete in ordered)
                {
                    var vm = PalisadeFactory.CreateViewModel(concrete);
                    if (vm != null)
                        viewModels.Add(vm);
                }
                if (viewModels.Count == 0) continue;
                var group = new PalisadeGroup(groupId);
                foreach (var vm in viewModels)
                    group.AddMember(vm);
                var tabbed = new TabbedPalisade(group);
                foreach (var vm in viewModels)
                    palisades[vm.Identifier] = tabbed;
            }

            foreach (PalisadeModelBase concrete in standalone)
            {
                var vm = PalisadeFactory.CreateViewModel(concrete);
                if (vm == null) continue;
                var window = PalisadeFactory.CreateWindow(vm);
                palisades.Add(concrete.Identifier, window);
            }
        }

        private static void RegisterViewModelInGroup(IPalisadeViewModel vm, string? groupId, TabbedPalisade? tabbedWindow)
        {
            if (!string.IsNullOrEmpty(groupId) && tabbedWindow?.DataContext is PalisadeGroup g)
            {
                g.AddMember(vm);
                palisades[vm.Identifier] = tabbedWindow;
                vm.Save();
                g.SelectedMember = vm;
                return;
            }

            var window = PalisadeFactory.CreateWindow(vm);
            palisades.Add(vm.Identifier, window);
            vm.Save();
            window.Show();
        }

        public static void CreatePalisade(int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedPalisade? tabbedWindow = null)
        {
            var model = new StandardPalisadeModel();
            if (x.HasValue) model.FenceX = x.Value;
            if (y.HasValue) model.FenceY = y.Value;
            if (width.HasValue) model.Width = width.Value;
            if (height.HasValue) model.Height = height.Value;
            if (!string.IsNullOrEmpty(groupId))
                model.GroupId = groupId;

            RegisterViewModelInGroup(new PalisadeViewModel(model), groupId, tabbedWindow);
        }

        public static void CreateFolderPortal(string rootPath, string title, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedPalisade? tabbedWindow = null)
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

        public static void ShowCreateFolderPortalDialogForGroup(PalisadeGroup group, TabbedPalisade tabbedWindow)
        {
            var dialog = new CreateFolderPortalDialog();
            SetOwnerSafe(dialog, tabbedWindow);
            if (dialog.ShowDialog() == true)
                CreateFolderPortal(dialog.SelectedPath, dialog.PortalTitle, group.X, group.Y, group.Width, group.Height, group.GroupId, tabbedWindow);
        }

        /// <summary>Ouvre le menu « + » : choix du type de palisade à ajouter comme onglet.</summary>
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

            Add(Strings.AddTabShortcutPalisade, () => AddTabFence(hostWindow));
            Add(Strings.AddTabBrowsePalisade, () => AddTabFolderPortal(hostWindow));
            Add(Strings.AddTabTaskPalisade, () => AddTabTaskPalisade(hostWindow));
            Add(Strings.AddTabCalendarPalisade, () => AddTabCalendarPalisade(hostWindow));
            Add(Strings.AddTabMailPalisade, () => AddTabMailPalisade(hostWindow));

            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private static void AddTabFence(Window hostWindow)
        {
            if (hostWindow is TabbedPalisade tabbed && tabbed.DataContext is PalisadeGroup g)
            {
                CreatePalisade(g.X, g.Y, g.Width, g.Height, g.GroupId, tabbed);
                return;
            }

            if (hostWindow.DataContext is not IPalisadeViewModel existing)
                return;

            var model = new StandardPalisadeModel
            {
                FenceX = existing.FenceX,
                FenceY = existing.FenceY,
                Width = existing.Width,
                Height = existing.Height,
            };
            var vm = new PalisadeViewModel(model);
            MergeStandaloneIntoTabbedWithNewMember(hostWindow, existing, vm);
        }

        // Squelette commun aux ajouts d'onglet passant par un dialogue de création : si l'hôte est déjà
        // un groupe à onglets, on délègue au dialogue « pour groupe » ; sinon on affiche le dialogue puis
        // on fusionne la palisade autonome existante avec le nouveau membre construit à partir du dialogue.
        private static void AddTabWithDialog<TDialog>(
            Window hostWindow,
            Action<PalisadeGroup, TabbedPalisade> showForGroup,
            Func<TDialog, IPalisadeViewModel, IPalisadeViewModel> buildViewModel)
            where TDialog : Window, new()
        {
            if (hostWindow is TabbedPalisade tabbed && tabbed.DataContext is PalisadeGroup g)
            {
                showForGroup(g, tabbed);
                return;
            }

            var dialog = new TDialog();
            SetOwnerSafe(dialog, hostWindow);
            if (dialog.ShowDialog() != true) return;
            if (hostWindow.DataContext is not IPalisadeViewModel existing) return;

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

        private static void AddTabTaskPalisade(Window hostWindow) =>
            AddTabWithDialog<CreateTaskPalisadeDialog>(hostWindow,
                (g, t) => ShowCreateTaskPalisadeDialog(g, t),
                (dialog, existing) => PalisadeFactory.CreateTaskViewModel(
                    dialog.CalDAVUrl, dialog.Username, dialog.Password,
                    dialog.SelectedTaskListIds ?? new List<string>(), dialog.PalisadeTitle,
                    existing.FenceX, existing.FenceY, existing.Width, existing.Height,
                    dialog.SelectedZimbraAccountId));

        private static void AddTabCalendarPalisade(Window hostWindow) =>
            AddTabWithDialog<CreateCalendarPalisadeDialog>(hostWindow,
                (g, t) => ShowCreateCalendarPalisadeDialog(g, t),
                (dialog, existing) => PalisadeFactory.CreateCalendarViewModel(
                    dialog.CalDAVUrl, dialog.Username, dialog.Password, dialog.SelectedCalendarIds,
                    dialog.PalisadeTitle, dialog.ViewMode, dialog.DaysToShow,
                    existing.FenceX, existing.FenceY, existing.Width, existing.Height,
                    dialog.SelectedZimbraAccountId));

        private static void AddTabMailPalisade(Window hostWindow) =>
            AddTabWithDialog<CreateMailPalisadeDialog>(hostWindow,
                (g, t) => ShowCreateMailPalisadeDialog(g, t),
                (dialog, existing) => PalisadeFactory.CreateMailViewModel(
                    dialog.ImapHost, dialog.ImapPort, dialog.Username, dialog.Password, dialog.SelectedFolders,
                    dialog.PalisadeTitle, dialog.DisplayMode, dialog.PollIntervalMinutes, dialog.WebmailUrl,
                    existing.FenceX, existing.FenceY, existing.Width, existing.Height,
                    dialog.SelectedZimbraAccountId));

        private static void MergeStandaloneIntoTabbedWithNewMember(Window hostWindow, IPalisadeViewModel existing, IPalisadeViewModel vmNew)
        {
            if (!palisades.TryGetValue(existing.Identifier, out var oldWindow) || oldWindow != hostWindow)
                return;

            var gid = Guid.NewGuid().ToString();
            var group = new PalisadeGroup(gid);
            group.AddMember(existing);
            group.AddMember(vmNew);

            palisades.Remove(existing.Identifier);
            oldWindow.Close();

            var tabbed = new TabbedPalisade(group);
            foreach (var m in group.Members)
                palisades[m.Identifier] = tabbed;
            group.SelectedMember = vmNew;
            vmNew.Save();
        }

        internal static void OpenEditDialog(IPalisadeViewModel? vm)
        {
            if (vm == null) return;
            Window? owner = null;
            try { owner = GetWindow(vm.Identifier); }
            catch (KeyNotFoundException) { /* fenêtre non enregistrée : dialogue affiché sans owner */ }
            switch (vm)
            {
                case PalisadeViewModel p:
                    new EditPalisade { DataContext = p, Owner = owner }.ShowDialog();
                    break;
                case FolderPortalViewModel f:
                    new EditFolderPortal { DataContext = f, Owner = owner }.ShowDialog();
                    break;
                case TaskPalisadeViewModel t:
                    new EditTaskPalisade(t) { Owner = owner }.ShowDialog();
                    break;
                case CalendarPalisadeViewModel c:
                    new EditCalendarPalisade(c) { Owner = owner }.ShowDialog();
                    break;
            }
        }

        public static void CreateTaskPalisade(string caldavUrl, string username, string password, List<string> taskListIds, string title, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedPalisade? tabbedWindow = null, Guid? zimbraAccountId = null)
        {
            RegisterViewModelInGroup(PalisadeFactory.CreateTaskViewModel(caldavUrl, username, password, taskListIds, title, x, y, width, height, zimbraAccountId), groupId, tabbedWindow);
        }

        public static void ShowCreateFolderPortalDialog()
        {
            CreateFolderPortalDialog dialog = new();
            if (dialog.ShowDialog() == true)
            {
                CreateFolderPortal(dialog.SelectedPath, dialog.PortalTitle);
            }
        }

        public static void ShowCreateTaskPalisadeDialog(PalisadeGroup? intoGroup = null, TabbedPalisade? tabbedWindow = null, Guid? preselectedZimbraAccountId = null)
        {
            var dialog = new CreateTaskPalisadeDialog(preselectedZimbraAccountId);
            SetOwnerSafe(dialog, tabbedWindow ?? Application.Current.MainWindow);
            if (dialog.ShowDialog() != true) return;
            var listIds = dialog.SelectedTaskListIds ?? new List<string>();
            if (intoGroup != null && tabbedWindow != null)
                CreateTaskPalisade(dialog.CalDAVUrl, dialog.Username, dialog.Password, listIds, dialog.PalisadeTitle, intoGroup.X, intoGroup.Y, intoGroup.Width, intoGroup.Height, intoGroup.GroupId, tabbedWindow, dialog.SelectedZimbraAccountId);
            else
                CreateTaskPalisade(dialog.CalDAVUrl, dialog.Username, dialog.Password, listIds, dialog.PalisadeTitle, zimbraAccountId: dialog.SelectedZimbraAccountId);
        }

        public static void CreateCalendarPalisade(string caldavUrl, string username, string password, List<string> calendarIds, string title, CalendarViewMode viewMode, int daysToShow, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedPalisade? tabbedWindow = null, Guid? zimbraAccountId = null)
        {
            RegisterViewModelInGroup(PalisadeFactory.CreateCalendarViewModel(caldavUrl, username, password, calendarIds, title, viewMode, daysToShow, x, y, width, height, zimbraAccountId), groupId, tabbedWindow);
        }

        public static void ShowCreateCalendarPalisadeDialog(PalisadeGroup? intoGroup = null, TabbedPalisade? tabbedWindow = null, Guid? preselectedZimbraAccountId = null)
        {
            var dialog = new CreateCalendarPalisadeDialog(preselectedZimbraAccountId);
            SetOwnerSafe(dialog, tabbedWindow ?? Application.Current.MainWindow);
            if (dialog.ShowDialog() != true) return;
            if (intoGroup != null && tabbedWindow != null)
                CreateCalendarPalisade(dialog.CalDAVUrl, dialog.Username, dialog.Password, dialog.SelectedCalendarIds, dialog.PalisadeTitle, dialog.ViewMode, dialog.DaysToShow, intoGroup.X, intoGroup.Y, intoGroup.Width, intoGroup.Height, intoGroup.GroupId, tabbedWindow, dialog.SelectedZimbraAccountId);
            else
                CreateCalendarPalisade(dialog.CalDAVUrl, dialog.Username, dialog.Password, dialog.SelectedCalendarIds, dialog.PalisadeTitle, dialog.ViewMode, dialog.DaysToShow, zimbraAccountId: dialog.SelectedZimbraAccountId);
        }

        public static void CreateMailPalisade(string imapHost, int imapPort, string username, string password, List<string> monitoredFolders, string title, MailDisplayMode displayMode, int pollIntervalMinutes, string? webmailUrl = null, int? x = null, int? y = null, int? width = null, int? height = null, string? groupId = null, TabbedPalisade? tabbedWindow = null, Guid? zimbraAccountId = null)
        {
            RegisterViewModelInGroup(PalisadeFactory.CreateMailViewModel(imapHost, imapPort, username, password, monitoredFolders, title, displayMode, pollIntervalMinutes, webmailUrl, x, y, width, height, zimbraAccountId), groupId, tabbedWindow);
        }

        public static void ShowCreateMailPalisadeDialog(PalisadeGroup? intoGroup = null, TabbedPalisade? tabbedWindow = null, Guid? preselectedZimbraAccountId = null)
        {
            var dialog = new CreateMailPalisadeDialog(preselectedZimbraAccountId);
            SetOwnerSafe(dialog, tabbedWindow ?? Application.Current.MainWindow);
            if (dialog.ShowDialog() != true) return;
            if (intoGroup != null && tabbedWindow != null)
                CreateMailPalisade(dialog.ImapHost, dialog.ImapPort, dialog.Username, dialog.Password, dialog.SelectedFolders, dialog.PalisadeTitle, dialog.DisplayMode, dialog.PollIntervalMinutes, dialog.WebmailUrl, intoGroup.X, intoGroup.Y, intoGroup.Width, intoGroup.Height, intoGroup.GroupId, tabbedWindow, dialog.SelectedZimbraAccountId);
            else
                CreateMailPalisade(dialog.ImapHost, dialog.ImapPort, dialog.Username, dialog.Password, dialog.SelectedFolders, dialog.PalisadeTitle, dialog.DisplayMode, dialog.PollIntervalMinutes, dialog.WebmailUrl, zimbraAccountId: dialog.SelectedZimbraAccountId);
        }

        public static void DeletePalisade(string identifier)
        {
            if (!palisades.TryGetValue(identifier, out Window? window)) return;

            if (window is TabbedPalisade tabbed && tabbed.DataContext is PalisadeGroup group)
            {
                var member = group.Members.FirstOrDefault(m => m.Identifier == identifier);
                if (member == null) return;
                (member as IDisposable)?.Dispose();
                member.Delete();
                group.RemoveMember(member);
                palisades.Remove(identifier);

                if (group.Members.Count == 0)
                {
                    tabbed.Close();
                    return;
                }
                if (group.Members.Count == 1)
                {
                    var single = group.Members[0];
                    tabbed.Close();
                    var standalone = PalisadeFactory.CreateWindow(single);
                    palisades[single.Identifier] = standalone;
                    standalone.Show();
                    single.GroupId = null;
                    return;
                }
                foreach (var m in group.Members)
                    palisades[m.Identifier] = tabbed;
                return;
            }

            if (window.DataContext is IPalisadeViewModel vm)
                vm.Delete();
            (window.DataContext as IDisposable)?.Dispose();
            window.Close();
            palisades.Remove(identifier);
        }

        public static Window GetWindow(string identifier)
        {
            palisades.TryGetValue(identifier, out Window? window);
            if (window == null)
            {
                throw new KeyNotFoundException(identifier);
            }
            return window;
        }

        public static Palisade GetPalisade(string identifier)
        {
            var window = GetWindow(identifier);
            if (window is Palisade palisade)
            {
                return palisade;
            }
            throw new KeyNotFoundException(identifier);
        }

        /// <summary>Ferme toutes les palisades et vide le dictionnaire (pour RestoreSnapshot).</summary>
        public static void CloseAllPalisades()
        {
            var windows = palisades.Values.Distinct().ToList();
            palisades.Clear();
            foreach (var w in windows)
            {
                try
                {
                    if (w is TabbedPalisade tabbed && tabbed.DataContext is PalisadeGroup group)
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
                catch (Exception ex) { PalisadeDiagnostics.LogDebug("PalisadesManager.CloseAllPalisades", ex); }
            }
        }

        /// <summary>Recalcule position/taille de toutes les palisades (résolution changée).</summary>
        public static void ApplyRescale(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            const int minW = 200, minH = 100;
            var seen = new HashSet<string>();
            foreach (var kv in palisades.ToList())
            {
                var window = kv.Value;
                if (window is View.TabbedPalisade tabbed && tabbed.DataContext is PalisadeGroup group)
                {
                    foreach (var vm in group.Members)
                    {
                        if (seen.Add(vm.Identifier))
                            RescaleVm(vm, oldWidth, oldHeight, newWidth, newHeight, minW, minH);
                    }
                }
                else if (window.DataContext is IPalisadeViewModel vm && seen.Add(vm.Identifier))
                {
                    RescaleVm(vm, oldWidth, oldHeight, newWidth, newHeight, minW, minH);
                }
            }
        }

        internal static void SetOwnerSafe(Window dialog, Window? owner)
        {
            try { dialog.Owner = owner; }
            catch (InvalidOperationException ex) { PalisadeDiagnostics.LogDebug("PalisadesManager.SetOwnerSafe", ex); }
        }

        private static void RescaleVm(IPalisadeViewModel vm, int oldW, int oldH, int newW, int newH, int minW, int minH)
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
