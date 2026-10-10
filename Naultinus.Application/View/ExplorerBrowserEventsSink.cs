using System;
using System.Runtime.InteropServices;
using Naultinus.Helpers.Native;

namespace Naultinus.View
{
    /// <summary>
    /// Récepteur des notifications de l'hôte de vue du shell. Sans lui, un double-clic sur un
    /// dossier déplace la vue sans rien prévenir côté naultinus : la barre de chemin reste sur
    /// l'ancien dossier et la flèche « remonter » ne sert plus.
    /// La classe doit être visible de COM pour que l'hôte puisse nous appeler.
    /// </summary>
    [ComVisible(true)]
    public class ExplorerBrowserEventsSink : IExplorerBrowserEvents
    {
        /// <summary>La vue vient d'être créée : ses fenêtres existent, on peut les retravailler.</summary>
        public event Action? ViewCreated;

        /// <summary>La navigation est terminée. Le PIDL fourni peut être nul (retour arrière).</summary>
        public event Action<IntPtr>? NavigationComplete;

        /// <summary>La navigation a échoué ; le dossier affiché n'a pas changé.</summary>
        public event Action<IntPtr>? NavigationFailed;

        public void OnNavigationPending(IntPtr pidlFolder)
        {
        }

        public void OnViewCreated(IntPtr shellView) => ViewCreated?.Invoke();

        public void OnNavigationComplete(IntPtr pidlFolder) => NavigationComplete?.Invoke(pidlFolder);

        public void OnNavigationFailed(IntPtr pidlFolder) => NavigationFailed?.Invoke(pidlFolder);
    }
}