using Naultinus.View;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class PortalShellChromeTests
    {
        [Fact]
        public void ColorRef_PacksBlueInTheHighByte()
        {
            Assert.Equal(0x0020191A, PortalShellChrome.ColorRef(0x1A, 0x19, 0x20));
        }

        [Fact]
        public void IsDark_ControlSurface_FollowsTheTheme()
        {
            Assert.True(PortalShellChrome.IsDark(PortalShellChrome.ColorRef(0x21, 0x20, 0x20)));
            Assert.False(PortalShellChrome.IsDark(PortalShellChrome.ColorRef(0xFF, 0xFF, 0xFF)));
        }
    }
}
