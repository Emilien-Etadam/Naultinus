using System;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalPathGuardTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("dossier")]
        [InlineData(@"foo\bar")]
        [InlineData(@"C:relative")]
        [InlineData(@"\Windows")]
        public void IsInsideRoot_RejectsMissingOrRelative(string? candidate)
        {
            Assert.False(PortalPathGuard.IsInsideRoot(@"C:\Portail", candidate));
        }

        [Fact]
        public void IsInsideRoot_RejectsEmptyRoot()
        {
            Assert.False(PortalPathGuard.IsInsideRoot(null, @"C:\Portail"));
            Assert.False(PortalPathGuard.IsInsideRoot("", @"C:\Portail"));
        }

        [Theory]
        [InlineData(@"C:\Portail", @"C:\Portail")]
        [InlineData(@"C:\Portail\", @"C:\Portail")]
        [InlineData(@"c:\portail", @"C:\Portail\Sous")]
        [InlineData(@"C:\Portail", @"C:\Portail\Sous\..\Sous\Fichier.txt")]
        [InlineData(@"C:\Portail", @"C:/Portail/Sous")]
        [InlineData(@"\\?\C:\Portail", @"C:\Portail\Sous")]
        [InlineData(@"C:\", @"C:\Windows")]
        [InlineData(@"\\serveur\partage", @"\\serveur\partage\dossier")]
        public void IsInsideRoot_AcceptsTheFolderAndItsChildren(string root, string candidate)
        {
            Assert.True(PortalPathGuard.IsInsideRoot(root, candidate));
        }

        [Theory]
        [InlineData(@"C:\Portail", @"C:\Portail2")]
        [InlineData(@"C:\Portail", @"C:\Portail2\Sous")]
        [InlineData(@"C:\Portail", @"C:\Autre")]
        [InlineData(@"C:\Portail\Sous", @"C:\Portail")]
        [InlineData(@"C:\Portail", @"D:\Portail")]
        [InlineData(@"C:\Portail", @"C:\Portail\..\Windows")]
        [InlineData(@"\\serveur\partage", @"\\serveur\autre")]
        [InlineData(@"\\serveur\partage\portail", @"\\serveur\partage")]
        public void IsInsideRoot_RejectsSiblingsParentsAndPrefixTwins(string root, string candidate)
        {
            Assert.False(PortalPathGuard.IsInsideRoot(root, candidate));
        }

        [Fact]
        public void IsInsideRoot_FinalPathOutside_IsRejected()
        {
            bool inside = PortalPathGuard.IsInsideRoot(
                @"C:\Portail",
                @"C:\Portail\lien",
                path => path.EndsWith("lien", StringComparison.OrdinalIgnoreCase) ? @"D:\Secret" : path);

            Assert.False(inside);
        }

        [Fact]
        public void IsInsideRoot_FinalPathStillInside_IsAccepted()
        {
            bool inside = PortalPathGuard.IsInsideRoot(
                @"C:\Portail",
                @"C:\Portail\lien",
                path => path.EndsWith("lien", StringComparison.OrdinalIgnoreCase) ? @"C:\Portail\reel" : path);

            Assert.True(inside);
        }

        [Fact]
        public void IsInsideRoot_ResolverFailure_IsRejected()
        {
            Assert.False(PortalPathGuard.IsInsideRoot(@"C:\Portail", @"C:\Portail\a", _ => null));
            Assert.False(PortalPathGuard.IsInsideRoot(@"C:\Portail", @"C:\Portail\a", _ => throw new InvalidOperationException("jonction")));
        }

        [Fact]
        public void AreSame_IgnoresCaseSlashAndDotSegments()
        {
            Assert.True(PortalPathGuard.AreSame(@"C:\Portail\Sous", @"c:\portail\Sous\"));
            Assert.True(PortalPathGuard.AreSame(@"C:\Portail\Sous", @"C:\Portail\.\Sous\..\Sous"));
            Assert.False(PortalPathGuard.AreSame(@"C:\Portail", @"C:\Portail\Sous"));
            Assert.False(PortalPathGuard.AreSame(null, @"C:\Portail"));
        }
    }
}
