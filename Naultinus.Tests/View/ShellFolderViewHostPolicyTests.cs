using System;
using Naultinus.Tests.Support;
using Naultinus.View;
using Xunit;

namespace Naultinus.Tests.View
{
    /// <summary>
    /// Politiques shell testables sans HWND : opt-in/out d'environnement et défauts d'interaction.
    /// </summary>
    public class ShellFolderViewHostPolicyTests
    {
        [Fact]
        public void IsRequested_Default_IsTrue()
        {
            string? previous = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW");
            try
            {
                Environment.SetEnvironmentVariable("NAULTINUS_SHELLVIEW", null);
                Assert.True(ShellFolderViewHost.IsRequested());
            }
            finally
            {
                Environment.SetEnvironmentVariable("NAULTINUS_SHELLVIEW", previous);
            }
        }

        [Theory]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("FALSE")]
        [InlineData("off")]
        [InlineData("Off")]
        public void IsRequested_OptOutValues_AreFalse(string value)
        {
            string? previous = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW");
            try
            {
                Environment.SetEnvironmentVariable("NAULTINUS_SHELLVIEW", value);
                Assert.False(ShellFolderViewHost.IsRequested());
            }
            finally
            {
                Environment.SetEnvironmentVariable("NAULTINUS_SHELLVIEW", previous);
            }
        }

        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("on")]
        public void IsRequested_ExplicitOn_IsTrue(string value)
        {
            string? previous = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW");
            try
            {
                Environment.SetEnvironmentVariable("NAULTINUS_SHELLVIEW", value);
                Assert.True(ShellFolderViewHost.IsRequested());
            }
            finally
            {
                Environment.SetEnvironmentVariable("NAULTINUS_SHELLVIEW", previous);
            }
        }

        [Fact]
        public void InteractionDefaults_AllowShellGestures()
        {
            // HwndHost exige un fil STA (xUnit tourne en MTA par défaut).
            StaRunner.Run(() =>
            {
                var host = new ShellFolderViewHost();
                Assert.True(host.AllowDoubleClick);
                Assert.True(host.AllowKeyboard);
                Assert.True(host.AllowContextMenu);
                Assert.True(host.AllowShellDrop);
                Assert.True(host.AllowShellDragOut);
                Assert.False(host.IsAvailable);
            });
        }
    }
}
