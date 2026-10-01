using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Naultinus.Helpers;
using Naultinus.ViewModel;

namespace Naultinus.View
{
    /// <summary>
    /// Zone de contenu du portail : la vue dossier du shell suit <see cref="FolderPortalViewModel.CurrentPath"/>.
    /// Le bandeau (retour, racine, fil d'Ariane) reste celui du portail. La vue shell n'est pas un enfant
    /// de la fenêtre stratifiée : elle est calée sur cet emplacement.
    /// </summary>
#pragma warning disable CA1001 // PortalShellSurface est libérée dans Unloaded ; WPF ne dispose pas ce contrôle.
    public partial class PortalShellFolderView : UserControl
    {
        private PortalShellSurface? _surface;
        private FolderPortalViewModel? _portal;
        private Window? _owner;
        private bool _ownerHooked;
        private bool _applyingShellNavigation;
        private bool _placingSurface;

        public PortalShellFolderView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Attach(DataContext as FolderPortalViewModel);
            Loaded += OnLoaded;
            Unloaded += (_, _) => Detach();
            IsVisibleChanged += (_, _) => OnHostVisibilityChanged();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _surface ??= new PortalShellSurface();
            _surface.Browser.Bind(AllowPath, OnShellNavigated);
            _owner = Window.GetWindow(this);
            HookOwner();

            if (_portal == null && DataContext is FolderPortalViewModel portal)
                Attach(portal);
            else
                SyncFromPortal();

            LayoutUpdated -= OnLayoutUpdated;
            LayoutUpdated += OnLayoutUpdated;
            PlaceSurface(forceStack: true);
        }

        private void HookOwner()
        {
            if (_owner == null || _ownerHooked)
                return;

            _owner.LocationChanged += OnOwnerMoved;
            _owner.SizeChanged += OnOwnerMoved;
            _owner.StateChanged += OnOwnerMoved;
            _ownerHooked = true;
        }

