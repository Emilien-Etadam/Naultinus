using Naultinus.Helpers;
using Naultinus.Model;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalNavigationHistoryTests
    {
        [Fact]
        public void Push_TruncatesForwardBranch()
        {
            var model = new FolderPortalModel
            {
                NavigationHistory = { @"C:\a", @"C:\b", @"C:\c" },
                NavigationIndex = 0,
            };

            PortalNavigationHistory.Push(model, @"C:\x");
            Assert.Equal(new[] { @"C:\a", @"C:\x" }, model.NavigationHistory);
            Assert.Equal(1, model.NavigationIndex);
        }

        [Fact]
        public void SeedFromCurrentPath_InitializesEmptyHistory()
        {
            var model = new FolderPortalModel { CurrentPath = @"C:\Users\Test" };
            PortalNavigationHistory.SeedFromCurrentPath(model);
            Assert.Single(model.NavigationHistory);
            Assert.Equal(0, model.NavigationIndex);
        }
    }
}
