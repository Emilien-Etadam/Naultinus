using System;
using System.Collections.Generic;
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
        private const string CalDavCipher = "cipher-caldav-blob";
        private const string ImapCipher = "cipher-imap-blob";

        [Fact]
        public void IsUsable_RequiresHttpsAndUsername()
        {
            Assert.False(SharedCalDavAccount.IsUsable(null));
            Assert.False(SharedCalDavAccount.IsUsable(new ZimbraAccount
            {
                CalDAVBaseUrl = "http://example.test/dav/",
                Email = "user",
            }));
            Assert.True(SharedCalDavAccount.IsUsable(new ZimbraAccount
            {
                CalDAVBaseUrl = "https://example.test/dav/",
                Email = "user",
            }));
        }

        [Fact]
        public void TryApplyToAccount_RejectsMissingPasswordOnFirstSave_AndKeepsExistingCipher()
        {
            var account = new ZimbraAccount { ImapHost = "imap.exemple.test" };
            Assert.False(SharedCalDavAccount.TryApplyToAccount(account, "https://example.test/dav/", "user", "", out var error));
            Assert.Equal(SharedCalDavAccountError.PasswordRequired, error);
            Assert.Equal(string.Empty, account.EncryptedPassword);
            Assert.Equal("imap.exemple.test", account.ImapHost);

            account.EncryptedPassword = ImapCipher;
            Assert.True(SharedCalDavAccount.TryApplyToAccount(account, "https://example.test/dav/", "user", "", out error));
            Assert.Equal(ImapCipher, account.EncryptedPassword);
            Assert.Equal("https://example.test/dav/", account.CalDAVBaseUrl);
            Assert.Equal("user", account.Email);
            Assert.Equal("imap.exemple.test", account.ImapHost);
        }

        [Fact]
        public void TryApplyToAccount_RejectsHttp_AndDoesNotStorePlaintextPassword()
        {
            var account = new ZimbraAccount { ImapHost = "imap.exemple.test", EncryptedPassword = ImapCipher };
            Assert.False(SharedCalDavAccount.TryApplyToAccount(account, "http://example.test/dav/", "user", Secret, out var error));
            Assert.Equal(SharedCalDavAccountError.HttpsRequired, error);
            Assert.Equal(ImapCipher, account.EncryptedPassword);
            Assert.Equal("imap.exemple.test", account.ImapHost);
            Assert.DoesNotContain(Secret, account.CalDAVBaseUrl ?? string.Empty);

            var fresh = new ZimbraAccount { ImapHost = "imap.exemple.test" };
            var assigned = SharedCalDavAccount.TryApplyToAccount(fresh, "https://example.test/dav/", "user", Secret, out error);
            Assert.NotEqual(Secret, fresh.EncryptedPassword);
            Assert.DoesNotContain(Secret, fresh.EncryptedPassword ?? string.Empty);
            Assert.Equal("imap.exemple.test", fresh.ImapHost);
            if (assigned)
                Assert.Equal(SharedCalDavAccountError.None, error);
            else
            {
                Assert.Equal(SharedCalDavAccountError.PasswordProtectFailed, error);
                Assert.Equal(string.Empty, fresh.EncryptedPassword);
                Assert.Equal(string.Empty, fresh.CalDAVBaseUrl);
            }
        }

        [Fact]
        public void TryAbsorbIntoList_CopiesCipherOnce_ThenClearsSettings()
        {
            var directory = Path.Combine(Path.GetTempPath(), "naultinus-accounts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var settings = new AppSettings
                {
                    DefaultTabStyle = TabStyle.Flat,
                    CalDavBaseUrl = "https://zimbra.exemple.test/dav/emilien/",
                    CalDavUsername = "emilien@etadam.com",
                    CalDavEncryptedPassword = CalDavCipher,
                };
                var accounts = new List<ZimbraAccount>();

                Assert.True(SharedCalDavAccount.TryAbsorbIntoList(settings, accounts));
                Assert.False(SharedCalDavAccount.TryAbsorbIntoList(settings, accounts));
                Assert.Single(accounts);
                Assert.Equal("emilien@etadam.com", accounts[0].Email);
                Assert.Equal(CalDavCipher, accounts[0].EncryptedPassword);
                Assert.True(accounts[0].UsedByCalendarsAndTasks);
                Assert.Equal(string.Empty, settings.CalDavBaseUrl);
                Assert.Equal(string.Empty, settings.CalDavUsername);
                Assert.Equal(string.Empty, settings.CalDavEncryptedPassword);
                Assert.Equal(TabStyle.Flat, settings.DefaultTabStyle);
                Assert.Equal(accounts[0], SharedCalDavAccount.FindMarked(accounts));

                var settingsPath = Path.Combine(directory, "settings.xml");
                var accountsPath = Path.Combine(directory, "accounts.xml");
                AppSettingsStore.SaveTo(settingsPath, settings);
                ZimbraAccountStore.SaveTo(accountsPath, accounts);
                var settingsXml = File.ReadAllText(settingsPath);
                var accountsXml = File.ReadAllText(accountsPath);
                Assert.DoesNotContain(Secret, settingsXml, StringComparison.Ordinal);
                Assert.DoesNotContain(Secret, accountsXml, StringComparison.Ordinal);
                Assert.DoesNotContain(CalDavCipher, settingsXml, StringComparison.Ordinal);
                Assert.Contains(CalDavCipher, accountsXml, StringComparison.Ordinal);

                var loaded = ZimbraAccountStore.LoadFrom(accountsPath);
                Assert.Equal(CalDavCipher, loaded[0].EncryptedPassword);
                Assert.True(loaded[0].UsedByCalendarsAndTasks);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void TryAbsorbIntoList_DoesNotOverwriteExistingPasswordOrImapHost()
        {
            var existing = new ZimbraAccount
            {
                Email = "emilien@etadam.com",
                CalDAVBaseUrl = "https://zimbra.exemple.test/dav/emilien/",
                EncryptedPassword = ImapCipher,
                ImapHost = "ssl0.ovh.net",
            };
            var settings = new AppSettings
            {
                CalDavBaseUrl = "https://zimbra.exemple.test/dav/emilien",
                CalDavUsername = "Emilien@Etadam.com",
                CalDavEncryptedPassword = CalDavCipher,
            };
            var accounts = new List<ZimbraAccount> { existing };

            Assert.True(SharedCalDavAccount.TryAbsorbIntoList(settings, accounts));
            Assert.Single(accounts);
            Assert.Equal(ImapCipher, existing.EncryptedPassword);
            Assert.Equal("ssl0.ovh.net", existing.ImapHost);
            Assert.Equal("https://zimbra.exemple.test/dav/emilien/", existing.CalDAVBaseUrl);
            Assert.True(existing.UsedByCalendarsAndTasks);
            Assert.Equal(string.Empty, settings.CalDavEncryptedPassword);
            Assert.DoesNotContain(CalDavCipher, existing.EncryptedPassword, StringComparison.Ordinal);
        }

        [Fact]
        public void TryAbsorbIntoList_AddsARowWhenIdentityDiffers_WithoutTouchingTheOtherBlob()
        {
            var existing = new ZimbraAccount
            {
                Email = "autre@exemple.test",
                CalDAVBaseUrl = "https://autre.exemple.test/dav/",
                EncryptedPassword = ImapCipher,
                ImapHost = "imap.autre.test",
                UsedByCalendarsAndTasks = true,
            };
            var settings = new AppSettings
            {
                CalDavBaseUrl = "https://zimbra.exemple.test/dav/emilien/",
                CalDavUsername = "emilien@etadam.com",
                CalDavEncryptedPassword = CalDavCipher,
            };
            var accounts = new List<ZimbraAccount> { existing };

            Assert.True(SharedCalDavAccount.TryAbsorbIntoList(settings, accounts));
            Assert.Equal(2, accounts.Count);
            Assert.Equal(ImapCipher, existing.EncryptedPassword);
            Assert.Equal("imap.autre.test", existing.ImapHost);
            Assert.False(existing.UsedByCalendarsAndTasks);
            Assert.Equal(CalDavCipher, accounts[1].EncryptedPassword);
            Assert.True(accounts[1].UsedByCalendarsAndTasks);
            Assert.Equal(string.Empty, accounts[1].ImapHost);
        }

        [Fact]
        public void TryAdoptWindowCredentials_DoesNotReplaceAnExistingBlob()
        {
            var existing = new ZimbraAccount
            {
                Email = "a@exemple.test",
                CalDAVBaseUrl = "https://a.example/dav/",
                EncryptedPassword = ImapCipher,
                ImapHost = "imap.exemple.test",
            };
            var accounts = new List<ZimbraAccount> { existing };

            Assert.True(SharedCalDavAccount.TryAdoptWindowCredentials(accounts, "https://a.example/dav", "A@exemple.test", CalDavCipher));
            Assert.Single(accounts);
            Assert.Equal(ImapCipher, existing.EncryptedPassword);
            Assert.Equal("imap.exemple.test", existing.ImapHost);
            Assert.True(existing.UsedByCalendarsAndTasks);

            Assert.False(SharedCalDavAccount.TryAdoptWindowCredentials(accounts, "https://b.example/dav/", "b@exemple.test", "cipher-b"));
            Assert.Single(accounts);
        }

        [Fact]
        public void TryMarkExisting_DoesNotCopyThePassword()
        {
            var account = new ZimbraAccount
            {
                Email = "user@exemple.test",
                CalDAVBaseUrl = "https://zimbra.exemple.test/dav/user/",
                EncryptedPassword = ImapCipher,
                ImapHost = "imap.exemple.test",
            };
            var accounts = new List<ZimbraAccount> { account };

            Assert.True(SharedCalDavAccount.TryMarkExisting(accounts, account.Id));
            Assert.Equal(ImapCipher, account.EncryptedPassword);
            Assert.Equal("imap.exemple.test", account.ImapHost);
            Assert.True(account.UsedByCalendarsAndTasks);

            SharedCalDavAccount.UnmarkAll(accounts);
            Assert.False(account.UsedByCalendarsAndTasks);
            Assert.Equal(ImapCipher, account.EncryptedPassword);
            Assert.Null(SharedCalDavAccount.FindMarked(accounts));
        }
    }
}
