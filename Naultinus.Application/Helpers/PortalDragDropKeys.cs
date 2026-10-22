using System.Windows;

namespace Naultinus.Helpers
{
    /// <summary>Interprétation commune des touches pour le glisser-déposer fichiers (portail / grille).</summary>
    public static class PortalDragDropKeys
    {
        public static bool IsCopy(DragDropKeyStates keyStates)
            => (keyStates & DragDropKeyStates.ControlKey) != 0;
    }
}
