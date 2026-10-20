using System;
using System.Windows.Media;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class NaultinusAppearanceColorsTests
    {
        [Fact]
        public void TryParseRgb_AcceptsHashRrGgBb()
        {
            Assert.True(NaultinusAppearanceColors.TryParseRgb("#1A2B3C", out Color color));
            Assert.Equal(0x1A, color.R);
            Assert.Equal(0x2B, color.G);
            Assert.Equal(0x3C, color.B);
        }

        [Fact]
        public void TryCreateBrush_AppliesOpacityUnlessForcedOpaque()
        {
            SolidColorBrush? tinted = NaultinusAppearanceColors.TryCreateBrush("#112233", 50, forceOpaque: false);
            Assert.NotNull(tinted);
            Assert.Equal((byte)Math.Round(255.0 * 50 / 100.0), tinted!.Color.A);

            SolidColorBrush? opaque = NaultinusAppearanceColors.TryCreateBrush("#112233", 50, forceOpaque: true);
            Assert.NotNull(opaque);
            Assert.Equal(255, opaque!.Color.A);
        }

        [Fact]
        public void ToRgbHex_RoundTrips()
        {
            Assert.True(NaultinusAppearanceColors.TryParseRgb("#AABBCC", out Color color));
            Assert.Equal("#AABBCC", NaultinusAppearanceColors.ToRgbHex(color));
        }
    }
}
