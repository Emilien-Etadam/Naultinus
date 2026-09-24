using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Naultinus.Helpers
{
    /// <summary>Erreur de saisie du compte CalDAV unique.</summary>
    public enum SharedCalDavAccountError
    {
        None = 0,
        UrlRequired,
        HttpsRequired,
        UrlTooLong,
        UsernameRequired,
        UsernameTooLong,
        PasswordRequired,
        PasswordProtectFailed,
        AlreadyConfigured,
    }

    /// <summary>
    /// Compte CalDAV des calendriers et des tâches : une ligne de accounts.xml, marquée
    /// <see cref="ZimbraAccount.UsedByCalendarsAndTasks"/>. Le mot de passe reste le blob
    /// du compte. settings.xml n'en garde pas une copie.
    /// </summary>
    public static class SharedCalDavAccount
    {
        public const int MaxUrlLength = 2000;
        public const int MaxUsernameLength = 200;

        public static bool IsConfigured()
        {
            return GetMarked() != null;
        }

        public static bool IsUsable(ZimbraAccount? account)
        {
            return IsUsable(account, out _);
        }

        public static bool IsUsable(ZimbraAccount? account, out SharedCalDavAccountError error)
        {
            if (account == null)
            {
                error = SharedCalDavAccountError.UrlRequired;
                return false;
            }

            return TryNormalize(account.CalDAVBaseUrl, account.Email, out _, out _, out error);
        }

        /// <summary>Ligne marquée pour les calendriers et les tâches, après migration éventuelle.</summary>
        public static ZimbraAccount? GetMarked()
        {
            return FindMarked(ZimbraAccountStore.Load());
        }

        public static ZimbraAccount? FindMarked(IEnumerable<ZimbraAccount>? accounts)
        {
            if (accounts == null)
                return null;
            foreach (var account in accounts)
            {
                if (account.UsedByCalendarsAndTasks && IsUsable(account))
                    return account;
            }

            return null;
        }

        public static string DescribeStatus()
        {
            return DescribeStatus(GetMarked());
        }

        public static string DescribeStatus(ZimbraAccount? account)
        {
            if (!IsUsable(account))
                return Strings.SharedCalDavNone;
            return string.Format(CultureInfo.CurrentCulture, Strings.SharedCalDavActiveFormat, account!.Email.Trim());
        }

        public static string Describe(SharedCalDavAccountError error)
        {
            return error switch
            {
                SharedCalDavAccountError.UrlRequired => Strings.CaldavEnterBaseUrl,
                SharedCalDavAccountError.HttpsRequired => Strings.CaldavHttpsRequired,
                SharedCalDavAccountError.UrlTooLong => Strings.TextTooLong,
                SharedCalDavAccountError.UsernameRequired => Strings.MailEnterUsername,
                SharedCalDavAccountError.UsernameTooLong => Strings.TitleTooLong,
                SharedCalDavAccountError.PasswordRequired => Strings.SharedCalDavPasswordRequired,
                SharedCalDavAccountError.PasswordProtectFailed => Strings.SharedCalDavPasswordProtectFailed,
                SharedCalDavAccountError.AlreadyConfigured => Strings.SharedCalDavAlreadyConfigured,
                _ => string.Empty,
            };
        }

        /// <summary>
        /// Écrit l'URL et l'identifiant sur le compte. Un mot de passe vide conserve le blob.
        /// Un mot de passe saisi est chiffré. L'hôte IMAP n'est pas modifié.
        /// </summary>
        public static bool TryApplyToAccount(ZimbraAccount account, string? url, string? username, string? password, out SharedCalDavAccountError error)
        {
            ArgumentNullException.ThrowIfNull(account);
            if (!TryNormalize(url, username, out var normalizedUrl, out var normalizedUser, out error))
                return false;

            if (string.IsNullOrEmpty(password))
            {
                if (string.IsNullOrEmpty(account.EncryptedPassword))
                {
                    error = SharedCalDavAccountError.PasswordRequired;
                    return false;
                }

                account.CalDAVBaseUrl = normalizedUrl;
                account.Email = normalizedUser;
                error = SharedCalDavAccountError.None;
                return true;
            }

            if (!TryProtect(password, out var encrypted, out error))
                return false;

            account.CalDAVBaseUrl = normalizedUrl;
            account.Email = normalizedUser;
            account.EncryptedPassword = encrypted;
            error = SharedCalDavAccountError.None;
            return true;
        }

        /// <summary>
        /// Au premier chargement : si settings.xml a encore un compte CalDAV absent de la liste,
        /// ajoute la ligne en copiant le blob chiffré. Une ligne déjà présente n'a pas son blob
        /// remplacé. Les champs CalDAV de settings sont ensuite vidés.
        /// </summary>
        public static bool TryAbsorbIntoList(AppSettings settings, IList<ZimbraAccount> accounts)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(accounts);
            if (!LegacySettingsConfigured(settings))
                return false;

            var url = settings.CalDavBaseUrl.Trim();
            var user = settings.CalDavUsername.Trim();
            var blob = settings.CalDavEncryptedPassword ?? string.Empty;
            var match = FindByIdentity(accounts, url, user);
            if (match == null)
            {
                match = new ZimbraAccount
                {
                    Email = user,
                    CalDAVBaseUrl = url,
                    EncryptedPassword = blob,
                };
                accounts.Add(match);
            }
            else if (string.IsNullOrEmpty(match.EncryptedPassword) && !string.IsNullOrEmpty(blob))
            {
                match.EncryptedPassword = blob;
            }

            if (!IsSoleMarked(accounts, match))
                MarkExclusive(accounts, match.Id);

            settings.CalDavBaseUrl = string.Empty;
            settings.CalDavUsername = string.Empty;
            settings.CalDavEncryptedPassword = string.Empty;
            return true;
        }

        /// <summary>
        /// Reprend les identifiants d'une ancienne fenêtre si aucun compte n'est encore marqué.
        /// Copie le blob. N'écrase pas le mot de passe d'une ligne déjà présente.
        /// </summary>
        public static bool TryAdoptWindowCredentials(IList<ZimbraAccount> accounts, string? url, string? username, string? encryptedPassword)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            if (FindMarked(accounts) != null)
                return false;
            if (!TryNormalize(url, username, out var normalizedUrl, out var normalizedUser, out _))
                return false;

            var match = FindByIdentity(accounts, normalizedUrl, normalizedUser);
            if (match != null)
            {
                if (string.IsNullOrEmpty(match.EncryptedPassword) && !string.IsNullOrEmpty(encryptedPassword))
                    match.EncryptedPassword = encryptedPassword;
                MarkExclusive(accounts, match.Id);
                return true;
            }

            accounts.Add(new ZimbraAccount
            {
                Email = normalizedUser,
                CalDAVBaseUrl = normalizedUrl,
                EncryptedPassword = encryptedPassword ?? string.Empty,
                UsedByCalendarsAndTasks = true,
            });
            return true;
        }

        /// <summary>Marque un compte déjà dans la liste, sans copier son mot de passe.</summary>
        public static bool TryMarkExisting(IList<ZimbraAccount> accounts, Guid id)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            if (FindMarked(accounts) != null)
                return false;

            ZimbraAccount? match = null;
            foreach (var account in accounts)
            {
                if (account.Id == id)
                {
                    match = account;
                    break;
                }
            }

            if (!IsUsable(match))
                return false;

            MarkExclusive(accounts, match!.Id);
            return true;
        }

        public static void MarkExclusive(IList<ZimbraAccount> accounts, Guid id)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            foreach (var account in accounts)
                account.UsedByCalendarsAndTasks = account.Id == id;
        }

        public static void UnmarkAll(IList<ZimbraAccount> accounts)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            foreach (var account in accounts)
                account.UsedByCalendarsAndTasks = false;
        }

        public static string ReadPassword(ZimbraAccount? account)
        {
            if (account == null || string.IsNullOrEmpty(account.EncryptedPassword))
                return string.Empty;
            return CredentialEncryptor.Decrypt(account.EncryptedPassword);
        }

        private static bool LegacySettingsConfigured(AppSettings settings)
        {
            var url = (settings.CalDavBaseUrl ?? string.Empty).Trim();
            var user = (settings.CalDavUsername ?? string.Empty).Trim();
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && user.Length > 0;
        }

        private static bool IsSoleMarked(IList<ZimbraAccount> accounts, ZimbraAccount match)
        {
            if (!match.UsedByCalendarsAndTasks || !IsUsable(match))
                return false;
            foreach (var account in accounts)
            {
                if (account.Id != match.Id && account.UsedByCalendarsAndTasks)
                    return false;
            }

            return true;
        }

        private static ZimbraAccount? FindByIdentity(IList<ZimbraAccount> accounts, string url, string user)
        {
            var normalizedUrl = NormalizeIdentityUrl(url);
            var normalizedUser = user.Trim();
            foreach (var account in accounts)
            {
                if (string.Equals(NormalizeIdentityUrl(account.CalDAVBaseUrl), normalizedUrl, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(account.Email.Trim(), normalizedUser, StringComparison.OrdinalIgnoreCase))
                    return account;
            }

            return null;
        }

        private static string NormalizeIdentityUrl(string? url)
        {
            return (url ?? string.Empty).Trim().TrimEnd('/');
        }

        private static bool TryProtect(string password, out string encrypted, out SharedCalDavAccountError error)
        {
            encrypted = CredentialEncryptor.Encrypt(password);
            if (string.IsNullOrEmpty(encrypted) || string.Equals(encrypted, password, StringComparison.Ordinal))
            {
                encrypted = string.Empty;
                error = SharedCalDavAccountError.PasswordProtectFailed;
                return false;
            }

            error = SharedCalDavAccountError.None;
            return true;
        }

        private static bool TryNormalize(string? url, string? username, out string normalizedUrl, out string normalizedUser, out SharedCalDavAccountError error)
        {
            normalizedUrl = (url ?? string.Empty).Trim();
            normalizedUser = (username ?? string.Empty).Trim();
            if (normalizedUrl.Length == 0)
            {
                error = SharedCalDavAccountError.UrlRequired;
                return false;
            }

            if (!normalizedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                error = SharedCalDavAccountError.HttpsRequired;
                return false;
            }

            if (normalizedUrl.Length > MaxUrlLength)
            {
                error = SharedCalDavAccountError.UrlTooLong;
                return false;
            }

            if (normalizedUser.Length == 0)
            {
                error = SharedCalDavAccountError.UsernameRequired;
                return false;
            }

            if (normalizedUser.Length > MaxUsernameLength)
            {
                error = SharedCalDavAccountError.UsernameTooLong;
                return false;
            }

            error = SharedCalDavAccountError.None;
            return true;
        }
    }
}