        private void UnhookOwner()
        {
            if (_owner == null || !_ownerHooked)
                return;

            _owner.LocationChanged -= OnOwnerMoved;
            _owner.SizeChanged -= OnOwnerMoved;
            _owner.StateChanged -= OnOwnerMoved;
            _ownerHooked = false;
        }

        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            PlaceSurface(forceStack: true);
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            PlaceSurface(forceStack: false);
        }

        private void OnHostVisibilityChanged()
        {
            if (IsVisible)
                SyncFromPortal();
            PlaceSurface(forceStack: false);
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
            LayoutUpdated -= OnLayoutUpdated;
            UnhookOwner();
            _owner = null;
            _surface?.Browser.Unbind();
            if (_portal != null)
                _portal.PropertyChanged -= OnPortalChanged;
            _portal = null;
            _surface?.Dispose();
            _surface = null;
        }

        private void OnPortalChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_applyingShellNavigation || _portal == null)
                return;

            if (e.PropertyName == nameof(FolderPortalViewModel.ErrorMessage))
            {
                PlaceSurface(forceStack: false);
                if (string.IsNullOrEmpty(_portal.ErrorMessage))
                    SyncFromPortal();
                return;
            }

            if (e.PropertyName is nameof(FolderPortalViewModel.CurrentPath) or nameof(FolderPortalViewModel.RootPath))
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
            if (portal == null || _surface == null)
                return;

            if (!PortalPathGuard.IsAllowed(portal.RootPath, path))
            {
                if (!string.IsNullOrEmpty(portal.CurrentPath))
                    _surface.Browser.Browse(portal.CurrentPath);
                return;
            }

            _surface.Browser.RememberShown(path);
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
            if (_applyingShellNavigation || _portal == null || !IsLoaded || !IsVisible || _surface == null)
                return;
            if (!string.IsNullOrEmpty(_portal.ErrorMessage))
                return;

            string path = _portal.CurrentPath;
            if (!PortalPathGuard.IsAllowed(_portal.RootPath, path))
                return;

            ExplorerBrowserHost browser = _surface.Browser;
            if (browser.IsShowing(path))
                browser.RefreshListing();
            else
                browser.Browse(path);
        }

        private void PlaceSurface(bool forceStack)
        {
            if (_surface == null || _placingSurface)
                return;

            _placingSurface = true;
            try
            {
                PlaceSurfaceCore(forceStack);
            }
            finally
            {
                _placingSurface = false;
            }
        }

        private void PlaceSurfaceCore(bool forceStack)
        {
            if (_surface == null)
                return;

            if (!ShouldShowShell() || _owner == null || !TryGetSlot(out PortalShellPlacement.PixelRect slot))
            {
                _surface.Hide();
                return;
            }

            if (TryGetBannerBottom(out int bannerBottom))
            {
                if (PortalShellPlacement.BelowBanner(slot, bannerBottom) is not PortalShellPlacement.PixelRect underBanner)
                {
                    _surface.Hide();
                    return;
                }

                slot = underBanner;
            }

            IntPtr ownerHwnd = new WindowInteropHelper(_owner).Handle;
            if (!PortalShellSurface.TryGetClientOnScreen(ownerHwnd, out PortalShellPlacement.PixelRect ownerClient))
            {
                _surface.Hide();
                return;
            }

            PortalShellPlacement.PixelRect? visible = PortalShellPlacement.Intersection(slot, ownerClient);
            if (visible is not PortalShellPlacement.PixelRect rect)
                _surface.Hide();
            else
                _surface.Show(rect, ownerHwnd, forceStack);
        }

        private bool ShouldShowShell()
        {
            return IsLoaded
                && IsVisible
                && _owner != null
                && _owner.WindowState != WindowState.Minimized
                && _portal != null
                && string.IsNullOrEmpty(_portal.ErrorMessage);
        }

        private bool TryGetSlot(out PortalShellPlacement.PixelRect slot)
        {
            return TryGetElementOnScreen(this, out slot);
        }

        private bool TryGetBannerBottom(out int bottom)
        {
            bottom = 0;
            if (_owner?.FindName("Header") is not FrameworkElement header || header.ActualHeight < 1)
                return false;
            if (PresentationSource.FromVisual(header) is not HwndSource source || source.CompositionTarget == null || _owner == null)
                return false;

            IntPtr hwnd = new WindowInteropHelper(_owner).Handle;
            if (!PortalShellSurface.TryGetClientOnScreen(hwnd, out PortalShellPlacement.PixelRect client))
                return false;

            Point far = header.TransformToAncestor(source.RootVisual).Transform(new Point(0, header.ActualHeight));
            far = source.CompositionTarget.TransformToDevice.Transform(far);
            bottom = client.Y + (int)Math.Ceiling(far.Y);
            return bottom > client.Y;
        }

        private bool TryGetElementOnScreen(FrameworkElement element, out PortalShellPlacement.PixelRect rect)
        {
            rect = default;
            if (!element.IsVisible || element.ActualWidth < 2 || element.ActualHeight < 2 || _owner == null)
                return false;
            if (PresentationSource.FromVisual(element) is not HwndSource source || source.CompositionTarget == null)
                return false;

            IntPtr hwnd = new WindowInteropHelper(_owner).Handle;
            if (!PortalShellSurface.TryGetClientOnScreen(hwnd, out PortalShellPlacement.PixelRect client))
                return false;

            GeneralTransform toRoot = element.TransformToAncestor(source.RootVisual);
            Point origin = source.CompositionTarget.TransformToDevice.Transform(toRoot.Transform(new Point(0, 0)));
            Point far = source.CompositionTarget.TransformToDevice.Transform(toRoot.Transform(new Point(element.ActualWidth, element.ActualHeight)));
            int left = client.X + (int)Math.Floor(Math.Min(origin.X, far.X));
            int top = client.Y + (int)Math.Ceiling(Math.Min(origin.Y, far.Y));
            int right = client.X + (int)Math.Floor(Math.Max(origin.X, far.X));
            int bottom = client.Y + (int)Math.Floor(Math.Max(origin.Y, far.Y));
            int width = right - left;
            int height = bottom - top;
            if (width <= 1 || height <= 1)
                return false;

            rect = new PortalShellPlacement.PixelRect(left, top, width, height);
            return true;
        }
    }
#pragma warning restore CA1001
}
