using GongSolutions.Wpf.DragDrop;
using Palisades.Helpers;
using Palisades.Properties;
using Palisades.Model;
using Palisades.Services;
using Palisades.View;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Palisades.ViewModel
{
    public class FolderPortalViewModel : ViewModelBase, IDropTarget, IDragSource
    {
        private readonly FolderPortalModel _model;
        private ObservableCollection<FolderPortalItem> _items;
        private string _breadcrumb;
        private string _currentFolderName;
        private string _errorMessage;
        private FileSystemWatcher? _watcher;
        private System.Threading.Timer? _fsDebounceTimer;
        private readonly object _fsTimerLock = new object();
        private bool _disposed;
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
                return !string.Equals(
                    Path.GetFullPath(CurrentPath).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
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

            if (!string.IsNullOrEmpty(model.CurrentPath) && Directory.Exists(model.CurrentPath))
                LoadFolder(model.CurrentPath);
            else if (!string.IsNullOrEmpty(model.RootPath) && Directory.Exists(model.RootPath))
            {
                model.CurrentPath = model.RootPath;
                LoadFolder(model.RootPath);
            }

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
                if (string.IsNullOrEmpty(currentPath)) return;
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
                if (string.IsNullOrEmpty(currentPath)) return;
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

            PasteFromClipboardCommand = new RelayCommand(() =>
            {
                var currentPath = CurrentPath;
                if (string.IsNullOrEmpty(currentPath)) return;
                if (!Clipboard.ContainsFileDropList()) return;
                var files = Clipboard.GetFileDropList();
                if (files == null) return;
                foreach (string? source in files)
                {
                    if (string.IsNullOrEmpty(source)) continue;
                    var destName = Path.GetFileName(source);
                    var dest = Path.Combine(currentPath, destName);
                    try
                    {
                        if (File.Exists(source))
                            File.Copy(source, dest, false);
                        else if (Directory.Exists(source))
                            PDirectory.CopyDirectory(source, dest);
                    }
                    catch (Exception ex) { PalisadeDiagnostics.Log("FolderPortal", "Collage depuis le presse-papiers impossible : " + source, ex); }
                }
                RefreshCommand.Execute(null);
            });

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
                if (parent != null)
                {
                    string rootFull = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar);
                    string parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
                    if (parentFull.Length >= rootFull.Length)
                        LoadFolder(parent);
                }
            });

            OpenInExplorerCommand = new RelayCommand(() =>
            {
                if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = CurrentPath, UseShellExecute = true });
                }
                catch (Exception ex) { PalisadeDiagnostics.Log("FolderPortal", "Ouverture dans l'Explorateur impossible : " + CurrentPath, ex); }
            });

            NavigateToRootCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrEmpty(RootPath) && Directory.Exists(RootPath))
                    LoadFolder(RootPath);
            });

        }

        public void LoadFolder(string path)
        {
            ErrorMessage = "";

            if (!Directory.Exists(path))
            {
                ErrorMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.FolderNotFoundFormat, path);
                Items.Clear();
                return;
            }

            try
            {
                var newItems = new ObservableCollection<FolderPortalItem>();
                string iconsDir = PDirectory.GetPalisadeIconsDirectory(Identifier);
                PDirectory.EnsureExists(iconsDir);

                foreach (string dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    if (IsHiddenOrSystemEntry(dir))
                        continue;
                    string dirName = Path.GetFileName(dir);
                    string iconPath = GetOrCreateIcon(dir, "folder_", iconsDir);
                    newItems.Add(new FolderPortalItem(dirName, dir, true, iconPath));
                }

                foreach (string file in Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("~$", StringComparison.Ordinal) || IsHiddenOrSystemEntry(file))
                        continue;
                    string iconPath = GetOrCreateIcon(file, "file_", iconsDir);
                    newItems.Add(new FolderPortalItem(fileName, file, false, iconPath));
                }

                Items = newItems;
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
            if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(CurrentPath))
            {
                Breadcrumb = "";
                CurrentFolderName = "";
                return;
            }

            string rootFull = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar);
            string currentFull = Path.GetFullPath(CurrentPath).TrimEnd(Path.DirectorySeparatorChar);

            CurrentFolderName = Path.GetFileName(currentFull);
            if (string.IsNullOrEmpty(CurrentFolderName))
                CurrentFolderName = currentFull;

            string rootName = Path.GetFileName(rootFull);
            if (string.IsNullOrEmpty(rootName))
                rootName = rootFull;

            if (string.Equals(rootFull, currentFull, StringComparison.OrdinalIgnoreCase))
                Breadcrumb = rootName;
            else
            {
                string relativePath = currentFull.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar);
                string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
                Breadcrumb = rootName + " > " + string.Join(" > ", parts);
            }
        }

        private static bool IsHiddenOrSystemEntry(string path)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0;
            }
            catch (Exception ex) { PalisadeDiagnostics.LogDebug("FolderPortal.IsHiddenOrSystemEntry", ex); return false; }
        }

        private static string StableHash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash, 0, 8);
        }

        private static string GetOrCreateIcon(string path, string prefix, string iconsDir)
        {
            string iconPath = Path.Combine(iconsDir, prefix + StableHash(path) + ".png");
            if (File.Exists(iconPath))
                return iconPath;
            try
            {
                using Bitmap? icon = IconExtractor.GetFileImageFromPath(path, Helpers.Native.IconSizeEnum.LargeIcon48);
                if (icon != null)
                {
                    using FileStream fileStream = new(iconPath, FileMode.Create);
                    icon.Save(fileStream, ImageFormat.Png);
                    return iconPath;
                }
            }
            catch (Exception ex) { PalisadeDiagnostics.LogDebug("FolderPortal.GetOrCreateIcon", ex); }
            return "";
        }

        private void SetupWatcher(string? path)
        {
            try
            {
                _watcher?.Dispose();
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("FolderPortal.SetupWatcher", "Dispose watcher", ex);
            }

            _watcher = null;

            lock (_fsTimerLock)
            {
                try
                {
                    _fsDebounceTimer?.Dispose();
                }
                catch (Exception ex)
                {
                    PalisadeDiagnostics.Log("FolderPortal.SetupWatcher", "Dispose debounce timer", ex);
                }

                _fsDebounceTimer = null;
            }

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                PalisadeDiagnostics.Log(
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
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("FolderPortal.SetupWatcher", "FileSystemWatcher init failed.", ex);
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
                _uiDispatcher.Invoke(() =>
                {
                    if (_disposed)
                        return;
                    if (!string.IsNullOrEmpty(CurrentPath)) LoadFolder(CurrentPath);
                });
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("FolderPortal.SetupWatcher", "Refresh after filesystem event.", ex);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _watcher?.Dispose();
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("FolderPortal", "Dispose watcher (Dispose)", ex);
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
                string iconsDir = PDirectory.GetPalisadeIconsDirectory(Identifier);
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
            catch (Exception ex) { PalisadeDiagnostics.LogDebug("FolderPortal.CleanupLegacyIcons", ex); }
        }

        #region IDragSource
        public void StartDrag(IDragInfo dragInfo)
        {
            if (dragInfo.SourceItem is FolderPortalItem item && !string.IsNullOrEmpty(item.FullPath))
            {
                dragInfo.DataObject = new DataObject(DataFormats.FileDrop, new[] { item.FullPath });
                dragInfo.Effects = DragDropEffects.Copy | DragDropEffects.Move;
            }
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
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
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
                            PDirectory.CopyDirectory(sourcePath, destPath);
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

        #region Commands
        public ICommand CreateNewFolderCommand { get; }
        public ICommand CreateNewFileCommand { get; }
        public ICommand PasteFromClipboardCommand { get; }
        public ICommand NavigateIntoFolderCommand { get; }
        public ICommand NavigateBackCommand { get; }
        public ICommand OpenInExplorerCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NavigateToRootCommand { get; }
        #endregion
    }
}
