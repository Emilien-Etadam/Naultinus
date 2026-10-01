using System;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Effet du presse-papiers shell : 1 copie, 2 déplacement (Preferred DropEffect).
    /// </summary>
    internal static class PortalClipboard
    {
        internal const int CopyEffect = 1;
        internal const int MoveEffect = 2;

        internal static bool IsMove(byte[]? preferredDropEffect)
        {
            return preferredDropEffect != null
                && preferredDropEffect.Length >= 4
                && BitConverter.ToInt32(preferredDropEffect, 0) == MoveEffect;
        }

        internal static byte[] EffectBytes(bool move)
        {
            return BitConverter.GetBytes(move ? MoveEffect : CopyEffect);
        }
    }
}
