using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Naultinus.View;
using Xunit;

namespace Naultinus.Tests.View
{
    /// <summary>
    /// Preuve d'acceptation anti-fuite : ouvrir/fermer l'hôte shell ×10 ne doit laisser
    /// aucun HWND <c>NaultinusShellViewSite</c> (DestroyWindowCore + IExplorerBrowser.Destroy).
    /// </summary>
    public class ShellFolderViewHostLeakTests
    {
        [Fact]
        public void OpenCloseTenTimes_LeavesNoSiteHwnd()
        {
            if (!OperatingSystem.IsWindows())
                return;

            Exception? error = null;
            var thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                Application? app = null;
                string? tempDir = null;
                try
                {
                    app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    tempDir = Path.Combine(Path.GetTempPath(), "NaultinusShellLeak_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    File.WriteAllText(Path.Combine(tempDir, "probe.txt"), "x");

                    bool sawAvailable = false;
                    for (int i = 0; i < 10; i++)
                    {
                        var host = new ShellFolderViewHost
                        {
                            Width = 320,
                            Height = 240,
                        };
                        var window = new Window
                        {
                            Title = "NaultinusShellLeak",
                            Width = 340,
                            Height = 260,
                            Content = host,
                            ShowInTaskbar = false,
                            WindowStyle = WindowStyle.ToolWindow,
                            Topmost = false,
                        };
                        window.Show();
                        Pump(dispatcher);

                        host.Navigate(tempDir);
                        var deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline && !host.IsAvailable)
                        {
                            Pump(dispatcher);
                            Thread.Sleep(25);
                        }

                        if (host.IsAvailable)
                            sawAvailable = true;

                        // Comme FolderPortal : Dispose explicite — Close seul ne déclenche pas
                        // DestroyWindowCore sur HwndHost.
                        host.Dispose();
                        window.Close();
                        Pump(dispatcher);
                        Pump(dispatcher);
                    }

                    Assert.True(sawAvailable, "IExplorerBrowser n'a pas pu être créé : test non concluant.");
                    Assert.Equal(0, CountClassDeep("NaultinusShellViewSite"));
                    Assert.Equal(0, CountClassDeep("ExplorerBrowserControl"));
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    if (tempDir != null)
                    {
                        try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
                    }

                    try { app?.Shutdown(); } catch { /* best-effort */ }
                    dispatcher.InvokeShutdown();
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(120000))
                throw new TimeoutException("Le thread STA anti-fuite shell ne s'est pas terminé.");
            if (error != null)
                throw new AggregateException(error);
        }

        private static void Pump(Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private sealed class Counter
        {
            public int Value;
        }

        private static int CountClassDeep(string className)
        {
            var counter = new Counter();
            EnumWindows((h, _) =>
            {
                CountRecursive(h, className, counter);
                return true;
            }, IntPtr.Zero);
            return counter.Value;
        }

        private static void CountRecursive(IntPtr hwnd, string className, Counter counter)
        {
            var sb = new StringBuilder(256);
            _ = GetClassName(hwnd, sb, sb.Capacity);
            if (string.Equals(sb.ToString(), className, StringComparison.Ordinal))
                counter.Value++;

            EnumChildWindows(hwnd, (child, _) =>
            {
                CountRecursive(child, className, counter);
                return true;
            }, IntPtr.Zero);
        }

        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
