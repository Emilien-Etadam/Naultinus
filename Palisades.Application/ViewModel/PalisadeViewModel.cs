using GongSolutions.Wpf.DragDrop;
using Palisades.Helpers;
using Palisades.Model;
using Palisades.View;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Palisades.ViewModel
{
    public class PalisadeViewModel : ViewModelBase, IDropTarget, IDragSource
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        private readonly StandardPalisadeModel _model;
        private Shortcut? _selectedShortcut;
        private bool _dragWasSameListReorder;

        public PalisadeViewModel() : this(new StandardPalisadeModel()) { }

        public PalisadeViewModel(StandardPalisadeModel model) : base(model)
        {
            _model = model;
            Shortcuts.CollectionChanged += (_, _) => Save();

            PasteShortcutCommand = new RelayCommand(() =>
            {
                if (!Clipboard.ContainsFileDropList()) return;
                var files = Clipboard.GetFileDropList();
                if (files == null) return;
                foreach (string? filePath in files)
                {
                    if (string.IsNullOrEmpty(filePath)) continue;
                    TryAddShortcutFromExternalPath(filePath);
                }
            });

            DropShortcut = new RelayCommand<object>(p =>
            {
                if (p is DragEventArgs e)
                    OnNativeFileDrop(e);
            });

            SortByNameCommand = new RelayCommand(() =>
            {
                var sorted = Shortcuts.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
                Shortcuts.Clear();
                foreach (var s in sorted) Shortcuts.Add(s);
                Save();
            });

            RemoveSelectedShortcutCommand = new RelayCommand<Shortcut>(sc =>
            {
                if (sc == null) return;
                if (!Shortcuts.Remove(sc))
                    return;
                if (SelectedShortcut == sc)
                    SelectedShortcut = null;
            });

            ClickShortcutCommand = new RelayCommand<Shortcut>(SelectShortcut);
            DeleteSelectionCommand = new RelayCommand(DeleteShortcut);
            DelKeyPressedCommand = new RelayCommand<object>(p =>
            {
                if (p is not System.Windows.Input.KeyEventArgs e)
                    return;
                if (e.Key != System.Windows.Input.Key.Delete && e.Key != System.Windows.Input.Key.Back)
                    return;
                DeleteShortcut();
                e.Handled = true;
            });
        }

        public ICommand PasteShortcutCommand { get; }
        public ICommand DropShortcut { get; }
        public ICommand SortByNameCommand { get; }
        public ICommand RemoveSelectedShortcutCommand { get; }
        public ICommand ClickShortcutCommand { get; }
        public ICommand DeleteSelectionCommand { get; }
        public ICommand DelKeyPressedCommand { get; }

        public ObservableCollection<Shortcut> Shortcuts
        {
            get => _model.Shortcuts;
            set { _model.Shortcuts = value; OnPropertyChanged(); Save(); }
        }

        public Shortcut? SelectedShortcut
        {
            get => _selectedShortcut;
            set { _selectedShortcut = value; OnPropertyChanged(); }
        }

        public ICommand EditPalisadeCommand { get; } = new RelayCommand<PalisadeViewModel>(viewModel =>
        {
            var edit = new EditPalisade
            {
                DataContext = viewModel,
                Owner = PalisadesManager.GetWindow(viewModel.Identifier)
            };
            edit.ShowDialog();
        });

        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is Shortcut)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Move;
                return;
            }

            if (dropInfo.Data is System.Windows.IDataObject dataObject &&
                dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                dropInfo.Effects = DragDropEffects.Copy;
                return;
            }

            dropInfo.Effects = DragDropEffects.None;
        }

        public void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is Shortcut shortcut)
            {
                var sourceList = dropInfo.DragInfo?.SourceCollection as System.Collections.IList;
                bool sameList = sourceList == Shortcuts;
                if (!sameList && sourceList != null && Shortcuts.Any(s => ShortcutTargetsEqual(s, shortcut)))
                    return;

                if (sourceList != null && !sameList)
                    sourceList.Remove(shortcut);
                else if (sameList)
                    Shortcuts.Remove(shortcut);

                int insertIndex = dropInfo.InsertIndex;
                if (insertIndex > Shortcuts.Count)
                    insertIndex = Shortcuts.Count;
                Shortcuts.Insert(insertIndex, shortcut);
                return;
            }

            if (dropInfo.Data is System.Windows.IDataObject dataObj &&
                dataObj.GetDataPresent(DataFormats.FileDrop))
            {
                var droppedFiles = dataObj.GetData(DataFormats.FileDrop) as string[];
                if (droppedFiles == null) return;
                foreach (var filePath in droppedFiles)
                {
                    if (!string.IsNullOrEmpty(filePath))
                        TryAddShortcutFromExternalPath(filePath);
                }

                return;
            }
        }

        public bool TryAddShortcutFromExternalPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            if (Directory.Exists(filePath))
                return TryAddDirectoryShortcut(filePath);

            if (!File.Exists(filePath))
                return false;

            string? desktopLinkToDelete = null;
            Shortcut? newSc;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".lnk")
            {
                var built = LnkShortcut.BuildFrom(filePath, Identifier);
                if (built == null)
                    return false;
                newSc = built;
                if (IsUnderDesktop(filePath))
                    desktopLinkToDelete = filePath;
            }
            else if (ext == ".url")
            {
                var built = UrlShortcut.BuildFrom(filePath, Identifier);
                if (built == null)
                    return false;
                newSc = built;
                if (IsUnderDesktop(filePath))
                    desktopLinkToDelete = filePath;
            }
            else
            {
                string uriPath = filePath;
                if (IsUnderDesktop(filePath))
                {
                    string imported = PDirectory.GetPalisadeImportedDirectory(Identifier);
                    uriPath = AllocateUniqueFilePathInDirectory(filePath, imported);
                    var probe = new LnkShortcut
                    {
                        Name = Path.GetFileNameWithoutExtension(uriPath),
                        UriOrFileAction = uriPath,
                        IconPath = string.Empty,
                    };
                    if (ContainsShortcutWithSameTarget(probe))
                        return false;
                    MovePathRobust(filePath, uriPath, isDirectory: false);
                }

                newSc = new LnkShortcut
                {
                    Name = Path.GetFileNameWithoutExtension(uriPath),
                    UriOrFileAction = uriPath,
                    IconPath = Shortcut.GetIcon(uriPath, Identifier),
                };
            }

            if (ContainsShortcutWithSameTarget(newSc))
                return false;

            Shortcuts.Add(newSc);

            if (!string.IsNullOrEmpty(desktopLinkToDelete))
            {
                try { File.Delete(desktopLinkToDelete); } catch { }
            }

            return true;
        }

        private bool TryAddDirectoryShortcut(string dirPath)
        {
            string uriPath = dirPath;
            if (IsUnderDesktop(dirPath))
            {
                string imported = PDirectory.GetPalisadeImportedDirectory(Identifier);
                uriPath = AllocateUniqueDirectoryPathInParent(dirPath, imported);
                var probe = new LnkShortcut
                {
                    Name = new DirectoryInfo(dirPath).Name,
                    UriOrFileAction = uriPath,
                    IconPath = string.Empty,
                };
                if (ContainsShortcutWithSameTarget(probe))
                    return false;
                MovePathRobust(dirPath, uriPath, isDirectory: true);
            }

            var newSc = new LnkShortcut
            {
                Name = new DirectoryInfo(uriPath).Name,
                UriOrFileAction = uriPath,
                IconPath = Shortcut.GetIcon(uriPath, Identifier),
            };
            if (ContainsShortcutWithSameTarget(newSc))
                return false;

            Shortcuts.Add(newSc);
            return true;
        }

        private void OnNativeFileDrop(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null)
                return;
            foreach (var f in files)
            {
                if (!string.IsNullOrEmpty(f))
                    TryAddShortcutFromExternalPath(f);
            }

            e.Handled = true;
        }

        private bool ContainsShortcutWithSameTarget(Shortcut candidate)
        {
            return Shortcuts.Any(s => ShortcutTargetsEqual(s, candidate));
        }

        private bool ShortcutTargetsEqual(Shortcut a, Shortcut b)
        {
            if (a is UrlShortcut && b is UrlShortcut)
            {
                return string.Equals(
                    (a.UriOrFileAction ?? string.Empty).Trim(),
                    (b.UriOrFileAction ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }

            if (a is UrlShortcut || b is UrlShortcut)
                return false;

            try
            {
                return PathComparer.Equals(
                    Path.GetFullPath(a.UriOrFileAction ?? string.Empty),
                    Path.GetFullPath(b.UriOrFileAction ?? string.Empty));
            }
            catch
            {
                return string.Equals(a.UriOrFileAction, b.UriOrFileAction, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static IEnumerable<string> DesktopRootPaths()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(d))
                yield return d;
            string cd = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(cd))
                yield return cd;
        }

        private static bool IsUnderDesktop(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (var root in DesktopRootPaths())
                {
                    string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (fullPath.Length > fullRoot.Length &&
                        fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (PathComparer.Equals(fullPath, fullRoot))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static string AllocateUniqueFilePathInDirectory(string sourceFile, string destDirectory)
        {
            PDirectory.EnsureExists(destDirectory);
            string name = Path.GetFileName(sourceFile);
            string dest = Path.Combine(destDirectory, name);
            int c = 1;
            string ext = Path.GetExtension(name);
            string baseName = Path.GetFileNameWithoutExtension(name);
            while (File.Exists(dest) || Directory.Exists(dest))
                dest = Path.Combine(destDirectory, $"{baseName} ({c++}){ext}");
            return dest;
        }

        private static string AllocateUniqueDirectoryPathInParent(string sourceDir, string destParent)
        {
            PDirectory.EnsureExists(destParent);
            string name = new DirectoryInfo(sourceDir).Name;
            string dest = Path.Combine(destParent, name);
            int c = 1;
            while (Directory.Exists(dest) || File.Exists(dest))
                dest = Path.Combine(destParent, $"{name} ({c++})");
            return dest;
        }

        private static void MovePathRobust(string source, string dest, bool isDirectory)
        {
            if (isDirectory)
            {
                try
                {
                    Directory.Move(source, dest);
                }
                catch (IOException)
                {
                    PDirectory.CopyDirectory(source, dest);
                    Directory.Delete(source, recursive: true);
                }
            }
            else
            {
                try
                {
                    File.Move(source, dest);
                }
                catch (IOException)
                {
                    File.Copy(source, dest, overwrite: false);
                    File.Delete(source);
                }
            }
        }


        public void SelectShortcut(Shortcut shortcut)
        {
            if (SelectedShortcut == shortcut)
            {
                SelectedShortcut = null;
                return;
            }

            SelectedShortcut = shortcut;
        }

        public void DeleteShortcut()
        {
            if (SelectedShortcut == null) return;
            var sc = SelectedShortcut;
            Shortcuts.Remove(sc);
            SelectedShortcut = null;
        }

        #region IDragSource

        public void StartDrag(IDragInfo dragInfo)
        {
            _dragWasSameListReorder = false;
            if (dragInfo.SourceItem is not Shortcut sc)
                return;
            if (!TryGetFileDropPathsForShortcut(sc, out var paths))
                return;
            dragInfo.DataObject = new DataObject(DataFormats.FileDrop, paths);
            dragInfo.Effects = DragDropEffects.Copy | DragDropEffects.Move;
        }

        public bool CanStartDrag(IDragInfo dragInfo) =>
            dragInfo.SourceItem is Shortcut sc && TryGetFileDropPathsForShortcut(sc, out _);

        public void Dropped(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo?.SourceCollection != null &&
                ReferenceEquals(dropInfo.DragInfo.SourceCollection, Shortcuts) &&
                dropInfo.TargetCollection != null &&
                ReferenceEquals(dropInfo.TargetCollection, Shortcuts))
            {
                _dragWasSameListReorder = true;
            }
        }

        public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo)
        {
            if (operationResult == DragDropEffects.None)
            {
                _dragWasSameListReorder = false;
                return;
            }

            if (_dragWasSameListReorder)
            {
                _dragWasSameListReorder = false;
                return;
            }

            if (dragInfo.SourceItem is not Shortcut sc)
                return;
            if (!Shortcuts.Contains(sc))
                return;
            Shortcuts.Remove(sc);
            if (SelectedShortcut == sc)
                SelectedShortcut = null;
        }

        public void DragCancelled()
        {
            _dragWasSameListReorder = false;
        }

        public bool TryCatchOccurredException(Exception exception) => false;

        private static bool TryGetFileDropPathsForShortcut(Shortcut sc, out string[]? paths)
        {
            paths = null;
            var t = sc.UriOrFileAction?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(t))
                return false;

            if (t.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string local = new Uri(t).LocalPath;
                    if (File.Exists(local) || Directory.Exists(local))
                    {
                        paths = new[] { Path.GetFullPath(local) };
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            if (File.Exists(t) || Directory.Exists(t))
            {
                paths = new[] { Path.GetFullPath(t) };
                return true;
            }

            if (Uri.TryCreate(t, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    string temp = Path.Combine(Path.GetTempPath(), "PalisadesDrag-" + Guid.NewGuid().ToString("N") + ".url");
                    File.WriteAllText(temp, "[InternetShortcut]\r\nURL=" + t + "\r\n");
                    paths = new[] { temp };
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        #endregion
    }
}
