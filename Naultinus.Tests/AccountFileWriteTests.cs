using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Naultinus.Model;
using Naultinus.Services;
using Xunit;

namespace Naultinus.Tests
{
    public class AccountFileWriteTests
    {
        private const string Cipher = "cipher-caldav-blob";
        private const string OtherCipher = "cipher-imap-blob";
        private const string ClearSecret = "mot-de-passe-UNIQUE-naultinus-9f3a";

        [Fact]
        public void WriteAtomic_IgnoresALockedLegacyTempFile()
        {
            var directory = CreateTempDirectory();
            try
            {
                var accountsPath = Path.Combine(directory, "accounts.xml");
                var legacyTemp = accountsPath + ".tmp";
                using (var held = new FileStream(legacyTemp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount("user@exemple.test", Cipher) });
                    Assert.Equal(0, held.Length);
                }

                var xml = File.ReadAllText(accountsPath);
                Assert.Contains(Cipher, xml, StringComparison.Ordinal);
                Assert.Contains("user@exemple.test", xml, StringComparison.Ordinal);
                Assert.DoesNotContain(ClearSecret, xml, StringComparison.Ordinal);
                Assert.Empty(Directory.GetFiles(directory, "accounts.xml.*.tmp"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void WriteAtomic_RetriesWhenTheDestinationIsBusy_ThenKeepsThePreviousFile()
        {
            var directory = CreateTempDirectory();
            try
            {
                var accountsPath = Path.Combine(directory, "accounts.xml");
                ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount("avant@exemple.test", Cipher) });
                var before = File.ReadAllText(accountsPath);

                using (new FileStream(accountsPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var watch = Stopwatch.StartNew();
                    var error = Assert.Throws<IOException>(() =>
                        ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount("pendant@exemple.test", OtherCipher) }));
                    watch.Stop();
                    Assert.InRange(watch.ElapsedMilliseconds, 120, 5000);
                    Assert.DoesNotContain(ClearSecret, error.Message, StringComparison.Ordinal);
                }

                Assert.Equal(before, File.ReadAllText(accountsPath));
                Assert.Contains(Cipher, before, StringComparison.Ordinal);
                Assert.DoesNotContain(OtherCipher, before, StringComparison.Ordinal);
                Assert.DoesNotContain(ClearSecret, before, StringComparison.Ordinal);
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

                ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount("apres@exemple.test", Cipher) });
                var after = File.ReadAllText(accountsPath);
                Assert.Contains("apres@exemple.test", after, StringComparison.Ordinal);
                Assert.Contains(Cipher, after, StringComparison.Ordinal);
                Assert.DoesNotContain(ClearSecret, after, StringComparison.Ordinal);
                Assert.DoesNotContain(OtherCipher, after, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void LoadMigrating_DoesNotSaveWhenNothingChanged()
        {
            var directory = CreateTempDirectory();
            try
            {
                var accountsPath = Path.Combine(directory, "accounts.xml");
                var settingsPath = Path.Combine(directory, "settings.xml");
                ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount("user@exemple.test", Cipher) });
                AppSettingsStore.SaveTo(settingsPath, new AppSettings());
                var before = File.ReadAllBytes(accountsPath);
                var writtenAt = File.GetLastWriteTimeUtc(accountsPath);

                var loaded = ZimbraAccountStore.LoadMigrating(accountsPath, settingsPath);
                var again = ZimbraAccountStore.LoadMigrating(accountsPath, settingsPath);

                Assert.Equal(Cipher, loaded[0].EncryptedPassword);
                Assert.Equal(Cipher, again[0].EncryptedPassword);
                Assert.Equal(before, File.ReadAllBytes(accountsPath));
                Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(accountsPath));
                Assert.DoesNotContain(ClearSecret, File.ReadAllText(accountsPath), StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void LoadMigrating_SavesARealMigrationOnce_AndKeepsTheBlob()
        {
            var directory = CreateTempDirectory();
            try
            {
                var accountsPath = Path.Combine(directory, "accounts.xml");
                var settingsPath = Path.Combine(directory, "settings.xml");
                AppSettingsStore.SaveTo(settingsPath, new AppSettings
                {
                    DefaultTabStyle = TabStyle.Flat,
                    CalDavBaseUrl = "https://zimbra.exemple.test/dav/emilien/",
                    CalDavUsername = "emilien@etadam.com",
                    CalDavEncryptedPassword = Cipher,
                });

                var loaded = ZimbraAccountStore.LoadMigrating(accountsPath, settingsPath);
                var accountsXml = File.ReadAllText(accountsPath);
                var settingsXml = File.ReadAllText(settingsPath);
                var writtenAt = File.GetLastWriteTimeUtc(accountsPath);

                Assert.Equal(Cipher, loaded[0].EncryptedPassword);
                Assert.True(loaded[0].UsedByCalendarsAndTasks);
                Assert.Contains(Cipher, accountsXml, StringComparison.Ordinal);
                Assert.DoesNotContain(Cipher, settingsXml, StringComparison.Ordinal);
                Assert.DoesNotContain(ClearSecret, accountsXml, StringComparison.Ordinal);
                Assert.Contains("Flat", settingsXml, StringComparison.Ordinal);

                var again = ZimbraAccountStore.LoadMigrating(accountsPath, settingsPath);

                Assert.Equal(Cipher, again[0].EncryptedPassword);
                Assert.Equal(accountsXml, File.ReadAllText(accountsPath));
                Assert.Equal(settingsXml, File.ReadAllText(settingsPath));
                Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(accountsPath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void LoadMigrating_KeepsReadAccountsWhenSaveCannotRun()
        {
            var directory = CreateTempDirectory();
            try
            {
                var accountsPath = Path.Combine(directory, "accounts.xml");
                var settingsPath = Path.Combine(directory, "settings.xml");
                ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount>
                {
                    new ZimbraAccount
                    {
                        Email = "autre@exemple.test",
                        CalDAVBaseUrl = "https://user:" + ClearSecret + "@autre.exemple.test/dav/",
                        EncryptedPassword = OtherCipher,
                        ImapHost = "imap.autre.test",
                    },
                });
                AppSettingsStore.SaveTo(settingsPath, new AppSettings
                {
                    DefaultTabStyle = TabStyle.Flat,
                    CalDavBaseUrl = "https://zimbra.exemple.test/dav/emilien/",
                    CalDavUsername = "emilien@etadam.com",
                    CalDavEncryptedPassword = Cipher,
                });
                var accountsBefore = File.ReadAllText(accountsPath);
                var settingsBefore = File.ReadAllText(settingsPath);

                File.SetAttributes(accountsPath, FileAttributes.ReadOnly);
                List<ZimbraAccount> loaded;
                try
                {
                    loaded = ZimbraAccountStore.LoadMigrating(accountsPath, settingsPath);
                }
                finally
                {
                    File.SetAttributes(accountsPath, FileAttributes.Normal);
                }

                Assert.Equal(2, loaded.Count);
                Assert.Equal(OtherCipher, loaded[0].EncryptedPassword);
                Assert.Equal("imap.autre.test", loaded[0].ImapHost);
                Assert.DoesNotContain(ClearSecret, loaded[0].CalDAVBaseUrl ?? string.Empty, StringComparison.Ordinal);
                Assert.Equal(Cipher, loaded[1].EncryptedPassword);
                Assert.DoesNotContain(ClearSecret, loaded[1].EncryptedPassword, StringComparison.Ordinal);
                Assert.Equal(accountsBefore, File.ReadAllText(accountsPath));
                Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
                Assert.Contains(Cipher, settingsBefore, StringComparison.Ordinal);
                Assert.Contains(OtherCipher, accountsBefore, StringComparison.Ordinal);

                var again = ZimbraAccountStore.LoadMigrating(accountsPath, settingsPath);

                Assert.Equal(accountsBefore, File.ReadAllText(accountsPath));
                Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
                Assert.Equal(OtherCipher, again[0].EncryptedPassword);
                Assert.Equal(Cipher, again[1].EncryptedPassword);
                Assert.DoesNotContain(ClearSecret, again[0].CalDAVBaseUrl ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(ClearSecret, again[1].EncryptedPassword, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task SaveTo_OverlappingWritersBothFinishWithoutLosingTheBlob()
        {
            var directory = CreateTempDirectory();
            try
            {
                var accountsPath = Path.Combine(directory, "accounts.xml");
                ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount("depart@exemple.test", Cipher) });
                var barrier = new Barrier(2);
                var errors = new List<Exception>();
                var gate = new object();
                var token = TestContext.Current.CancellationToken;

                void Write(string email)
                {
                    try
                    {
                        barrier.SignalAndWait(token);
                        ZimbraAccountStore.SaveTo(accountsPath, new List<ZimbraAccount> { MarkedAccount(email, Cipher) });
                    }
                    catch (Exception ex)
                    {
                        lock (gate)
                            errors.Add(ex);
                    }
                }

                await Task.WhenAll(
                    Task.Run(() => Write("un@exemple.test"), token),
                    Task.Run(() => Write("deux@exemple.test"), token));

                Assert.Empty(errors);
                var saved = Assert.Single(ZimbraAccountStore.LoadFrom(accountsPath));
                Assert.Equal(Cipher, saved.EncryptedPassword);
                Assert.DoesNotContain(ClearSecret, saved.EncryptedPassword, StringComparison.Ordinal);
                Assert.DoesNotContain(ClearSecret, File.ReadAllText(accountsPath), StringComparison.Ordinal);
                Assert.True(saved.Email is "un@exemple.test" or "deux@exemple.test");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static ZimbraAccount MarkedAccount(string email, string cipher)
        {
            return new ZimbraAccount
            {
                Email = email,
                CalDAVBaseUrl = "https://exemple.test/dav/",
                EncryptedPassword = cipher,
                UsedByCalendarsAndTasks = true,
            };
        }

        private static string CreateTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "naultinus-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
