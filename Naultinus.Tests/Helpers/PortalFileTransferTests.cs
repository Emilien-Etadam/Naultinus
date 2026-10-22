using System;
using System.IO;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalFileTransferTests
    {
        [Fact]
        public void ResolveUniqueDestination_AddsCounterWhenExists()
        {
            var dir = Path.Combine(Path.GetTempPath(), "NaultinusPortalXfer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.txt"), "1");
                var unique = PortalFileTransfer.ResolveUniqueDestination(dir, "a.txt");
                Assert.Equal(Path.Combine(dir, "a (1).txt"), unique);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public void TryImport_CopyFile_UnderRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "NaultinusPortalRoot_" + Guid.NewGuid().ToString("N"));
            var srcDir = Path.Combine(root, "src");
            var destDir = Path.Combine(root, "dest");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            try
            {
                var src = Path.Combine(srcDir, "note.txt");
                File.WriteAllText(src, "hello");
                Assert.Null(PortalFileTransfer.TryImport(root, destDir, new[] { src }, isCopy: true));
                Assert.True(File.Exists(Path.Combine(destDir, "note.txt")));
                Assert.True(File.Exists(src));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
            }
        }
    }
}
