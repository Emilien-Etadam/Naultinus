using System;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Rectangle en pixels d'écran pour caler la vue shell sur la zone de contenu,
    /// sans recouvrir le bandeau ni déborder de la fenêtre.
    /// </summary>
    internal static class PortalShellPlacement
    {
        internal readonly struct PixelRect
        {
            internal PixelRect(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            internal int X { get; }

            internal int Y { get; }

            internal int Width { get; }

            internal int Height { get; }

            internal bool SamePlace(PixelRect other)
            {
                return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
            }
        }

        /// <summary>
        /// Intersection de la zone de contenu et du client de la fenêtre.
        /// Retourne null si l'intersection n'a pas de surface.
        /// </summary>
        internal static PixelRect? Intersection(PixelRect slot, PixelRect owner)
        {
            if (slot.Width <= 0 || slot.Height <= 0 || owner.Width <= 0 || owner.Height <= 0)
                return null;

            int left = Math.Max(slot.X, owner.X);
            int top = Math.Max(slot.Y, owner.Y);
            int right = Math.Min(slot.X + slot.Width, owner.X + owner.Width);
            int bottom = Math.Min(slot.Y + slot.Height, owner.Y + owner.Height);
            int width = right - left;
            int height = bottom - top;
            if (width <= 0 || height <= 0)
                return null;

            return new PixelRect(left, top, width, height);
        }

        /// <summary>
        /// Découpe le haut du rectangle pour qu'il commence au bord inférieur du bandeau, ou plus bas.
        /// </summary>
        internal static PixelRect? BelowBanner(PixelRect slot, int bannerBottom)
        {
            if (slot.Width <= 0 || slot.Height <= 0)
                return null;
            if (slot.Y >= bannerBottom)
                return slot;

            int height = slot.Height - (bannerBottom - slot.Y);
            if (height <= 0)
                return null;

            return new PixelRect(slot.X, bannerBottom, slot.Width, height);
        }

        /// <summary>
        /// Bande du bandeau, sur toute la largeur du client, du haut de la fenêtre jusqu'à son bord inférieur.
        /// </summary>
        internal static PixelRect? TopBar(PixelRect owner, int bannerBottom)
        {
            int height = bannerBottom - owner.Y;
            if (height <= 0 || owner.Width <= 0 || owner.Height <= 0)
                return null;
            if (height > owner.Height)
                height = owner.Height;

            return new PixelRect(owner.X, owner.Y, owner.Width, height);
        }

        /// <summary>
        /// Vrai si les deux rectangles ont une surface commune. Un bord partagé n'est pas un recouvrement.
        /// </summary>
        internal static bool Overlaps(PixelRect first, PixelRect second)
        {
            if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
                return false;

            return first.X < second.X + second.Width
                && second.X < first.X + first.Width
                && first.Y < second.Y + second.Height
                && second.Y < first.Y + first.Height;
        }

        /// <summary>
        /// Zone de la vue shell : sous le bandeau, dans le client, sans pixel commun avec le bandeau.
        /// Null si le bandeau n'est pas mesuré ou s'il ne reste aucune surface : on n'affiche pas plutôt que de couvrir.
        /// </summary>
        internal static PixelRect? ContentBelowBanner(PixelRect slot, PixelRect owner, int bannerBottom)
        {
            if (TopBar(owner, bannerBottom) is not PixelRect banner)
                return null;
            if (BelowBanner(slot, bannerBottom) is not PixelRect below)
                return null;
            if (Intersection(below, owner) is not PixelRect visible)
                return null;
            if (Overlaps(visible, banner))
                return null;

            return visible;
        }

        /// <summary>
        /// Région fenêtre (origine en haut à gauche de la fenêtre) qui retire le bandeau.
        /// La fenêtre peut rester au-dessus du propriétaire : cette région l'empêche de peindre et de recevoir les clics du bandeau.
        /// </summary>
        internal static PixelRect? RegionExcludingBanner(PixelRect window, PixelRect banner)
        {
            if (window.Width <= 0 || window.Height <= 0)
                return null;

            int top = 0;
            if (Overlaps(window, banner) && window.Y < banner.Y + banner.Height)
                top = Math.Min(window.Height, banner.Y + banner.Height - window.Y);

            int height = window.Height - top;
            if (height <= 0)
                return null;

            return new PixelRect(0, top, window.Width, height);
        }

        internal static bool Contains(PixelRect rect, int x, int y)
        {
            return rect.Width > 0
                && rect.Height > 0
                && x >= rect.X
                && x < rect.X + rect.Width
                && y >= rect.Y
                && y < rect.Y + rect.Height;
        }

        /// <summary>
        /// Dépaquette le <c>lParam</c> de <c>WM_NCHITTEST</c> (coordonnées d'écran signées).
        /// </summary>
        internal static void UnpackScreenPoint(long packed, out int x, out int y)
        {
            x = unchecked((short)(packed & 0xFFFF));
            y = unchecked((short)((packed >> 16) & 0xFFFF));
        }

        /// <summary>
        /// Vrai si le point d'écran appartient au bandeau : le popup doit le laisser passer.
        /// </summary>
        internal static bool BannerHit(long packedPoint, PixelRect banner)
        {
            UnpackScreenPoint(packedPoint, out int x, out int y);
            return Contains(banner, x, y);
        }
    }
}
