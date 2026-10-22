using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class UserDialogsTests
    {
        [Fact]
        public void ConfirmYesNo_CanBeReplacedForTests()
        {
            var previous = UserDialogs.ConfirmYesNo;
            try
            {
                UserDialogs.ConfirmYesNo = static (_, _) => true;
                Assert.True(UserDialogs.ConfirmYesNo("msg", "title"));
                UserDialogs.ConfirmYesNo = static (_, _) => false;
                Assert.False(UserDialogs.ConfirmYesNo("msg", "title"));
            }
            finally
            {
                UserDialogs.ConfirmYesNo = previous;
            }
        }
    }
}
