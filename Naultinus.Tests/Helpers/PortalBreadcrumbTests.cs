using System.Collections.Generic;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalBreadcrumbTests
    {
        [Fact]
        public void Build_AtTheRoot_HasOneSegment()
        {
            IReadOnlyList<PortalPathSegment> segments = PortalBreadcrumb.Build(@"C:\Portail", @"C:\Portail");

            PortalPathSegment segment = Assert.Single(segments);
            Assert.Equal("Portail", segment.Label);
            Assert.Equal(@"C:\Portail", segment.Path);
        }

        [Fact]
        public void Build_UnderTheRoot_KeepsEachFolder()
        {
            IReadOnlyList<PortalPathSegment> segments = PortalBreadcrumb.Build(
                @"C:\Portail",
                @"C:\Portail\Travail\Notes");

            Assert.Equal(3, segments.Count);
            Assert.Equal("Portail", segments[0].Label);
            Assert.Equal(@"C:\Portail", segments[0].Path);
            Assert.Equal("Travail", segments[1].Label);
            Assert.Equal(@"C:\Portail\Travail", segments[1].Path);
            Assert.Equal("Notes", segments[2].Label);
            Assert.Equal(@"C:\Portail\Travail\Notes", segments[2].Path);
        }

        [Fact]
        public void Build_DriveRoot_UsesTheDriveAsLabel()
        {
            IReadOnlyList<PortalPathSegment> segments = PortalBreadcrumb.Build(@"C:\", @"C:\Docs");

            Assert.Equal(2, segments.Count);
            Assert.Equal(@"C:\", segments[0].Label);
            Assert.Equal(@"C:\", segments[0].Path);
            Assert.Equal("Docs", segments[1].Label);
            Assert.Equal(@"C:\Docs", segments[1].Path);
        }

        [Fact]
        public void Build_OutsideTheRoot_IsEmpty()
        {
            Assert.Empty(PortalBreadcrumb.Build(@"C:\Portail", @"C:\Ailleurs"));
            Assert.Empty(PortalBreadcrumb.Build(null, @"C:\Portail"));
            Assert.Empty(PortalBreadcrumb.Build(@"C:\Portail", ""));
        }
    }
}
