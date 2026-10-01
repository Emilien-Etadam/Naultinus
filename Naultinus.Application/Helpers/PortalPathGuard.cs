using System;
using System.Collections.Generic;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Décide si un chemin reste dans le dossier racine d'un portail.
    /// La comparaison est lexicale (lecteur, UNC, <c>.</c>, <c>..</c>, préfixe <c>\\?\</c>)
    /// pour rester testable sans disque. Le résolveur optionnel applique le chemin final
    /// (jonction, lien) avant cette comparaison ; un échec du résolveur refuse le chemin.
    /// </summary>
    internal static class PortalPathGuard
    {
        private const char Separator = '\\';

        internal static bool IsAllowed(string? rootPath, string? candidatePath)
        {
            Func<string, string?>? resolve = OperatingSystem.IsWindows()
                ? ShellPathResolver.TryResolveFinal
                : null;
            return IsInsideRoot(rootPath, candidatePath, resolve);
        }

        internal static bool IsInsideRoot(string? rootPath, string? candidatePath, Func<string, string?>? resolveFinal = null)
        {
            if (!TryNormalize(rootPath, out string root))
                return false;
            if (!TryNormalize(candidatePath, out string candidate))
                return false;

            if (resolveFinal != null)
            {
                if (!TryApply(resolveFinal, root, out root))
                    return false;
                if (!TryApply(resolveFinal, candidate, out candidate))
                    return false;
            }

            return IsNormalizedInside(root, candidate);
        }

        internal static bool AreSame(string? left, string? right)
        {
            if (!TryNormalize(left, out string a) || !TryNormalize(right, out string b))
                return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryNormalize(string? path, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string text = path.Trim().Replace('/', Separator);
            if (text.Contains('\0'))
                return false;

            if (text.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                if (text.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    text = @"\\" + text[8..];
                else
                    text = text[4..];
            }

            bool unc = text.StartsWith(@"\\", StringComparison.Ordinal);
            string[] raw = text.Split(Separator);
            var stack = new List<string>();
            if (unc)
            {
                if (raw.Length < 4 || raw[2].Length == 0 || raw[3].Length == 0)
                    return false;
                stack.Add(raw[2]);
                stack.Add(raw[3]);
                for (int i = 4; i < raw.Length; i++)
                    Push(stack, raw[i], rootCount: 2);
                normalized = @"\\" + string.Join(Separator, stack);
                return true;
            }

            if (text.Length < 3 || text[1] != ':' || text[2] != Separator || !char.IsLetter(text[0]))
                return false;

            stack.Add(raw[0]);
            for (int i = 1; i < raw.Length; i++)
                Push(stack, raw[i], rootCount: 1);

            normalized = stack.Count == 1
                ? stack[0] + Separator
                : string.Join(Separator, stack);
            return true;
        }

        private static void Push(List<string> stack, string segment, int rootCount)
        {
            if (segment.Length == 0 || segment == ".")
                return;
            if (segment == "..")
            {
                if (stack.Count > rootCount)
                    stack.RemoveAt(stack.Count - 1);
                return;
            }

            stack.Add(segment);
        }

        private static bool TryApply(Func<string, string?> resolveFinal, string path, out string normalized)
        {
            normalized = string.Empty;
            string? resolved;
            try
            {
                resolved = resolveFinal(path);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("PortalPathGuard.Resolve", ex);
                return false;
            }

            return TryNormalize(resolved, out normalized);
        }

        private static bool IsNormalizedInside(string root, string candidate)
        {
            if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = root[root.Length - 1] == Separator ? root : root + Separator;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
