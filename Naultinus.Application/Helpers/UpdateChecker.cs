using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Naultinus.Helpers
{
    public static class UpdateChecker
    {
        private const string ReleasesUrl =
            "https://api.github.com/repos/Emilien-Etadam/Naultinus/releases/latest";

        public static string GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly()
                       .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                       ?.InformationalVersion?.Split('+')[0]
                   ?? "0.0.0";
        }

        public static async Task<UpdateInfo?> CheckAsync()
        {
            try
            {
                using var client = CreateClient();
                var json = await client.GetStringAsync(ReleasesUrl).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? "";
                var remoteVersion = tagName.TrimStart('v', 'V');

                if (!Version.TryParse(remoteVersion, out var remote) ||
                    !Version.TryParse(GetCurrentVersion(), out var current) ||
                    remote <= current)
                    return null;

                string? assetUrl = null;
                string? assetDigest = null;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = asset.GetProperty("browser_download_url").GetString();
                        if (asset.TryGetProperty("digest", out var digest))
                            assetDigest = digest.GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(assetUrl))
                    return null;
                if (!IsTrustedReleaseAssetUrl(assetUrl))
                {
                    NaultinusDiagnostics.Log("UpdateChecker", "Asset de mise à jour refusé : " + assetUrl);
                    return null;
                }

                var notes = root.GetProperty("body").GetString() ?? "";
                return new UpdateInfo(remoteVersion, assetUrl, notes, assetDigest);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("UpdateChecker", "La vérification des mises à jour a échoué.", ex);
                return null;
            }
        }

        public static async Task ApplyUpdateAsync(UpdateInfo update, IProgress<double>? progress = null)
        {
            if (!IsTrustedReleaseAssetUrl(update.AssetUrl))
                throw new InvalidOperationException("L'URL de l'installateur n'appartient pas aux releases Naultinus attendues.");

            var tempInstaller = Path.Combine(Path.GetTempPath(), $"Naultinus-{update.Version}-setup.exe");

            using (var client = CreateClient())
            {
                using var response = await client.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var fileStream = new FileStream(tempInstaller, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                        progress?.Report((double)totalRead / totalBytes * 100);
                }
            }

            if (!await VerifyInstallerHashAsync(tempInstaller, update.Sha256).ConfigureAwait(false))
            {
                TryDeleteInstaller(tempInstaller);
                throw new InvalidOperationException("Empreinte SHA-256 de l'installateur invalide (fichier corrompu ou altéré).");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = tempInstaller,
                Arguments = "/SILENT /RESTARTAPPLICATIONS",
                UseShellExecute = true
            });
        }

        private static bool IsTrustedReleaseAssetUrl(string assetUrl)
        {
            if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var uri))
                return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            return uri.AbsolutePath.StartsWith(
                "/Emilien-Etadam/Naultinus/releases/download/",
                StringComparison.OrdinalIgnoreCase);
        }

        // Vérifie l'intégrité de l'installateur en comparant son empreinte SHA-256 à celle
        // publiée par l'API GitHub (champ "digest" de l'asset, récupéré via TLS sur api.github.com).
        // En l'absence d'empreinte, la mise à jour est refusée (échec fermé).
        private static async Task<bool> VerifyInstallerHashAsync(string filePath, string? expectedDigest)
        {
            if (string.IsNullOrWhiteSpace(expectedDigest))
            {
                NaultinusDiagnostics.Log("UpdateChecker", "Empreinte SHA-256 absente des métadonnées GitHub ; mise à jour refusée.");
                return false;
            }

            // Le champ digest de l'API GitHub a la forme "sha256:<hex>".
            var expected = expectedDigest;
            var separator = expected.IndexOf(':');
            if (separator >= 0)
                expected = expected[(separator + 1)..];

            string actual;
            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
            {
                var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
                actual = Convert.ToHexString(hash);
            }

            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            NaultinusDiagnostics.Log("UpdateChecker", "Empreinte SHA-256 incohérente. Attendu=" + expected + " Obtenu=" + actual);
            return false;
        }

        private static void TryDeleteInstaller(string tempInstaller)
        {
            try
            {
                File.Delete(tempInstaller);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("UpdateChecker", "Suppression de l'installateur temporaire impossible : " + tempInstaller, ex);
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Naultinus", GetCurrentVersion()));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }
    }

    public record UpdateInfo(string Version, string AssetUrl, string ReleaseNotes, string? Sha256);
}
