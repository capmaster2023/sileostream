using System.ComponentModel;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Les couleurs de l'interface. Siloé Stream 2.0.1, design « régie pro » : graphite sombre par défaut, clair au choix (Réglages › Apparence ou barre de gauche).
/// Couleurs de sens, jamais décoratives : rouge = à l'antenne, ambre = suivant, vert = prêt et niveaux, bleu = sélection.
/// Chaque pinceau de Theme.xaml est lié à une couleur d'ici : changer de thème met à jour d'un coup toute l'interface,
/// fenêtres ouvertes et éléments construits en C# compris (ils utilisent les mêmes pinceaux).
/// Les noms viennent des versions précédentes : « Blanc » est le texte, « Encre » et « Nuit » le fond des cartes, « Or » l'accent.
/// </summary>
public sealed class Palette : INotifyPropertyChanged
{
    /// <summary>Clé → (clair, sombre). Déclaré avant <see cref="Actuelle"/> : les champs statiques s'initialisent dans l'ordre.</summary>
    static readonly Dictionary<string, (string Clair, string Sombre)> Couleurs = new()
    {
        // fonds (1.5.0 : blanc chaud en clair ; noyer très foncé en sombre)
        ["Page"] = ("#F6F7F9", "#0E0F11"),
        ["Rail"] = ("#EEF0F3", "#16181C"),
        ["RailActif"] = ("#E1E6EE", "#23272E"),
        ["Encre"] = ("#FFFFFF", "#16181C"),
        ["Nuit"] = ("#FFFFFF", "#16181C"),
        ["Nuit2"] = ("#F1F3F6", "#1E2126"),
        ["Nuit3"] = ("#DDE1E7", "#2A2E35"),
        ["Trait"] = ("#E6E9EE", "#23262C"),
        ["Trait2"] = ("#C9CED6", "#3A3F48"),
        // textes
        ["Blanc"] = ("#14161A", "#ECEEF1"),
        ["Encre2"] = ("#3B4048", "#C3C8D0"),
        ["Brume"] = ("#5F6670", "#9BA1AB"),
        ["Brume2"] = ("#6B727D", "#8A909A"),
        // accent (1.5.0) : bordeaux #930000
        ["Or"] = ("#2F6FEB", "#4C8DFF"),
        ["Or2"] = ("#2459C9", "#7AABFF"),
        ["OrVif"] = ("#2F6FEB", "#4C8DFF"),
        ["OrLeger"] = ("#E8F0FE", "#16233A"),
        ["OrBord"] = ("#BBD0FA", "#2A4370"),
        ["SurOr"] = ("#FFFFFF", "#FFFFFF"),
        ["OrDegrade"] = ("#2F6FEB", "#4C8DFF"),
        ["OrDegradeClair"] = ("#2F6FEB", "#4C8DFF"),
        // bouton principal (Suivant, Importer…) : bordeaux #930000 dans les deux thèmes
        ["Primaire"] = ("#14161A", "#ECEEF1"),
        ["PrimaireSurvol"] = ("#2B2F36", "#FFFFFF"),
        ["SurPrimaire"] = ("#FFFFFF", "#0E0F11"),
        // ce qui vient avec Suivant (1.5.0) : or, à côté du rouge du direct
        ["Ensuite"] = ("#D98A00", "#F5A623"),
        ["SurEnsuite"] = ("#1A1205", "#1A1205"),
        // MyStream (1.6.0) : l'écran envoyé sur Internet (Zoom, YouTube)
        ["Stream"] = ("#4F46E5", "#8B93FF"),
        ["StreamLeger"] = ("#EEF0FF", "#1B1D33"),
        // en direct, sourdine, erreurs
        ["Rouge"] = ("#DC3D43", "#E5484D"),
        ["Direct"] = ("#DC3D43", "#E5484D"),
        ["RougeLeger"] = ("#FFF0F0", "#2A1517"),
        ["RougeBord"] = ("#F5C2C4", "#5C2326"),
        ["RougeTexte"] = ("#C42B31", "#FF6369"),
        ["Danger"] = ("#C42B31", "#FF6369"),
        // projection et moteur actifs
        ["Vert"] = ("#18794E", "#30A46C"),
        ["VertLeger"] = ("#E9F7EF", "#11251A"),
        // types d'éléments du programme (icônes des lignes, groupes de vignettes)
        ["TypeAccueil"] = ("#64748B", "#9BA1AB"),
        ["TypeChant"] = ("#2F6FEB", "#6E9BFF"),
        ["TypeLecture"] = ("#7C3AED", "#A78BFA"),
        ["TypeDisplay"] = ("#C2417B", "#F07FB0"),
        ["TypeDuo"] = ("#0F766E", "#3FC1B0"),
        ["TypePrincipal"] = ("#A16207", "#E5B455"),
        // divers
        ["BleuFonce"] = ("#1D3FA8", "#9DB0FF"),
        ["Marque"] = ("#E8F0FE", "#1B2A45"),
        ["Surligne"] = ("#FFE9A8", "#4A3A12"),
        ["TuileFond"] = ("#111316", "#0B0C0E"),
        ["TuileSource"] = ("#1E2126", "#1E2126"),
        ["TuileTexte"] = ("#C3C8D0", "#9BA1AB"),
        ["VuFond"] = ("#E6E9EE", "#1E2126"),
        ["VuCrete"] = ("#FAD9DA", "#3A1F22"),
        ["DamierA"] = ("#F1F3F6", "#16181C"),
        ["DamierB"] = ("#E3E6EB", "#1E2126"),
    };

