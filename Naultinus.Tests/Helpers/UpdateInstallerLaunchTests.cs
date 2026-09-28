using System;
using System.IO;
using Naultinus.Helpers;
using Xunit;

namespace Naultinus.Tests.Helpers
{
    public class UpdateInstallerLaunchTests
    {
        [Fact]
        public void SilentInstallerArguments_CloseFilesWithoutRestartManagerRelaunch()
        {
            var args = UpdateChecker.SilentInstallerArguments;

            Assert.Contains("/SILENT", args, StringComparison.Ordinal);
            Assert.Contains("/CLOSEAPPLICATIONS", args, StringComparison.Ordinal);
            Assert.Contains("/NORESTARTAPPLICATIONS", args, StringComparison.Ordinal);
            Assert.DoesNotContain("/VERYSILENT", args, StringComparison.Ordinal);

            var withoutNegatedRestart = args.Replace("/NORESTARTAPPLICATIONS", "", StringComparison.Ordinal);
            Assert.DoesNotContain("/RESTARTAPPLICATIONS", withoutNegatedRestart, StringComparison.Ordinal);

            var withoutClose = args.Replace("/CLOSEAPPLICATIONS", "", StringComparison.Ordinal);
            Assert.DoesNotContain("/NOCLOSEAPPLICATIONS", withoutClose, StringComparison.Ordinal);
        }

        [Fact]
        public void InstallerScript_SilentSetupLaunchesExactlyOneCopy()
        {
            var issPath = LocateRepoFile(Path.Combine("installer", "naultinus.iss"));
            var text = File.ReadAllText(issPath);

            Assert.Contains("CloseApplications=yes", text, StringComparison.Ordinal);
            Assert.Contains("RestartApplications=no", text, StringComparison.Ordinal);

            var runHeader = text.IndexOf("[Run]", StringComparison.Ordinal);
            Assert.True(runHeader >= 0, "Section [Run] absente.");
            var runBody = text[runHeader..];

            Assert.Contains("Flags: nowait postinstall", runBody, StringComparison.Ordinal);
            Assert.DoesNotContain("skipifsilent", runBody, StringComparison.Ordinal);
            Assert.Contains("\"{app}\\{#MyAppExeName}\"", runBody, StringComparison.Ordinal);
        }

        private static string LocateRepoFile(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException("Fichier du dépôt introuvable depuis le répertoire de test.", relativePath);
        }
    }
}
