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
    }
}
