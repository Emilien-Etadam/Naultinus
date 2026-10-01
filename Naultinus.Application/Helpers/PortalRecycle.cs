using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Envoie des chemins vers la corbeille. La confirmation est faite avant l'appel.
    /// </summary>
    internal static class PortalRecycle
    {
        private const uint Delete = 3;
        private const ushort AllowUndo = 0x0040;
        private const ushort NoConfirmation = 0x0010;

        internal static bool Send(IReadOnlyList<string> paths)
        {
            if (!OperatingSystem.IsWindows() || paths.Count == 0)
                return false;

            var from = new StringBuilder();
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                from.Append(path);
                from.Append('\0');
            }

            if (from.Length == 0)
                return false;

            from.Append('\0');
            // LPWStr s'arrête au premier nul : le tampon double-nul est alloué à la main.
            IntPtr buffer = Marshal.StringToHGlobalUni(from.ToString());
            try
            {
                var operation = new FileOperation
                {
                    Function = Delete,
                    From = buffer,
                    Flags = (ushort)(AllowUndo | NoConfirmation),
                };
                return SHFileOperation(ref operation) == 0 && !operation.Aborted;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileOperation
        {
            public IntPtr Owner;
            public uint Function;
            public IntPtr From;
            public IntPtr To;
            public ushort Flags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool Aborted;
            public IntPtr NameMappings;
            public IntPtr ProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        private static extern int SHFileOperation(ref FileOperation operation);
    }
}
