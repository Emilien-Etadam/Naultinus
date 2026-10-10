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
using System.Threading;
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
        private FileSystemWatcher? _watcher;
        private string? _watchedPath;
        private System.Threading.Timer? _fsDebounceTimer;
        private readonly object _fsTimerLock = new object();
        private bool _disposed;
        private PortalSortField _sortField = PortalSortField.Name;
        private FolderPortalItem? _selectionAnchor;
        /// <summary>Dispatcher UI capturé à la construction : le debounce timer s’exécute sur le pool de threads,
        /// où <see cref="Dispatcher.CurrentDispatcher"/> n’est pas le dispatcher WPF de l’application.</summary>
        private readonly Dispatcher _uiDispatcher;

        public string RootPath
        {
            get => _model.RootPath;
            set { _model.RootPath = value; OnPropertyChanged(); Save(); }
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
                SetupWatcher(CurrentPath);
            }
        }

        /// <summary>Vrai pendant l'adoption d'un chemin venant de la vue hébergée (pas de renvoi).</summary>
        private bool _adoptingShellPath;

        public bool IsAdoptingShellPath => _adoptingShellPath;

        /// <summary>
        /// Enregistre le dossier que la vue d'éléments du shell affiche réellement, sans renvoyer de
        /// navigation à la vue. Garde la barre de chemin et la flèche « remonter » justes quand
        /// l'utilisateur double-clique un dossier.
        /// </summary>
        public void AdoptShellPath(string path)
        {
            if (string.IsNullOrEmpty(path) || string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase))
                return;

            _adoptingShellPath = true;
            try
            {
                CurrentPath = path;
            }
            finally
            {
                _adoptingShellPath = false;
            }
        }

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
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public bool CanNavigateBack
        {
            get
            {
                if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(CurrentPath))
                    return false;
                return !PortalPathGuard.AreSame(RootPath, CurrentPath);
            }
        }

        public FolderPortalViewModel() : this(new FolderPortalModel { Name = Strings.FolderDefaultName })
        { }

        public FolderPortalViewModel(FolderPortalModel model) : base(model)
        {
            _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _model = model;
            _items = new ObservableCollection<FolderPortalItem>();
            _breadcrumb = "";
            _currentFolderName = "";
            _errorMessage = "";

            CleanupLegacyIcons();

            if (!TryLoadContained(model.CurrentPath))
                TryLoadContained(model.RootPath);

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
                if (!CanNavigateBack) return;
                string? parent = Directory.GetParent(CurrentPath)?.FullName;
                if (parent != null && PortalPathGuard.IsAllowed(RootPath, parent))
                    LoadFolder(parent);
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

        public void LoadFolder(string path)
        {
            ErrorMessage = "";

            if (!PortalPathGuard.IsAllowed(RootPath, path))
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.AccessDeniedFormat, path);
                return;
            }

            if (!Directory.Exists(path))
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.FolderNotFoundFormat, path);
                Items.Clear();
                return;
            }

            try
            {
                var found = new List<FolderPortalItem>();
                string iconsDir = AppPaths.GetNaultinusIconsDirectory(Identifier);

                foreach (string dir in Directory.GetDirectories(path))
                {
                    if (IsHiddenOrSystemEntry(dir))
                        continue;
                    string dirName = Path.GetFileName(dir);
                    string iconPath = AppPaths.GetOrCreateIcon(dir, "folder_", iconsDir);
                    found.Add(new FolderPortalItem(dirName, dir, true, iconPath) { LastWriteUtc = LastWriteUtc(dir) });
                }

                foreach (string file in Directory.GetFiles(path))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("~$", StringComparison.Ordinal) || IsHiddenOrSystemEntry(file))
                        continue;
                    string iconPath = AppPaths.GetOrCreateIcon(file, "file_", iconsDir);
                    found.Add(new FolderPortalItem(fileName, file, false, iconPath) { LastWriteUtc = LastWriteUtc(file) });
                }

                _selectionAnchor = null;
                Items = new ObservableCollection<FolderPortalItem>(PortalItemSort.Order(found, _sortField));
                CurrentPath = path;
                OnPropertyChanged(nameof(CanNavigateBack));
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

        private static bool IsHiddenOrSystemEntry(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0;
            }
            catch (Exception ex) { NaultinusDiagnostics.LogDebug("FolderPortal.IsHiddenOrSystemEntry", ex); return false; }
        }

        private void SetupWatcher(string? path)
        {
            // Recharger le même dossier ne doit pas détruire le watcher : le rafraîchissement
            // est souvent déclenché par son propre callback.
            if (_watcher != null && PortalPathGuard.AreSame(_watchedPath, path))
                return;

            try
            {
                _watcher?.Dispose();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "Dispose watcher", ex);
            }

            _watcher = null;
            _watchedPath = null;

            lock (_fsTimerLock)
            {
                try
                {
                    _fsDebounceTimer?.Dispose();
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "Dispose debounce timer", ex);
                }

                _fsDebounceTimer = null;
            }

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                NaultinusDiagnostics.Log(
                    "FolderPortal.SetupWatcher",
                    "path not found or inaccessible: " + (path ?? "(null)"));
                return;
            }

            try
            {
                var w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                };
                w.Created += (_, _) => OnFileSystemEvent();
                w.Deleted += (_, _) => OnFileSystemEvent();
                w.Renamed += (_, _) => OnFileSystemEvent();
                w.Changed += (_, _) => OnFileSystemEvent();
                w.EnableRaisingEvents = true;
                _watcher = w;
                _watchedPath = path;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "FileSystemWatcher init failed.", ex);
                _watcher = null;
            }
        }

        private void OnFileSystemEvent()
        {
            lock (_fsTimerLock)
            {
                if (_disposed)
                    return;
                if (_fsDebounceTimer == null)
                    _fsDebounceTimer = new System.Threading.Timer(_ => OnFsDebounceFire(), null, 500, Timeout.Infinite);
                else
                    _fsDebounceTimer.Change(500, Timeout.Infinite);
            }
        }

        private void OnFsDebounceFire()
        {
            try
            {
                // BeginInvoke : le thread du timer ne doit pas attendre le thread UI.
                // Un Invoke synchrone pendant que l'UI recrée le watcher (ou résout le chemin)
                // peut bloquer les deux côtés, et le processus de test ne se termine plus.
                _uiDispatcher.BeginInvoke(() =>
                {
                    if (_disposed)
                        return;
                    if (!string.IsNullOrEmpty(CurrentPath)) LoadFolder(CurrentPath);
                });
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "Refresh after filesystem event.", ex);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _watchedPath = null;
            try
            {
                _watcher?.Dispose();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Dispose watcher (Dispose)", ex);
            }

            _watcher = null;
            lock (_fsTimerLock)
            {
                _fsDebounceTimer?.Dispose();
                _fsDebounceTimer = null;
            }

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

        public bool CanStartDrag(IDragInfo dragInfo) => dragInfo.SourceItem is FolderPortalItem;

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
            if (dropInfo.Data is DataObject dataObject && dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                bool isCopy = (dropInfo.KeyStates & DragDropKeyStates.ControlKey) != 0;
                dropInfo.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                return;
            }
            if (dropInfo.Data is IDataObject iDataObject && iDataObject.GetDataPresent(DataFormats.FileDrop))
            {
                bool isCopy = (dropInfo.KeyStates & DragDropKeyStates.ControlKey) != 0;
                dropInfo.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                return;
            }
            if (dropInfo.Data is FolderPortalItem)
            {
                bool isCopy = (dropInfo.KeyStates & DragDropKeyStates.ControlKey) != 0;
                dropInfo.Effects = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            }
        }

        public void Drop(IDropInfo dropInfo)
        {
            string[]? files = null;
            if (dropInfo.Data is DataObject dataObject && dataObject.GetDataPresent(DataFormats.FileDrop))
                files = dataObject.GetData(DataFormats.FileDrop) as string[];
            else if (dropInfo.Data is IDataObject iDataObject && iDataObject.GetDataPresent(DataFormats.FileDrop))
                files = iDataObject.GetData(DataFormats.FileDrop) as string[];
            else if (dropInfo.Data is FolderPortalItem item && !string.IsNullOrEmpty(item.FullPath))
                files = new[] { item.FullPath };

            if (files == null || files.Length == 0)
                return;

            bool isCopy = (dropInfo.KeyStates & DragDropKeyStates.ControlKey) != 0;
            ImportFileSystemPaths(files, isCopy);
        }

        public void ImportExplorerFileDrop(string[] files, bool isCopy)
        {
            if (files == null || files.Length == 0)
                return;
            ImportFileSystemPaths(files, isCopy);
        }

        private void ImportFileSystemPaths(string[] files, bool isCopy)
        {
            string targetDir = CurrentPath;
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir) || !PortalPathGuard.IsAllowed(RootPath, targetDir))
                return;

            foreach (string sourcePath in files)
            {
                try
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(targetDir, fileName);

                    if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (File.Exists(destPath) || Directory.Exists(destPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int counter = 1;
                        do
                        {
                            destPath = Path.Combine(targetDir, $"{nameWithoutExt} ({counter}){ext}");
                            counter++;
                        }
                        while (File.Exists(destPath) || Directory.Exists(destPath));
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        if (isCopy)
                            AppPaths.CopyDirectory(sourcePath, destPath);
                        else
                            Directory.Move(sourcePath, destPath);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        if (isCopy)
                            File.Copy(sourcePath, destPath);
                        else
                            File.Move(sourcePath, destPath);
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.FileOperationFailedFormat, isCopy ? Strings.CopyVerb : Strings.MoveVerb, Path.GetFileName(sourcePath), ex.Message);
                }
            }

            if (!string.IsNullOrEmpty(CurrentPath)) LoadFolder(CurrentPath);
        }

        #endregion

        private void SortBy(PortalSortField field)
        {
            _sortField = field;
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
