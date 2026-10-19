using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalFolderAccessTests
    {
        [Fact]
        public void DescribeShellParseFailure_AccessDenied_UsesAccessMessage()
        {
            string message = PortalFolderAccess.DescribeShellParseFailure(@"C:\Secret", unchecked((int)0x80070005));
            Assert.Contains("Secret", message);
            Assert.DoesNotContain("unreachable", message, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DescribeShellParseFailure_UncPath_UsesUnreachableMessage()
        {
            string message = PortalFolderAccess.DescribeShellParseFailure(@"\\server\share\gone", unchecked((int)0x80070003));
            Assert.Contains(@"\\server\share\gone", message);
        }

        [Fact]
        public void DescribeMissing_LocalPath_UsesGoneMessage()
        {
            string message = PortalFolderAccess.DescribeMissing(@"C:\Temp\DeletedFolder");
            Assert.Contains("DeletedFolder", message);
        }

        [Fact]
        public void TryExists_EmptyPath_Fails()
        {
            Assert.False(PortalFolderAccess.TryExists("", out string? error));
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
