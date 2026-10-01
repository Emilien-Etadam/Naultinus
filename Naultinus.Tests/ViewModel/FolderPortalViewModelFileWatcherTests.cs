using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Naultinus.Model;
using Naultinus.ViewModel;
using Xunit;

namespace Naultinus.Tests.ViewModel
{
    /// <summary>
    /// Nécessite un thread STA et une <see cref="Dispatcher"/> WPF : le watcher déclenche un timer
    /// qui rappelle l’UI via <c>Dispatcher.BeginInvoke</c> ; sans Application, le test est exécuté sur un
    /// thread STA dédié avec pompage du dispatcher.
    /// </summary>
    public class FolderPortalViewModelFileWatcherTests
    {
        [Fact]
        public void FileSystemWatcher_NewFileAppearsInItems_AfterDebounce()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                    var tempDir = Path.Combine(Path.GetTempPath(), "NaultinusWatcherTest_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        var model = new FolderPortalModel
                        {
                            Name = "Test",
                            RootPath = tempDir,
                            CurrentPath = tempDir,
                        };
                        var vm = new FolderPortalViewModel(model);

                        Assert.Empty(vm.Items);

                        var testFile = Path.Combine(tempDir, "test.txt");
                        File.WriteAllText(testFile, "content");

                        // Attendre l'apparition du fichier ou 8 s max (debounce 500 ms).
                        var deadline = DateTime.UtcNow.AddMilliseconds(8000);
                        var dispatcher = Application.Current!.Dispatcher;
                        while (DateTime.UtcNow < deadline && vm.Items.Count == 0)
                        {
                            Pump(dispatcher);
                            Thread.Sleep(25);
                        }

                        Assert.True(vm.Items.Count > 0 && vm.Items[0].Name == "test.txt", "test.txt did not appear in Items within 8 s");
                        vm.Dispose();
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }

                    Application.Current?.Shutdown();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            // Arrière-plan : un blocage du dispatcher ne doit pas retenir le processus de test
            // jusqu'au délai de six heures de l'agent Windows.
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(30000))
                throw new TimeoutException("Le thread STA du portail ne s'est pas terminé.");
            if (error != null)
                throw new AggregateException(error);
        }

        private static void Pump(Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
