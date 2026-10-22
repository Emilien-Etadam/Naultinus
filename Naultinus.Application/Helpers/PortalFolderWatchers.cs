using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using Naultinus.Helpers.Native;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Surveillance d'un dossier portail : notifications shell + FileSystemWatcher en défense,
    /// avec debounce 500 ms vers le dispatcher UI.
    /// </summary>
    internal sealed class PortalFolderWatchers : IDisposable
    {
        private readonly Dispatcher _uiDispatcher;
        private readonly Func<bool> _isDisposed;
        private readonly Action _onContentsChanged;
        private readonly Action<string> _onUnreachable;

        private FileSystemWatcher? _watcher;
        private ShellChangeNotifyWatcher? _shellWatcher;
        private string? _watchedPath;
        private Timer? _fsDebounceTimer;
        private readonly object _fsTimerLock = new object();
        private bool _disposed;

        public PortalFolderWatchers(
            Dispatcher uiDispatcher,
            Func<bool> isDisposed,
            Action onContentsChanged,
            Action<string> onUnreachable)
        {
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _onContentsChanged = onContentsChanged ?? throw new ArgumentNullException(nameof(onContentsChanged));
            _onUnreachable = onUnreachable ?? throw new ArgumentNullException(nameof(onUnreachable));
        }

        public string? WatchedPath => _watchedPath;

        public void Setup(string? path)
        {
            if (_disposed)
                return;

            if (string.IsNullOrEmpty(path) || !PortalFolderAccess.TryExists(path, out _))
            {
                NaultinusDiagnostics.Log(
                    "FolderPortal.SetupWatcher",
                    "path not found or inaccessible: " + (path ?? "(null)"));
                Stop();
                return;
            }

            // Recharger le même dossier ne doit pas détruire les watchers : le rafraîchissement
            // est souvent déclenché par leur propre callback.
            bool samePath = PortalPathGuard.AreSame(_watchedPath, path);
            bool shellActive = samePath && _shellWatcher != null && _shellWatcher.IsActive;
            bool fswActive = samePath && _watcher != null;
            bool wantFsw = !PortalFolderAccess.IsShellNamespacePath(path);

            // Shell + FSW en parallèle sur un dossier FS : SHChangeNotifyRegister peut réussir
            // sans livrer d'événements (pompe de messages minimale / agents CI), d'où le FSW
            // en défense — auparavant exclusif, ce qui faisait échouer le test FileWatcher.
            if (shellActive && (!wantFsw || fswActive))
                return;

            if (!samePath)
                Stop();

            if (!shellActive)
            {
                _shellWatcher ??= new ShellChangeNotifyWatcher(_uiDispatcher);
                _shellWatcher.TryStart(path, RaiseContentsChanged);
            }

            if (wantFsw && _watcher == null)
                TryStartFileSystemWatcher(path);

            _watchedPath = path;

            if ((_shellWatcher == null || !_shellWatcher.IsActive) && _watcher == null)
                _onUnreachable(path);
        }

        public void Stop()
        {
            try
            {
                _shellWatcher?.Stop();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "Stop shell watcher", ex);
            }

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
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
            try
            {
                _shellWatcher?.Dispose();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Dispose watchers", ex);
            }

            _shellWatcher = null;
        }

        private void TryStartFileSystemWatcher(string path)
        {
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
                w.Error += (_, args) => OnWatcherError(path, args.GetException());
                w.EnableRaisingEvents = true;
                _watcher = w;
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "FileSystemWatcher : " + path);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "FileSystemWatcher init failed.", ex);
                _watcher = null;
            }
        }

        private void OnWatcherError(string path, Exception? exception)
        {
            NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "FileSystemWatcher error on " + path, exception);
            _uiDispatcher.BeginInvoke(() =>
            {
                if (_disposed || _isDisposed())
                    return;
                _onUnreachable(path);
                Stop();
            });
        }

        private void OnFileSystemEvent()
        {
            lock (_fsTimerLock)
            {
                if (_disposed || _isDisposed())
                    return;
                if (_fsDebounceTimer == null)
                    _fsDebounceTimer = new Timer(_ => OnFsDebounceFire(), null, 500, Timeout.Infinite);
                else
                    _fsDebounceTimer.Change(500, Timeout.Infinite);
            }
        }

        private void OnFsDebounceFire()
        {
            try
            {
                _uiDispatcher.BeginInvoke(new Action(RaiseContentsChanged));
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal.SetupWatcher", "Refresh after filesystem event.", ex);
            }
        }

        private void RaiseContentsChanged()
        {
            if (_disposed || _isDisposed())
                return;
            _onContentsChanged();
        }
    }
}
