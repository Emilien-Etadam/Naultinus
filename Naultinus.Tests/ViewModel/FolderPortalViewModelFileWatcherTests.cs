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
    /// qui rappelle l’UI via <c>Dispatcher.Invoke</c> ; sans Application, le test est exécuté sur un
    /// thread STA dédié avec pompage du dispatcher.
    /// </summary>
    public class FolderPortalViewModelFileWatcherTests
    {
        [Fact]
        public void FileSystemWatcher_NewFileAppearsInItems_AfterDebounce()
        {
            Exception? error = null;
            bool found = false;
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
                        while (DateTime.UtcNow < deadline)
                        {
                            Application.Current!.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                            if (vm.Items.Count > 0) break;
                            Thread.Sleep(25);
                        }

                        found = vm.Items.Count > 0 && vm.Items[0].Name == "test.txt";
                        Assert.True(found, "test.txt did not appear in Items within 8 s");
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

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(30000);
            if (error != null)
                throw new AggregateException(error);
        }
    }
}
