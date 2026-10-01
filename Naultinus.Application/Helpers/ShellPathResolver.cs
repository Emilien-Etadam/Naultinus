using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Chemin final Windows (jonctions et liens) pour le garde-fou du portail.
    /// </summary>
    internal static class ShellPathResolver
    {
        private const uint FileReadAttributes = 0x80;
        private const uint FileShareAll = 0x7;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;
        private static readonly IntPtr InvalidHandle = new(-1);

        internal static string? TryResolveFinal(string path)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
                return null;

            IntPtr handle = CreateFileW(ToExtendedIfNeeded(path), (int)FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
            if (handle == InvalidHandle || handle == IntPtr.Zero)
            {
                if (IsReparsePoint(path))
                    return null;
                return path;
            }

            try
            {
                var buffer = new StringBuilder(4096);
                int length = GetFinalPathNameByHandleW(handle, buffer, buffer.Capacity, 0);
                if (length <= 0)
                    return path;
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder(length);
                    length = GetFinalPathNameByHandleW(handle, buffer, buffer.Capacity, 0);
                    if (length <= 0 || length >= buffer.Capacity)
                        return path;
                }

                return buffer.ToString(0, length);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellPathResolver.IsReparsePoint", ex);
                return false;
            }
        }

        private static string ToExtendedIfNeeded(string path)
        {
            if (path.Length < 248 || path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + path[2..];
            return @"\\?\" + path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string fileName,
            int desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandleW(IntPtr handle, StringBuilder path, int capacity, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
