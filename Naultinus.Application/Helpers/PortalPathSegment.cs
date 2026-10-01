namespace Naultinus.Helpers
{
    /// <summary>
    /// Un dossier du fil d'Ariane : le libellé affiché et le chemin qu'il ouvre.
    /// </summary>
    public sealed class PortalPathSegment
    {
        public PortalPathSegment(string label, string path)
        {
            Label = label;
            Path = path;
        }

        public string Label { get; }

        public string Path { get; }
    }
}
