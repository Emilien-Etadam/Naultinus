namespace Naultinus.ViewModel
{
    public interface INaultinusViewModel
    {
        string Identifier { get; set; }
        string Name { get; set; }
        /// <summary>Texte de l’onglet dans une fenêtre groupée (nom + contexte, ex. dossier courant pour un browse).</summary>
        string TabBarLabel { get; }
        int FenceX { get; set; }
        int FenceY { get; set; }
        int Width { get; set; }
        int Height { get; set; }
        string? GroupId { get; set; }
        int TabOrder { get; set; }
        void Delete();
        void Save();
    }
}
