using System;
using System.Collections.Generic;
using Naultinus.Helpers;
using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalGridTests
    {
        [Fact]
        public void Sort_ByName_PutsFoldersFirstThenIgnoresCase()
        {
            IReadOnlyList<FolderPortalItem> ordered = PortalItemSort.Order(
                new[]
                {
                    Item("zeta.txt", false, new DateTime(2024, 1, 1)),
                    Item("Images", true, new DateTime(2020, 1, 1)),
                    Item("alpha.txt", false, new DateTime(2024, 6, 1)),
                    Item("docs", true, new DateTime(2021, 1, 1)),
                },
                PortalSortField.Name);

            Assert.Equal(new[] { "docs", "Images", "alpha.txt", "zeta.txt" }, Names(ordered));
        }

        [Fact]
        public void Sort_ByType_GroupsTheExtension()
        {
            IReadOnlyList<FolderPortalItem> ordered = PortalItemSort.Order(
                new[]
                {
                    Item("b.png", false, new DateTime(2024, 1, 1)),
                    Item("a.txt", false, new DateTime(2024, 1, 1)),
                    Item("c.png", false, new DateTime(2024, 1, 1)),
                    Item("Dossier", true, new DateTime(2024, 1, 1)),
                },
                PortalSortField.Type);

            Assert.Equal(new[] { "Dossier", "b.png", "c.png", "a.txt" }, Names(ordered));
        }

        [Fact]
        public void Sort_ByDate_PutsTheNewestFileFirst()
        {
            IReadOnlyList<FolderPortalItem> ordered = PortalItemSort.Order(
                new[]
                {
                    Item("old.txt", false, new DateTime(2020, 1, 1)),
                    Item("new.txt", false, new DateTime(2024, 5, 1)),
                    Item("mid", true, new DateTime(2023, 1, 1)),
                },
                PortalSortField.Date);

            Assert.Equal(new[] { "mid", "new.txt", "old.txt" }, Names(ordered));
        }

        [Fact]
        public void Selection_Click_ReplacesTheSelection()
        {
            PortalSelection.Result result = PortalSelection.Click(4, new[] { 0, 1 }, anchor: 0, index: 3, control: false, shift: false);

            Assert.Equal(new[] { 3 }, result.Indexes);
            Assert.Equal(3, result.Anchor);
        }

        [Fact]
        public void Selection_Control_TogglesOneItem()
        {
            PortalSelection.Result added = PortalSelection.Click(4, new[] { 1 }, anchor: 1, index: 3, control: true, shift: false);
            PortalSelection.Result removed = PortalSelection.Click(4, added.Indexes, added.Anchor, index: 1, control: true, shift: false);

            Assert.Equal(new[] { 1, 3 }, added.Indexes);
            Assert.Equal(new[] { 3 }, removed.Indexes);
        }

        [Fact]
        public void Selection_Shift_SelectsTheRangeAndKeepsTheAnchor()
        {
            PortalSelection.Result result = PortalSelection.Click(6, new[] { 1 }, anchor: 1, index: 4, control: false, shift: true);

            Assert.Equal(new[] { 1, 2, 3, 4 }, result.Indexes);
            Assert.Equal(1, result.Anchor);
        }

        [Fact]
        public void Rename_RejectsAnEmptyOrExistingName()
        {
            Assert.Null(PortalRename.Target(@"C:\Portail", "a.txt", "  ", _ => false));
            Assert.Null(PortalRename.Target(@"C:\Portail", "a.txt", "a.txt", _ => true));
            Assert.Null(PortalRename.Target(@"C:\Portail", "a.txt", "b.txt", path => path.EndsWith("b.txt", StringComparison.Ordinal)));
        }

        [Fact]
        public void Rename_AcceptsAFreeNameAndACaseChange()
        {
            Assert.Equal(@"C:\Portail\b.txt", PortalRename.Target(@"C:\Portail", "a.txt", "b.txt", _ => false));
            Assert.Equal(@"C:\Portail\A.txt", PortalRename.Target(@"C:\Portail", "a.txt", "A.txt", path => path.EndsWith("A.txt", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void Clipboard_MoveEffect_IsNotACopy()
        {
            Assert.True(PortalClipboard.IsMove(PortalClipboard.EffectBytes(move: true)));
            Assert.False(PortalClipboard.IsMove(PortalClipboard.EffectBytes(move: false)));
            Assert.False(PortalClipboard.IsMove(null));
        }

        private static FolderPortalItem Item(string name, bool directory, DateTime written)
        {
            return new FolderPortalItem(name, @"C:\Portail\" + name, directory, string.Empty) { LastWriteUtc = written };
        }

        private static string[] Names(IReadOnlyList<FolderPortalItem> items)
        {
            var names = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
                names[i] = items[i].Name;
            return names;
        }
    }
}
