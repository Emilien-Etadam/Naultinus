using System;
using System.Runtime.InteropServices;

namespace Naultinus.Helpers.Native
{
    /// <summary>
    /// Filtre de nom pour <c>IExplorerBrowser</c> : ne garde que les éléments dont le libellé
    /// contient la requête (insensible à la casse). Requête vide = tout afficher.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid("B7E4C2A1-9F3D-4E8A-A1C6-5D2E8F0B1A47")]
    internal sealed class PortalFolderNameFilter : IFolderFilter
    {
        private string _query = "";

        public string Query
        {
            get => _query;
            set => _query = (value ?? string.Empty).Trim();
        }

        public int ShouldShow(IntPtr shellFolder, IntPtr pidlFolder, IntPtr pidlItem)
        {
            if (string.IsNullOrEmpty(_query))
                return 0; // S_OK : afficher

            string? name = TryGetDisplayName(pidlFolder, pidlItem);
            if (string.IsNullOrEmpty(name))
                return 0;

            return name.Contains(_query, StringComparison.OrdinalIgnoreCase) ? 0 : 1; // S_FALSE : masquer
        }

        public int GetEnumFlags(IntPtr shellFolder, IntPtr pidlFolder, out IntPtr hwnd, ref uint flags)
        {
            hwnd = IntPtr.Zero;
            return 0;
        }

        private static string? TryGetDisplayName(IntPtr pidlFolder, IntPtr pidlItem)
        {
            IntPtr absolute = IntPtr.Zero;
            IntPtr namePtr = IntPtr.Zero;
            try
            {
                absolute = ShellBrowserNative.ILCombine(pidlFolder, pidlItem);
                if (absolute == IntPtr.Zero)
                    return null;

                if (ShellBrowserNative.SHGetNameFromIDList(
                        absolute,
                        ShellBrowserNative.SigdnNormalDisplay,
                        out namePtr) != 0
                    || namePtr == IntPtr.Zero)
                    return null;

                return Marshal.PtrToStringUni(namePtr);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (namePtr != IntPtr.Zero)
                    ShellBrowserNative.CoTaskMemFree(namePtr);
                if (absolute != IntPtr.Zero)
                    ShellBrowserNative.ILFree(absolute);
            }
        }
    }
}
