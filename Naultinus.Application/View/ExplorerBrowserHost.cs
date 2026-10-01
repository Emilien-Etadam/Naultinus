using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Naultinus.Helpers;

namespace Naultinus.View
{
    /// <summary>
    /// Hôtesse HWND de la vue dossier du shell. Les cadres (volet, barre de commandes, ruban)
    /// restent masqués : le bandeau Naultinus est le seul chrome.
    /// </summary>
    internal sealed class ExplorerBrowserHost : HwndHost
    {
        private readonly ExplorerBrowserSite _site = new();
        private ExplorerBrowserInterop.IExplorerBrowser? _browser;
        private IntPtr _eventsPointer;
        private uint _adviseCookie;
        private string? _shownPath;
        private IntPtr _hostWindow;
        private bool _windowReady;

        internal ExplorerBrowserHost()
        {
            Focusable = true;
        }

        internal bool IsReady => _browser != null;

        internal void Bind(Func<string, bool> allowPath, Action<string> navigated)
        {
            _site.Bind(allowPath, navigated);
        }

        internal void Unbind()
        {
            _site.Clear();
        }

        internal bool IsShowing(string path)
        {
            return PortalPathGuard.AreSame(_shownPath, path);
        }

        internal bool Browse(string path)
        {
            if (_browser == null || !OperatingSystem.IsWindows())
                return false;

            IntPtr pidl = ShellPathResolver.ParseFolderPidl(path);
            if (pidl == IntPtr.Zero)
                return false;

            try
            {
                int hr = _browser.BrowseToIDList(pidl, ExplorerBrowserInterop.BrowseNoHistory);
                if (hr != ExplorerBrowserInterop.Ok)
                {
                    NaultinusDiagnostics.Log("FolderPortal", "BrowseToIDList a échoué : " + hr);
                    return false;
                }

                _shownPath = path;
                Fit();
                return true;
            }
            finally
            {
                ShellPathResolver.FreePidl(pidl);
            }
        }

        internal void RefreshListing()
        {
            ExplorerBrowserInterop.IShellView? view = _site.ShellView;
            if (view == null)
                return;
            try
            {
                view.Refresh();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Actualisation de la vue shell impossible.", ex);
            }
        }

        internal void SetWindowVisible(bool visible)
        {
            if (!_windowReady)
                return;

            ExplorerBrowserInterop.ShowWindow(
                _hostWindow,
                visible ? ExplorerBrowserInterop.ShowCommand : ExplorerBrowserInterop.HideCommand);
            if (visible)
                Fit();
        }

        internal void RememberShown(string path)
        {
            _shownPath = path;
        }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            IntPtr window = ExplorerBrowserInterop.CreateWindowExW(
                0,
                "Static",
                string.Empty,
                ExplorerBrowserInterop.ChildWindowStyle,
                0,
                0,
                Math.Max(1, (int)ActualWidth),
                Math.Max(1, (int)ActualHeight),
                parent.Handle,
                IntPtr.Zero,
                ExplorerBrowserInterop.GetModuleHandleW(null),
                IntPtr.Zero);

            if (window == IntPtr.Zero)
                throw new InvalidOperationException("Impossible de créer la fenêtre hôte de la vue shell.");

            _hostWindow = window;
            _windowReady = true;

            if (OperatingSystem.IsWindows())
                TryCreateBrowser(window);

            return new HandleRef(this, window);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _windowReady = false;
            _hostWindow = IntPtr.Zero;
            TearDownBrowser();
            if (hwnd.Handle != IntPtr.Zero)
                ExplorerBrowserInterop.DestroyWindow(hwnd.Handle);
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            Fit();
        }

        protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers)
        {
            if (TryTranslate(ref msg))
                return true;
            return base.TranslateAcceleratorCore(ref msg, modifiers);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                TearDownBrowser();
            base.Dispose(disposing);
        }

        private void TryCreateBrowser(IntPtr window)
        {
            try
            {
                var created = new ExplorerBrowserInterop.ExplorerBrowserClass();
                _browser = (ExplorerBrowserInterop.IExplorerBrowser)(object)created;
                if (_browser is ExplorerBrowserInterop.IObjectWithSite site)
                    site.SetSite(_site);

                ExplorerBrowserInterop.GetClientRect(window, out ExplorerBrowserInterop.NativeRect rect);
                var settings = new ExplorerBrowserInterop.FolderSettings
                {
                    ViewMode = ExplorerBrowserInterop.FolderViewMode.Auto,
                    Options = 0,
                };
                int hr = _browser.Initialize(window, ref rect, settings);
                if (hr != ExplorerBrowserInterop.Ok)
                {
                    NaultinusDiagnostics.Log("FolderPortal", "IExplorerBrowser.Initialize a échoué : " + hr);
                    TearDownBrowser();
                    return;
                }

                _browser.SetOptions(ExplorerBrowserInterop.ExplorerBrowserOptions.NoTravelLog | ExplorerBrowserInterop.ExplorerBrowserOptions.NoBorder);
                _browser.SetPropertyBag("Naultinus.Portal");
                _eventsPointer = Marshal.GetComInterfaceForObject(_site, typeof(ExplorerBrowserInterop.IExplorerBrowserEvents));
                hr = _browser.Advise(_eventsPointer, out _adviseCookie);
                if (hr != ExplorerBrowserInterop.Ok)
                    NaultinusDiagnostics.Log("FolderPortal", "IExplorerBrowser.Advise a échoué : " + hr);

                Fit();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Création de la vue shell impossible.", ex);
                TearDownBrowser();
            }
        }

        private void TearDownBrowser()
        {
            _site.Clear();
            if (_browser != null)
            {
                try
                {
                    if (_adviseCookie != 0)
                    {
                        _browser.Unadvise(_adviseCookie);
                        _adviseCookie = 0;
                    }
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ExplorerBrowserHost.Unadvise", ex);
                }

                try
                {
                    if (_browser is ExplorerBrowserInterop.IObjectWithSite site)
                        site.SetSite(null);
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ExplorerBrowserHost.SetSite", ex);
                }

                try
                {
                    _browser.Destroy();
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ExplorerBrowserHost.Destroy", ex);
                }

                try
                {
                    Marshal.ReleaseComObject(_browser);
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ExplorerBrowserHost.Release", ex);
                }

                _browser = null;
            }

            if (_eventsPointer != IntPtr.Zero)
            {
                Marshal.Release(_eventsPointer);
                _eventsPointer = IntPtr.Zero;
            }

            _shownPath = null;
        }

        private void Fit()
        {
            if (_browser == null || _hostWindow == IntPtr.Zero)
                return;
            if (!ExplorerBrowserInterop.GetClientRect(_hostWindow, out ExplorerBrowserInterop.NativeRect client))
                return;

            var bounds = new ExplorerBrowserInterop.NativeRect
            {
                Left = 0,
                Top = 0,
                Right = client.Right,
                Bottom = client.Bottom,
            };
            IntPtr defer = IntPtr.Zero;
            try
            {
                _browser.SetRect(ref defer, bounds);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ExplorerBrowserHost.SetRect", ex);
            }
        }

        private bool TryTranslate(ref MSG msg)
        {
            ExplorerBrowserInterop.IShellView? view = _site.ShellView;
            if (view == null)
                return false;

            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MSG>());
            try
            {
                Marshal.StructureToPtr(msg, pointer, false);
                return view.TranslateAccelerator(pointer) == ExplorerBrowserInterop.Ok;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ExplorerBrowserHost.TranslateAccelerator", ex);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
