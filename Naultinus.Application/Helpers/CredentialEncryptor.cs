using System;
using System.Security.Cryptography;
using System.Text;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Chiffrement des secrets (mots de passe) via DPAPI (Phase 3.2).
    /// Déchiffrable uniquement par la session Windows du même utilisateur.
    /// </summary>
    public static class CredentialEncryptor
    {
        private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Naultinus.v1");

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var protectedBytes = ProtectedData.Protect(bytes, OptionalEntropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("CredentialEncryptor", "Encrypt a échoué (DPAPI).", ex);
                return string.Empty;
            }
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;
            try
            {
                var protectedBytes = Convert.FromBase64String(cipherText);
                var bytes = ProtectedData.Unprotect(protectedBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("CredentialEncryptor", "Decrypt a échoué (données corrompues ou autre profil utilisateur).", ex);
                return string.Empty;
            }
        }
    }
}
