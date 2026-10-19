using System;
using System.Runtime.InteropServices;

namespace Naultinus.Helpers.Native
{
    /// <summary>
    /// Déclarations COM de l'hôte de vue d'éléments du shell (<c>IExplorerBrowser</c>, shobjidl_core.h),
    /// qui affiche le contenu d'un dossier avec le rendu de l'Explorateur Windows.
    /// Signatures, ordre des méthodes et identifiants repris de l'API documentée par Microsoft.
    /// </summary>
    internal static class ShellBrowserNative
    {
        /// <summary>CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER.</summary>
        internal const uint ClsContext = 0x00000001 | 0x00000004;

        /// <summary>GW_CHILD : premier enfant de la fenêtre hôte.</summary>
        internal const uint GwChild = 5;

        /// <summary>GW_HWNDNEXT : frère suivant, pour parcourir les enfants.</summary>
        internal const uint GwHwndNext = 2;

        /// <summary>CLSID_ExplorerBrowser : l'implémentation fournie par le shell.</summary>
        internal static readonly Guid ClsidExplorerBrowser = new Guid("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// <c>FOLDERSETTINGS</c> : mode de vue et attributs d'affichage demandés à la création.
        /// Un pointeur nul laisse le shell appliquer les réglages mémorisés pour le dossier.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FolderSettings
        {
            public int ViewMode;
            public uint Flags;
        }

        #endregion

        #region Options et indicateurs

        /// <summary>
        /// <c>FOLDERVIEWMODE</c> : mode d'affichage de la vue d'éléments. Valeurs de l'en-tête
        /// <c>shobjidl_core.h</c> — attention, le mode « détails » vaut 4, pas 3.
        /// </summary>
        internal static class FolderViewModes
        {
            public const uint Icon = 1;
            public const uint SmallIcon = 2;
            public const uint List = 3;
            public const uint Details = 4;
            public const uint Thumbnail = 5;
            public const uint Tile = 6;
            public const uint Content = 8;
        }

        /// <summary>
        /// <c>FOLDERFLAGS</c> : attributs demandés à la vue. <c>FWF_NOHEADERINALLVIEWS</c> affiche
        /// les titres de colonnes dans tous les modes, <c>FWF_NOCOLUMNHEADER</c> les masque.
        /// </summary>
        internal static class FolderViewFlags
        {
            public const uint NoColumnHeader = 0x00800000;
            public const uint NoHeaderInAllViews = 0x01000000;
            public const uint FullRowSelect = 0x00200000;
        }

        /// <summary>Catégorie du PIDL passée à <c>BrowseToIDList</c> (SBSP_*).</summary>
        internal static class BrowseFlags
        {
            internal const uint Absolute = 0x00000000;
            internal const uint Relative = 0x00001000;
            internal const uint Parent = 0x00002000;
            internal const uint NavigateBack = 0x00004000;
            internal const uint NavigateForward = 0x00008000;
            internal const uint KeepWordWheelText = 0x00040000;
            internal const uint ActivateNoFocus = 0x00080000;
            internal const uint CreateNoHistory = 0x00100000;
            internal const uint PlayNoSound = 0x00200000;
        }

        /// <summary>Options de l'hôte (EXPLORER_BROWSER_OPTIONS), passées à <c>SetOptions</c>.</summary>
        internal static class ExplorerBrowserOptions
        {
            internal const uint None = 0x00000000;
            internal const uint NavigateOnce = 0x00000001;
            internal const uint ShowFrames = 0x00000002;
            internal const uint AlwaysNavigate = 0x00000004;
            internal const uint NoTravelLog = 0x00000008;
            internal const uint NoWrapperWindow = 0x00000010;
            internal const uint HtmlSharePointView = 0x00000020;
            internal const uint NoBorder = 0x00000040;
            internal const uint NoPersistViewState = 0x00000080;
        }

        /// <summary>Indicateurs de <c>FillFromObject</c> (EXPLORER_BROWSER_FILL_FLAGS).</summary>
        internal static class FillFlags
        {
            internal const uint None = 0x00000000;
            internal const uint SelectFromDataObject = 0x00000100;
            internal const uint NoDropTarget = 0x00000200;
        }

        #endregion

        #region Thème et chemins

        /// <summary><c>SIGDN_FILESYSPATH</c> : chemin complet, pour les éléments du système de fichiers.</summary>
        internal const uint SigdnFilePath = 0x80058000;

        /// <summary><c>SVGIO_ALLVIEW</c> : tous les éléments de la vue, pour <c>IFolderView::ItemCount</c>.</summary>
        internal const uint SvgIoAllView = 0x00000002;

        /// <summary>
        /// Indicateurs <c>SVSIF</c> pour un déplacement de sélection au clavier (↑/↓) :
        /// sélectionner, défocaliser les autres, assurer la visibilité, focus et marque.
        /// </summary>
        internal const uint SvsiKeyboardMove =
            0x00000001 // SVSI_SELECT
            | 0x00000004 // SVSI_DESELECTOTHERS
            | 0x00000008 // SVSI_ENSUREVISIBLE
            | 0x00000010 // SVSI_FOCUSED
            | 0x00000040; // SVSI_SELECTIONMARK

        [DllImport("ole32.dll")]
        internal static extern void CoTaskMemFree(IntPtr pv);

        #endregion

        #region P/Invoke

        [DllImport("ole32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int CoCreateInstance(
            [In] ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHParseDisplayName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            IntPtr pbc,
            out IntPtr ppidl,
            uint sfgaoIn,
            out uint psfgaoOut);

        [DllImport("shell32.dll")]
        internal static extern IntPtr ILFree(IntPtr pidl);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        #endregion
    }

    /// <summary>
    /// <c>IInputObject</c> (<c>shobjidl_core.h</c>) : activation et traduction des accélérateurs
    /// clavier d'un objet hébergé. <c>IExplorerBrowser</c> l'expose ; sans
    /// <c>TranslateAcceleratorIO</c> dans la boucle de messages, F2 / Suppr / Ctrl+A / flèches
    /// verticales n'ont aucun effet alors que la souris et ←/→ passent par le WndProc.
    /// GUID et vtable : <c>IID_IInputObject</c> = 68284FAA-6A48-11D0-8C78-00C04FD918B4.
    /// </summary>
    [ComImport]
    [Guid("68284FAA-6A48-11D0-8C78-00C04FD918B4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInputObject
    {
        [PreserveSig]
        int UIActivateIO(int activate, ref System.Windows.Interop.MSG msg);

        [PreserveSig]
        int HasFocusIO();

        [PreserveSig]
        int TranslateAcceleratorIO(ref System.Windows.Interop.MSG msg);
    }

    /// <summary>
    /// <c>IExplorerBrowser</c> : hôte de vue fourni par le shell. L'ordre des méthodes reprend
    /// exactement celui de <c>shobjidl_core.h</c> ; le moindre décalage se traduirait par un appel
    /// d'une méthode voisine, donc par un plantage difficile à diagnostiquer.
    /// Les <c>HRESULT</c> non préservés se traduisent en <c>COMException</c>, capturées par l'hôte.
    /// </summary>
    [ComImport]
    [Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExplorerBrowser
    {
        /// <param name="folderSettings">Pointeur vers <c>FOLDERSETTINGS</c>, ou IntPtr.Zero pour reprendre les réglages du dossier.</param>
        void Initialize(IntPtr hwndParent, ref ShellBrowserNative.RECT prc, IntPtr folderSettings);

        void Destroy();

        void SetRect(IntPtr hdwp, [In] ShellBrowserNative.RECT rcBrowser);

        void SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string propertyBag);

        void SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string emptyText);

        void SetFolderSettings(ref ShellBrowserNative.FolderSettings folderSettings);

        void Advise(IntPtr browserEvents, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void BrowseToIDList(IntPtr pidl, uint flags);

        void BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint flags);

        void FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint flags);

        void RemoveAll();

        void GetCurrentView([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object view);

        void NoBrowserUI();

        /// <summary>Redemande le contenu du dossier affiché, sans navigation : la vue garde son
        /// défilement et sa sélection. C'est ce que l'Explorateur fait quand la surveillance du
        /// dossier signale un changement.</summary>
        void Refresh();
    }

    /// <summary>
    /// <c>IExplorerBrowserEvents</c> : ce que l'hôte de vue nous apprend. <c>OnNavigationComplete</c>
    /// est le seul moyen de savoir où l'utilisateur a atterri après un double-clic sur un dossier.
    /// </summary>
    [ComImport]
    [Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExplorerBrowserEvents
    {
        void OnNavigationPending(IntPtr pidlFolder);

        void OnViewCreated(IntPtr shellView);

        void OnNavigationComplete(IntPtr pidlFolder);

        void OnNavigationFailed(IntPtr pidlFolder);
    }

    /// <summary>
    /// <c>IFolderView</c> : la vue d'éléments. <c>GetFolder</c> donne le dossier affiché à l'instant
    /// où on l'interroge ; c'est ce qui sert à resynchroniser la barre de chemin.
    /// </summary>
    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFolderView
    {
        void GetCurrentViewMode(out uint viewMode);

        void SetCurrentViewMode(uint viewMode);

        void GetFolder([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object folder);

        void Item(int item, out IntPtr pidl);

        void ItemCount(uint flags, out int count);

        void Items(uint flags, [In] ref Guid riid, out IntPtr items);

        // IFolderView::GetSelection n'est PAS déclaré ici : mesuré sur le poste, le compte rendu par
        // la vue à cet emplacement est GetSelectionMarkedItem (GetSelection appelé là fait planter la
        // vue en accès illégal, et les deux méthodes de sélection répondent correctement sans lui).
        void GetSelectionMarkedItem(out int index);

        void GetFocusedItem(out int index);

        void GetItemPosition(IntPtr pidl, out ShellBrowserNative.POINT position);

        void GetSpacing(out ShellBrowserNative.POINT spacing);

        void GetDefaultSpacing(out ShellBrowserNative.POINT spacing);

        void GetAutoArrange();

        void SelectItem(int index, uint flags);

        void SelectAndPositionItems(uint count, IntPtr pidls, IntPtr points, uint flags);
    }

    /// <summary>
    /// <c>IShellItem</c> : un élément du shell. <c>GetDisplayName</c> rend sa chaîne, à libérer
    /// avec <c>CoTaskMemFree</c>.
    /// </summary>
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, [In] ref Guid handlerId, [In] ref Guid riid, out IntPtr result);

        void GetParent(out IntPtr parent);

        void GetDisplayName(uint displayNameKind, out IntPtr displayName);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IntPtr other, uint hint, out int order);
    }
}