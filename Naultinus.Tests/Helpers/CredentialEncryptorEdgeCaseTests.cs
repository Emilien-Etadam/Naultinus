using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class CredentialEncryptorEdgeCaseTests
    {
        [Fact]
        public void Decrypt_Null_ReturnsEmpty()
        {
            Assert.Equal("", CredentialEncryptor.Decrypt(null!));
        }

        [Fact]
        public void Encrypt_Null_ReturnsEmpty()
        {
            Assert.Equal("", CredentialEncryptor.Encrypt(null!));
        }

        [Fact]
        public void RoundTrip_UnicodeString()
        {
            if (!System.OperatingSystem.IsWindows()) return;
            var original = "mot\u00e9cl\u00e9\ud83d\udd11";
            var encrypted = CredentialEncryptor.Encrypt(original);
            Assert.Equal(original, CredentialEncryptor.Decrypt(encrypted));
        }

        [Fact]
        public void RoundTrip_LongString()
        {
            if (!System.OperatingSystem.IsWindows()) return;
            var original = new string('A', 10000);
            var encrypted = CredentialEncryptor.Encrypt(original);
            Assert.Equal(original, CredentialEncryptor.Decrypt(encrypted));
        }
    }
}
