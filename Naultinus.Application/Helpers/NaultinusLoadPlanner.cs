using System.Collections.Generic;
using System.Linq;
using Naultinus.Model;

namespace Naultinus.Helpers
{
    /// <summary>
    /// Partitionne les modèles chargés depuis le disque comme <c>NaultinusManager.LoadNaultinus</c> :
    /// bump de schéma, exclusion des masqués, regroupement par <see cref="NaultinusModelBase.GroupId"/>
    /// (ordonnés par <see cref="NaultinusModelBase.TabOrder"/>) vs autonomes.
    /// </summary>
    internal static class NaultinusLoadPlanner
    {
        internal static NaultinusLoadPlan Plan(IEnumerable<NaultinusModelBase> loaded)
        {
            var concrete = new List<NaultinusModelBase>();
            foreach (NaultinusModelBase model in loaded)
            {
                if (model.SchemaVersion == 0)
                    model.SchemaVersion = 1;
                concrete.Add(model);
            }

            var visible = concrete.Where(m => !m.IsHidden).ToList();
            var groups = visible
                .Where(m => !string.IsNullOrEmpty(m.GroupId))
                .GroupBy(m => m.GroupId!)
                .Select(g => (IReadOnlyList<NaultinusModelBase>)g.OrderBy(m => m.TabOrder).ToList())
                .ToList();
            var standalone = visible
                .Where(m => string.IsNullOrEmpty(m.GroupId))
                .ToList();

            return new NaultinusLoadPlan(groups, standalone);
        }
    }

    /// <summary>Résultat du plan de chargement (groupes à onglets + fenêtres autonomes).</summary>
    internal sealed class NaultinusLoadPlan
    {
        internal NaultinusLoadPlan(
            IReadOnlyList<IReadOnlyList<NaultinusModelBase>> groups,
            IReadOnlyList<NaultinusModelBase> standalone)
        {
            Groups = groups;
            Standalone = standalone;
        }

        /// <summary>Chaque entrée est un groupe déjà trié par <c>TabOrder</c>.</summary>
        internal IReadOnlyList<IReadOnlyList<NaultinusModelBase>> Groups { get; }

        internal IReadOnlyList<NaultinusModelBase> Standalone { get; }
    }
}
