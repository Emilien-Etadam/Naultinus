using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Naultinus.Helpers.Native
{
    /// <summary>
    /// Surveille un dossier via <c>SHChangeNotifyRegister</c> (notifications du shell), avec
    /// repli possible côté appelant sur <c>FileSystemWatcher</c>. Fenêtre message-only + debounce.
    /// </summary>
    internal sealed class ShellChangeNotifyWatcher : IDisposable
    {
        private const int WmAppShellChange = 0x8000 + 0x4E; // WM_APP + 'N'
        private static readonly IntPtr HwndMessage = new IntPtr(-3);

        private readonly Dispatcher _dispatcher;
        private HwndSource? _source;
        private IntPtr _folderPidl = IntPtr.Zero;
        private uint _notifyId;
        private Action? _onChanged;
        private System.Threading.Timer? _debounce;
        private readonly object _debounceLock = new object();
        private bool _disposed;
        private string? _path;

        public ShellChangeNotifyWatcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public string? Path => _path;

        public bool IsActive => _notifyId != 0;

        /// <summary>Démarre ou bascule la surveillance sur <paramref name="folderPath"/>.</summary>
        public bool TryStart(string folderPath, Action onChanged)
        {
            if (_disposed || string.IsNullOrWhiteSpace(folderPath) || onChanged == null)
                return false;

            if (IsActive && PortalPathGuard.AreSame(_path, folderPath))
            {
                _onChanged = onChanged;
                return true;
            }

            Stop();

            int hr = ShellBrowserNative.SHParseDisplayName(folderPath, IntPtr.Zero, out IntPtr pidl, 0, out _);
            if (hr != 0 || pidl == IntPtr.Zero)
                return false;

            try
            {
                var parameters = new HwndSourceParameters("NaultinusShellChangeNotify")
                {
                    Width = 0,
                    Height = 0,
                    ParentWindow = HwndMessage,
                    WindowStyle = 0,
                };
                var source = new HwndSource(parameters);
                source.AddHook(WndProc);

                var entries = new[]
                {
                    new ShellBrowserNative.SHChangeNotifyEntry
                    {
                        Pidl = pidl,
                        Recursive = false,
                    },
                };

                uint id = ShellBrowserNative.SHChangeNotifyRegister(
                    source.Handle,
                    ShellBrowserNative.ChangeNotifySources.InterruptLevel
                    | ShellBrowserNative.ChangeNotifySources.ShellLevel
                    | ShellBrowserNative.ChangeNotifySources.NewDelivery,
                    ShellBrowserNative.ShcneDiskEvents,
                    WmAppShellChange,
                    entries.Length,
                    entries);

                if (id == 0)
                {
                    source.RemoveHook(WndProc);
                    source.Dispose();
                    ShellBrowserNative.ILFree(pidl);
                    return false;
                }

                _source = source;
                _folderPidl = pidl;
                _notifyId = id;
                _onChanged = onChanged;
                _path = folderPath;
                NaultinusDiagnostics.Log("ShellChangeNotifyWatcher", "surveillance shell : " + folderPath);
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ShellChangeNotifyWatcher", "démarrage impossible pour " + folderPath, ex);
                ShellBrowserNative.ILFree(pidl);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            lock (_debounceLock)
            {
                try
                {
                    _debounce?.Dispose();
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ShellChangeNotifyWatcher.Stop.debounce", ex);
                }

                _debounce = null;
            }

            if (_notifyId != 0)
            {
                try
                {
                    ShellBrowserNative.SHChangeNotifyDeregister(_notifyId);
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ShellChangeNotifyWatcher.Stop.Deregister", ex);
                }

                _notifyId = 0;
            }

            if (_folderPidl != IntPtr.Zero)
            {
                ShellBrowserNative.ILFree(_folderPidl);
                _folderPidl = IntPtr.Zero;
            }

            if (_source != null)
            {
                try
                {
                    _source.RemoveHook(WndProc);
                    _source.Dispose();
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ShellChangeNotifyWatcher.Stop.HwndSource", ex);
                }

                _source = null;
            }

            _onChanged = null;
            _path = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmAppShellChange)
                return IntPtr.Zero;

            handled = true;
            IntPtr lockHandle = IntPtr.Zero;
            try
            {
                lockHandle = ShellBrowserNative.SHChangeNotification_Lock(
                    wParam,
                    unchecked((uint)lParam.ToInt64()),
                    out _,
                    out _);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellChangeNotifyWatcher.Lock", ex);
            }
            finally
            {
                if (lockHandle != IntPtr.Zero)
                {
                    try
                    {
                        ShellBrowserNative.SHChangeNotification_Unlock(lockHandle);
                    }
                    catch (Exception ex)
                    {
                        NaultinusDiagnostics.LogDebug("ShellChangeNotifyWatcher.Unlock", ex);
                    }
                }
            }

            ScheduleCallback();
            return IntPtr.Zero;
        }

        private void ScheduleCallback()
        {
            lock (_debounceLock)
            {
                if (_disposed)
                    return;
                if (_debounce == null)
                    _debounce = new System.Threading.Timer(_ => Fire(), null, 400, System.Threading.Timeout.Infinite);
                else
                    _debounce.Change(400, System.Threading.Timeout.Infinite);
            }
        }

        private void Fire()
        {
            try
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed)
                        return;
                    _onChanged?.Invoke();
                }));
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellChangeNotifyWatcher.Fire", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }
    }
}
