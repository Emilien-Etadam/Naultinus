using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Naultinus.Helpers;

namespace Naultinus.View
{
    /// <summary>
    /// Fenêtre non stratifiée qui porte <see cref="ExplorerBrowserHost"/>.
    /// Le portail est une fenêtre WPF <c>AllowsTransparency</c> (WS_EX_LAYERED) : un HWND enfant
    /// n'y est pas composé, donc la vue Explorateur resterait vide. Cette surface est un popup
    /// calé sur la zone sous le bandeau, au-dessus de sa fenêtre propriétaire et pas au-dessus des autres.
    /// </summary>
    internal sealed class PortalShellSurface : IDisposable
    {
        private readonly ExplorerBrowserHost _browser = new();
        private readonly Grid _root = new();
        private HwndSource? _source;
        private HwndSource? _ownerSource;
        private IntPtr _ownerHwnd;
        private bool _placing;
        private bool _visible;
        private bool _disposed;
        private int _ownerMoveDepth;
        private IntPtr _fillBrush;
        private PortalShellPlacement.PixelRect _last;

        internal static bool TryGetClientOnScreen(IntPtr window, out PortalShellPlacement.PixelRect rect)
        {
            rect = default;
            if (window == IntPtr.Zero)
                return false;
            if (!ExplorerBrowserInterop.GetClientRect(window, out ExplorerBrowserInterop.NativeRect client))
                return false;

            var origin = default(ExplorerBrowserInterop.NativePoint);
            if (!ExplorerBrowserInterop.ClientToScreen(window, ref origin))
                return false;

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0)
                return false;

            rect = new PortalShellPlacement.PixelRect(origin.X, origin.Y, width, height);
            return true;
        }

        private static void ToDeviceIndependent(IntPtr ownerHwnd, PortalShellPlacement.PixelRect physical, out int x, out int y, out int width, out int height)
        {
            x = physical.X;
            y = physical.Y;
            width = Math.Max(1, physical.Width);
            height = Math.Max(1, physical.Height);
            HwndSource? source = HwndSource.FromHwnd(ownerHwnd);
            if (source?.CompositionTarget == null)
                return;

            Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
            Point origin = fromDevice.Transform(new Point(physical.X, physical.Y));
            x = (int)Math.Round(origin.X);
            y = (int)Math.Round(origin.Y);
            width = Math.Max(1, (int)Math.Round(physical.Width * fromDevice.M11));
            height = Math.Max(1, (int)Math.Round(physical.Height * fromDevice.M22));
        }

        internal PortalShellSurface()
        {
            _browser.HorizontalAlignment = HorizontalAlignment.Stretch;
            _browser.VerticalAlignment = VerticalAlignment.Stretch;
            _root.Children.Add(_browser);
            _root.SizeChanged += (_, _) => _browser.Fit();
            UseThemeFill();
            PortalShellChrome.Changed += OnThemeChanged;
        }

        internal ExplorerBrowserHost Browser => _browser;

        internal void Show(PortalShellPlacement.PixelRect rect, IntPtr ownerHwnd, bool forceStack)
        {
            if (_disposed || ownerHwnd == IntPtr.Zero || rect.Width <= 1 || rect.Height <= 1)
            {
                Hide();
                return;
            }

            Ensure(ownerHwnd, rect);
            if (_source == null)
                return;

            bool deferFit = !_visible || _last.Width != rect.Width || _last.Height != rect.Height;
            if (!forceStack && _visible && rect.SamePlace(_last))
                return;

            _last = rect;
            _visible = true;
            _placing = true;
            try
            {
                ExplorerBrowserInterop.SetWindowPos(
                    _source.Handle,
                    ownerHwnd,
                    rect.X,
                    rect.Y,
                    rect.Width,
                    rect.Height,
                    ExplorerBrowserInterop.SwpNoActivate | ExplorerBrowserInterop.SwpShowWindow | ExplorerBrowserInterop.SwpNoCopyBits);
            }
            finally
            {
                _placing = false;
            }

            _browser.NavigateToRequestedFolder();
            if (!deferFit)
                return;

            _browser.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_disposed)
                    return;

