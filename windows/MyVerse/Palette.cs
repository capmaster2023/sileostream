using System.ComponentModel;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Les couleurs de l'interface. Siloé Stream 2.0.2, design « Conducteur » : sombre chaud par défaut, clair au choix (Réglages › Apparence ou barre de gauche).
/// Couleurs de sens : or = À L'ÉCRAN (l'église), rouge = À L'ANTENNE (YouTube, Zoom, MyStream), bordeaux = ENVOYER À L'ANTENNE, vert = prêt.
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
        ["Page"] = ("#FAF8F5", "#141011"),
        ["Rail"] = ("#F2EEE9", "#1D1718"),
        ["RailActif"] = ("#EAE3DC", "#2A2124"),
        ["Encre"] = ("#FFFFFF", "#1D1718"),
        ["Nuit"] = ("#FFFFFF", "#1D1718"),
        ["Nuit2"] = ("#F5F1EC", "#271F21"),
        ["Nuit3"] = ("#E3DCD4", "#382C2F"),
        ["Trait"] = ("#ECE6DF", "#2C2325"),
        ["Trait2"] = ("#D3CAC0", "#4A3F41"),
        // textes
        ["Blanc"] = ("#1E1718", "#F4EDE4"),
        ["Encre2"] = ("#463D3D", "#D9CFC6"),
        ["Brume"] = ("#6B5F61", "#B5A8A0"),
        ["Brume2"] = ("#7A6E70", "#8F8279"),
        // accent (1.5.0) : bordeaux #930000
        ["Or"] = ("#9A6B0E", "#E3B04B"),
        ["Or2"] = ("#7C560B", "#F0C870"),
        ["OrVif"] = ("#B07A10", "#E3B04B"),
        ["OrLeger"] = ("#FBF3E1", "#2E2414"),
        ["OrBord"] = ("#E8D3A6", "#6B5320"),
        ["SurOr"] = ("#FFFFFF", "#1A1205"),
        ["OrDegrade"] = ("#9A6B0E", "#E3B04B"),
        ["OrDegradeClair"] = ("#9A6B0E", "#E3B04B"),
        // bouton principal (Suivant, Importer…) : bordeaux #930000 dans les deux thèmes
        ["Primaire"] = ("#1E1718", "#F4EDE4"),
        ["PrimaireSurvol"] = ("#3A3133", "#FFFFFF"),
        ["SurPrimaire"] = ("#FFFFFF", "#141011"),
        // ce qui vient avec Suivant (1.5.0) : or, à côté du rouge du direct
        ["Ensuite"] = ("#6B5F61", "#B5A8A0"),
        ["SurEnsuite"] = ("#FFFFFF", "#141011"),
        // MyStream (1.6.0) : l'écran envoyé sur Internet (Zoom, YouTube)
        ["Stream"] = ("#B3261E", "#FF8F8F"),
        ["StreamLeger"] = ("#FBEDED", "#3A1A1C"),
        // en direct, sourdine, erreurs
        ["Rouge"] = ("#C42B31", "#D13B40"),
        ["Direct"] = ("#C42B31", "#D13B40"),
        ["RougeLeger"] = ("#FFF0F0", "#3A1A1C"),
        ["RougeBord"] = ("#F5C2C4", "#6A2A2D"),
        ["RougeTexte"] = ("#B3261E", "#FF8F8F"),
        ["Danger"] = ("#B3261E", "#FF8F8F"),
        // projection et moteur actifs
        ["Vert"] = ("#18794E", "#5BC98B"),
        ["VertLeger"] = ("#E9F7EF", "#16271D"),
        // types d'éléments du programme (icônes des lignes, groupes de vignettes)
        ["TypeAccueil"] = ("#64748B", "#B5A8A0"),
        ["TypeChant"] = ("#2F6FEB", "#8FB4FF"),
        ["TypeLecture"] = ("#7C3AED", "#C9A6FF"),
        ["TypeDisplay"] = ("#C2417B", "#F59BC0"),
        ["TypeDuo"] = ("#0F766E", "#63D2C3"),
        ["TypePrincipal"] = ("#A16207", "#E3B04B"),
        // divers
        ["BleuFonce"] = ("#1D3FA8", "#9DB0FF"),
        ["Marque"] = ("#FBF3E1", "#3A2A16"),
        ["Surligne"] = ("#FFE9A8", "#4A3A12"),
        ["TuileFond"] = ("#141011", "#0E0B0C"),
        ["TuileSource"] = ("#271F21", "#271F21"),
        ["TuileTexte"] = ("#D9CFC6", "#B5A8A0"),
        ["VuFond"] = ("#ECE6DF", "#271F21"),
        ["VuCrete"] = ("#FAD9DA", "#3A1F22"),
        ["DamierA"] = ("#F5F1EC", "#1D1718"),
        ["DamierB"] = ("#EAE3DC", "#271F21"),
        // 2.0.2, design « Conducteur » : l'or pour ce qui est À L'ÉCRAN (l'église), le bordeaux pour ENVOYER À L'ANTENNE
        ["Ecran"] = ("#B07A10", "#E3B04B"),
        ["SurEcran"] = ("#FFFFFF", "#1A1205"),
        ["Antenne"] = ("#930000", "#930000"),
        ["SurAntenne"] = ("#FFFFFF", "#FFFFFF"),
        // 2.1.0 : le texte sur le vert (« Commencer le culte »)
        ["SurVert"] = ("#FFFFFF", "#0B1A10"),
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
    public Color Ecran => C(nameof(Ecran));
    public Color SurEcran => C(nameof(SurEcran));
    public Color Antenne => C(nameof(Antenne));
    public Color SurAntenne => C(nameof(SurAntenne));
    public Color SurVert => C(nameof(SurVert));
}
