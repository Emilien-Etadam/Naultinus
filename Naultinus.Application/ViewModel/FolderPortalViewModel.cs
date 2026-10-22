using GongSolutions.Wpf.DragDrop;
using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Model;
using Naultinus.Services;
using Naultinus.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Naultinus.ViewModel
{
    public class FolderPortalViewModel : ViewModelBase, IDropTarget, IDragSource
    {
        private readonly FolderPortalModel _model;
        private readonly ObservableCollection<PortalPathSegment> _breadcrumbSegments = new ObservableCollection<PortalPathSegment>();
        private ObservableCollection<FolderPortalItem> _items;
        private string _breadcrumb;
        private string _currentFolderName;
        private string _errorMessage;
        private bool _disposed;
        private readonly PortalFolderWatchers _folderWatchers;

        /// <summary>
        /// Demande un rafraîchissement de la vue shell (sans réécrire <see cref="CurrentPath"/>).
        /// </summary>
        internal event Action? ShellFolderNeedsRefresh;
        private PortalSortField _sortField = PortalSortField.Name;
        private FolderPortalItem? _selectionAnchor;

        public string RootPath
        {
            get => _model.RootPath;
            set
            {
                if (string.Equals(_model.RootPath, value, StringComparison.OrdinalIgnoreCase))
                    return;
                _model.RootPath = value ?? "";
                OnPropertyChanged();
                Save();
            }
        }

        /// <summary>Change la racine du portail et y navigue, sans recréer la naultinus.</summary>
        public void ChangeRoot(string newRoot)
        {
            if (string.IsNullOrWhiteSpace(newRoot))
                return;
            if (!PortalFolderAccess.TryExists(newRoot, out string? error))
            {
                ErrorMessage = error ?? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.FolderNotFoundFormat,
                    newRoot);
                return;
            }

            RootPath = newRoot;
            _model.NavigationHistory.Clear();
            _model.NavigationIndex = -1;
            LoadFolder(newRoot);
            OnPropertyChanged(nameof(CanNavigateBack));
        }

        public string CurrentPath
        {
            get => _model.CurrentPath;
            set
            {
                _model.CurrentPath = value;
                OnPropertyChanged();
                UpdateBreadcrumb();
                Save();
                _folderWatchers.Setup(CurrentPath);
            }
        }

        /// <summary>Vrai pendant l'adoption d'un chemin venant de la vue hébergée (pas de renvoi).</summary>
        private bool _adoptingShellPath;

        public bool IsAdoptingShellPath => _adoptingShellPath;

        /// <summary>
        /// Vrai quand le corps du portail est rendu par la vue d'éléments du shell. Le portail n'a
        /// alors plus rien à entretenir : la grille WPF est masquée, et la vue se rafraîchit seule
        /// quand le disque bouge (mesuré : elle se redirige elle-même à la notification de
        /// changement, sans attendre le portail). Sans ce drapeau, chaque création de fichier dans
        /// le dossier déclenche une énumération complète et une extraction d'icônes sur le fil
        /// d'interface, pour une grille que personne ne voit.
        /// </summary>
        public bool BodyRenderedByShell { get; set; }

        /// <summary>
        /// Quitte le mode corps shell (fenêtre autonome fermée / fusion en onglets) : la grille WPF
        /// peut à nouveau énumérer le dossier. Recharge si le drapeau était actif ou si la grille est vide.
        /// </summary>
        internal void ReleaseShellBodyRendering()
        {
            bool wasShell = BodyRenderedByShell;
            BodyRenderedByShell = false;
            ForceOpaqueBody = false;
            if (_disposed || string.IsNullOrEmpty(CurrentPath))
                return;
            if (wasShell || Items.Count == 0)
                LoadFolder(CurrentPath, fromHistory: true);
        }

        /// <summary>
        /// Filtre de nom (sous-chaîne). Persisté ; appliqué à la vue shell via
        /// <c>IFolderFilter</c>, ou à la grille WPF à l'énumération.
        /// </summary>
        public string FilterQuery
        {
            get => _model.FilterQuery ?? "";
            set
            {
                string next = value ?? "";
                if (string.Equals(_model.FilterQuery, next, StringComparison.Ordinal))
                    return;
                _model.FilterQuery = next;
                OnPropertyChanged();
                Save();
                if (!BodyRenderedByShell && !string.IsNullOrEmpty(CurrentPath))
                    LoadFolder(CurrentPath, fromHistory: true);
            }
        }

        public bool AllowDoubleClick
        {
            get => _model.AllowDoubleClick;
            set { if (_model.AllowDoubleClick == value) return; _model.AllowDoubleClick = value; OnPropertyChanged(); Save(); }
        }

        public bool AllowDragOut
        {
            get => _model.AllowDragOut;
            set { if (_model.AllowDragOut == value) return; _model.AllowDragOut = value; OnPropertyChanged(); Save(); }
        }

        public bool AllowDrop
        {
            get => _model.AllowDrop;
            set { if (_model.AllowDrop == value) return; _model.AllowDrop = value; OnPropertyChanged(); Save(); }
        }

        public bool AllowContextMenu
        {
            get => _model.AllowContextMenu;
            set { if (_model.AllowContextMenu == value) return; _model.AllowContextMenu = value; OnPropertyChanged(); Save(); }
        }

        public bool AllowKeyboard
        {
            get => _model.AllowKeyboard;
            set { if (_model.AllowKeyboard == value) return; _model.AllowKeyboard = value; OnPropertyChanged(); Save(); }
        }

        public bool HideShortcutOverlay
        {
            get => _model.HideShortcutOverlay;
            set
            {
                if (_model.HideShortcutOverlay == value) return;
                _model.HideShortcutOverlay = value;
                OnPropertyChanged();
                Save();
                if (!BodyRenderedByShell)
                    LoadFolder(CurrentPath);
            }
        }

        public bool HideSyncOverlay
        {
            get => _model.HideSyncOverlay;
            set
            {
                if (_model.HideSyncOverlay == value) return;
                _model.HideSyncOverlay = value;
                OnPropertyChanged();
                Save();
                if (!BodyRenderedByShell)
                    LoadFolder(CurrentPath);
            }
        }

        /// <summary>
        /// Enregistre le dossier que la vue d'éléments du shell affiche réellement, sans renvoyer de
        /// navigation à la vue. Garde la barre de chemin et l'historique justes quand l'utilisateur
        /// double-clique un dossier.
        /// </summary>
        public void AdoptShellPath(string path)
        {
            if (string.IsNullOrEmpty(path) || string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
                return;

            _adoptingShellPath = true;
            try
            {
                PushHistory(path);
                CurrentPath = path;
            }
            finally
            {
                _adoptingShellPath = false;
            }
        }

        /// <summary>Titre du bandeau : dossier affiché, ou le nom du portail s'il n'y en a pas.</summary>
        public string HeaderTitle =>
            string.IsNullOrEmpty(CurrentFolderName) ? Name : CurrentFolderName;

        public ObservableCollection<FolderPortalItem> Items
        {
            get => _items;
            set { _items = value; OnPropertyChanged(); }
        }

        public string Breadcrumb
        {
            get => _breadcrumb;
            set { _breadcrumb = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PortalPathSegment> BreadcrumbSegments => _breadcrumbSegments;

        public string CurrentFolderName
        {
            get => _currentFolderName;
            set
            {
                _currentFolderName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TabBarLabel));
                OnPropertyChanged(nameof(HeaderTitle));
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        /// <summary>Vrai s'il reste une entrée précédente dans l'historique de navigation.</summary>
        public bool CanNavigateBack => _model.NavigationIndex > 0;

        public FolderPortalViewModel() : this(new FolderPortalModel { Name = Strings.FolderDefaultName })
        { }

        public FolderPortalViewModel(FolderPortalModel model) : base(model)
        {
            var uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _model = model;
            _items = new ObservableCollection<FolderPortalItem>();
            _breadcrumb = "";
            _currentFolderName = "";
            _errorMessage = "";
            _folderWatchers = new PortalFolderWatchers(
                uiDispatcher,
                () => _disposed,
                OnFolderContentsChanged,
                path =>
                {
                    ErrorMessage = string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.PortalFolderUnreachableFormat,
                        path);
                });

            CleanupLegacyIcons();

            if (!TryLoadContained(model.CurrentPath))
                TryLoadContained(model.RootPath);

            SeedHistoryFromCurrentPath();
            UpdateBreadcrumb();

            // Assigné avant les commandes qui l'invoquent (CreateNewFolder/File, Paste) pour que
            // le flux nullable le voie initialisé.
            RefreshCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrEmpty(CurrentPath))
                    LoadFolder(CurrentPath);
            });

            CreateNewFolderCommand = new RelayCommand(() =>
            {
                var currentPath = CurrentPath;
                if (string.IsNullOrEmpty(currentPath) || !PortalPathGuard.IsAllowed(RootPath, currentPath)) return;
                var name = Strings.NewFolderName;
                var path = Path.Combine(currentPath, name);
                var counter = 1;
                while (Directory.Exists(path))
                {
                    name = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.NewFolderNameFormat, counter++);
                    path = Path.Combine(currentPath, name);
                }
                Directory.CreateDirectory(path);
                RefreshCommand.Execute(null);
            });

            CreateNewFileCommand = new RelayCommand(() =>
            {
                var currentPath = CurrentPath;
                if (string.IsNullOrEmpty(currentPath) || !PortalPathGuard.IsAllowed(RootPath, currentPath)) return;
                var name = Strings.NewFileName;
                var path = Path.Combine(currentPath, name);
                var counter = 1;
                while (File.Exists(path))
                {
                    name = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.NewFileNameFormat, counter++);
                    path = Path.Combine(currentPath, name);
                }
                File.WriteAllText(path, string.Empty);
                RefreshCommand.Execute(null);
            });

            PasteFromClipboardCommand = new RelayCommand(PasteClipboard);

            SortByNameCommand = new RelayCommand(() => SortBy(PortalSortField.Name));
            SortByTypeCommand = new RelayCommand(() => SortBy(PortalSortField.Type));
            SortByDateCommand = new RelayCommand(() => SortBy(PortalSortField.Date));

            NavigateIntoFolderCommand = new RelayCommand<FolderPortalItem>(item =>
            {
                if (item == null) return;
                if (item.IsDirectory)
                    LoadFolder(item.FullPath);
                else
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = item.FullPath, UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.CannotOpenFileFormat, ex.Message);
                    }
                }
            });

            NavigateBackCommand = new RelayCommand(() =>
            {
                if (!CanNavigateBack)
                    return;

                _model.NavigationIndex--;
                string previous = _model.NavigationHistory[_model.NavigationIndex];
                LoadFolder(previous, fromHistory: true);
            });

            OpenInExplorerCommand = new RelayCommand(() =>
            {
                if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath) || !PortalPathGuard.IsAllowed(RootPath, CurrentPath)) return;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = CurrentPath, UseShellExecute = true });
                }
                catch (Exception ex) { NaultinusDiagnostics.Log("FolderPortal", "Ouverture dans l'Explorateur impossible : " + CurrentPath, ex); }
            });

            NavigateToRootCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrEmpty(RootPath) && Directory.Exists(RootPath))
                    LoadFolder(RootPath);
            });

            NavigateToSegmentCommand = new RelayCommand<PortalPathSegment>(segment =>
            {
                if (segment == null || string.IsNullOrEmpty(segment.Path))
                    return;
                if (!PortalPathGuard.IsAllowed(RootPath, segment.Path))
                    return;
                LoadFolder(segment.Path);
            });
        }

        private bool TryLoadContained(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;
            if (!PortalPathGuard.IsAllowed(_model.RootPath, path))
                return false;
            LoadFolder(path);
            return true;
        }

        public void LoadFolder(string path) => LoadFolder(path, fromHistory: false);

        private void LoadFolder(string path, bool fromHistory)
        {
            ErrorMessage = "";

            if (!PortalPathGuard.IsAllowed(RootPath, path))
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.AccessDeniedFormat, path);
                return;
            }

            if (!PortalFolderAccess.TryExists(path, out string? accessError))
            {
                ErrorMessage = accessError
                    ?? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.FolderNotFoundFormat, path);
                Items.Clear();
                return;
            }

            try
            {
                if (!fromHistory)
                    PushHistory(path);

                if (BodyRenderedByShell || PortalFolderAccess.IsShellNamespacePath(path))
                {
                    // Corps shell, ou racine espace de noms (search-ms:) : pas d'énumération WPF.
                    _selectionAnchor = null;
                    CurrentPath = path;
                    OnPropertyChanged(nameof(CanNavigateBack));
                    return;
                }

                var found = new List<FolderPortalItem>();
                string iconsDir = AppPaths.GetNaultinusIconsDirectory(Identifier);
                var loadingStarted = Stopwatch.StartNew();

                string filter = FilterQuery;
                foreach (string dir in Directory.GetDirectories(path))
                {
                    if (IsHiddenOrSystemEntry(dir))
                        continue;
                    string dirName = Path.GetFileName(dir);
                    if (!MatchesFilter(dirName, filter))
                        continue;
                    string iconPath = AppPaths.GetOrCreateIcon(
                        dir, "folder_", iconsDir, HideShortcutOverlay, HideSyncOverlay);
                    found.Add(new FolderPortalItem(dirName, dir, true, iconPath) { LastWriteUtc = LastWriteUtc(dir) });
                }

                foreach (string file in Directory.GetFiles(path))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("~$", StringComparison.Ordinal) || IsHiddenOrSystemEntry(file))
                        continue;
                    if (!MatchesFilter(fileName, filter))
                        continue;
                    string iconPath = AppPaths.GetOrCreateIcon(
                        file, "file_", iconsDir, HideShortcutOverlay, HideSyncOverlay);
                    found.Add(new FolderPortalItem(fileName, file, false, iconPath) { LastWriteUtc = LastWriteUtc(file) });
                }

                _selectionAnchor = null;
                Items = new ObservableCollection<FolderPortalItem>(PortalItemSort.Order(found, _sortField));
                CurrentPath = path;
                OnPropertyChanged(nameof(CanNavigateBack));

                // Mesure : le chargement d'un dossier se paie sur le fil d'interface (icônes
                // extraites élément par élément). Utile pour juger ce que coûte la grille WPF.
                NaultinusDiagnostics.LogDebug(
                    "FolderPortal.LoadFolder : " + path + " -> " + Items.Count + " éléments en "
                    + loadingStarted.ElapsedMilliseconds + " ms");
            }
            catch (UnauthorizedAccessException)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.AccessDeniedFormat, path);
            }
            catch (Exception ex)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.ErrorGenericFormat, ex.Message);
            }
        }

        public IReadOnlyList<FolderPortalItem> SelectedItems => Items.Where(item => item.IsSelected).ToList();

        public void ClearSelection()
        {
            foreach (FolderPortalItem item in Items)
                item.IsSelected = false;
            _selectionAnchor = null;
        }

        public void SelectItem(FolderPortalItem item, bool control, bool shift)
        {
            int index = Items.IndexOf(item);
            if (index < 0)
                return;

            int anchor = _selectionAnchor == null ? -1 : Items.IndexOf(_selectionAnchor);
            int[] current = Items.Select((entry, position) => entry.IsSelected ? position : -1).Where(position => position >= 0).ToArray();
            PortalSelection.Result result = PortalSelection.Click(Items.Count, current, anchor, index, control, shift);
            for (int i = 0; i < Items.Count; i++)
                Items[i].IsSelected = result.Contains(i);
            _selectionAnchor = result.Anchor >= 0 && result.Anchor < Items.Count ? Items[result.Anchor] : null;
        }

        public void OpenSelection()
        {
            List<FolderPortalItem> selected = SelectedItems.ToList();
            if (selected.Count == 1 && selected[0].IsDirectory)
            {
                LoadFolder(selected[0].FullPath);
                return;
            }

            foreach (FolderPortalItem item in selected)
            {
                if (item.IsDirectory || string.IsNullOrEmpty(item.FullPath))
                    continue;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = item.FullPath, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.CannotOpenFileFormat, ex.Message);
                }
            }
        }

        public void CopySelection() => PlaceSelectionOnClipboard(move: false);

        public void CutSelection() => PlaceSelectionOnClipboard(move: true);

        public bool TryRename(FolderPortalItem item, string requestedName)
        {
            if (item == null || string.IsNullOrEmpty(CurrentPath) || !PortalPathGuard.IsAllowed(RootPath, item.FullPath))
                return false;

            string? target = PortalRename.Target(CurrentPath, item.Name, requestedName, path => File.Exists(path) || Directory.Exists(path));
            if (target == null)
                return false;

            try
            {
                if (item.IsDirectory)
                    Directory.Move(item.FullPath, target);
                else
                    File.Move(item.FullPath, target);
            }
            catch (Exception ex)
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.PortalRenameFailedFormat, item.Name, ex.Message);
                return false;
            }

            LoadFolder(CurrentPath);
            return true;
        }

        public void RecycleSelection()
        {
            string[] paths = SelectedItems.Select(item => item.FullPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            if (paths.Length == 0)
                return;
            if (!PortalRecycle.Send(paths))
                return;
            LoadFolder(CurrentPath);
        }

        public override string TabBarLabel
        {
            get
            {
                if (string.IsNullOrEmpty(CurrentFolderName))
                    return Name;
                return $"{Name} — {CurrentFolderName}";
            }
        }

        /// <summary>Ajoute un chemin à l'historique et tronque la branche « avant ».</summary>
        private void PushHistory(string path)
        {
            string normalized = Path.TrimEndingDirectorySeparator(path.Trim());
            List<string> hist = _model.NavigationHistory;
            int idx = _model.NavigationIndex;

            if (idx >= 0 && idx < hist.Count && PortalPathGuard.AreSame(hist[idx], normalized))
                return;

            if (idx >= 0 && idx < hist.Count - 1)
                hist.RemoveRange(idx + 1, hist.Count - idx - 1);

            hist.Add(normalized);
            _model.NavigationIndex = hist.Count - 1;
        }

        /// <summary>Au chargement : amorce la pile depuis <see cref="CurrentPath"/> si elle est vide.</summary>
        private void SeedHistoryFromCurrentPath()
        {
            if (string.IsNullOrEmpty(CurrentPath))
                return;

            List<string> hist = _model.NavigationHistory;
            if (hist.Count == 0)
            {
                hist.Add(Path.TrimEndingDirectorySeparator(CurrentPath.Trim()));
                _model.NavigationIndex = 0;
                return;
            }

            if (_model.NavigationIndex < 0 || _model.NavigationIndex >= hist.Count)
                _model.NavigationIndex = hist.Count - 1;
        }

        private void UpdateBreadcrumb()
        {
            IReadOnlyList<PortalPathSegment> segments = PortalBreadcrumb.Build(RootPath, CurrentPath);
            _breadcrumbSegments.Clear();
            for (int i = 0; i < segments.Count; i++)
                _breadcrumbSegments.Add(segments[i]);

            if (segments.Count == 0)
            {
                Breadcrumb = "";
                CurrentFolderName = "";
            }
            else
            {
                Breadcrumb = string.Join(" > ", segments.Select(segment => segment.Label));
                CurrentFolderName = segments[segments.Count - 1].Label;
            }

            OnPropertyChanged(nameof(CanNavigateBack));
        }

        private static bool MatchesFilter(string name, string filter)
        {
            return string.IsNullOrEmpty(filter)
                || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHiddenOrSystemEntry(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0;
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("FolderPortal.IsHiddenOrSystemEntry", ex); return false; }
        }

        private void OnFolderContentsChanged()
        {
            if (_disposed)
                return;

            if (BodyRenderedByShell)
            {
                ShellFolderNeedsRefresh?.Invoke();
                return;
            }

            if (!string.IsNullOrEmpty(CurrentPath))
                LoadFolder(CurrentPath, fromHistory: true);
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _folderWatchers.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        private void CleanupLegacyIcons()
        {
            try
            {
                string iconsDir = AppPaths.GetNaultinusIconsDirectory(Identifier);
                if (!Directory.Exists(iconsDir)) return;
                foreach (var file in Directory.GetFiles(iconsDir, "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    // Ancien format : "folder_HHHHHHHH" (15 chars) ou "file_HHHHHHHH" (13 chars) — hash 4 octets
                    if ((name.StartsWith("folder_", StringComparison.Ordinal) && name.Length == 15) ||
                        (name.StartsWith("file_", StringComparison.Ordinal) && name.Length == 13))
                        File.Delete(file);
                }
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("FolderPortal.CleanupLegacyIcons", ex); }
        }

        #region IDragSource
        public void StartDrag(IDragInfo dragInfo)
        {
            if (dragInfo.SourceItem is not FolderPortalItem item || string.IsNullOrEmpty(item.FullPath))
                return;

            if (!item.IsSelected)
                SelectItem(item, control: false, shift: false);

            string[] paths = SelectedItems.Select(selected => selected.FullPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            if (paths.Length == 0)
                paths = new[] { item.FullPath };
            dragInfo.DataObject = new DataObject(DataFormats.FileDrop, paths);
            dragInfo.Effects = DragDropEffects.Copy | DragDropEffects.Move;
        }

        public bool CanStartDrag(IDragInfo dragInfo) =>
            AllowDragOut && dragInfo.SourceItem is FolderPortalItem;

        public void Dropped(IDropInfo dropInfo) { }

        public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo)
        {
            if (operationResult.HasFlag(DragDropEffects.Move))
                if (!string.IsNullOrEmpty(CurrentPath)) LoadFolder(CurrentPath);
        }

        public void DragCancelled() { }

        public bool TryCatchOccurredException(Exception exception) => false;
        #endregion

        #region IDropTarget
        public void DragOver(IDropInfo dropInfo)
        {
            if (!AllowDrop)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            if (PortalDropPayload.LooksLikeFileDrop(dropInfo.Data))
                PortalDropPayload.ApplyFileDropEffects(dropInfo);
        }

        public void Drop(IDropInfo dropInfo)
        {
            if (!AllowDrop)
                return;
            if (!PortalDropPayload.TryGetPaths(dropInfo.Data, out var files))
                return;

            bool isCopy = PortalDragDropKeys.IsCopy(dropInfo.KeyStates);
            ImportFileSystemPaths(files, isCopy, PortalDropPayload.TargetDirectory(dropInfo));
        }

        public void ImportExplorerFileDrop(string[] files, bool isCopy, string? targetDirectory = null)
        {
            if (files == null || files.Length == 0)
                return;
            ImportFileSystemPaths(files, isCopy, targetDirectory);
        }

        private void ImportFileSystemPaths(string[] files, bool isCopy, string? targetDirectory = null)
        {
            string targetDir = string.IsNullOrEmpty(targetDirectory) ? CurrentPath : targetDirectory;
            string? error = PortalFileTransfer.TryImport(RootPath, targetDir, files, isCopy);
            if (error != null)
                ErrorMessage = error;
            if (!string.IsNullOrEmpty(CurrentPath))
                LoadFolder(CurrentPath);
        }

        #endregion

        /// <summary>
        /// Demandé quand le corps est rendu par la vue shell : le portail applique le tri via
        /// <c>IFolderView2</c> plutôt que de réordonner la grille WPF masquée.
        /// </summary>
        internal event Action<PortalSortField>? ShellSortRequested;

        private void SortBy(PortalSortField field)
        {
            _sortField = field;
            if (BodyRenderedByShell)
            {
                ShellSortRequested?.Invoke(field);
                return;
            }

            FolderPortalItem? anchor = _selectionAnchor;
            Items = new ObservableCollection<FolderPortalItem>(PortalItemSort.Order(Items, field));
            _selectionAnchor = anchor != null && Items.Contains(anchor) ? anchor : null;
        }

        private void PlaceSelectionOnClipboard(bool move)
        {
            string[] paths = SelectedItems.Select(item => item.FullPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            if (paths.Length == 0)
                return;

            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, paths);
                data.SetData("Preferred DropEffect", new MemoryStream(PortalClipboard.EffectBytes(move)));
                Clipboard.SetDataObject(data);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Copie vers le presse-papiers impossible.", ex);
            }
        }

        private void PasteClipboard()
        {
            if (string.IsNullOrEmpty(CurrentPath) || !PortalPathGuard.IsAllowed(RootPath, CurrentPath))
                return;

            string[]? files = null;
            bool move = false;
            try
            {
                if (!Clipboard.ContainsFileDropList())
                    return;
                var dropped = Clipboard.GetFileDropList();
                if (dropped == null)
                    return;
                files = dropped.Cast<string>().Where(path => !string.IsNullOrEmpty(path)).ToArray();
                move = ClipboardMove();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Lecture du presse-papiers impossible.", ex);
                return;
            }

            if (files == null || files.Length == 0)
                return;

            ImportFileSystemPaths(files, isCopy: !move);
            if (!move)
                return;

            try
            {
                Clipboard.Clear();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("FolderPortal.Clipboard.Clear", ex);
            }
        }

        private static bool ClipboardMove()
        {
            try
            {
                IDataObject? data = Clipboard.GetDataObject();
                if (data == null || !data.GetDataPresent("Preferred DropEffect"))
                    return false;
                if (data.GetData("Preferred DropEffect") is not MemoryStream stream)
                    return false;
                return PortalClipboard.IsMove(stream.ToArray());
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("FolderPortal.Clipboard", ex);
                return false;
            }
        }

        private static DateTime LastWriteUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("FolderPortal.LastWrite", ex);
                return DateTime.MinValue;
            }
        }

        #region Commands
        public ICommand CreateNewFolderCommand { get; }
        public ICommand CreateNewFileCommand { get; }
        public ICommand PasteFromClipboardCommand { get; }
        public ICommand NavigateIntoFolderCommand { get; }
        public ICommand NavigateBackCommand { get; }
        public ICommand OpenInExplorerCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateToRootCommand { get; }
        public ICommand NavigateToSegmentCommand { get; }
        public ICommand SortByNameCommand { get; }
        public ICommand SortByTypeCommand { get; }
        public ICommand SortByDateCommand { get; }
        #endregion
    }
}
