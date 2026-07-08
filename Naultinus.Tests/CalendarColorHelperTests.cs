using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests
{
    public class CalendarColorHelperTests
    {
        [Theory]
        [InlineData("#FF0000", true)]
        [InlineData("#F00", true)]
        [InlineData("#80FF0000", true)]
        [InlineData("708090", false)]
        [InlineData("#GGGGGG", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidHexColor_ValideLesFormatsAttendus(string? colorHex, bool expected)
        {
            Assert.Equal(expected, CalendarColorHelper.IsValidHexColor(colorHex));
        }

        [Fact]
        public void GetPaletteColor_UtiliseLeModuloEtRepliParDefaut()
        {
            Assert.Equal(CalendarColorHelper.Palette[0], CalendarColorHelper.GetPaletteColor(0));
            Assert.Equal(CalendarColorHelper.Palette[1], CalendarColorHelper.GetPaletteColor(9));
            Assert.Equal(CalendarColorHelper.DefaultColor, CalendarColorHelper.GetPaletteColor(-1));
        }

        [Fact]
        public void ResolveColor_ConserveUneCouleurValidePersistee()
        {
            var resolved = CalendarColorHelper.ResolveColor(3, "#ABCDEF");
            Assert.Equal("#ABCDEF", resolved);
        }

        [Fact]
        public void ResolveColor_AttribueLaPaletteSiCouleurInvalide()
        {
            var resolved = CalendarColorHelper.ResolveColor(2, "invalid");
            Assert.Equal(CalendarColorHelper.Palette[2], resolved);
        }

        [Theory]
        [InlineData("/dav/user@domain/Calendar", "Calendar")]
        [InlineData("/dav/user@domain/Work/", "Work")]
        [InlineData("", "?")]
        public void GetDisplayNameFromHref_ExtraitLeDernierSegment(string href, string expected)
        {
            Assert.Equal(expected, CalendarColorHelper.GetDisplayNameFromHref(href));
        }
    }
}
