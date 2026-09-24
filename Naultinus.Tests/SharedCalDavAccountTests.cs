using System;
using System.IO;
using Naultinus.Helpers;
using Naultinus.Model;
using Naultinus.Services;
using Xunit;

namespace Naultinus.Tests
{
    public class SharedCalDavAccountTests
    {
        private const string Secret = "mot-de-passe-UNIQUE-naultinus-9f3a";

        [Fact]
        public void IsConfigured_RequiresHttpsAndUsername()
        {
            Assert.False(SharedCalDavAccount.IsConfigured(new AppSettings()));
            Assert.False(SharedCalDavAccount.IsConfigured(new AppSettings
            {
                CalDavBaseUrl = "http://example.test/dav/",
                CalDavUsername = "user",
            }));
            Assert.True(SharedCalDavAccount.IsConfigured(new AppSettings
            {
                CalDavBaseUrl = "https://example.test/dav/",
                CalDavUsername = "user",
            }));
        }

        [Fact]
        public void TryApply_RejectsMissingPasswordOnFirstSave_AndKeepsExistingCipher()
        {
            var settings = new AppSettings();
            Assert.False(SharedCalDavAccount.TryApply(settings, "https://example.test/dav/", "user", "", out var error));
            Assert.Equal(SharedCalDavAccountError.PasswordRequired, error);
            Assert.Equal(string.Empty, settings.CalDavEncryptedPassword);

            settings.CalDavEncryptedPassword = "cipher-deja-present";
            Assert.True(SharedCalDavAccount.TryApply(settings, "https://example.test/dav/", "user", "", out error));
            Assert.Equal("cipher-deja-present", settings.CalDavEncryptedPassword);
            Assert.Equal("https://example.test/dav/", settings.CalDavBaseUrl);
            Assert.Equal("user", settings.CalDavUsername);
        }

        [Fact]
        public void TryApply_RejectsHttp_AndDoesNotStorePlaintextPassword()
        {
            var settings = new AppSettings();
            Assert.False(SharedCalDavAccount.TryApply(settings, "http://example.test/dav/", "user", Secret, out var error));
            Assert.Equal(SharedCalDavAccountError.HttpsRequired, error);
            Assert.DoesNotContain(Secret, settings.CalDavBaseUrl ?? string.Empty);
            Assert.Equal(string.Empty, settings.CalDavEncryptedPassword);

            var assigned = SharedCalDavAccount.TryAssignPassword(settings, Secret, out error);
            Assert.NotEqual(Secret, settings.CalDavEncryptedPassword);
            Assert.DoesNotContain(Secret, settings.CalDavEncryptedPassword ?? string.Empty);
            if (assigned)
                Assert.Equal(SharedCalDavAccountError.None, error);
            else
                Assert.Equal(SharedCalDavAccountError.PasswordProtectFailed, error);
        }

        [Fact]
        public void TryAdoptLegacy_FirstAccountWins_AndRoundTripsWithoutPlaintext()
        {
            var directory = Path.Combine(Path.GetTempPath(), "naultinus-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.xml");
            try
            {
                var settings = new AppSettings { DefaultTabStyle = TabStyle.Flat };
                Assert.True(SharedCalDavAccount.TryAdoptLegacy(settings, "https://a.example/dav/", "a@exemple.test", "cipher-a"));
                Assert.False(SharedCalDavAccount.TryAdoptLegacy(settings, "https://b.example/dav/", "b@exemple.test", "cipher-b"));
                Assert.Equal("https://a.example/dav/", settings.CalDavBaseUrl);
                Assert.Equal("a@exemple.test", settings.CalDavUsername);

                AppSettingsStore.SaveTo(path, settings);
                var xml = File.ReadAllText(path);
                Assert.DoesNotContain(Secret, xml);
                Assert.Contains("cipher-a", xml, StringComparison.Ordinal);

                var loaded = AppSettingsStore.LoadFrom(path);
                Assert.True(SharedCalDavAccount.IsConfigured(loaded));
                Assert.Equal("a@exemple.test", loaded.CalDavUsername);
                Assert.Equal("cipher-a", loaded.CalDavEncryptedPassword);
                Assert.Equal(TabStyle.Flat, loaded.DefaultTabStyle);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void TryCopyFromZimbra_OverwritesSharedAccount_WithoutTouchingImapFields()
        {
            var account = new ZimbraAccount
            {
                Email = "user@exemple.test",
                CalDAVBaseUrl = "https://zimbra.exemple.test/dav/user/",
                EncryptedPassword = "cipher-zimbra",
                ImapHost = "imap.exemple.test",
            };
            var settings = new AppSettings
            {
                CalDavBaseUrl = "https://ancien.exemple.test/dav/",
                CalDavUsername = "ancien",
                CalDavEncryptedPassword = "cipher-ancien",
            };

            Assert.True(SharedCalDavAccount.TryCopyFromZimbra(settings, account, overwrite: true, out var error));
            Assert.Equal(SharedCalDavAccountError.None, error);
            Assert.Equal(account.CalDAVBaseUrl, settings.CalDavBaseUrl);
            Assert.Equal(account.Email, settings.CalDavUsername);
            Assert.Equal("cipher-zimbra", settings.CalDavEncryptedPassword);
            Assert.Equal("imap.exemple.test", account.ImapHost);
        }

        [Fact]
        public void Clear_RemovesTheSharedAccount()
        {
            var settings = new AppSettings
            {
                CalDavBaseUrl = "https://exemple.test/dav/",
                CalDavUsername = "user",
                CalDavEncryptedPassword = "cipher",
            };
            SharedCalDavAccount.Clear(settings);
            Assert.False(SharedCalDavAccount.IsConfigured(settings));
            Assert.Equal(string.Empty, settings.CalDavEncryptedPassword);
        }
    }
}