                _browser.Fit();
                _browser.NavigateToRequestedFolder();
            }));
        }

        internal void Hide()
        {
            if (!_visible)
                return;

            _visible = false;
            if (_source == null)
                return;

            ExplorerBrowserInterop.ShowWindow(_source.Handle, ExplorerBrowserInterop.HideCommand);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _visible = false;
            PortalShellChrome.Changed -= OnThemeChanged;
            PortalShellChrome.DeleteFill(_fillBrush);
            _fillBrush = IntPtr.Zero;
            if (_ownerSource != null)
            {
                _ownerSource.RemoveHook(OnOwnerMessage);
                _ownerSource = null;
            }

            if (_source != null)
            {
                _source.RemoveHook(OnSurfaceMessage);
                _source.RootVisual = null;
                _source.Dispose();
                _source = null;
            }

            _browser.Dispose();
        }

        private void Ensure(IntPtr ownerHwnd, PortalShellPlacement.PixelRect rect)
        {
            if (_source != null)
                return;

            ToDeviceIndependent(ownerHwnd, rect, out int x, out int y, out int width, out int height);
            var parameters = new HwndSourceParameters("Naultinus.Portal")
            {
                WindowStyle = ExplorerBrowserInterop.PopupClipStyle,
                ExtendedWindowStyle = ExplorerBrowserInterop.ToolWindowExtendedStyle,
                PositionX = x,
                PositionY = y,
                Width = width,
                Height = height,
                UsesPerPixelOpacity = false,
                RestoreFocusMode = RestoreFocusMode.None,
            };
            _source = new HwndSource(parameters);
            _ownerHwnd = ownerHwnd;
            ExplorerBrowserInterop.SetOwner(_source.Handle, ownerHwnd);
            ExplorerBrowserInterop.ShowWindow(_source.Handle, ExplorerBrowserInterop.HideCommand);
            _source.RootVisual = _root;
            _source.AddHook(OnSurfaceMessage);

            _ownerSource = HwndSource.FromHwnd(ownerHwnd);
            _ownerSource?.AddHook(OnOwnerMessage);
        }

        private IntPtr OnOwnerMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == ExplorerBrowserInterop.WindowPosChanged && _visible && _ownerMoveDepth == 0 && _source != null)
            {
                _ownerMoveDepth++;
                try
                {
                    Show(_last, _ownerHwnd, forceStack: true);
                }
                finally
                {
                    _ownerMoveDepth--;
                }
            }

            return IntPtr.Zero;
        }

        private void OnThemeChanged()
        {
            if (_disposed)
                return;

            UseThemeFill();
        }

        private void UseThemeFill()
        {
            if (!PortalShellChrome.TryGetTheme(out int background, out _))
                return;

            var color = Color.FromRgb((byte)(background & 0xFF), (byte)((background >> 8) & 0xFF), (byte)((background >> 16) & 0xFF));
            _root.Background = new SolidColorBrush(color);
            IntPtr next = PortalShellChrome.CreateFill(background);
            IntPtr previous = _fillBrush;
            _fillBrush = next;
            PortalShellChrome.DeleteFill(previous);
        }

        private IntPtr OnSurfaceMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0014 && _fillBrush != IntPtr.Zero)
            {
                PortalShellChrome.Fill(wParam, hwnd, _fillBrush);
                handled = true;
                return (IntPtr)1;
            }

            if (msg != ExplorerBrowserInterop.WindowPosChanging || _placing)
                return IntPtr.Zero;

            var pos = Marshal.PtrToStructure<ExplorerBrowserInterop.WindowPos>(lParam);
            pos.Flags |= ExplorerBrowserInterop.SwpNoZOrder;
            if (_visible && _last.Width > 1 && _last.Height > 1 && (pos.Flags & ExplorerBrowserInterop.SwpHideWindow) == 0)
            {
                pos.X = _last.X;
                pos.Y = _last.Y;
                pos.Width = _last.Width;
                pos.Height = _last.Height;
                pos.Flags &= ~(ExplorerBrowserInterop.SwpNoMove | ExplorerBrowserInterop.SwpNoSize);
            }

            Marshal.StructureToPtr(pos, lParam, false);
            return IntPtr.Zero;
        }
    }
}
