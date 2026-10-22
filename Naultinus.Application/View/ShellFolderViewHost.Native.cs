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

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

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
