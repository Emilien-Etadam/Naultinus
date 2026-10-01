using System;
using System.Collections.Generic;
using System.Linq;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Sélection de la grille : clic, Ctrl pour basculer, Maj pour une plage.
    /// </summary>
    internal static class PortalSelection
    {
        internal readonly struct Result
        {
            internal Result(int[] indexes, int anchor)
            {
                Indexes = indexes;
                Anchor = anchor;
            }

            internal int[] Indexes { get; }

            internal int Anchor { get; }

            internal bool Contains(int index)
            {
                for (int i = 0; i < Indexes.Length; i++)
                {
                    if (Indexes[i] == index)
                        return true;
                }

                return false;
            }
        }

        internal static Result Click(int count, IReadOnlyList<int> current, int anchor, int index, bool control, bool shift)
        {
            if (count <= 0 || index < 0 || index >= count)
                return new Result(Copy(current), anchor);

            if (shift && anchor >= 0 && anchor < count)
            {
                int start = Math.Min(anchor, index);
                int end = Math.Max(anchor, index);
                int[] range = new int[end - start + 1];
                for (int i = 0; i < range.Length; i++)
                    range[i] = start + i;

                if (!control)
                    return new Result(range, anchor);

                var set = new HashSet<int>(current);
                for (int i = 0; i < range.Length; i++)
                    set.Add(range[i]);
                return new Result(set.OrderBy(value => value).ToArray(), anchor);
            }

            if (control)
            {
                var set = new HashSet<int>(current);
                if (!set.Remove(index))
                    set.Add(index);
                int nextAnchor = set.Contains(index) ? index : anchor;
                return new Result(set.OrderBy(value => value).ToArray(), nextAnchor);
            }

            return new Result(new[] { index }, index);
        }

        private static int[] Copy(IReadOnlyList<int> current)
        {
            var copy = new int[current.Count];
            for (int i = 0; i < current.Count; i++)
                copy[i] = current[i];
            return copy;
        }
    }
}
