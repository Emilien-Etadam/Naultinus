using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Naultinus.View
{
    /// <summary>
    /// Héberge la vue d'éléments du shell (celle de l'Explorateur Windows) dans une fenêtre WPF.
    /// Le rendu, les icônes et leurs superpositions, les vignettes, les colonnes, le tri, le
    /// renommage, le glisser-déposer et les menus viennent du shell ; cette classe ne fait que
    /// créer la fenêtre hôte, la dimensionner et la diriger vers un dossier.
    ///
    /// Points à valider sur Windows (premier spike, voir docs/PORTAL_EXPLORER_PARITY.md) :
    /// - la fenêtre ne peut pas vivre dans une fenêtre WPF <c>AllowsTransparency="True"</c> ;
    /// - le site COM n'expose ni <c>IShellBrowser</c> parent ni portée de saisie : Tab/MAJ+Tab entre
    ///   la vue et le reste de l'interface sont donc inactifs, l'affichage et le clavier dans la vue
    ///   doivent fonctionner sans cela.
    /// </summary>
    public class ShellFolderViewHost : HwndHost
    {
        private const int WsChild = 0x40000000;
        private const int WsVisible = 0x10000000;
        private const int WsClipChildren = 0x02000000;
        private const int WsClipSiblings = 0x04000000;
        private const int CsHredraw = 0x0002;
        private const int CsVredraw = 0x0001;
        private const uint WmSize = 0x0005;

        private static readonly string ClassName = "NaultinusShellViewSite";
        private static readonly WndProcDelegate StaticWndProc = WndProcThunk;
        private static readonly ConcurrentDictionary<IntPtr, ShellFolderViewHost> Instances = new ConcurrentDictionary<IntPtr, ShellFolderViewHost>();
        private static bool _classRegistered;

        private object? _browser;
        private IntPtr _siteHwnd;
        private bool _initialized;

        /// <summary>Récepteur des notifications de la vue, à garder vivant tant que l'hôte écoute.</summary>
        private ExplorerBrowserEventsSink? _events;
        private uint _eventsCookie;
        private bool _themeApplied;

        /// <summary>Vrai pendant une navigation déclenchée par nous : la réponse ne doit pas nous revenir.</summary>
        private bool _navigatingFromApp;

        /// <summary>Chemin demandé, conservé si la vue n'est pas encore prête.</summary>
        private string _pendingPath = "";

        /// <summary>Faux si l'hôte de vue n'a pas pu être créé : l'affichage WPF prend le relais.</summary>
        public bool IsAvailable => _initialized;

        /// <summary>
        /// Dossier que la vue affiche désormais, y compris quand l'utilisateur y est allé tout seul
        /// (double-clic sur un dossier, retour arrière). Sert à garder la barre de chemin et la
        /// flèche « remonter » cohérentes.
        /// </summary>
        public event Action<string>? Navigated;

        public ShellFolderViewHost()
        {
            Focusable = true;
        }

        /// <summary>
        /// Mode expérimental demandé par variable d'environnement, le temps de valider l'architecture.
        /// </summary>
        internal static bool IsRequested()
        {
            string? value = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW");
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Enregistre la classe de fenêtre hôte. À appeler avant d'exposer l'hôte : un échec doit
        /// se décider avant la construction de la fenêtre WPF, pas pendant.
        /// </summary>
        internal static bool TryPrepare()
        {
            try
            {
                RegisterWindowClass();
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Classe de fenêtre hôte indisponible.", ex);
                return false;
            }
        }

        #region HwndHost

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            RegisterWindowClass();

            IntPtr hInstance = GetModuleHandle(null);
            _siteHwnd = CreateWindowEx(
                0,
                ClassName,
                string.Empty,
                WsChild | WsVisible | WsClipChildren | WsClipSiblings,
                0, 0, 1, 1,
                hwndParent.Handle,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (_siteHwnd == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();

                // WPF rejette un handle nul : on lui donne une fenêtre vide pour que le portail
                // s'affiche sans corps plutôt que de fermer l'application.
                _siteHwnd = CreateWindowEx(
                    0, "Static", string.Empty,
                    WsChild | WsVisible | WsClipChildren | WsClipSiblings,
                    0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, hInstance, IntPtr.Zero);

                NaultinusDiagnostics.Log("ShellFolderViewHost", "CreateWindowEx a échoué (erreur " + error + ") ; fenêtre de repli utilisée.");
                if (_siteHwnd == IntPtr.Zero)
                    return new HandleRef(this, IntPtr.Zero);
            }

            Instances[_siteHwnd] = this;
            TryCreateBrowser();
            return new HandleRef(this, _siteHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Instances.TryRemove(hwnd.Handle, out _);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            ReleaseBrowser();
            if (hwnd.Handle != IntPtr.Zero)
                DestroyWindow(hwnd.Handle);
            _siteHwnd = IntPtr.Zero;
            _initialized = false;
        }

        /// <summary>La fenêtre créée pour la vue (different de la fenêtre de gestion WPF).</summary>
        public IntPtr ViewWindow => _siteHwnd;

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)WmSize)
            {
                ResizeView();
                handled = true;
                return IntPtr.Zero;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        #endregion

        #region Navigation

        /// <summary>Dirige la vue vers un dossier. Sans effet tant que la vue n'est pas prête.</summary>
        public void Navigate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (!_initialized)
            {
                _pendingPath = path;
                return;
            }

            IntPtr pidl = IntPtr.Zero;
            try
            {
                _navigatingFromApp = true;
                int hr = ShellBrowserNative.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero)
                {
                    NaultinusDiagnostics.Log("ShellFolderViewHost", $"Chemin non résolu par le shell ({path}) : 0x{hr:X8}");
                    return;
                }

                ((IExplorerBrowser)_browser!).BrowseToIDList(pidl, ShellBrowserNative.BrowseFlags.Absolute);
                _navigatingFromApp = false;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Navigation impossible vers " + path, ex);
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl);
                _navigatingFromApp = false;
            }
        }

        /// <summary>Donne le focus clavier à la vue hébergée (F2, Ctrl+C, saisie du début du nom…).</summary>
        public void FocusView()
        {
            if (_siteHwnd == IntPtr.Zero)
                return;

            // Le focus doit aller à la fenêtre la plus profonde (la liste elle-même), pas à la
            // fenêtre hôte qui ne reçoit que les messages de dimensionnement.
            IntPtr target = _siteHwnd;
            while (true)
            {
                IntPtr child = ShellBrowserNative.GetWindow(target, ShellBrowserNative.GwChild);
                if (child == IntPtr.Zero)
                    break;
                target = child;
            }

            SetFocus(target);
        }

        #endregion

        #region Vue

        private void TryCreateBrowser()
        {
            try
            {
                Guid clsid = ShellBrowserNative.ClsidExplorerBrowser;
                Guid iid = typeof(IExplorerBrowser).GUID;
                int hr = ShellBrowserNative.CoCreateInstance(ref clsid, IntPtr.Zero, ShellBrowserNative.ClsContext, ref iid, out object browser);
                if (hr != 0 || browser is not IExplorerBrowser explorerBrowser)
                {
                    NaultinusDiagnostics.Log("ShellFolderViewHost", $"Création de IExplorerBrowser impossible : 0x{hr:X8}");
                    return;
                }

                _browser = browser;

                // La zone cliente est encore à 1x1 ici : WPF dimensionne la fenêtre hôte juste après,
                // et SetRect prend le relais. Les réglages de vue sont laissés à NULL pour que le
                // shell applique ceux mémorisés pour le dossier, comme dans l'Explorateur.
                ShellBrowserNative.GetClientRect(_siteHwnd, out ShellBrowserNative.RECT rect);
                explorerBrowser.Initialize(_siteHwnd, ref rect, IntPtr.Zero);
                explorerBrowser.SetOptions(ShellBrowserNative.ExplorerBrowserOptions.None);

                _initialized = true;
                AdviseEvents();
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Vue du shell créée.");
                if (!string.IsNullOrEmpty(_pendingPath))
                {
                    string pending = _pendingPath;
                    _pendingPath = "";
                    Navigate(pending);
                }
            }
            catch (Exception ex)
            {
                ReleaseBrowser();
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Initialisation de la vue du shell impossible.", ex);
            }
        }

        private void ReleaseBrowser()
        {
            if (_browser == null)
                return;

            try
            {
                if (_events != null && _eventsCookie != 0)
                    ((IExplorerBrowser)_browser).Unadvise(_eventsCookie);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.Unadvise", ex);
            }

            _eventsCookie = 0;
            _events = null;

            try
            {
                // Le contrat de IExplorerBrowser demande Destroy avant de relâcher l'objet :
                // sans cela, les fenêtres de la vue restent en vie.
                ((IExplorerBrowser)_browser).Destroy();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.Destroy", ex);
            }

            try
            {
                Marshal.ReleaseComObject(_browser);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ReleaseBrowser", ex);
            }

            _browser = null;
        }

        /// <summary>
        /// La vue hébergée ne suit pas la taille de sa fenêtre parente toute seule : on lui notifie
        /// la nouvelle zone cliente, puis on étire aussi ses fenêtres enfants, la fenêtre de gestion
        /// de l'hôte pouvant rester placée ailleurs.
        /// </summary>
        private void ResizeView()
        {
            if (_siteHwnd == IntPtr.Zero)
                return;

            if (!ShellBrowserNative.GetClientRect(_siteHwnd, out ShellBrowserNative.RECT rect))
                return;

            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);

            if (_browser is IExplorerBrowser explorerBrowser)
            {
                try
                {
                    explorerBrowser.SetRect(IntPtr.Zero, rect);
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SetRect", ex);
                }
            }

            IntPtr child = ShellBrowserNative.GetWindow(_siteHwnd, ShellBrowserNative.GwChild);
            while (child != IntPtr.Zero)
            {
                ShellBrowserNative.MoveWindow(child, 0, 0, width, height, true);
                child = ShellBrowserNative.GetWindow(child, ShellBrowserNative.GwHwndNext);
            }
        }

        #endregion

        #region Notifications de la vue

        /// <summary>
        /// Déclare notre récepteur d'événements auprès de l'hôte de vue. Le pointeur COM rendu par
        /// <c>GetComInterfaceForObject</c> porte notre propre référence : on la rend tout de suite,
        /// l'hôte garde la sienne tant que le cookie court.
        /// </summary>
        private void AdviseEvents()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                var sink = new ExplorerBrowserEventsSink();
                sink.ViewCreated += OnViewCreated;
                sink.NavigationComplete += OnNavigationComplete;

                IntPtr sinkPointer = Marshal.GetComInterfaceForObject(sink, typeof(IExplorerBrowserEvents));
                try
                {
                    browser.Advise(sinkPointer, out _eventsCookie);
                }
                finally
                {
                    Marshal.Release(sinkPointer);
                }

                _events = sink;
            }
            catch (Exception ex)
            {
                // Sans notifications, la vue fonctionne : seule la barre de chemin ne se resynchronise pas.
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.AdviseEvents", ex);
            }
        }

        private void OnViewCreated()
        {
            NaultinusDiagnostics.Log("ShellFolderViewHost", "vue créée par l'hôte de navigation.");
            ApplyViewTheme();
        }

        private void OnNavigationComplete(IntPtr pidlFolder)
        {
            // Trace de diagnostic : prouve que l'hôte de vue nous rappelle bien, y compris pour une
            // navigation qu'il a faite tout seul (double-clic sur un dossier).
            NaultinusDiagnostics.Log("ShellFolderViewHost", "notification de navigation reçue (initiée par l'application : " + _navigatingFromApp + ")");

            // La navigation recrée des fenêtres : elles naissent avec le thème par défaut (clair).
            _themeApplied = false;
            ApplyViewTheme();

            string? path = CurrentFolderPath();
            NaultinusDiagnostics.Log("ShellFolderViewHost", "dossier réellement affiché : " + (path ?? "(élément hors système de fichiers)"));

            if (_navigatingFromApp)
                return;

            if (!string.IsNullOrEmpty(path))
                Navigated?.Invoke(path);
        }

        private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General)
                return;

            _themeApplied = false;
            Application.Current?.Dispatcher.BeginInvoke(new Action(ApplyViewTheme));
        }

        /// <summary>
        /// Le shell ne rend ses vues en sombre que si l'on demande le sous-thème « Explorer » à
        /// chacune de ses fenêtres. Sans cela, le corps du portail reste blanc dans une application
        /// sombre, ce qui est pire que l'inverse.
        /// </summary>
        private void ApplyViewTheme()
        {
            if (_siteHwnd == IntPtr.Zero || _themeApplied)
                return;

            _themeApplied = true;
            ApplyTheme(_siteHwnd, ThemeWatcher.IsDarkMode());
        }

        private static void ApplyTheme(IntPtr hwnd, bool dark)
        {
            // Sous-thème « Explorer » et liste de parties vide : c'est la recette qui fait basculer
            // les vues de dossiers. En clair on rend son thème par défaut à la fenêtre.
            _ = ShellBrowserNative.SetWindowTheme(hwnd, dark ? "Explorer" : null, dark ? string.Empty : null);

            for (IntPtr child = ShellBrowserNative.GetWindow(hwnd, ShellBrowserNative.GwChild); child != IntPtr.Zero; child = ShellBrowserNative.GetWindow(child, ShellBrowserNative.GwHwndNext))
                ApplyTheme(child, dark);
        }

        /// <summary>
        /// Lit le dossier réellement affiché par la vue : vue courante → dossier → nom complet.
        /// Retourne null pour un élément hors système de fichiers (Corbeille, Réseau…).
        /// </summary>
        private string? CurrentFolderPath()
        {
            if (_browser is not IExplorerBrowser browser)
                return null;

            IntPtr namePointer = IntPtr.Zero;
            object? folder = null;
            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is not IFolderView folderView)
                    return null;

                Guid shellItemId = typeof(IShellItem).GUID;
                folderView.GetFolder(ref shellItemId, out folder);
                if (folder is not IShellItem item)
                    return null;

                item.GetDisplayName(ShellBrowserNative.SigdnFilePath, out namePointer);
                return namePointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(namePointer);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.CurrentFolderPath", ex);
                return null;
            }
            finally
            {
                if (namePointer != IntPtr.Zero)
                    ShellBrowserNative.CoTaskMemFree(namePointer);
                if (folder != null)
                    Marshal.ReleaseComObject(folder);
            }
        }

        #endregion

        #region Fenêtre hôte

        private static void RegisterWindowClass()
        {
            if (_classRegistered)
                return;

            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = CsHredraw | CsVredraw,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(StaticWndProc),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = LoadCursor(IntPtr.Zero, 32512),
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = ClassName,
                hIconSm = IntPtr.Zero,
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1410) // ERROR_CLASS_ALREADY_EXISTS
                    throw new InvalidOperationException("RegisterClassEx a échoué (" + error + ").");
            }

            _classRegistered = true;
        }

        /// <summary>
        /// Procedure partagée par toutes les fenêtres hôtes : l'instance est retrouvée par sa fenêtre.
        /// Les messages non traités vont à <c>DefWindowProc</c>, indispensable au comportement par
        /// défaut des fenêtres enfants.
        /// </summary>
        private static IntPtr WndProcThunk(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmSize && Instances.TryGetValue(hwnd, out ShellFolderViewHost? host))
            {
                host.ResizeView();
                return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        #endregion

        #region Win32

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string? windowName,
            int style,
            int x, int y, int width, int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr instance, int resource);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        #endregion
    }
}