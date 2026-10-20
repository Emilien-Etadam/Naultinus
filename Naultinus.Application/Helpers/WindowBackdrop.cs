using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Naultinus.ViewModel;

namespace Naultinus.Helpers
{
    public static class WindowBackdrop
    {
        /// <summary>DWM_SYSTEMBACKDROP_TYPE : aucun matériau (bureau visible à travers une fenêtre layered).</summary>
        private const int DwmSbtNone = 1;

        /// <summary>DWM_SYSTEMBACKDROP_TYPE : Mica (DWMSBT_MAINWINDOW).</summary>
        private const int DwmSbtMainWindow = 2;

        private static readonly ConditionalWeakTable<Window, AppearanceSubscription> Subscriptions = new();

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(WindowBackdrop),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window)
                return;

            if ((bool)e.NewValue)
            {
                window.DataContextChanged += OnDataContextChanged;
                EnsureSubscription(window).Retarget(window.DataContext as INotifyPropertyChanged);
                if (window.IsLoaded)
                    Apply(window);
                else
                    window.Loaded += OnWindowLoaded;
            }
            else
            {
                window.DataContextChanged -= OnDataContextChanged;
                window.Loaded -= OnWindowLoaded;
                if (Subscriptions.TryGetValue(window, out AppearanceSubscription? sub))
                    sub.Retarget(null);
            }
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
                Apply(window);
        }

        private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not Window window)
                return;
            EnsureSubscription(window).Retarget(e.NewValue as INotifyPropertyChanged);
            if (window.IsLoaded)
                Apply(window);
        }

        private static AppearanceSubscription EnsureSubscription(Window window)
        {
            if (Subscriptions.TryGetValue(window, out AppearanceSubscription? existing))
                return existing;

            var created = new AppearanceSubscription(window);
            Subscriptions.Add(window, created);
            return created;
        }

        /// <summary>
        /// Applique Mica (thème) ou aucun backdrop (teinte transparente façon Fences).
        /// Avec Mica, l'alpha WPF ne laisse voir que le matériau DWM — pas le papier peint.
        /// </summary>
        public static void Apply(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int useDark = IsSystemDarkMode() ? 1 : 0;
            TrySetAttribute(hwnd, 20, useDark, "dark-mode");

            bool wallpaperTint = window.AllowsTransparency
                && window.DataContext is ViewModelBase vm
                && vm.UsesBodyTint;

            if (IsWindows11OrGreater())
            {
                int backdrop = wallpaperTint ? DwmSbtNone : DwmSbtMainWindow;
                TrySetAttribute(hwnd, 38, backdrop, wallpaperTint ? "none" : "mica");
                TrySetAttribute(hwnd, 33, 1, "corner");
            }

            if (window.AllowsTransparency)
                window.Background = Brushes.Transparent;
        }

        private static void TrySetAttribute(IntPtr hwnd, int attribute, int value, string label)
        {
            int hr = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            if (hr != 0)
                NaultinusDiagnostics.LogDebug($"WindowBackdrop: DwmSetWindowAttribute({label}) a échoué (0x{hr:X8})");
        }

        private static bool IsWindows11OrGreater()
        {
            var v = Environment.OSVersion.Version;
            return v.Major >= 10 && v.Build >= 22000;
        }

        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("WindowBackdrop.IsSystemDarkMode", ex);
                return false;
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private sealed class AppearanceSubscription
        {
            private readonly Window _window;
            private INotifyPropertyChanged? _source;

            public AppearanceSubscription(Window window) => _window = window;

            public void Retarget(INotifyPropertyChanged? next)
            {
                if (ReferenceEquals(_source, next))
                    return;
                if (_source != null)
                    _source.PropertyChanged -= OnPropertyChanged;
                _source = next;
                if (_source != null)
                    _source.PropertyChanged += OnPropertyChanged;
            }

            private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(ViewModelBase.UsesBodyTint)
                    or nameof(ViewModelBase.WantsTranslucentBody)
                    or nameof(ViewModelBase.BodyOpacity)
                    or nameof(ViewModelBase.BodyColor)
                    or nameof(ViewModelBase.HeaderColor)
                    or nameof(ViewModelBase.BodyTintOpacity)
                    or nameof(ViewModelBase.BodyTintBrush))
                {
                    Apply(_window);
                }
            }
        }
    }
}
