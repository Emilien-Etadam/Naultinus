using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
        private string? _requestedPath;
        private IntPtr _hostWindow;

        internal ExplorerBrowserHost()
        {
            Focusable = true;
            _site.ViewReady = () => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyChrome));
            PortalShellChrome.Changed += ApplyChrome;
        }

        internal bool IsReady => _browser != null;

        internal void Bind(Func<string, bool> allowPath, Action<string> navigated, Action? navigationFailed)
        {
            _site.Bind(allowPath, navigated, navigationFailed);
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
            _requestedPath = path;
            return TryBrowseRequested();
        }

        internal void Fit()
        {
            FitToHost();
        }

        private bool TryBrowseRequested()
        {
            if (_browser == null || string.IsNullOrEmpty(_requestedPath) || !OperatingSystem.IsWindows())
                return false;

            string path = _requestedPath;
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
                FitToHost();
                ApplyChrome();
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

        /// <summary>
        /// Navigue vers le dossier demandé si la vue shell existe et n'affiche pas déjà ce chemin.
        /// </summary>
        internal void NavigateToRequestedFolder()
        {
            if (string.IsNullOrEmpty(_requestedPath) || IsShowing(_requestedPath))
                return;

            TryBrowseRequested();
        }

        internal void RememberShown(string path)
        {
            _shownPath = path;
        }

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            ResolveInitialSize(parent.Handle, out int width, out int height);
            IntPtr window = ExplorerBrowserInterop.CreateWindowExW(
                0,
                "Static",
                string.Empty,
                ExplorerBrowserInterop.ChildWindowStyle,
                0,
                0,
                width,
                height,
                parent.Handle,
                IntPtr.Zero,
                ExplorerBrowserInterop.GetModuleHandleW(null),
                IntPtr.Zero);

            if (window == IntPtr.Zero)
                throw new InvalidOperationException("Impossible de créer la fenêtre hôte de la vue shell.");

            _hostWindow = window;

            if (OperatingSystem.IsWindows())
                TryCreateBrowser(window);

            return new HandleRef(this, window);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _hostWindow = IntPtr.Zero;
            TearDownBrowser();
            if (hwnd.Handle != IntPtr.Zero)
                ExplorerBrowserInterop.DestroyWindow(hwnd.Handle);
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            FitToHost();
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
            {
                PortalShellChrome.Changed -= ApplyChrome;
                _site.ViewReady = null;
                TearDownBrowser();
            }

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
                    Options = ExplorerBrowserInterop.FolderHeaderOnlyInDetails,
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

                FitToHost();
                ApplyChrome();
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

        private void ResolveInitialSize(IntPtr parent, out int width, out int height)
        {
            width = Math.Max(1, (int)ActualWidth);
            height = Math.Max(1, (int)ActualHeight);
            if (parent == IntPtr.Zero)
                return;
            if (!ExplorerBrowserInterop.GetClientRect(parent, out ExplorerBrowserInterop.NativeRect client))
                return;

            int clientWidth = client.Right - client.Left;
            int clientHeight = client.Bottom - client.Top;
            if (clientWidth > width)
                width = clientWidth;
            if (clientHeight > height)
                height = clientHeight;
        }

        private void ApplyChrome()
        {
            if (_hostWindow == IntPtr.Zero)
                return;

            PortalShellChrome.Apply(_hostWindow, _site.ShellView);
        }

        private void FitToHost()
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
