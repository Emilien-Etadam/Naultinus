using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalShellPlacementTests
    {
        [Fact]
        public void Intersection_SlotUnderTheBanner_StaysInsideTheWindow()
        {
            var window = new PortalShellPlacement.PixelRect(0, 0, 400, 300);
            var content = new PortalShellPlacement.PixelRect(0, 64, 400, 236);

            PortalShellPlacement.PixelRect? visible = PortalShellPlacement.Intersection(content, window);

            Assert.True(visible.HasValue);
            Assert.Equal(0, visible.Value.X);
            Assert.Equal(64, visible.Value.Y);
            Assert.Equal(400, visible.Value.Width);
            Assert.Equal(236, visible.Value.Height);
        }

        [Fact]
        public void Intersection_ClipsThePartThatLeavesTheWindow()
        {
            var window = new PortalShellPlacement.PixelRect(10, 20, 100, 80);
            var content = new PortalShellPlacement.PixelRect(80, 70, 80, 60);

            PortalShellPlacement.PixelRect? visible = PortalShellPlacement.Intersection(content, window);

            Assert.True(visible.HasValue);
            Assert.Equal(80, visible.Value.X);
            Assert.Equal(70, visible.Value.Y);
            Assert.Equal(30, visible.Value.Width);
            Assert.Equal(30, visible.Value.Height);
        }

        [Fact]
        public void Intersection_OutsideTheWindow_HasNoSurface()
        {
            var window = new PortalShellPlacement.PixelRect(0, 0, 200, 100);
            var content = new PortalShellPlacement.PixelRect(220, 0, 40, 40);

            Assert.False(PortalShellPlacement.Intersection(content, window).HasValue);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(10, 0)]
        [InlineData(-1, 10)]
        public void Intersection_EmptySide_HasNoSurface(int width, int height)
        {
            var window = new PortalShellPlacement.PixelRect(0, 0, 200, 100);
            var content = new PortalShellPlacement.PixelRect(0, 0, width, height);

            Assert.False(PortalShellPlacement.Intersection(content, window).HasValue);
        }

        [Fact]
        public void BelowBanner_SlotAlreadyUnderTheBanner_Stays()
        {
            var content = new PortalShellPlacement.PixelRect(10, 80, 200, 120);

            PortalShellPlacement.PixelRect? visible = PortalShellPlacement.BelowBanner(content, 64);

            Assert.True(visible.HasValue);
            Assert.Equal(10, visible.Value.X);
            Assert.Equal(80, visible.Value.Y);
            Assert.Equal(200, visible.Value.Width);
            Assert.Equal(120, visible.Value.Height);
        }

        [Fact]
        public void BelowBanner_SlotCoveringTheBanner_IsCut()
        {
            var content = new PortalShellPlacement.PixelRect(0, 0, 400, 300);

            PortalShellPlacement.PixelRect? visible = PortalShellPlacement.BelowBanner(content, 64);

            Assert.True(visible.HasValue);
            Assert.Equal(0, visible.Value.X);
            Assert.Equal(64, visible.Value.Y);
            Assert.Equal(400, visible.Value.Width);
            Assert.Equal(236, visible.Value.Height);
        }

        [Fact]
        public void BelowBanner_SlotEntirelyAboveTheBanner_HasNoSurface()
        {
            var content = new PortalShellPlacement.PixelRect(0, 0, 400, 40);

            Assert.False(PortalShellPlacement.BelowBanner(content, 64).HasValue);
        }

        [Fact]
        public void ContentBelowBanner_FullWindowSlot_StartsUnderTheBannerWithoutOverlap()
        {
            var window = new PortalShellPlacement.PixelRect(20, 40, 400, 300);
            var slot = new PortalShellPlacement.PixelRect(20, 40, 400, 300);

            PortalShellPlacement.PixelRect? visible = PortalShellPlacement.ContentBelowBanner(slot, window, 104);
            PortalShellPlacement.PixelRect? banner = PortalShellPlacement.TopBar(window, 104);

            Assert.True(visible.HasValue);
            Assert.True(banner.HasValue);
            Assert.Equal(20, visible.Value.X);
            Assert.Equal(104, visible.Value.Y);
            Assert.Equal(400, visible.Value.Width);
            Assert.Equal(236, visible.Value.Height);
            Assert.False(PortalShellPlacement.Overlaps(visible.Value, banner.Value));
        }

        [Fact]
        public void ContentBelowBanner_UnknownBanner_HasNoSurface()
        {
            var window = new PortalShellPlacement.PixelRect(0, 0, 400, 300);
            var slot = new PortalShellPlacement.PixelRect(0, 0, 400, 300);

            Assert.False(PortalShellPlacement.ContentBelowBanner(slot, window, 0).HasValue);
        }

        [Fact]
        public void Overlaps_SharedEdge_IsNotACover()
        {
            var banner = new PortalShellPlacement.PixelRect(0, 0, 400, 64);
            var files = new PortalShellPlacement.PixelRect(0, 64, 400, 200);

            Assert.False(PortalShellPlacement.Overlaps(files, banner));
        }

        [Fact]
        public void RegionExcludingBanner_WindowAlreadyUnder_IsTheWholeWindow()
        {
            var window = new PortalShellPlacement.PixelRect(10, 80, 200, 120);
            var banner = new PortalShellPlacement.PixelRect(10, 16, 200, 64);

            PortalShellPlacement.PixelRect? region = PortalShellPlacement.RegionExcludingBanner(window, banner);

            Assert.True(region.HasValue);
            Assert.Equal(0, region.Value.X);
            Assert.Equal(0, region.Value.Y);
            Assert.Equal(200, region.Value.Width);
            Assert.Equal(120, region.Value.Height);
        }

        [Fact]
        public void RegionExcludingBanner_WindowCoveringTheBanner_StartsUnderIt()
        {
            var window = new PortalShellPlacement.PixelRect(10, 16, 200, 300);
            var banner = new PortalShellPlacement.PixelRect(10, 16, 200, 64);

            PortalShellPlacement.PixelRect? region = PortalShellPlacement.RegionExcludingBanner(window, banner);

            Assert.True(region.HasValue);
            Assert.Equal(0, region.Value.X);
            Assert.Equal(64, region.Value.Y);
            Assert.Equal(200, region.Value.Width);
            Assert.Equal(236, region.Value.Height);
            Assert.False(PortalShellPlacement.Overlaps(
                new PortalShellPlacement.PixelRect(window.X, window.Y + region.Value.Y, region.Value.Width, region.Value.Height),
                banner));
        }

        [Fact]
        public void RegionExcludingBanner_WindowInsideTheBanner_HasNoSurface()
        {
            var window = new PortalShellPlacement.PixelRect(0, 8, 100, 20);
            var banner = new PortalShellPlacement.PixelRect(0, 0, 100, 64);

            Assert.False(PortalShellPlacement.RegionExcludingBanner(window, banner).HasValue);
        }

        [Fact]
        public void BannerHit_PointOnTheTopBar_PassesThrough()
        {
            var banner = new PortalShellPlacement.PixelRect(20, 40, 400, 64);
            long onHome = Pack(30, 50);
            long onPath = Pack(180, 100);
            long inFiles = Pack(30, 104);

            Assert.True(PortalShellPlacement.BannerHit(onHome, banner));
            Assert.True(PortalShellPlacement.BannerHit(onPath, banner));
            Assert.False(PortalShellPlacement.BannerHit(inFiles, banner));
        }

        [Fact]
        public void UnpackScreenPoint_KeepsANegativeCoordinate()
        {
            PortalShellPlacement.UnpackScreenPoint(Pack(-8, -20), out int x, out int y);

            Assert.Equal(-8, x);
            Assert.Equal(-20, y);
        }

        private static long Pack(int x, int y)
        {
            return (ushort)x | ((long)(ushort)y << 16);
        }
    }
}
