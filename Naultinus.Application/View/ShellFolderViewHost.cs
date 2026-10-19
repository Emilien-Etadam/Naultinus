using Naultinus.Helpers;
using Naultinus.Helpers.Native;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
        private const uint WmSetFocus = 0x0007;

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

        /// <summary>Mode de vue demandé, 0 pour laisser le shell faire à sa tête.</summary>
        private uint _viewMode;
        private bool _showColumnHeader = true;

        /// <summary>Mode relevé dans la vue à la dernière surveillance, pour ne réagir qu'aux changements.</summary>
        private uint _lastKnownMode = uint.MaxValue;

        /// <summary>Ventilateur du mode : tourne tant que le portail est au premier plan.</summary>
        private System.Windows.Threading.DispatcherTimer? _modeWatch;

        /// <summary>Fil d'interface, capté à la construction : le ventilateur doit y vivre.</summary>
        private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        /// <summary>Chemin demandé, conservé si la vue n'est pas encore prête.</summary>
        private string _pendingPath = "";

        /// <summary>Dossier réellement affiché par la vue, sans le séparateur final.</summary>
        private string _displayedPath = "";

        /// <summary>Filtre de nom (IFolderFilter), vivant tant que l'hôte existe.</summary>
        private PortalFolderNameFilter? _nameFilter;

        /// <summary>Index à resélectionner après un rafraîchissement par renavigation (−1 = rien).</summary>
        private int _restoreSelectionIndex = -1;

        /// <summary>Double-clic autorisé (politique portail).</summary>
        public bool AllowDoubleClick { get; set; } = true;

        /// <summary>Clavier / accélérateurs DefView autorisés.</summary>
        public bool AllowKeyboard { get; set; } = true;

        /// <summary>Menus contextuels DefView autorisés (best-effort : filtre WM_CONTEXTMENU).</summary>
        public bool AllowContextMenu { get; set; } = true;

        /// <summary>Faux si l'hôte de vue n'a pas pu être créé : l'affichage WPF prend le relais.</summary>
        public bool IsAvailable => _initialized;

        /// <summary>
        /// Dossier que la vue affiche désormais, y compris quand l'utilisateur y est allé tout seul
        /// (double-clic sur un dossier, retour arrière). Sert à garder la barre de chemin et la
        /// flèche « remonter » cohérentes.
        /// </summary>
        public event Action<string>? Navigated;

        /// <summary>
        /// Navigation impossible (chemin invalide, partage mort, droits). Message déjà localisé.
        /// </summary>
        public event Action<string>? BrowseFailed;

        /// <summary>Navigation aboutie : le portail peut effacer le bandeau d'erreur.</summary>
        public event Action? BrowseSucceeded;

        /// <summary>
        /// Double-clic sur le vide de la vue (aucune sélection). Argument : Ctrl enfoncé
        /// (racine du portail) ou non (ouvrir dans l'Explorateur).
        /// </summary>
        public event Action<bool>? EmptyBackgroundDoubleClick;

        public ShellFolderViewHost()
        {
            Focusable = true;
        }

        /// <summary>
        /// Vue shell activée par défaut. Opt-out : <c>NAULTINUS_SHELLVIEW=0</c> (ou <c>false</c>/<c>off</c>).
        /// En cas d'échec de <see cref="TryPrepare"/>, le portail garde la grille WPF.
        /// </summary>
        internal static bool IsRequested()
        {
            string? value = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW");
            if (string.IsNullOrEmpty(value))
                return true;

            if (string.Equals(value, "0", StringComparison.Ordinal)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Enregistre la classe de fenêtre hôte. À appeler avant d'exposer l'hôte : un échec doit
        /// se décider avant la construction de la fenêtre WPF, pas pendant.
        /// </summary>
        internal static bool TryPrepare()
        {
            try
            {
                // IExplorerBrowser est un objet OLE en place : sans OleInitialize sur le thread, la
                // vue reçoit la souris mais son activation en place — donc tout son routage clavier —
                // ne s'arme jamais (F2, Suppr, Ctrl+A et les flèches restent sans effet).
                int ole = OleInitialize(IntPtr.Zero);
                NaultinusDiagnostics.Log("ShellFolderViewHost.TryPrepare", "OleInitialize : 0x" + ole.ToString("X8"));

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
            AttachRootFocusGuard();
            AttachAcceleratorFilter();
            TryCreateBrowser();
            return new HandleRef(this, _siteHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            Instances.TryRemove(hwnd.Handle, out _);
            DetachAcceleratorFilter();
            _rootSource?.RemoveHook(RootFocusGuard);
            _rootSource = null;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            StopModeWatch();
            ReleaseBrowser();
            if (hwnd.Handle != IntPtr.Zero)
                DestroyWindow(hwnd.Handle);
            _siteHwnd = IntPtr.Zero;
            _initialized = false;
        }

        /// <summary>La fenêtre créée pour la vue (different de la fenêtre de gestion WPF).</summary>
        public IntPtr ViewWindow => _siteHwnd;

        #region Garde du focus

        private HwndSource? _rootSource;
        private DateTime _lastFocusRelay = DateTime.MinValue;

        /// <summary><c>NAULTINUS_SHELLVIEW_KEYS=1</c> : sonde les messages des fenêtres de la vue.</summary>
        private static readonly bool ProbeKeys =
            Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_KEYS") == "1";

        /// <summary>
        /// <c>NAULTINUS_SHELLVIEW_GUARD=1</c> : rend le focus à la vue quand WPF le reprend pour lui.
        /// Mesuré inefficace sur le clavier (voir docs/PORTAL_KEYBOARD.md), gardé comme banc d'essai.
        /// </summary>
        private static readonly bool FocusGuard =
            Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_GUARD") == "1";

        /// <summary>
        /// WPF rend le focus à sa fenêtre racine après un clic ou une activation : les touches
        /// n'atteignent plus la liste et les flèches, F2, Suppr, Ctrl+A restent sans effet (mesuré :
        /// focus clavier sur la fenêtre racine pendant la frappe). Le portail n'a aucune zone de
        /// saisie WPF, ce focus y est donc perdu : on le rend à la vue.
        /// </summary>
        private void AttachRootFocusGuard()
        {
            if (_rootSource != null)
                return;

            _rootSource = PresentationSource.FromVisual(this) as HwndSource;
            _rootSource?.AddHook(RootFocusGuard);
        }

        private IntPtr RootFocusGuard(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // WPF ne répercute pas WM_ACTIVATE sur ses fenêtres enfants : ni la fenêtre site ni
            // SHELLDLL_DefView ne sont jamais activées, et la vue du shell n'arme pas son routage
            // clavier (la souris, elle, fonctionne). On relaie le message ; DefWindowProc le propage
            // ensuite dans toute la hiérarchie hébergée.
            if (msg == 0x0111 && ProbeKeys)
                NaultinusDiagnostics.Log(
                    "SondeClavier",
                    $"fenêtre WPF 0x{hwnd.ToInt64():X} WM_COMMAND {CommandName((ushort)(wParam.ToInt64() & 0xffff))}");

            if (msg == WmActivate && _siteHwnd != IntPtr.Zero)
                _ = SendMessage(_siteHwnd, WmActivate, wParam, lParam);
            if (msg == (int)WmSetFocus && FocusGuard && WantsListFocus())
            {
                DateTime now = DateTime.UtcNow;
                if (now - _lastFocusRelay >= TimeSpan.FromMilliseconds(150))
                {
                    _lastFocusRelay = now;
                    string pile = Environment.StackTrace;
                    string[] lignes = pile.Split('\n');
                    NaultinusDiagnostics.Log(
                        "ShellFolderViewHost.GardeFocus",
                        $"fenêtre racine 0x{hwnd.ToInt64():X} saisie par WPF ; focus logique WPF="
                        + (System.Windows.Input.Keyboard.FocusedElement?.GetType().Name ?? "aucun")
                        + " ; rendu à la vue. Pile : "
                        + string.Join(" | ", lignes.Skip(1).Take(26).Select(t => t.Trim())));
                    Dispatcher.BeginInvoke(new Action(FocusView), System.Windows.Threading.DispatcherPriority.Input);
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Fenêtre à laquelle le focus clavier doit être donné. Banc d'essai : NAULTINUS_SHELLVIEW_FOCUS
        /// vaut « browser » (la fenêtre du navigateur, qui sait activer sa vue elle-même), « defview »
        /// ou rien du tout (la fenêtre de liste, comportement par défaut).
        /// </summary>
        private IntPtr FocusTarget()
        {
            string want = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_FOCUS") ?? string.Empty;
            if (want.Length == 0 || _siteHwnd == IntPtr.Zero)
                return FindListWindow();

            string wantedClass = want switch
            {
                "browser" => "ExplorerBrowserControl",
                "defview" => "SHELLDLL_DefView",
                _ => "DirectUIHWND",
            };

            IntPtr found = IntPtr.Zero;
            EnumChildWindows(_siteHwnd, (h, l) =>
            {
                var sb = new System.Text.StringBuilder(256);
                _ = GetClassName(h, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), wantedClass, StringComparison.Ordinal))
                {
                    found = h;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found != IntPtr.Zero ? found : FindListWindow();
        }

        /// <summary>Le clavier est-il libre côté WPF (aucune zone de saisie ne le réclame) ?</summary>
        private static bool WantsListFocus()
        {
            object? focused = System.Windows.Input.Keyboard.FocusedElement;
            return focused is null
                || focused is System.Windows.Window
                || (focused is System.Windows.FrameworkElement fe && !fe.Focusable);
        }

        #endregion


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

        /// <summary>
        /// Donne le focus clavier à la fenêtre de liste de la vue (F2, Suppr, Ctrl+A, saisie du début
        /// du nom). À appeler après l'activation de la fenêtre, pas pendant : WPF rend le focus à son
        /// propre contenu au moment de l'activation.
        /// </summary>
        public void FocusView()
        {
            IntPtr list = FocusTarget();
            if (list != IntPtr.Zero)
                _ = SetFocus(list);

            // Le focus Win32 ne prévient pas le shell : UIActivateIO arme HasFocusIO, sans quoi
            // TranslateAcceleratorIO refuse les accélérateurs (F2, Suppr, Ctrl+A…).
            if (_browser is IInputObject input)
            {
                try
                {
                    var msg = new MSG();
                    int activate = input.UIActivateIO(1, ref msg);
                    int hasFocus = input.HasFocusIO();
                    NaultinusDiagnostics.Log(
                        "ShellFolderViewHost.FocusView",
                        "UIActivateIO=0x" + activate.ToString("X8")
                        + ", HasFocusIO=0x" + hasFocus.ToString("X8")
                        + ", focus 0x" + GetFocus().ToInt64().ToString("X"));
                }
                catch (Exception ex)
                {
                    NaultinusDiagnostics.LogDebug("ShellFolderViewHost.FocusView", ex);
                }
            }
            else
            {
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost.FocusView",
                    "IExplorerBrowser n'expose pas IInputObject (QI échoué)");
            }

            InstallKeyProbe();
        }

        #region Accélérateurs clavier (IInputObject)

        private const int WmKeyFirst = 0x0100;
        private const int WmKeyLast = 0x0109;
        private const int WmKeyDown = 0x0100;
        private const int VkUp = 0x26;
        private const int VkDown = 0x28;
        private const int SOk = 0;

        private bool _acceleratorFilterAttached;

        /// <summary>
        /// Équivalent WPF de <c>Application.AddMessageFilter</c> (Windows API Code Pack).
        /// <c>ThreadFilterMessage</c> (pas Preprocess) : avant <c>HwndSource</c>, qui sinon donne
        /// ↑/↓ à <c>KeyboardNavigation</c> et vole le focus Win32.
        /// </summary>
        private void AttachAcceleratorFilter()
        {
            if (_acceleratorFilterAttached)
                return;

            ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
            _acceleratorFilterAttached = true;
        }

        private void DetachAcceleratorFilter()
        {
            if (!_acceleratorFilterAttached)
                return;

            ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
            _acceleratorFilterAttached = false;
        }

        private const int WmLButtonDblClk = 0x0203;
        private const int WmContextMenu = 0x007B;
        private const int WmRButtonUp = 0x0205;

        private void OnThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            if (_siteHwnd != IntPtr.Zero && IsMessageForView(msg.hwnd))
            {
                if (!AllowContextMenu
                    && (msg.message == WmContextMenu || msg.message == WmRButtonUp))
                {
                    handled = true;
                    return;
                }

                if (msg.message == WmLButtonDblClk && AllowDoubleClick && TryIsEmptySelection())
                {
                    bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                    EmptyBackgroundDoubleClick?.Invoke(ctrl);
                    handled = true;
                    return;
                }

                if (msg.message == WmLButtonDblClk && !AllowDoubleClick)
                {
                    handled = true;
                    return;
                }
            }

            if (_browser is not IInputObject input)
                return;

            if (msg.message < WmKeyFirst || msg.message > WmKeyLast)
                return;

            if (!AllowKeyboard)
            {
                if (_siteHwnd != IntPtr.Zero && IsMessageForView(msg.hwnd))
                    handled = true;
                return;
            }

            // ↑/↓ : HwndSource.OnPreprocessMessage (souvent avant nous) les donne à
            // KeyboardNavigation, qui pose handled=true et vole le focus. On les traite quand même
            // si la cible est notre vue OU si le focus Win32 y est encore / vient de l'être.
            int vk = unchecked((int)(msg.wParam.ToInt64() & 0xffff));
            bool vertical = vk == VkUp || vk == VkDown;
            if (vertical && msg.message == WmKeyDown && _siteHwnd != IntPtr.Zero
                && (IsMessageForView(msg.hwnd) || IsMessageForView(GetFocus())))
            {
                if (TryMoveSelectionVertical(delta: vk == VkDown ? 1 : -1))
                {
                    IntPtr list = FindListWindow();
                    if (list != IntPtr.Zero)
                        _ = SetFocus(list);
                    handled = true;
                    if (ProbeKeys)
                    {
                        NaultinusDiagnostics.Log(
                            "SondeClavier",
                            "↑/↓ géré par IFolderView, wParam 0x" + vk.ToString("X"));
                    }

                    return;
                }
            }

            if (handled)
                return;

            if (_siteHwnd == IntPtr.Zero || !IsMessageForView(msg.hwnd))
                return;

            try
            {
                // Clic souris seul : HasFocusIO peut rester S_FALSE alors que DirectUIHWND a le
                // focus Win32. On réarme alors l'activation shell avant de traduire.
                if (input.HasFocusIO() != SOk)
                {
                    IntPtr focus = GetFocus();
                    if (!IsMessageForView(focus))
                        return;

                    input.UIActivateIO(1, ref msg);
                    if (input.HasFocusIO() != SOk)
                        return;
                }

                if (input.TranslateAcceleratorIO(ref msg) == SOk)
                {
                    handled = true;
                    if (ProbeKeys)
                    {
                        NaultinusDiagnostics.Log(
                            "SondeClavier",
                            "TranslateAcceleratorIO a pris msg 0x" + msg.message.ToString("X4")
                            + " wParam 0x" + msg.wParam.ToInt64().ToString("X"));
                    }
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TranslateAcceleratorIO", ex);
            }
        }

        private bool TryIsEmptySelection()
        {
            if (_browser is not IExplorerBrowser browser)
                return false;

            try
            {
                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return false;

                folderView2.GetSelection(false, out object arrayObj);
                if (arrayObj is IShellItemArray array)
                {
                    array.GetCount(out uint count);
                    return count == 0;
                }

                folderView2.GetFocusedItem(out int focused);
                return focused < 0;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryIsEmptySelection", ex);
                return false;
            }
        }

        /// <summary>
        /// Déplace la sélection d'un cran dans l'ordre des éléments de la vue (mode détails /
        /// colonne unique). Retourne faux si la vue est inaccessible ou déjà en bout de liste.
        /// </summary>
        private bool TryMoveSelectionVertical(int delta)
        {
            if (_browser is not IExplorerBrowser browser || delta == 0)
                return false;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is not IFolderView folderView)
                    return false;

                folderView.ItemCount(ShellBrowserNative.SvgIoAllView, out int count);
                if (count <= 0)
                    return false;

                folderView.GetFocusedItem(out int focused);
                if (focused < 0)
                    folderView.GetSelectionMarkedItem(out focused);

                int next = focused < 0 ? (delta > 0 ? 0 : count - 1) : focused + delta;
                if (next < 0 || next >= count)
                    return true; // bout de liste : avaler quand même pour ne pas perdre le focus

                folderView.SelectItem(next, ShellBrowserNative.SvsiKeyboardMove);
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryMoveSelectionVertical", ex);
                return false;
            }
        }

        /// <summary>Le message cible-t-il la fenêtre site ou l'une de ses descendantes ?</summary>
        private bool IsMessageForView(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || _siteHwnd == IntPtr.Zero)
                return false;

            return hwnd == _siteHwnd || IsChild(_siteHwnd, hwnd);
        }

        #endregion

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
                // La vue montre déjà ce dossier : le lui redemander jetterait le défilement et la
                // sélection. En revanche elle ne se rafraîchit pas toute seule (mesuré : cinq
                // secondes après la création d'un fichier, il n'était pas là) : on le lui demande.
                string target = Path.TrimEndingDirectorySeparator(path.Trim());
                if (string.Equals(target, _displayedPath, StringComparison.OrdinalIgnoreCase))
                {
                    NaultinusDiagnostics.LogDebug("ShellFolderViewHost.Navigate : dossier déjà affiché, rafraîchissement demandé (" + target + ")");
                    RefreshView();
                    return;
                }

                _navigatingFromApp = true;
                int hr = ShellBrowserNative.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero)
                {
                    _navigatingFromApp = false;
                    string message = PortalFolderAccess.DescribeShellParseFailure(path, hr);
                    NaultinusDiagnostics.Log(
                        "ShellFolderViewHost",
                        $"Chemin non résolu par le shell ({path}) : 0x{hr:X8}");
                    ShowUnavailable(message);
                    BrowseFailed?.Invoke(message);
                    return;
                }

                ((IExplorerBrowser)_browser!).BrowseToIDList(pidl, ShellBrowserNative.BrowseFlags.Absolute);
                _displayedPath = target;
            }
            catch (Exception ex)
            {
                _navigatingFromApp = false;
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Navigation impossible vers " + path, ex);
                string message = PortalFolderAccess.DescribeNavigationFailed(path);
                ShowUnavailable(message);
                BrowseFailed?.Invoke(message);
            }
            finally
            {
                // Le drapeau « initiée par l'application » ne se remet PAS ici : la notification de
                // navigation arrive après ce retour, et le remettre trop tôt faisait passer nos
                // navigations pour celles de la vue.
                if (pidl != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl);
            }
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
                // Sac à dos dédié : mode, taille d'icônes et tri mémorisés par dossier (pas le magasin Explorer.exe).
                explorerBrowser.SetPropertyBag("Naultinus.FolderPortal");
                // Persistance activée (pas de EBO_NOPERSITVIEWSTATE / NoPersistViewState).
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

        /// <summary>
        /// Mode d'affichage pour le dossier courant. Le réglage est ensuite laissé au sac à dos
        /// du shell (pas de réimposition à chaque navigation).
        /// </summary>
        public void SetViewMode(uint viewMode, bool showColumnHeader)
        {
            _viewMode = viewMode;
            _showColumnHeader = showColumnHeader;
            ApplyViewMode();
            _lastKnownMode = viewMode;
            // Délégation au sac à dos : ne pas forcer ce mode sur les dossiers suivants.
            _viewMode = 0;
        }

        private ShellBrowserNative.PropertyKey? _lastSortKey;
        private int _lastSortDirection = ShellBrowserNative.SortDirections.Ascending;

        /// <summary>
        /// Tri de la vue courante via <c>IFolderView2::SetSortColumns</c>. Un second appel sur le
        /// même critère inverse le sens. Persistance : sac à dos du shell.
        /// </summary>
        internal bool TrySetSort(PortalSortField field)
        {
            if (_browser is not IExplorerBrowser browser)
                return false;

            try
            {
                ShellBrowserNative.PropertyKey key = field switch
                {
                    PortalSortField.Type => ShellBrowserNative.SortPropertyKeys.ItemTypeText,
                    PortalSortField.Date => ShellBrowserNative.SortPropertyKeys.DateModified,
                    _ => ShellBrowserNative.SortPropertyKeys.ItemNameDisplay,
                };

                int direction;
                if (_lastSortKey.HasValue
                    && _lastSortKey.Value.FormatId == key.FormatId
                    && _lastSortKey.Value.PropertyId == key.PropertyId)
                {
                    direction = -_lastSortDirection;
                }
                else
                {
                    // Aligné sur la grille WPF : date = plus récents d'abord.
                    direction = field == PortalSortField.Date
                        ? ShellBrowserNative.SortDirections.Descending
                        : ShellBrowserNative.SortDirections.Ascending;
                }

                var column = new ShellBrowserNative.SortColumn
                {
                    PropertyKey = key,
                    Direction = direction,
                };

                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return false;

                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<ShellBrowserNative.SortColumn>());
                try
                {
                    Marshal.StructureToPtr(column, buffer, false);
                    folderView2.SetSortColumns(buffer, 1);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }

                _lastSortKey = key;
                _lastSortDirection = direction;
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "tri demandé : " + field + ", sens " + direction);
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TrySetSort", ex);
                return false;
            }
        }

        private const int MinIconSize = 16;
        private const int MaxIconSize = 256;
        private const int IconSizeStep = 16;

        /// <summary>
        /// Taille d'icônes du bureau (sac à dos Shell Bags\1\Desktop), repli 48 px.
        /// </summary>
        internal static int GetDesktopIconSize()
        {
            try
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Bags\1\Desktop");
                object? value = key?.GetValue("IconSize");
                if (value is int pixels && pixels >= MinIconSize && pixels <= MaxIconSize)
                    return pixels;
                if (value != null
                    && int.TryParse(value.ToString(), out int parsed)
                    && parsed >= MinIconSize
                    && parsed <= MaxIconSize)
                    return parsed;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.GetDesktopIconSize", ex);
            }

            return 48;
        }

        /// <summary>
        /// Ctrl+molette : ajuste la taille d'icônes via <c>IFolderView2</c>. En mode liste / détails /
        /// contenu, un zoom avant bascule en icônes ; un zoom arrière au minimum revient en détails.
        /// </summary>
        public bool TryAdjustIconSize(int wheelDelta)
        {
            if (wheelDelta == 0 || _browser is not IExplorerBrowser browser)
                return false;

            try
            {
                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return false;

                if (folderView2.GetViewModeAndIconSize(out int mode, out int size) != SOk)
                    return false;

                int steps = Math.Max(1, Math.Abs(wheelDelta) / 120);
                int delta = (wheelDelta > 0 ? IconSizeStep : -IconSizeStep) * steps;
                int newMode = mode;
                int newSize = size;

                bool listLike = mode == (int)ShellBrowserNative.FolderViewModes.Details
                    || mode == (int)ShellBrowserNative.FolderViewModes.List
                    || mode == (int)ShellBrowserNative.FolderViewModes.Content
                    || mode == (int)ShellBrowserNative.FolderViewModes.SmallIcon;

                if (listLike)
                {
                    if (delta <= 0)
                        return true; // déjà compact : ne pas avaler sans effet utile au-delà

                    newMode = (int)ShellBrowserNative.FolderViewModes.Icon;
                    int baseline = size > 0 ? size : GetDesktopIconSize();
                    newSize = Math.Clamp(baseline + delta, MinIconSize, MaxIconSize);
                }
                else
                {
                    newSize = size + delta;
                    if (newSize < MinIconSize)
                    {
                        newMode = (int)ShellBrowserNative.FolderViewModes.Details;
                        newSize = MinIconSize;
                    }
                    else
                    {
                        newSize = Math.Clamp(newSize, MinIconSize, MaxIconSize);
                    }
                }

                if (newMode == mode && newSize == size)
                    return true;

                if (folderView2.SetViewModeAndIconSize(newMode, newSize) != SOk)
                    return false;

                // Sac à dos : ne pas réimposer ce mode aux dossiers suivants.
                _viewMode = 0;
                _lastKnownMode = (uint)newMode;
                _showColumnHeader = newMode == (int)ShellBrowserNative.FolderViewModes.Details;
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "taille d'icônes : mode " + newMode + ", " + newSize + " px");
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryAdjustIconSize", ex);
                return false;
            }
        }

        /// <summary>
        /// Filtre les éléments affichés par libellé (sous-chaîne, insensible à la casse).
        /// Chaîne vide = tout afficher. Réappliqué après chaque navigation.
        /// </summary>
        public void SetNameFilter(string? query)
        {
            _nameFilter ??= new PortalFolderNameFilter();
            string next = (query ?? string.Empty).Trim();
            if (string.Equals(_nameFilter.Query, next, StringComparison.Ordinal))
                return;

            _nameFilter.Query = next;
            ApplyNameFilter(refresh: true);
        }

        private void ApplyNameFilter(bool refresh)
        {
            if (_browser == null || _nameFilter == null)
                return;

            try
            {
                if (!TryGetFolderFilterSite(out IFolderFilterSite site))
                {
                    NaultinusDiagnostics.LogDebug(
                        "ShellFolderViewHost.ApplyNameFilter : IFolderFilterSite indisponible");
                    return;
                }

                object? filter = string.IsNullOrEmpty(_nameFilter.Query) ? null : _nameFilter;
                site.SetFilter(filter);
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "filtre de nom : " + (filter == null ? "(aucun)" : _nameFilter.Query));

                if (refresh)
                    RefreshView();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ApplyNameFilter", ex);
            }
        }

        private bool TryGetFolderFilterSite(out IFolderFilterSite site)
        {
            site = null!;
            if (_browser == null)
                return false;

            if (TryQueryInterface(_browser, out site))
                return true;

            try
            {
                if (_browser is not IExplorerBrowser browser)
                    return false;

                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                return view != null && TryQueryInterface(view, out site);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryGetFolderFilterSite", ex);
                return false;
            }
        }

        private static bool TryQueryInterface<T>(object comObject, out T iface)
            where T : class
        {
            iface = null!;
            IntPtr unknown = Marshal.GetIUnknownForObject(comObject);
            try
            {
                Guid iid = typeof(T).GUID;
                int hr = Marshal.QueryInterface(unknown, ref iid, out IntPtr pointer);
                if (hr != 0 || pointer == IntPtr.Zero)
                    return false;

                try
                {
                    iface = (T)Marshal.GetTypedObjectForIUnknown(pointer, typeof(T));
                    return iface != null;
                }
                finally
                {
                    Marshal.Release(pointer);
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        /// <summary>
        /// Surveille le mode de la vue. Le menu de la vue change le mode sans prévenir personne, et
        /// comme il ne désactive pas le portail, un clic dedans ne déclenche pas non plus
        /// l'activation : sans cette veille, les titres de colonnes restent accrochés jusqu'au
        /// prochain clic extérieur. Le rythme est rapide quand le portail est au premier plan et
        /// lent ailleurs, pour ne pas réveiller la machine pour rien.
        /// </summary>
        public void StartModeWatch()
        {
            // Banc d'essai : permet de reproduire l'état bloqué, veille désactivée.
            if (Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_NOWATCH") == "1")
                return;

            // L'événement « vue créée » arrive parfois sur le fil COM : le ventilateur doit vivre
            // sur le fil d'interface, sinon il ne bat jamais.
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(StartModeWatch));
                return;
            }

            if (_modeWatch == null)
            {
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Tick += (_, _) => CheckModeChanged();
                _modeWatch = timer;
                NaultinusDiagnostics.Log("ShellFolderViewHost", "veille du mode de vue démarrée");
            }

            _modeWatch.Interval = TimeSpan.FromMilliseconds(400);
            if (!_modeWatch.IsEnabled)
                _modeWatch.Start();
        }

        /// <summary>Rythme lent : la veille continue, mais sans réveiller la machine en continu.</summary>
        public void SlowModeWatch()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(SlowModeWatch));
                return;
            }

            StartModeWatch();
            if (_modeWatch != null)
                _modeWatch.Interval = TimeSpan.FromMilliseconds(1500);
        }

        public void StopModeWatch()
        {
            if (_modeWatch == null)
                return;

            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(new Action(StopModeWatch));
                return;
            }

            _modeWatch.Stop();
            _modeWatch = null;
        }

        /// <summary>
        /// Redemande le contenu du dossier affiché. La vue du shell ne surveille pas le dossier à
        /// notre place (mesuré) : sans cet appel, un fichier créé pendant que le portail est ouvert
        /// n'apparaît pas.
        /// </summary>
        public void RefreshView()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                // Banc d'essai : « com » appelle IExplorerBrowser::Refresh, « navigate » (le défaut
                // retenu) redirige la vue vers le même dossier. Mesure : Refresh seul n'affiche pas
                // un fichier créé pendant la course ; la renavigation l'affiche. On restaure ensuite
                // l'index de sélection (best-effort) dans OnNavigationComplete.
                string how = Environment.GetEnvironmentVariable("NAULTINUS_SHELLVIEW_REFRESH") ?? "navigate";
                if (!string.Equals(how, "com", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_displayedPath))
                {
                    int before = SelectionMarkedItem();
                    if (before < 0)
                        before = FocusedItem();
                    _restoreSelectionIndex = before >= 0 ? before : -1;
                    string saved = _displayedPath;
                    _displayedPath = "";
                    Navigate(saved);
                    NaultinusDiagnostics.LogDebug(
                        "ShellFolderViewHost.RefreshView : renavigation vers " + saved
                        + ", restauration prévue index " + _restoreSelectionIndex);
                    return;
                }

                browser.Refresh();
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.RefreshView : IExplorerBrowser::Refresh appelé — sans effet observable sur la liste");
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.Log("ShellFolderViewHost", "Rafraîchissement de la vue impossible", ex);
            }
        }

        private void TryRestoreSelectionAfterRefresh()
        {
            int index = _restoreSelectionIndex;
            _restoreSelectionIndex = -1;
            if (index < 0 || _browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                    folderView.SelectItem(index, ShellBrowserNative.SvsiKeyboardMove);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryRestoreSelectionAfterRefresh", ex);
            }
        }

        /// <summary>
        /// Index de l'élément marqué par la sélection (-1 si la vue n'est pas joignable). Sert à
        /// juger ce que coûte un rafraîchissement par renavigation.
        /// </summary>
        private int SelectionMarkedItem()
        {
            if (_browser is not IExplorerBrowser browser)
                return -2;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetSelectionMarkedItem(out int index);
                    return index;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SelectionMarkedItem", ex);
            }

            return -2;
        }

        /// <summary>Index de l'élément qui porte le focus dans la vue (-2 si la vue n'est pas joignable).</summary>
        private int FocusedItem()
        {
            if (_browser is not IExplorerBrowser browser)
                return -2;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetFocusedItem(out int index);
                    return index;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.FocusedItem", ex);
            }

            return -2;
        }


        private void CheckModeChanged()
        {
            try
            {
                uint mode = ReadViewMode();
                bool headerVisible = IsColumnHeaderVisible();
                bool layoutKnown = TryLayoutLooksLikeDetails(out bool layoutDetails);
                // Mode API figé (menu de la vue) alors que la grille n'est plus en détails, ou
                // en-tête encore visible hors détails : forcer la remise à l'heure.
                bool staleHeader = headerVisible && mode != ShellBrowserNative.FolderViewModes.Details;
                bool staleMode = layoutKnown && !layoutDetails && mode == ShellBrowserNative.FolderViewModes.Details;

                if (mode == _lastKnownMode && !staleHeader && !staleMode)
                    return;

                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "mode de la vue passé à " + mode
                    + " (dernier " + _lastKnownMode
                    + ", en-tête visible=" + headerVisible
                    + ", disposition détails=" + (layoutKnown ? layoutDetails.ToString() : "?") + ")");
                EnforceColumnHeader();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.CheckModeChanged", ex);
            }
        }

        /// <summary>
        /// Change le mode par la seule vue, sans réglages de dossier : c'est le chemin qu'emprunte le
        /// menu contextuel de la vue, et il laisse l'en-tête de colonnes en place.
        /// </summary>
        public void SetViewModeThroughViewOnly(uint viewMode)
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.SetCurrentViewMode(viewMode);
                    folderView.GetCurrentViewMode(out uint relu);
                    NaultinusDiagnostics.Log("ShellFolderViewHost", "mode demandé à la vue seule : " + viewMode + ", relu juste après " + relu);
                }
                else
                {
                    NaultinusDiagnostics.Log("ShellFolderViewHost", "mode demandé à la vue seule : aucune vue sous la main");
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.SetViewModeThroughViewOnly", ex);
            }
        }

        /// <summary>
        /// Mode réellement affiché par la vue (0 si la vue n'est pas joignable).
        /// Préfère <c>IShellView::GetCurrentInfo</c> : plus fidèle après un changement par le menu.
        /// </summary>
        private uint ReadViewMode()
        {
            if (_browser is not IExplorerBrowser browser)
                return 0;

            try
            {
                Guid shellViewId = typeof(IShellView).GUID;
                browser.GetCurrentView(ref shellViewId, out object shellViewObj);
                if (shellViewObj is IShellView shellView)
                {
                    var settings = new ShellBrowserNative.FolderSettings();
                    if (shellView.GetCurrentInfo(ref settings) == SOk && settings.ViewMode != 0)
                        return (uint)settings.ViewMode;
                }

                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                {
                    folderView.GetCurrentViewMode(out uint mode);
                    return mode;
                }
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ReadViewMode", ex);
            }

            return 0;
        }

        /// <summary>La vue details affiche un <c>SysHeader32</c> ; s'il reste après un passage en icônes, bug.</summary>
        private bool IsColumnHeaderVisible()
        {
            if (_siteHwnd == IntPtr.Zero)
                return false;

            IntPtr header = IntPtr.Zero;
            EnumChildWindows(_siteHwnd, (h, _) =>
            {
                var sb = new System.Text.StringBuilder(64);
                _ = GetClassName(h, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "SysHeader32", StringComparison.Ordinal)
                    && IsWindowVisible(h))
                {
                    header = h;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return header != IntPtr.Zero;
        }

        /// <summary>
        /// Heuristique de disposition : deux premiers éléments sur la même colonne ⇒ détails/liste.
        /// Sert quand <c>GetCurrentViewMode</c> reste à « détails » après un changement par le menu.
        /// </summary>
        private bool TryLayoutLooksLikeDetails(out bool looksLikeDetails)
        {
            looksLikeDetails = false;
            if (_browser is not IExplorerBrowser browser)
                return false;

            IntPtr pidl0 = IntPtr.Zero;
            IntPtr pidl1 = IntPtr.Zero;
            try
            {
                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is not IFolderView folderView)
                    return false;

                folderView.ItemCount(ShellBrowserNative.SvgIoAllView, out int count);
                if (count < 2)
                    return false;

                folderView.Item(0, out pidl0);
                folderView.Item(1, out pidl1);
                if (pidl0 == IntPtr.Zero || pidl1 == IntPtr.Zero)
                    return false;

                folderView.GetItemPosition(pidl0, out ShellBrowserNative.POINT a);
                folderView.GetItemPosition(pidl1, out ShellBrowserNative.POINT b);
                looksLikeDetails = Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) > 2;
                return true;
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.TryLayoutLooksLikeDetails", ex);
                return false;
            }
            finally
            {
                if (pidl0 != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl0);
                if (pidl1 != IntPtr.Zero)
                    ShellBrowserNative.ILFree(pidl1);
            }
        }

        /// <summary>
        /// Remet l'en-tête de colonnes d'accord avec le mode affiché. À appeler quand on a une raison
        /// de croire que la vue a changé d'avis toute seule (menu contextuel de la vue).
        /// </summary>
        public void ReconcileViewMode()
        {
            if (_viewMode != 0)
            {
                ApplyViewMode();
                return;
            }

            // Repousser les réglages de dossier recharge la vue et efface la sélection : le faire à
            // chaque activation faisait perdre la sélection à chaque clic dans le corps (mesuré :
            // rectangle de focus sur l'élément 2, aucune ligne surlignée, F2 sans effet). On ne
            // corrige donc que si le mode a réellement bougé — la veille du mode s'en charge.
            uint mode = ReadViewMode();
            if (mode == _lastKnownMode)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ReconcileViewMode : mode " + mode + " inchangé, rien poussé");
                return;
            }

            EnforceColumnHeader();
        }

        /// <summary>
        /// Les titres de colonnes n'ont rien à faire dans un mode qui n'est pas « détails ». Mesuré :
        /// un mode changé par nos soins masque d'office les titres ; un mode changé par le menu de la
        /// vue, lui, laisse l'en-tête en place. On recopie donc les réglages selon le mode réel, en
        /// corrigeant le cas où l'API reste à « détails » alors que la disposition ne l'est plus.
        /// </summary>
        private void EnforceColumnHeader()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                uint mode = ReadViewMode();
                if (mode == 0)
                    return;

                bool showHeader = mode == ShellBrowserNative.FolderViewModes.Details;
                if (showHeader && TryLayoutLooksLikeDetails(out bool layoutDetails) && !layoutDetails)
                {
                    // Menu de la vue : mode API encore à 4, éléments déjà en grille.
                    showHeader = false;
                    mode = ShellBrowserNative.FolderViewModes.Icon;
                }

                var settings = new ShellBrowserNative.FolderSettings
                {
                    ViewMode = (int)mode,
                    Flags = showHeader ? 0u : ShellBrowserNative.FolderViewFlags.NoColumnHeader,
                };

                browser.SetFolderSettings(ref settings);
                _lastKnownMode = mode;
                NaultinusDiagnostics.Log(
                    "ShellFolderViewHost",
                    "mode relevé dans la vue : " + mode + ", titres de colonnes : " + showHeader);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.EnforceColumnHeader", ex);
            }
        }

        /// <summary>
        /// Le mode passe par les réglages de dossier et par la vue courante : les deux sont
        /// nécessaires, <c>SetFolderSettings</c> seul ne repeint pas une vue déjà dessinée.
        /// </summary>
        private void ApplyViewMode()
        {
            if (_browser is not IExplorerBrowser browser || _viewMode == 0)
                return;

            try
            {
                var settings = new ShellBrowserNative.FolderSettings
                {
                    ViewMode = (int)_viewMode,
                    Flags = _showColumnHeader ? 0u : ShellBrowserNative.FolderViewFlags.NoColumnHeader,
                };

                browser.SetFolderSettings(ref settings);

                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view2);
                if (view2 is IFolderView2 folderView2
                    && folderView2.GetViewModeAndIconSize(out _, out int iconSize) == SOk)
                {
                    int size = iconSize > 0 ? iconSize : GetDesktopIconSize();
                    if (folderView2.SetViewModeAndIconSize((int)_viewMode, size) == SOk)
                    {
                        NaultinusDiagnostics.Log(
                            "ShellFolderViewHost",
                            "mode de vue demandé (IFolderView2) : " + _viewMode
                            + ", titres de colonnes : " + _showColumnHeader);
                        return;
                    }
                }

                Guid folderViewId = typeof(IFolderView).GUID;
                browser.GetCurrentView(ref folderViewId, out object view);
                if (view is IFolderView folderView)
                    folderView.SetCurrentViewMode(_viewMode);

                NaultinusDiagnostics.Log("ShellFolderViewHost", "mode de vue demandé : " + _viewMode + ", titres de colonnes : " + _showColumnHeader);
                EnforceAutoArrange();
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ApplyViewMode", ex);
            }
        }

        /// <summary>
        /// Empêche le réordonnancement manuel libre : le tri / l'arrangement automatique font foi.
        /// </summary>
        private void EnforceAutoArrange()
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                Guid folderView2Id = typeof(IFolderView2).GUID;
                browser.GetCurrentView(ref folderView2Id, out object view);
                if (view is not IFolderView2 folderView2)
                    return;

                uint flags = ShellBrowserNative.FolderViewFlags.AutoArrange
                    | ShellBrowserNative.FolderViewFlags.SnapToGrid;
                folderView2.SetCurrentFolderFlags(flags, flags);
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.EnforceAutoArrange", ex);
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
                sink.NavigationFailed += OnNavigationFailed;

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
            ApplyViewMode();
            EnforceAutoArrange();
            ApplyNameFilter(refresh: false);
            StartModeWatch();
        }

        private void OnNavigationFailed(IntPtr pidlFolder)
        {
            // Une navigation ratée ne rend jamais la notification de fin : sans ceci, le drapeau
            // resterait levé et la navigation suivante passerait pour la nôtre.
            _navigatingFromApp = false;
            string? attempted = _pendingPath;
            if (string.IsNullOrEmpty(attempted))
                attempted = _displayedPath;
            string message = PortalFolderAccess.DescribeNavigationFailed(attempted);
            NaultinusDiagnostics.Log("ShellFolderViewHost", "notification d'échec de navigation reçue");
            ShowUnavailable(message);
            BrowseFailed?.Invoke(message);
        }

        /// <summary>Texte de vue vide + oubli du chemin affiché pour forcer une vraie renavigation.</summary>
        public void ShowUnavailable(string message)
        {
            if (_browser is not IExplorerBrowser browser)
                return;

            try
            {
                browser.SetEmptyText(string.IsNullOrEmpty(message) ? " " : message);
                _displayedPath = "";
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ShowUnavailable", ex);
            }
        }

        private void OnNavigationComplete(IntPtr pidlFolder)
        {
            // Le drapeau ne se lit qu'ici : la notification arrive après le retour de
            // BrowseToIDList, et le remettre à faux trop tôt faisait passer nos propres navigations
            // pour celles de la vue.
            bool initiatedByApp = _navigatingFromApp;
            _navigatingFromApp = false;

            // Trace de diagnostic : prouve que l'hôte de vue nous rappelle bien, y compris pour une
            // navigation qu'il a faite tout seul (double-clic sur un dossier).
            NaultinusDiagnostics.Log("ShellFolderViewHost", "notification de navigation reçue (initiée par l'application : " + initiatedByApp + ")");

            // La navigation recrée des fenêtres : elles naissent avec le thème par défaut (clair).
            _themeApplied = false;
            ApplyViewTheme();

            // Le shell rétablit le mode mémorisé pour ce dossier : on redemande le nôtre.
            ReconcileViewMode();
            // Le filtre n'est pas dans le sac à dos de vue : on le réapplique à chaque dossier.
            ApplyNameFilter(refresh: false);

            string? path = CurrentFolderPath();
            NaultinusDiagnostics.Log("ShellFolderViewHost", "dossier réellement affiché : " + (path ?? "(élément hors système de fichiers)"));
            if (!string.IsNullOrEmpty(path))
                _displayedPath = Path.TrimEndingDirectorySeparator(path);

            try
            {
                if (_browser is IExplorerBrowser browser)
                    browser.SetEmptyText(" ");
            }
            catch (Exception ex)
            {
                NaultinusDiagnostics.LogDebug("ShellFolderViewHost.OnNavigationComplete.SetEmptyText", ex);
            }

            BrowseSucceeded?.Invoke();
            TryRestoreSelectionAfterRefresh();

            if (initiatedByApp)
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

            // Pas de SetWindowTheme ici : mesuré sur le poste, la vue de dossier suit déjà le thème
            // (sombre ou clair) déclaré par Windows pour les applications. Lui imposer le sous-thème
            // « Explorer » ne la noircissait pas davantage mais écrasait les brosses de la sélection
            // et du survol : les lignes restaient cliquables et sélectionnables, sans aucun retour
            // visuel. Le thème de la vue ne se pilote donc pas depuis Naultinus.
            NaultinusDiagnostics.LogDebug("ShellFolderViewHost.ApplyViewTheme : thème laissé à Windows");
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

            // Le focus donné à la fenêtre site doit redescendre jusqu'à la liste : c'est elle qui
            // traite les touches. Sans ce relais, WPF reprend le focus à l'activation de la fenêtre
            // et F2 comme Suppr restent sans effet tant qu'on ne clique pas une deuxième fois.
            if (msg == WmSetFocus && Instances.TryGetValue(hwnd, out ShellFolderViewHost? focused))
            {
                IntPtr list = focused.FindListWindow();
                if (list != IntPtr.Zero)
                    _ = SetFocus(list);
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        #endregion

        #region Sonde clavier (temporaire)

        /// <summary>Arbre complet des fenêtres hébergées : permet de vérifier qu'on cible la liste.</summary>
        private void LogChildTree()
        {
            var sb = new System.Text.StringBuilder();
            AppendChildren(sb, _siteHwnd, 0);
            IntPtr site = _siteHwnd;
            if (site != IntPtr.Zero && ShellBrowserNative.GetClientRect(site, out ShellBrowserNative.RECT rc))
            {
                var pt = new POINT { x = rc.Right / 2, y = rc.Bottom / 2 };
                _ = ClientToScreen(site, ref pt);
                IntPtr under = WindowFromPoint(pt);
                sb.Append($" | au centre du site : 0x{under.ToInt64():X}");
            }

            NaultinusDiagnostics.Log("ArbreFenetres", sb.ToString());
        }

        private void AppendChildren(System.Text.StringBuilder sb, IntPtr parent, int depth)
        {
            for (IntPtr child = ShellBrowserNative.GetWindow(parent, ShellBrowserNative.GwChild); child != IntPtr.Zero; child = ShellBrowserNative.GetWindow(child, ShellBrowserNative.GwHwndNext))
            {
                var sbName = new System.Text.StringBuilder(256);
                _ = GetClassName(child, sbName, sbName.Capacity);
                bool vis = IsWindowVisible(child);
                GetWindowRect(child, out WinRect r);
                sb.Append(new string(' ', depth * 2)).Append($"{sbName} 0x{child.ToInt64():X} {(vis ? "visible" : "masquée")} {r.Right - r.Left}x{r.Bottom - r.Top}; ");
                AppendChildren(sb, child, depth + 1);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT pt);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate IntPtr ProbeWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly ProbeWndProc StaticProbe = ProbeThunk;
        private static readonly System.Collections.Generic.Dictionary<IntPtr, (IntPtr Prev, bool Unicode)> ProbePrev = new();

        private IntPtr _probeHwnd;

        /// <summary>La sous-classe ne fait qu'enregistrer : elle rend tous les messages à la procédure d'origine.</summary>
        private static IntPtr ProbeThunk(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg is 0x0006 or 0x0007 or 0x0008 or 0x0100 or 0x0101 or 0x0102 or 0x0104 or 0x0106
                or 0x0111 or 0x0116 or 0x0120 or 0x0081 or 0x0210)
            {
                var name = new System.Text.StringBuilder(256);
                _ = GetClassName(hwnd, name, name.Capacity);
                string details = msg == 0x0111
                    ? "WM_COMMAND " + CommandName((ushort)(wParam.ToInt64() & 0xffff)) + " depuis 0x" + ((lParam.ToInt64() & 0xffffffff)).ToString("X")
                    : $"msg 0x{msg:X4} wParam 0x{wParam.ToInt64():X}";
                NaultinusDiagnostics.Log("SondeClavier", $"{name} 0x{hwnd.ToInt64():X} {details}");
            }

            if (ProbePrev.TryGetValue(hwnd, out (IntPtr Prev, bool Unicode) prev))
                return prev.Unicode ? CallWindowProcW(prev.Prev, hwnd, msg, wParam, lParam) : CallWindowProcA(prev.Prev, hwnd, msg, wParam, lParam);

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void InstallKeyProbe()
        {
            if (!ProbeKeys || _probeHwnd != IntPtr.Zero)
                return;

            IntPtr list = FindListWindow();
            if (list == IntPtr.Zero)
                return;

            // Toutes les fenêtres de la vue sont surveillées : les touches peuvent être relayées vers
            // une fenêtre parente avant d'arriver au traitement des commandes.
            EnumChildWindows(_siteHwnd, (h, l) =>
            {
                SubclassProbe(h);
                return true;
            }, IntPtr.Zero);

            SubclassProbe(_siteHwnd);
            _probeHwnd = list;
            LogChildTree();
            NaultinusDiagnostics.Log(
                "SondeClavier",
                $"sonde posée sur {ProbePrev.Count} fenêtres à partir de la liste 0x{list.ToInt64():X}");
        }

        /// <summary>Noms des commandes que la vue du shell envoie à son cadre (WM_COMMAND).</summary>
        private static string CommandName(ushort id) => id switch
        {
            0x7D00 => "VCMD_EMPTYMENU",
            0x7D01 => "VCMD_FINDFILE",
            0x7D02 => "VCMD_SELECTALL",
            0x7D03 => "VCMD_SELECTONE",
            0x7D04 => "VCMD_UNSELECTALL",
            0x7D05 => "VCMD_DELETE",
            0x7D06 => "VCMD_SETFOCUS",
            0x7D07 => "VCMD_ADVANCESELECT",
            0x7D15 => "VCMD_RENAME",
            0x7D16 => "VCMD_PASTEOBJECTS",
            0x7D17 => "VCMD_MOVEOBJECTS",
            0x7D18 => "VCMD_REFRESH",
            0x7D20 => "VCMD_GETSELECTION",
            0x7D28 => "VCMD_SELECTITEM",
            0x7D29 => "VCMD_SETITEMFOCUS",
            0x7D30 => "VCMD_INVERTSELECTION",
            0x7D31 => "VCMD_KILLFOCUS",
            _ => "0x" + id.ToString("X4"),
        };

        private void SubclassProbe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || ProbePrev.ContainsKey(hwnd))
                return;

            bool unicode = IsWindowUnicode(hwnd);
            IntPtr fn = Marshal.GetFunctionPointerForDelegate(StaticProbe);
            IntPtr prev = unicode ? SetWindowLongPtrW(hwnd, GwlpWndProc, fn) : SetWindowLongPtrA(hwnd, GwlpWndProc, fn);
            if (prev == IntPtr.Zero)
                return;

            ProbePrev[hwnd] = (prev, unicode);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr lpNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrA")]
        private static extern IntPtr SetWindowLongPtrA(IntPtr hWnd, int nIndex, IntPtr lpNewLong);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcA")]
        private static extern IntPtr CallWindowProcA(IntPtr lpPrevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowUnicode(IntPtr hWnd);

        private const int GwlpWndProc = -4;
        private const int WmActivate = 0x0006;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr reserved);

        #endregion

        #region Fenêtre de liste

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        private const uint GaRoot = 2;

        /// <summary>Titre d'une fenêtre, pour savoir qui tient le premier plan.</summary>
        private static string WindowTitle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            var sb = new System.Text.StringBuilder(256);
            _ = GetWindowTextW(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        /// <summary>
        /// Fenêtre de liste de la vue (<c>DirectUIHWND</c>) sous la fenêtre site. C'est elle qui traite
        /// les touches et la sélection ; la fenêtre site et les barres de défilement qui l'entourent
        /// ne peuvent pas recevoir le clavier.
        /// </summary>
        private IntPtr FindListWindow()
        {
            IntPtr site = _siteHwnd;
            if (site == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            EnumChildWindows(site, (h, l) =>
            {
                var sb = new System.Text.StringBuilder(256);
                _ = GetClassName(h, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "DirectUIHWND", StringComparison.Ordinal))
                {
                    found = h;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
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

        #endregion
    }
}