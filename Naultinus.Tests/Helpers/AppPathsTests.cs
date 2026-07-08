using Naultinus.Helpers;
using System;
using System.IO;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class AppPathsTests : IDisposable
    {
        private readonly string _tempDir;

        public AppPathsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "AppPathsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        #region IsUnderDesktop

        [Fact]
        public void IsUnderDesktop_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(AppPaths.IsUnderDesktop(null!));
            Assert.False(AppPaths.IsUnderDesktop(""));
        }

        [Fact]
        public void IsUnderDesktop_TempPath_ReturnsFalse()
        {
            Assert.False(AppPaths.IsUnderDesktop(Path.GetTempPath()));
        }

        [Fact]
        public void IsUnderDesktop_DesktopItself_ReturnsTrue()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop)) return;
            Assert.True(AppPaths.IsUnderDesktop(desktop));
        }

        [Fact]
        public void IsUnderDesktop_FileOnDesktop_ReturnsTrue()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop)) return;
            string filePath = Path.Combine(desktop, "test.txt");
            Assert.True(AppPaths.IsUnderDesktop(filePath));
        }

        [Fact]
        public void IsUnderDesktop_SubfolderOnDesktop_ReturnsTrue()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop)) return;
            Assert.True(AppPaths.IsUnderDesktop(Path.Combine(desktop, "subfolder", "file.txt")));
        }

        #endregion

        #region AllocateUniqueFilePath

        [Fact]
        public void AllocateUniqueFilePath_NoConflict_ReturnsOriginalName()
        {
            string dest = AppPaths.AllocateUniqueFilePath("report.pdf", _tempDir);
            Assert.Equal(Path.Combine(_tempDir, "report.pdf"), dest);
        }

        [Fact]
        public void AllocateUniqueFilePath_Conflict_AppendsCounter()
        {
            string existing = Path.Combine(_tempDir, "report.pdf");
            File.WriteAllText(existing, "");

            string dest = AppPaths.AllocateUniqueFilePath("report.pdf", _tempDir);
            Assert.Equal(Path.Combine(_tempDir, "report (1).pdf"), dest);
        }

        [Fact]
        public void AllocateUniqueFilePath_MultipleConflicts_IncrementsCounter()
        {
            File.WriteAllText(Path.Combine(_tempDir, "report.pdf"), "");
            File.WriteAllText(Path.Combine(_tempDir, "report (1).pdf"), "");

            string dest = AppPaths.AllocateUniqueFilePath("report.pdf", _tempDir);
            Assert.Equal(Path.Combine(_tempDir, "report (2).pdf"), dest);
        }

        [Fact]
        public void AllocateUniqueFilePath_CreatesDestDirectory()
        {
            string sub = Path.Combine(_tempDir, "newsubdir");
            AppPaths.AllocateUniqueFilePath("file.txt", sub);
            Assert.True(Directory.Exists(sub));
        }

        #endregion

        #region AllocateUniqueDirectoryPath

        [Fact]
        public void AllocateUniqueDirectoryPath_NoConflict_ReturnsOriginalName()
        {
            string sourceDir = Path.Combine(_tempDir, "src", "myFolder");
            Directory.CreateDirectory(sourceDir);

            string dest = AppPaths.AllocateUniqueDirectoryPath(sourceDir, _tempDir);
            Assert.Equal(Path.Combine(_tempDir, "myFolder"), dest);
        }

        [Fact]
        public void AllocateUniqueDirectoryPath_Conflict_AppendsCounter()
        {
            string sourceDir = Path.Combine(_tempDir, "myFolder");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(Path.Combine(_tempDir, "myFolder"));

            string conflict = Path.Combine(_tempDir, "myFolder2");
            Directory.CreateDirectory(conflict);

            string sourceDir2 = Path.Combine(_tempDir, "src", "myFolder2");
            Directory.CreateDirectory(sourceDir2);

            string dest = AppPaths.AllocateUniqueDirectoryPath(sourceDir2, _tempDir);
            Assert.Equal(Path.Combine(_tempDir, "myFolder2 (1)"), dest);
        }

        #endregion

        #region MoveRobust

        [Fact]
        public void MoveRobust_File_MovesFile()
        {
            string src = Path.Combine(_tempDir, "source.txt");
            string dst = Path.Combine(_tempDir, "dest.txt");
            File.WriteAllText(src, "hello");

            AppPaths.MoveRobust(src, dst, isDirectory: false);

            Assert.False(File.Exists(src));
            Assert.True(File.Exists(dst));
            Assert.Equal("hello", File.ReadAllText(dst));
        }

        [Fact]
        public void MoveRobust_Directory_MovesDirectory()
        {
            string src = Path.Combine(_tempDir, "srcDir");
            string dst = Path.Combine(_tempDir, "dstDir");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "file.txt"), "content");

            AppPaths.MoveRobust(src, dst, isDirectory: true);

            Assert.False(Directory.Exists(src));
            Assert.True(Directory.Exists(dst));
            Assert.True(File.Exists(Path.Combine(dst, "file.txt")));
        }

        #endregion

        #region CopyDirectory

        [Fact]
        public void CopyDirectory_CopiesFilesAndSubdirs()
        {
            string src = Path.Combine(_tempDir, "srcCopy");
            string sub = Path.Combine(src, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.WriteAllText(Path.Combine(sub, "b.txt"), "b");

            string dst = Path.Combine(_tempDir, "dstCopy");
            AppPaths.CopyDirectory(src, dst);

            Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "sub", "b.txt")));
            Assert.True(Directory.Exists(src));
        }

        #endregion
    }
}