    public static Palette Actuelle { get; } = new();

    readonly Dictionary<string, Color> actuelles = new();

    Palette() => Remplir(false);

    public bool Sombre { get; private set; }
    /// <summary>« clair » ou « sombre » (Reglages.Apparence).</summary>
    public string Nom => Sombre ? "sombre" : "clair";
    /// <summary>Toutes les clés de couleur (pour le test automatique).</summary>
    public static IReadOnlyCollection<string> Cles => Couleurs.Keys;
    /// <summary>La couleur d'une clé dans un thème donné (aperçus des Réglages, test automatique).</summary>
    public static Color De(string cle, bool sombre) => Lire(sombre ? Couleurs[cle].Sombre : Couleurs[cle].Clair);

    /// <summary>Le thème a changé (après la mise à jour des pinceaux).</summary>
    public static event Action? Change;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Applique « clair » ou « sombre » (toute autre valeur : clair). Sans effet si c'est déjà le thème.</summary>
    public void Appliquer(string? nom)
    {
        bool sombre = nom == "sombre";
        if (sombre == Sombre) return;
        Remplir(sombre);
        // nom vide : toutes les couleurs ont changé
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Change?.Invoke();
    }

    void Remplir(bool sombre)
    {
        Sombre = sombre;
        foreach (var (cle, (clair, fonce)) in Couleurs) actuelles[cle] = Lire(sombre ? fonce : clair);
    }

    static Color Lire(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    Color C(string cle) => actuelles[cle];

    public Color Page => C(nameof(Page));
    public Color Rail => C(nameof(Rail));
    public Color RailActif => C(nameof(RailActif));
    public Color Encre => C(nameof(Encre));
    public Color Nuit => C(nameof(Nuit));
    public Color Nuit2 => C(nameof(Nuit2));
    public Color Nuit3 => C(nameof(Nuit3));
    public Color Trait => C(nameof(Trait));
    public Color Trait2 => C(nameof(Trait2));
    public Color Blanc => C(nameof(Blanc));
    public Color Encre2 => C(nameof(Encre2));
    public Color Brume => C(nameof(Brume));
    public Color Brume2 => C(nameof(Brume2));
    public Color Or => C(nameof(Or));
    public Color Or2 => C(nameof(Or2));
    public Color OrVif => C(nameof(OrVif));
    public Color OrLeger => C(nameof(OrLeger));
    public Color OrBord => C(nameof(OrBord));
    public Color Primaire => C(nameof(Primaire));
    public Color PrimaireSurvol => C(nameof(PrimaireSurvol));
    public Color SurPrimaire => C(nameof(SurPrimaire));
    public Color Ensuite => C(nameof(Ensuite));
    public Color SurEnsuite => C(nameof(SurEnsuite));
    public Color SurOr => C(nameof(SurOr));
    public Color OrDegrade => C(nameof(OrDegrade));
    public Color OrDegradeClair => C(nameof(OrDegradeClair));
    public Color Stream => C(nameof(Stream));
    public Color StreamLeger => C(nameof(StreamLeger));
    public Color Rouge => C(nameof(Rouge));
    public Color Direct => C(nameof(Direct));
    public Color RougeLeger => C(nameof(RougeLeger));
    public Color RougeBord => C(nameof(RougeBord));
    public Color RougeTexte => C(nameof(RougeTexte));
    public Color Danger => C(nameof(Danger));
    public Color Vert => C(nameof(Vert));
    public Color VertLeger => C(nameof(VertLeger));
    public Color TypeAccueil => C(nameof(TypeAccueil));
    public Color TypeChant => C(nameof(TypeChant));
    public Color TypeLecture => C(nameof(TypeLecture));
    public Color TypeDisplay => C(nameof(TypeDisplay));
    public Color TypeDuo => C(nameof(TypeDuo));
    public Color TypePrincipal => C(nameof(TypePrincipal));
    public Color BleuFonce => C(nameof(BleuFonce));
    public Color Marque => C(nameof(Marque));
    public Color Surligne => C(nameof(Surligne));
    public Color TuileFond => C(nameof(TuileFond));
    public Color TuileSource => C(nameof(TuileSource));
    public Color TuileTexte => C(nameof(TuileTexte));
    public Color VuFond => C(nameof(VuFond));
    public Color VuCrete => C(nameof(VuCrete));
    public Color DamierA => C(nameof(DamierA));
    public Color DamierB => C(nameof(DamierB));
}
