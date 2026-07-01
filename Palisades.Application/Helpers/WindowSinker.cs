using Palisades.Helpers.Native;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Palisades.Helpers
{
    internal class WindowSinker : IDisposable
    {
        #region Windows API
        private const int WM_WINDOWPOSCHANGING = 0x0046;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static readonly IntPtr HWND_BOTTOM = new(1);
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        #endregion

        #region Attributs

        private readonly Window window;
        private bool disposed;

        #endregion

        internal WindowSinker(Window window)
        {
            this.window = window;

            if (window.IsLoaded)
            {
                OnWindowLoaded(window, null);
            }
            else
            {
                window.Loaded += OnWindowLoaded;
            }

            window.Closing += OnWindowClosing;
        }

        #region Methods

        // Pas de finaliseur : WindowSinker ne détient aucune ressource non managée, et son nettoyage
        // désabonne des événements d'un objet Window à affinité de thread. Cela ne doit donc s'exécuter
        // que sur le thread UI (Dispose est appelé depuis OnAlwaysOnBottomChanged), jamais depuis le
        // thread finaliseur du GC.
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            window.Loaded -= OnWindowLoaded;
            window.Closing -= OnWindowClosing;
            window.PreviewMouseDown -= OnPreviewMouseDown;

            disposed = true;
        }
        #endregion

        #region Event Handlers

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
            uint uFlags);

        private void OnWindowLoaded(object? sender, RoutedEventArgs? e)
        {
            SetWindowPos(new WindowInteropHelper(window).Handle, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(WndProc);

            window.PreviewMouseDown += OnPreviewMouseDown;
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            window.PreviewMouseDown -= OnPreviewMouseDown;

            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.RemoveHook(WndProc);
        }

        /// <summary>
        /// Au clic, passage au premier plan : le hook WM_WINDOWPOSCHANGING impose SWP_NOZORDER et bloque sinon tout remontage.
        /// </summary>
        private void OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            BringWindowToFront();
        }

        private void BringWindowToFront()
        {
            if (!window.IsLoaded)
                return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var source = HwndSource.FromHwnd(hwnd);
            source?.RemoveHook(WndProc);
            try
            {
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
                window.Activate();
            }
            finally
            {
                source?.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING)
            {
                var windowPos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                windowPos.flags |= SWP_NOZORDER;
                Marshal.StructureToPtr(windowPos, lParam, false);
            }

            return IntPtr.Zero;
        }

        #endregion

        #region Attached Properties

        private static readonly DependencyProperty SinkerProperty = DependencyProperty.RegisterAttached(
            "Sinker",
            typeof(WindowSinker),
            typeof(WindowSinker),
            null);

        public static readonly DependencyProperty AlwaysOnBottomProperty = DependencyProperty.RegisterAttached(
            "AlwaysOnBottom",
            typeof(bool),
            typeof(WindowSinker),
            new UIPropertyMetadata(false, OnAlwaysOnBottomChanged));

        public static WindowSinker? GetSinker(DependencyObject d)
        {
            return (WindowSinker?)d.GetValue(SinkerProperty);
        }

        private static void SetSinker(DependencyObject d, WindowSinker? value)
        {
            d.SetValue(SinkerProperty, value);
        }

        public static bool GetAlwaysOnBottom(DependencyObject d)
        {
            return (bool)d.GetValue(AlwaysOnBottomProperty);
        }

        public static void SetAlwaysOnBottom(DependencyObject d, bool value)
        {
            d.SetValue(AlwaysOnBottomProperty, value);
        }

        private static void OnAlwaysOnBottomChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Window window)
            {
                if ((bool)e.NewValue)
                {
                    SetSinker(window, new WindowSinker(window));
                }
                else
                {
                    GetSinker(window)?.Dispose();
                    SetSinker(window, null);
                }
            }
        }

        #endregion
    }

    namespace Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }
    }
}