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
    public partial class ShellFolderViewHost : HwndHost
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

        /// <summary>Dépôt dans la vue (RevokeDragDrop sur DefView / DirectUIHWND si faux).</summary>
        public bool AllowShellDrop { get; set; } = true;

        /// <summary>Glisser hors de la vue (annule le geste souris avant le seuil de drag OLE).</summary>
        public bool AllowShellDragOut { get; set; } = true;

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

    }
}
