using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Naultinus.Helpers;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Zone de contenu du portail : la vue dossier du shell suit <see cref="FolderPortalViewModel.CurrentPath"/>.
    /// Le bandeau (retour, racine, fil d'Ariane) reste celui du portail.
    /// </summary>
#pragma warning disable CA1001 // ExplorerBrowserHost est un HwndHost enfant : WPF le détruit avec l'arbre visuel.
    public partial class PortalShellFolderView : UserControl
    {
        private readonly ExplorerBrowserHost _browser = new();
        private FolderPortalViewModel? _portal;
        private bool _applyingShellNavigation;

        public PortalShellFolderView()
        {
            InitializeComponent();
            HostRoot.Children.Add(_browser);
            DataContextChanged += (_, _) => Attach(DataContext as FolderPortalViewModel);
            Loaded += (_, _) =>
            {
                _browser.Bind(AllowPath, OnShellNavigated);
                if (_portal == null && DataContext is FolderPortalViewModel portal)
                    Attach(portal);
                else
                    SyncFromPortal();
            };
            Unloaded += (_, _) => Detach();
            IsVisibleChanged += (_, _) => UpdateShellVisibility();
        }

        private void Attach(FolderPortalViewModel? portal)
        {
            if (ReferenceEquals(_portal, portal))
            {
                SyncFromPortal();
                return;
            }

            if (_portal != null)
                _portal.PropertyChanged -= OnPortalChanged;

            _portal = portal;
            if (_portal != null)
                _portal.PropertyChanged += OnPortalChanged;

            SyncFromPortal();
        }

        private void Detach()
        {
            _browser.Unbind();
            if (_portal != null)
                _portal.PropertyChanged -= OnPortalChanged;
            _portal = null;
        }

        private void OnPortalChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_applyingShellNavigation || _portal == null)
                return;

            if (e.PropertyName == nameof(FolderPortalViewModel.ErrorMessage))
            {
                UpdateShellVisibility();
                if (string.IsNullOrEmpty(_portal.ErrorMessage))
                    SyncFromPortal();
                return;
            }

            if (e.PropertyName is nameof(FolderPortalViewModel.CurrentPath) or nameof(FolderPortalViewModel.RootPath))
                SyncFromPortal();
        }

        private void UpdateShellVisibility()
        {
            bool show = IsVisible && _portal != null && string.IsNullOrEmpty(_portal.ErrorMessage);
            _browser.SetWindowVisible(show);
            if (show)
                SyncFromPortal();
        }

        private bool AllowPath(string path)
        {
            FolderPortalViewModel? portal = _portal;
            return portal != null && PortalPathGuard.IsAllowed(portal.RootPath, path);
        }

        private void OnShellNavigated(string path)
        {
            _ = Dispatcher.InvokeAsync(() => ApplyShellNavigation(path));
        }

        private void ApplyShellNavigation(string path)
        {
            FolderPortalViewModel? portal = _portal;
            if (portal == null)
                return;

            if (!PortalPathGuard.IsAllowed(portal.RootPath, path))
            {
                if (!string.IsNullOrEmpty(portal.CurrentPath))
                    _browser.Browse(portal.CurrentPath);
                return;
            }

            _browser.RememberShown(path);
            if (PortalPathGuard.AreSame(portal.CurrentPath, path))
                return;

            _applyingShellNavigation = true;
            try
            {
                portal.LoadFolder(path);
            }
            finally
            {
                _applyingShellNavigation = false;
            }
        }

        private void SyncFromPortal()
        {
            if (_applyingShellNavigation || _portal == null || !IsLoaded || !IsVisible)
                return;
            if (!string.IsNullOrEmpty(_portal.ErrorMessage))
                return;

            string path = _portal.CurrentPath;
            if (!PortalPathGuard.IsAllowed(_portal.RootPath, path))
                return;

            if (_browser.IsShowing(path))
                _browser.RefreshListing();
            else
                _browser.Browse(path);
        }
    }
#pragma warning restore CA1001
}
