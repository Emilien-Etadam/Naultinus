using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Palisades.Helpers
{
    public static class UpdateChecker
    {
        private const string ReleasesUrl =
            "https://api.github.com/repos/Emilien-Etadam/Palisades-Navigation/releases/latest";

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
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(assetUrl))
                    return null;
                if (!IsTrustedReleaseAssetUrl(assetUrl))
                {
                    PalisadeDiagnostics.Log("UpdateChecker", "Asset de mise à jour refusé : " + assetUrl);
                    return null;
                }

                var notes = root.GetProperty("body").GetString() ?? "";
                return new UpdateInfo(remoteVersion, assetUrl, notes);
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("UpdateChecker", "La vérification des mises à jour a échoué.", ex);
                return null;
            }
        }

        public static async Task ApplyUpdateAsync(UpdateInfo update, IProgress<double>? progress = null)
        {
            if (!IsTrustedReleaseAssetUrl(update.AssetUrl))
                throw new InvalidOperationException("L'URL de l'installateur n'appartient pas aux releases Palisades attendues.");

            var tempInstaller = Path.Combine(Path.GetTempPath(), $"Palisades-{update.Version}-setup.exe");

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

            if (!VerifyInstallerSignature(tempInstaller, out var signatureError))
            {
                TryDeleteInstaller(tempInstaller);
                throw new InvalidOperationException("Signature de l'installateur invalide : " + signatureError);
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
                "/Emilien-Etadam/Palisades-Navigation/releases/download/",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyInstallerSignature(string filePath, out string error)
        {
            error = string.Empty;
            if (!OperatingSystem.IsWindows())
            {
                error = "vérification Authenticode indisponible hors Windows";
                return false;
            }

            var fileInfo = new WinTrustFileInfo(filePath);
            var data = new WinTrustData(fileInfo);
            var action = WintrustActionGenericVerifyV2;

            try
            {
                var result = WinVerifyTrust(IntPtr.Zero, ref action, data);
                if (result == 0)
                    return true;

                error = "WinVerifyTrust code 0x" + result.ToString("X8");
                PalisadeDiagnostics.Log("UpdateChecker", "Signature Authenticode refusée pour " + filePath + " : " + error);
                return false;
            }
            finally
            {
                data.Dispose();
            }
        }

        private static void TryDeleteInstaller(string tempInstaller)
        {
            try
            {
                File.Delete(tempInstaller);
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("UpdateChecker", "Suppression de l'installateur temporaire impossible : " + tempInstaller, ex);
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Palisades", GetCurrentVersion()));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        private static readonly Guid WintrustActionGenericVerifyV2 =
            new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern int WinVerifyTrust(
            IntPtr hwnd,
            ref Guid pgActionId,
            [In] WinTrustData pWvtData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class WinTrustFileInfo
        {
            public WinTrustFileInfo(string filePath)
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
                PcwszFilePath = filePath;
            }

            public uint CbStruct;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string PcwszFilePath;
            public IntPtr HFile = IntPtr.Zero;
            public IntPtr PgKnownSubject = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class WinTrustData : IDisposable
        {
            public WinTrustData(WinTrustFileInfo fileInfo)
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>();
                DwUIChoice = 2; // WTD_UI_NONE
                FdwRevocationChecks = 1; // WTD_REVOKE_WHOLECHAIN
                DwUnionChoice = 1; // WTD_CHOICE_FILE
                DwProvFlags = 0x100; // WTD_REVOCATION_CHECK_CHAIN
                PFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, PFile, false);
            }

            public uint CbStruct;
            public IntPtr PPolicyCallbackData = IntPtr.Zero;
            public IntPtr PSipClientData = IntPtr.Zero;
            public uint DwUIChoice;
            public uint FdwRevocationChecks;
            public uint DwUnionChoice;
            public IntPtr PFile;
            public uint DwStateAction = 0;
            public IntPtr HWvtStateData = IntPtr.Zero;
            public IntPtr PwszUrlReference = IntPtr.Zero;
            public uint DwProvFlags;
            public uint DwUIContext = 0;

            public void Dispose()
            {
                if (PFile != IntPtr.Zero)
                {
                    Marshal.DestroyStructure<WinTrustFileInfo>(PFile);
                    Marshal.FreeHGlobal(PFile);
                    PFile = IntPtr.Zero;
                }
            }
        }
    }

    public record UpdateInfo(string Version, string AssetUrl, string ReleaseNotes);
}
