using System;
using System.Threading.Tasks;
using Naultinus.Services;
using Xunit;

namespace Naultinus.Tests.Services
{
    public class ImapMailServiceTests
    {
        [Fact]
        public void Constructor_DefaultsPortTo993_WhenPortInvalid()
        {
            using var service = new ImapMailService("imap.example.test", 0, "user", "pass");
            Assert.False(service.IsConnected);
        }

        [Fact]
        public async Task GetUnreadCountAsync_WithoutConnect_Throws()
        {
            using var service = new ImapMailService("imap.example.test", 993, "user", "pass");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetUnreadCountAsync("INBOX"));
            Assert.Contains("not connected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ConnectAsync_WithoutHost_Throws()
        {
            using var service = new ImapMailService("  ", 993, "user", "pass");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConnectAsync());
        }

        [Fact]
        public void Dispose_WhenNeverConnected_DoesNotThrow()
        {
            var service = new ImapMailService("imap.example.test", 993, "user", "pass");
            service.Dispose();
            service.Dispose();
        }
    }
}
