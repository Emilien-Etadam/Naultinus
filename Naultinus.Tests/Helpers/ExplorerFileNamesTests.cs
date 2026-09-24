using Naultinus.Helpers;
using System;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class ExplorerFileNamesTests
    {
        private static bool Known(string extension) =>
            extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);

        private static bool Never(string _) => false;

        [Fact]
        public void ToDisplayName_HideKnownExtension_StripsIt()
        {
            string shown = ExplorerFileNames.ToDisplayName("rapport.txt", hideKnownExtensions: true, Known);
            Assert.Equal("rapport", shown);
        }

        [Fact]
        public void ToDisplayName_HideUnknownExtension_KeepsIt()
        {
            string shown = ExplorerFileNames.ToDisplayName("notes.xyz", hideKnownExtensions: true, Known);
            Assert.Equal("notes.xyz", shown);
        }

        [Fact]
        public void ToDisplayName_ShowExtensions_KeepsKnownExtension()
        {
            string shown = ExplorerFileNames.ToDisplayName("photo.jpg", hideKnownExtensions: false, Known);
            Assert.Equal("photo.jpg", shown);
        }

        [Fact]
        public void FormatShortcutLabel_DroppedFile_FollowsHideFileExt()
        {
            const string path = @"C:\imported\photo.jpg";
            string hidden = ExplorerFileNames.FormatShortcutLabel(
                "photo", path, isUrlShortcut: false, hideKnownExtensions: true, Known,
                fileExists: p => p == path, directoryExists: _ => false);
            string shown = ExplorerFileNames.FormatShortcutLabel(
                "photo", path, isUrlShortcut: false, hideKnownExtensions: false, Known,
                fileExists: p => p == path, directoryExists: _ => false);

            Assert.Equal("photo", hidden);
            Assert.Equal("photo.jpg", shown);
        }

        [Fact]
        public void FormatShortcutLabel_ShortcutWhoseTitleDiffersFromTarget_UsesLnkExtension()
        {
            const string target = @"C:\Games\game.exe";
            string hidden = ExplorerFileNames.FormatShortcutLabel(
                "Mon jeu", target, isUrlShortcut: false, hideKnownExtensions: true, Known,
                fileExists: p => p == target, directoryExists: _ => false);
            string shown = ExplorerFileNames.FormatShortcutLabel(
                "Mon jeu", target, isUrlShortcut: false, hideKnownExtensions: false, Known,
                fileExists: p => p == target, directoryExists: _ => false);

            Assert.Equal("Mon jeu", hidden);
            Assert.Equal("Mon jeu.lnk", shown);
        }

        [Fact]
        public void FormatShortcutLabel_Url_FollowsHideFileExt()
        {
            string hidden = ExplorerFileNames.FormatShortcutLabel(
                "Docs", "https://example.com", isUrlShortcut: true, hideKnownExtensions: true, Known,
                fileExists: _ => false, directoryExists: _ => false);
            string shown = ExplorerFileNames.FormatShortcutLabel(
                "Docs", "https://example.com", isUrlShortcut: true, hideKnownExtensions: false, Known,
                fileExists: _ => false, directoryExists: _ => false);

            Assert.Equal("Docs", hidden);
            Assert.Equal("Docs.url", shown);
        }

        [Fact]
        public void FormatShortcutLabel_MissingFile_KeepsStoredName()
        {
            string shown = ExplorerFileNames.FormatShortcutLabel(
                "brouillon", @"C:\gone\brouillon.txt", isUrlShortcut: false, hideKnownExtensions: false, Known,
                fileExists: _ => false, directoryExists: _ => false);
            Assert.Equal("brouillon", shown);
        }

        [Fact]
        public void ResolvePreviewPath_RasterFile_UsesTheFileItself()
        {
            const string photo = @"C:\imported\photo.jpg";
            const string icon = @"C:\icons\file_abc.png";
            string preview = ExplorerFileNames.ResolvePreviewPath(
                photo, icon, fileExists: _ => true, isRasterPath: path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(photo, preview);
        }

        [Fact]
        public void ResolvePreviewPath_OtherFile_UsesCachedShellIcon()
        {
            const string notes = @"C:\imported\notes.txt";
            const string icon = @"C:\icons\file_abc.png";
            string preview = ExplorerFileNames.ResolvePreviewPath(
                notes, icon, fileExists: _ => true, isRasterPath: Never);
            Assert.Equal(icon, preview);
        }

        [Fact]
        public void ResolvePreviewPath_WithoutCache_FallsBackToTheFile()
        {
            const string notes = @"C:\imported\notes.txt";
            string preview = ExplorerFileNames.ResolvePreviewPath(
                notes, iconPath: "", fileExists: path => path == notes, isRasterPath: Never);
            Assert.Equal(notes, preview);
        }
    }
}
