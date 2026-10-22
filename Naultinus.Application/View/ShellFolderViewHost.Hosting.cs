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
    public partial class ShellFolderViewHost : HwndHost
    {
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
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmMouseMove = 0x0200;
        private const int WmContextMenu = 0x007B;
        private const int WmRButtonUp = 0x0205;
        private const int SmCxDrag = 68;
        private const int SmCyDrag = 69;

        /// <summary>Suivi du geste souris pour bloquer un glisser-déposer sortant.</summary>
        private bool _trackingPotentialDrag;
        private int _dragStartX;
        private int _dragStartY;
        private bool _dropTargetsRevoked;

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

                if (!AllowShellDragOut && TrySuppressDragOut(ref msg))
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
    }
}
