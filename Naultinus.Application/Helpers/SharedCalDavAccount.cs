using Naultinus.Model;
using Naultinus.Properties;
using Naultinus.Services;
using System;
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
    /// Compte CalDAV unique, lu par tous les calendriers et toutes les tâches.
    /// Il est stocké dans settings.xml. Le courriel IMAP n'utilise pas ces champs.
    /// </summary>
    public static class SharedCalDavAccount
    {
        public const int MaxUrlLength = 2000;
        public const int MaxUsernameLength = 200;

        public static bool IsConfigured()
        {
            return IsConfigured(AppSettingsStore.Load());
        }

        public static bool IsConfigured(AppSettings? settings)
        {
            if (settings == null)
                return false;
            var url = (settings.CalDavBaseUrl ?? string.Empty).Trim();
            var user = (settings.CalDavUsername ?? string.Empty).Trim();
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && user.Length > 0;
        }

        public static string DescribeStatus(AppSettings? settings)
        {
            if (!IsConfigured(settings))
                return Strings.SharedCalDavNone;
            return string.Format(CultureInfo.CurrentCulture, Strings.SharedCalDavActiveFormat, settings!.CalDavUsername.Trim());
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
        /// Valide l'URL et l'identifiant. Un mot de passe vide est accepté seulement s'il en existe déjà un chiffré.
        /// </summary>
        public static bool TryValidate(AppSettings settings, string? url, string? username, string? password, out SharedCalDavAccountError error)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (!TryNormalize(url, username, out _, out _, out error))
                return false;

            var hasStoredPassword = !string.IsNullOrEmpty(settings.CalDavEncryptedPassword);
            if (string.IsNullOrEmpty(password) && !hasStoredPassword)
            {
                error = SharedCalDavAccountError.PasswordRequired;
                return false;
            }

            error = SharedCalDavAccountError.None;
            return true;
        }

        /// <summary>
        /// Écrit le compte dans les paramètres. Le mot de passe en clair n'est pas conservé :
        /// il passe par DPAPI, ou le précédent chiffré est gardé si le champ est vide.
        /// </summary>
        public static bool TryApply(AppSettings settings, string? url, string? username, string? password, out SharedCalDavAccountError error)
        {
            if (!TryValidate(settings, url, username, password, out error))
                return false;

            TryNormalize(url, username, out var normalizedUrl, out var normalizedUser, out _);
            settings.CalDavBaseUrl = normalizedUrl;
            settings.CalDavUsername = normalizedUser;
            if (string.IsNullOrEmpty(password))
                return true;

            return TryAssignPassword(settings, password, out error);
        }

        public static bool TryAssignPassword(AppSettings settings, string password, out SharedCalDavAccountError error)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (string.IsNullOrEmpty(password))
            {
                error = SharedCalDavAccountError.PasswordRequired;
                return false;
            }

            var encrypted = CredentialEncryptor.Encrypt(password);
            if (string.IsNullOrEmpty(encrypted) || string.Equals(encrypted, password, StringComparison.Ordinal))
            {
                error = SharedCalDavAccountError.PasswordProtectFailed;
                return false;
            }

            settings.CalDavEncryptedPassword = encrypted;
            error = SharedCalDavAccountError.None;
            return true;
        }

        public static void Clear(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.CalDavBaseUrl = string.Empty;
            settings.CalDavUsername = string.Empty;
            settings.CalDavEncryptedPassword = string.Empty;
        }

        /// <summary>
        /// Reprend une fois les identifiants déjà enregistrés sur une fenêtre, si aucun compte partagé n'existe.
        /// </summary>
        public static bool TryAdoptLegacy(AppSettings settings, string? url, string? username, string? encryptedPassword)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (IsConfigured(settings))
                return false;
            if (!TryNormalize(url, username, out var normalizedUrl, out var normalizedUser, out _))
                return false;

            settings.CalDavBaseUrl = normalizedUrl;
            settings.CalDavUsername = normalizedUser;
            settings.CalDavEncryptedPassword = encryptedPassword ?? string.Empty;
            return true;
        }

        /// <summary>
        /// Copie le CalDAV d'un compte Zimbra vers le compte partagé. N'efface pas les champs IMAP du compte Zimbra.
        /// </summary>
        public static bool TryCopyFromZimbra(AppSettings settings, ZimbraAccount? account, bool overwrite, out SharedCalDavAccountError error)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (account == null)
            {
                error = SharedCalDavAccountError.UrlRequired;
                return false;
            }

            if (!overwrite && IsConfigured(settings))
            {
                error = SharedCalDavAccountError.AlreadyConfigured;
                return false;
            }

            if (!TryNormalize(account.CalDAVBaseUrl, account.Email, out var url, out var user, out error))
                return false;
            if (string.IsNullOrEmpty(account.EncryptedPassword))
            {
                error = SharedCalDavAccountError.PasswordRequired;
                return false;
            }

            settings.CalDavBaseUrl = url;
            settings.CalDavUsername = user;
            settings.CalDavEncryptedPassword = account.EncryptedPassword;
            error = SharedCalDavAccountError.None;
            return true;
        }

        public static string ReadPassword(AppSettings? settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.CalDavEncryptedPassword))
                return string.Empty;
            return CredentialEncryptor.Decrypt(settings.CalDavEncryptedPassword);
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
