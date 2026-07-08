using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Naultinus.Helpers;
using Naultinus.Properties;
using Naultinus.Services;
using Naultinus.View;
using System.Windows.Threading;

namespace Naultinus
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability", "CA1001:Types that own disposable fields should be disposable",
        Justification = "_trayIcon est libéré dans le gestionnaire Exit — cycle de vie approprié pour une App WPF, que personne ne Dispose.")]
    public partial class App : System.Windows.Application
    {
        private TrayIconManager? _trayIcon;

        [Conditional("DEBUG")]
        private static void WriteStartupLog(string message, Exception? ex = null)
        {
            NaultinusDiagnostics.LogDebug(message, ex);
        }

        private static void WriteCrashLog(string context, Exception? ex)
        {
            NaultinusDiagnostics.Log(context, ex?.Message ?? "(null)", ex);
        }

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                WriteCrashLog("UnhandledException", e.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += App_DispatcherUnhandledException;

            WriteStartupLog("App() début");

            try
            {
                ThemeWatcher.Apply(Resources);

                NaultinusManager.LoadNaultinus();
                WriteStartupLog("LoadNaultinus() ok, count=" + NaultinusManager.naultinus.Count);

                if (NaultinusManager.naultinus.Count == 0)
                {
                    NaultinusManager.CreateNaultinus();
                    WriteStartupLog("CreateNaultinus() ok");
                }

                var overlay = new DesktopDrawingOverlay();
                overlay.Show();
                overlay.ShowActivated = false;
                WriteStartupLog("DesktopDrawingOverlay shown");

                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _trayIcon = new TrayIconManager();
                _ = RunAutoUpdateAsync();
                WriteStartupLog("TrayIcon created");

                Exit += (_, _) =>
                {
                    Helpers.ToastHelper.Cleanup();
                    _trayIcon?.Dispose();
                    LayoutSnapshotService.SaveAutoSnapshotAndPrune(3);
                };

                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(100);
                    foreach (var kv in NaultinusManager.naultinus)
                    {
                        if (kv.Value is Window w)
                        { w.Activate(); break; }
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                WriteCrashLog("EXCEPTION au démarrage", ex);
                try
                {
                    MessageBox.Show(
                        string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.StartupErrorFormat, ex.Message, Environment.NewLine, ex.StackTrace ?? string.Empty),
                        Strings.NaultinusErrorTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { WriteCrashLog("MessageBox a échoué", null); }
                Shutdown();
                return;
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            NaultinusDiagnostics.Log("DispatcherUnhandledException", e.Exception.Message, e.Exception);

            if (IsFatalException(e.Exception))
            {
                e.Handled = false;
                LayoutSnapshotService.SaveAutoSnapshotAndPrune(3);
                return;
            }

            e.Handled = true;
        }

        private static bool IsFatalException(Exception ex)
        {
            return ex is OutOfMemoryException
                or StackOverflowException
                or AccessViolationException
                or BadImageFormatException
                or TypeInitializationException
                or InvalidProgramException;
        }

        private static async Task RunAutoUpdateAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            var update = await UpdateChecker.CheckAsync();
            if (update == null) return;

            var result = Current.Dispatcher.Invoke(() =>
                MessageBox.Show(
                    string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.UpdateAvailableFormat, update.Version, Environment.NewLine, update.ReleaseNotes ?? string.Empty),
                    Strings.UpdateTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information));

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var progressHandler = new Progress<double>(percent =>
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (Application.Current.MainWindow != null)
                                Application.Current.MainWindow.Title = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.MainWindowTitleUpdatingFormat, percent);
                        }
                        catch (Exception ex) { NaultinusDiagnostics.LogDebug("App: MAJ du titre de progression", ex); }
                    });
                });
                await UpdateChecker.ApplyUpdateAsync(update, progressHandler);
                Current.Dispatcher.Invoke(() => Current.Shutdown());
            }
            catch (Exception ex)
            {
                Current.Dispatcher.Invoke(() =>
                    MessageBox.Show(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.UpdateFailedFormat, ex.Message), Strings.ErrorGenericTitle,
                        MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        }
    }
}
