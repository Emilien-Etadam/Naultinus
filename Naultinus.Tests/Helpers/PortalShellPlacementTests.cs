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
    }
}
