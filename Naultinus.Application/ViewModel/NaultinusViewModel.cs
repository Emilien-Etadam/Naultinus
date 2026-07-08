using GongSolutions.Wpf.DragDrop;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.View;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Naultinus.ViewModel
{
    public class NaultinusViewModel : ViewModelBase, IDropTarget, IDragSource
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        private readonly StandardNaultinusModel _model;
        private Shortcut? _selectedShortcut;
        private bool _dragWasSameListReorder;

        public NaultinusViewModel() : this(new StandardNaultinusModel()) { }

        public NaultinusViewModel(StandardNaultinusModel model) : base(model)
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
                if (PDirectory.IsUnderDesktop(filePath))
                    desktopLinkToDelete = filePath;
            }
            else if (ext == ".url")
            {
                var built = UrlShortcut.BuildFrom(filePath, Identifier);
                if (built == null)
                    return false;
                newSc = built;
                if (PDirectory.IsUnderDesktop(filePath))
                    desktopLinkToDelete = filePath;
            }
            else
            {
                string uriPath = filePath;
                if (PDirectory.IsUnderDesktop(filePath))
                {
                    string imported = PDirectory.GetNaultinusImportedDirectory(Identifier);
                    uriPath = PDirectory.AllocateUniqueFilePath(filePath, imported);
                    var probe = new LnkShortcut
                    {
                        Name = Path.GetFileNameWithoutExtension(uriPath),
                        UriOrFileAction = uriPath,
                        IconPath = string.Empty,
                    };
                    if (ContainsShortcutWithSameTarget(probe))
                        return false;
                    PDirectory.MoveRobust(filePath, uriPath, isDirectory: false);
                }

                newSc = new LnkShortcut
                {
                    Name = Path.GetFileNameWithoutExtension(uriPath),
                    UriOrFileAction = uriPath,
                    IconPath = PDirectory.CreateIconPng(uriPath, Identifier),
                };
            }

            if (ContainsShortcutWithSameTarget(newSc))
                return false;

            Shortcuts.Add(newSc);

            if (!string.IsNullOrEmpty(desktopLinkToDelete))
            {
                try { File.Delete(desktopLinkToDelete); }
                catch (Exception ex) { NaultinusDiagnostics.Log("NaultinusViewModel", "Suppression du raccourci bureau importé impossible : " + desktopLinkToDelete, ex); }
            }

            return true;
        }

        private bool TryAddDirectoryShortcut(string dirPath)
        {
            string uriPath = dirPath;
            if (PDirectory.IsUnderDesktop(dirPath))
            {
                string imported = PDirectory.GetNaultinusImportedDirectory(Identifier);
                uriPath = PDirectory.AllocateUniqueDirectoryPath(dirPath, imported);
                var probe = new LnkShortcut
                {
                    Name = new DirectoryInfo(dirPath).Name,
                    UriOrFileAction = uriPath,
                    IconPath = string.Empty,
                };
                if (ContainsShortcutWithSameTarget(probe))
                    return false;
                PDirectory.MoveRobust(dirPath, uriPath, isDirectory: true);
            }

            var newSc = new LnkShortcut
            {
                Name = new DirectoryInfo(uriPath).Name,
                UriOrFileAction = uriPath,
                IconPath = PDirectory.CreateIconPng(uriPath, Identifier),
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

        private static bool ShortcutTargetsEqual(Shortcut a, Shortcut b)
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

        private static bool TryGetFileDropPathsForShortcut(Shortcut sc, [NotNullWhen(true)] out string[]? paths)
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
                    string temp = Path.Combine(Path.GetTempPath(), "NaultinusDrag-" + Guid.NewGuid().ToString("N") + ".url");
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

        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
