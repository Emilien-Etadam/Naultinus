using System.Windows.Controls;
using Naultinus.Helpers;
using Naultinus.Tests.Support;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class HeaderChromeInputTests
    {
        [Fact]
        public void IsInteractiveSource_Null_IsFalse()
        {
            Assert.False(HeaderChromeInput.IsInteractiveSource(null));
        }

        [Fact]
        public void IsInteractiveSource_Button_IsTrue()
        {
            StaRunner.Run(() =>
                Assert.True(HeaderChromeInput.IsInteractiveSource(new Button())));
        }

        [Fact]
        public void IsInteractiveSource_TextBox_IsTrue()
        {
            StaRunner.Run(() =>
                Assert.True(HeaderChromeInput.IsInteractiveSource(new TextBox())));
        }
    }
}
