using System;
using System.Runtime.InteropServices;
using Naultinus.Helpers;

namespace Naultinus.View
{
    /// <summary>
    /// Site COM du navigateur : masque les volets (navigation, commandes, ruban)
    /// et relaie la navigation pour que le portail reste le seul historique.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class ExplorerBrowserSite : ExplorerBrowserInterop.IServiceProvider, ExplorerBrowserInterop.IExplorerPaneVisibility, ExplorerBrowserInterop.IExplorerBrowserEvents
    {
        private static readonly Guid[] HiddenPanes =
        {
            ExplorerBrowserInterop.NavPane,
            ExplorerBrowserInterop.CommandsPane,
            ExplorerBrowserInterop.CommandsOrganizePane,
            ExplorerBrowserInterop.CommandsViewPane,
            ExplorerBrowserInterop.DetailsPane,
            ExplorerBrowserInterop.PreviewPane,
            ExplorerBrowserInterop.QueryPane,
            ExplorerBrowserInterop.AdvancedQueryPane,
            ExplorerBrowserInterop.StatusBarPane,
            ExplorerBrowserInterop.RibbonPane,
        };

        private Func<string, bool>? _allowPath;
        private Action<string>? _navigated;
        private Action? _navigationFailed;
        private ExplorerBrowserInterop.IShellView? _shellView;

        internal Action? ViewReady { get; set; }

        internal void Bind(Func<string, bool> allowPath, Action<string> navigated, Action? navigationFailed)
        {
            _allowPath = allowPath;
            _navigated = navigated;
            _navigationFailed = navigationFailed;
        }

        internal void Clear()
        {
            _allowPath = null;
            _navigated = null;
            _navigationFailed = null;
            ReleaseView();
        }

        internal ExplorerBrowserInterop.IShellView? ShellView => _shellView;

        public int QueryService(ref Guid serviceId, ref Guid interfaceId, out IntPtr instance)
        {
            instance = IntPtr.Zero;
            if (serviceId != ExplorerBrowserInterop.PaneVisibilityId || interfaceId != ExplorerBrowserInterop.PaneVisibilityId)
                return ExplorerBrowserInterop.NoInterface;

            instance = Marshal.GetComInterfaceForObject(this, typeof(ExplorerBrowserInterop.IExplorerPaneVisibility));
            return ExplorerBrowserInterop.Ok;
        }

        public int GetPaneState(ref Guid paneId, out uint state)
        {
            state = 0;
            for (int i = 0; i < HiddenPanes.Length; i++)
            {
                if (paneId == HiddenPanes[i])
                {
                    state = ExplorerBrowserInterop.PaneForceOff;
                    break;
                }
            }

            return ExplorerBrowserInterop.Ok;
        }

        public int OnNavigationPending(IntPtr folderPidl)
        {
            try
            {
                string? path = ShellPathResolver.PathFromPidl(folderPidl);
                if (string.IsNullOrEmpty(path))
                    return ExplorerBrowserInterop.AccessDenied;

                Func<string, bool>? allow = _allowPath;
                if (allow == null || !allow(path))
                    return ExplorerBrowserInterop.AccessDenied;

                _navigated?.Invoke(path);
                return ExplorerBrowserInterop.Ok;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Navigation shell refusée.", ex);
                return ExplorerBrowserInterop.Fail;
            }
        }

        public int OnViewCreated(object shellView)
        {
            try
            {
                ReleaseView();
                if (shellView == null)
                    return ExplorerBrowserInterop.Ok;

                IntPtr unknown = Marshal.GetIUnknownForObject(shellView);
                try
                {
                    _shellView = (ExplorerBrowserInterop.IShellView)Marshal.GetTypedObjectForIUnknown(
                        unknown,
                        typeof(ExplorerBrowserInterop.IShellView));
                }
                finally
                {
                    Marshal.Release(unknown);
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Vue shell créée, raccourcis clavier indisponibles.", ex);
            }

            try
            {
                ViewReady?.Invoke();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ExplorerBrowserSite.ViewReady", ex);
            }

            return ExplorerBrowserInterop.Ok;
        }

        public int OnNavigationComplete(IntPtr folderPidl)
        {
            try
            {
                string? path = ShellPathResolver.PathFromPidl(folderPidl);
                if (!string.IsNullOrEmpty(path))
                    _navigated?.Invoke(path);
                ViewReady?.Invoke();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Fin de navigation shell ignorée.", ex);
            }

            return ExplorerBrowserInterop.Ok;
        }

        public int OnNavigationFailed(IntPtr folderPidl)
        {
            try
            {
                _navigationFailed?.Invoke();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("FolderPortal", "Échec de navigation shell ignoré.", ex);
            }

            return ExplorerBrowserInterop.Ok;
        }

        private void ReleaseView()
        {
            if (_shellView == null)
                return;
            try
            {
                Marshal.ReleaseComObject(_shellView);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ExplorerBrowserSite.ReleaseView", ex);
            }

            _shellView = null;
        }
    }
}
