using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyVerse.Miniatures;

// ======================================================================================================================
// Siloé Stream 2.3.0 : les miniatures YouTube. Une miniature est une pile de calques (image, texte, forme, dégradé) posés sur
// une image de 1280 × 720 (la taille que YouTube conseille). Le fichier « .siloeminiature » est du JSON lisible ; il ne garde
// que des références aux images (jamais modifiées). Le dessin (WPF) est dans l'interface ; ici, les données, les modèles et les
// règles (mots en couleur, mot mis en avant automatiquement, lisibilité sur téléphone).
// ======================================================================================================================

/// <summary>Une miniature : les calques, du fond vers le dessus.</summary>
public sealed class ProjetMiniature
{
    public const int Largeur = 1280, Hauteur = 720;
    public const string Extension = ".siloeminiature";
    /// <summary>YouTube refuse une miniature de plus de 2 Mo.</summary>
    public const long TailleMax = 2L * 1024 * 1024;

    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("nom")] public string Nom { get; set; } = "Miniature";
    /// <summary>La couleur sous tous les calques.</summary>
    [JsonPropertyName("fond")] public string Fond { get; set; } = "#101010";
    [JsonPropertyName("calques")] public List<Calque> Calques { get; set; } = new();

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string NouvelId() => Guid.NewGuid().ToString("N")[..10];

    public void Normaliser()
    {
        Nom = string.IsNullOrWhiteSpace(Nom) ? "Miniature" : Nom.Trim();
        Fond = Couleurs.Valide(Fond, "#101010");
        Calques = (Calques ?? new()).Where(c => c != null).Take(60).ToList();
        var vus = new HashSet<string>();
        foreach (var c in Calques)
        {
            c.Normaliser();
            if (!vus.Add(c.Id)) { c.Id = NouvelId(); vus.Add(c.Id); }
        }
    }

    public string VersJson() => JsonSerializer.Serialize(this, Options);

    public static ProjetMiniature DepuisJson(string json)
    {
        var p = JsonSerializer.Deserialize<ProjetMiniature>(json) ?? throw new InvalidDataException("Miniature illisible.");
        p.Normaliser();
        return p;
    }

    public ProjetMiniature Copie() => DepuisJson(VersJson());

    public void Enregistrer(string chemin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(chemin))!);
        var tmp = chemin + ".tmp";
        File.WriteAllText(tmp, VersJson());
        File.Move(tmp, chemin, true);
    }

    public static ProjetMiniature Ouvrir(string chemin) => DepuisJson(File.ReadAllText(chemin));

    /// <summary>Le calque qui a ce rôle (« fond », « titre », « sous », « logo », « badge »), le plus haut d'abord.</summary>
    public Calque? Role(string role) => Calques.LastOrDefault(c => c.Role == role);

    /// <summary>
    /// Les conseils de lisibilité : un texte trop petit pour un téléphone (la miniature y fait environ 168 pixels de large), un
    /// texte sous le minutage de YouTube (en bas à droite), pas de texte du tout.
    /// </summary>
    public List<string> Conseils()
    {
        var l = new List<string>();
        var textes = Calques.Where(c => c.Visible && c.Type == Calque.TypeTexte && Calque.Accents(c.Texte).Texte.Trim().Length > 0).ToList();
        if (textes.Count == 0) l.Add("Pas de texte : une miniature avec 2 à 5 mots forts attire bien plus de clics.");
        var minutage = new Rectangle(Largeur - 250, Hauteur - 95, 250, 95);
        foreach (var t in textes)
        {
            string nom = Court(Calque.Accents(t.Texte).Texte);
            if (t.Taille * 168.0 / Largeur < 7) l.Add($"« {nom} » sera illisible sur un téléphone : agrandissez-le (au moins {Math.Ceiling(7 * Largeur / 168.0)}).");
            if (new Rectangle(t.X, t.Y, t.L, t.H).Croise(minutage)) l.Add($"Le minutage de YouTube couvre le coin en bas à droite : déplacez « {nom} ».");
        }
        int mots = textes.Sum(t => Calque.Accents(t.Texte).Texte.Split(' ', '\n', StringSplitOptions.RemoveEmptyEntries).Length);
        if (mots > 14) l.Add($"{mots} mots : c'est beaucoup pour une miniature. Gardez l'essentiel (le titre complet est sous la vidéo).");
        return l;
    }

    static string Court(string s)
    {
        s = s.Replace('\n', ' ').Trim();
        return s.Length > 28 ? s[..28] + "…" : s;
    }

    readonly record struct Rectangle(double X, double Y, double L, double H)
    {
        public bool Croise(Rectangle o) => X < o.X + o.L && o.X < X + L && Y < o.Y + o.H && o.Y < Y + H;
    }
}

/// <summary>Un calque : une image, un texte, une forme ou un dégradé, posé dans un cadre (pixels de la miniature 1280 × 720).</summary>
public sealed class Calque
{
    public const string TypeImage = "image", TypeTexte = "texte", TypeForme = "forme", TypeDegrade = "degrade";

    [JsonPropertyName("id")] public string Id { get; set; } = ProjetMiniature.NouvelId();
    [JsonPropertyName("type")] public string Type { get; set; } = TypeTexte;
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    /// <summary>« fond », « titre », « sous », « logo », « badge » ou vide : les champs rapides et les modèles s'en servent.</summary>
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
    [JsonPropertyName("verrouille")] public bool Verrouille { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("l")] public double L { get; set; } = 400;
    [JsonPropertyName("h")] public double H { get; set; } = 200;
    [JsonPropertyName("rotation")] public double Rotation { get; set; }
    [JsonPropertyName("opacite")] public double Opacite { get; set; } = 1;

    // ---------- image
    [JsonPropertyName("chemin")] public string Chemin { get; set; } = "";
    /// <summary>« remplir » (l'image couvre le cadre, quitte à être coupée) ou « ajuster » (toute l'image dans le cadre).</summary>
    [JsonPropertyName("ajustement")] public string Ajustement { get; set; } = "remplir";
    [JsonPropertyName("zoom")] public double Zoom { get; set; } = 1;
    /// <summary>Quelle partie de l'image garder quand elle est coupée (0 : gauche / haut, 1 : droite / bas).</summary>
    [JsonPropertyName("cadrageX")] public double CadrageX { get; set; } = 0.5;
    [JsonPropertyName("cadrageY")] public double CadrageY { get; set; } = 0.5;
    [JsonPropertyName("luminosite")] public double Luminosite { get; set; }
    [JsonPropertyName("contraste")] public double Contraste { get; set; } = 1;
    [JsonPropertyName("saturation")] public double Saturation { get; set; } = 1;
    [JsonPropertyName("flou")] public double Flou { get; set; }
    [JsonPropertyName("miroir")] public bool Miroir { get; set; }

    // ---------- image et forme
    [JsonPropertyName("arrondi")] public double Arrondi { get; set; }
    [JsonPropertyName("bordure")] public string Bordure { get; set; } = "#FFFFFF";
    [JsonPropertyName("epaisseurBordure")] public double EpaisseurBordure { get; set; }

    // ---------- texte
    /// <summary>Le texte ; les mots entre astérisques (« la *grâce* de Dieu ») prennent la couleur d'accent.</summary>
    [JsonPropertyName("texte")] public string Texte { get; set; } = "";
    [JsonPropertyName("police")] public string Police { get; set; } = "Impact";
    [JsonPropertyName("taille")] public double Taille { get; set; } = 110;
    [JsonPropertyName("poids")] public int Poids { get; set; } = 700;
    [JsonPropertyName("italique")] public bool Italique { get; set; }
    [JsonPropertyName("couleur")] public string Couleur { get; set; } = "#FFFFFF";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#FFD400";
    [JsonPropertyName("contour")] public string Contour { get; set; } = "#000000";
    /// <summary>L'épaisseur du contour du texte (0 : sans contour).</summary>
    [JsonPropertyName("epaisseur")] public double Epaisseur { get; set; } = 6;
    [JsonPropertyName("ombre")] public bool Ombre { get; set; } = true;
    [JsonPropertyName("ombreCouleur")] public string OmbreCouleur { get; set; } = "#000000";
    [JsonPropertyName("ombreFlou")] public double OmbreFlou { get; set; } = 18;
    [JsonPropertyName("ombreDistance")] public double OmbreDistance { get; set; } = 6;
    [JsonPropertyName("majuscules")] public bool Majuscules { get; set; } = true;
    /// <summary>« gauche », « centre » ou « droite ».</summary>
    [JsonPropertyName("alignement")] public string Alignement { get; set; } = "gauche";
    [JsonPropertyName("interligne")] public double Interligne { get; set; } = 0.95;
    /// <summary>Un fond plein derrière le texte (badge, bandeau) ; vide : pas de fond.</summary>
    [JsonPropertyName("fondTexte")] public string FondTexte { get; set; } = "";
    [JsonPropertyName("marge")] public double Marge { get; set; } = 16;

    // ---------- forme et dégradé
    /// <summary>« rectangle », « cercle » ou « fleche ».</summary>
    [JsonPropertyName("forme")] public string FormeType { get; set; } = "rectangle";
    [JsonPropertyName("remplissage")] public string Remplissage { get; set; } = "#E1251B";
    /// <summary>Dégradé : « gauche », « droite », « haut », « bas » (d'où vient l'ombre) ou « vignette » (les bords).</summary>
    [JsonPropertyName("direction")] public string Direction { get; set; } = "gauche";
    [JsonPropertyName("force")] public double Force { get; set; } = 0.85;
    [JsonPropertyName("etendue")] public double Etendue { get; set; } = 0.6;

    static double B(double v, double min, double max, double d) => double.IsFinite(v) ? Math.Clamp(v, min, max) : d;

    public void Normaliser()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? ProjetMiniature.NouvelId() : Id;
        if (Type is not (TypeImage or TypeTexte or TypeForme or TypeDegrade)) Type = TypeTexte;
        Nom ??= ""; Role ??= ""; Chemin ??= ""; Texte ??= ""; Police = string.IsNullOrWhiteSpace(Police) ? "Impact" : Police;
        X = B(X, -5000, 5000, 0); Y = B(Y, -5000, 5000, 0);
        L = B(L, 4, 10000, 400); H = B(H, 4, 10000, 200);
        Rotation = B(Rotation, -360, 360, 0);
        Opacite = B(Opacite, 0, 1, 1);
        if (Ajustement is not ("remplir" or "ajuster")) Ajustement = "remplir";
        Zoom = B(Zoom, 0.2, 6, 1);
        CadrageX = B(CadrageX, 0, 1, 0.5); CadrageY = B(CadrageY, 0, 1, 0.5);
        Luminosite = B(Luminosite, -1, 1, 0); Contraste = B(Contraste, 0, 3, 1); Saturation = B(Saturation, 0, 3, 1);
        Flou = B(Flou, 0, 60, 0);
        Arrondi = B(Arrondi, 0, 2000, 0);
        EpaisseurBordure = B(EpaisseurBordure, 0, 80, 0);
        Taille = B(Taille, 6, 600, 110);
        Poids = Math.Clamp(Poids, 100, 950);
        Epaisseur = B(Epaisseur, 0, 60, 6);
        OmbreFlou = B(OmbreFlou, 0, 120, 18); OmbreDistance = B(OmbreDistance, 0, 80, 6);
        if (Alignement is not ("gauche" or "centre" or "droite")) Alignement = "gauche";
        Interligne = B(Interligne, 0.6, 2, 0.95);
        Marge = B(Marge, 0, 200, 16);
        if (FormeType is not ("rectangle" or "cercle" or "fleche")) FormeType = "rectangle";
        if (Direction is not ("gauche" or "droite" or "haut" or "bas" or "vignette")) Direction = "gauche";
        Force = B(Force, 0, 1, 0.85); Etendue = B(Etendue, 0.05, 1, 0.6);
        Couleur = Couleurs.Valide(Couleur, "#FFFFFF"); Accent = Couleurs.Valide(Accent, "#FFD400"); Contour = Couleurs.Valide(Contour, "#000000");
        OmbreCouleur = Couleurs.Valide(OmbreCouleur, "#000000"); Remplissage = Couleurs.Valide(Remplissage, "#E1251B"); Bordure = Couleurs.Valide(Bordure, "#FFFFFF");
        FondTexte = FondTexte is { Length: > 0 } f ? Couleurs.Valide(f, "") : "";
    }

    public Calque Copie() => JsonSerializer.Deserialize<Calque>(JsonSerializer.Serialize(this))!;

    /// <summary>
    /// Le texte sans les astérisques, et les morceaux à mettre en couleur d'accent : « la *grâce* de Dieu » → « la grâce de Dieu »,
    /// (3, 5). Un astérisque seul reste tel quel.
    /// </summary>
    public static (string Texte, List<(int Debut, int Longueur)> Accents) Accents(string? brut)
    {
        brut ??= "";
        var sb = new System.Text.StringBuilder();
        var l = new List<(int, int)>();
        int i = 0;
        while (i < brut.Length)
        {
            if (brut[i] == '*')
            {
                int fin = brut.IndexOf('*', i + 1);
                if (fin > i + 1)
                {
                    l.Add((sb.Length, fin - i - 1));
                    sb.Append(brut, i + 1, fin - i - 1);
                    i = fin + 1;
                    continue;
                }
            }
            sb.Append(brut[i]);
            i++;
        }
        return (sb.ToString(), l);
    }

    static readonly HashSet<string> Petits = new(StringComparer.OrdinalIgnoreCase)
    {
        "les", "des", "une", "est", "que", "qui", "dans", "pour", "avec", "sur", "par", "mon", "ton", "son", "nos", "vos", "ses", "mes", "tes",
        "ils", "elle", "nous", "vous", "tout", "tous", "mais", "aux", "the", "and", "you", "for", "with", "this", "that", "dieu", "god", "culte",
    };

    /// <summary>
    /// Met en avant un mot fort (le plus long, hors petits mots) s'il n'y en a pas déjà : « Marcher par la foi » → « Marcher par la
    /// *foi* » n'est pas choisi (3 lettres) ; « La puissance de la prière » → « La *puissance* de la prière ».
    /// </summary>
    public static string AccentAuto(string? titre)
    {
        titre ??= "";
        if (titre.Contains('*')) return titre;
        var mots = Regex.Matches(titre, @"[\p{L}'’-]+").Where(m => m.Value.Length >= 4 && !Petits.Contains(m.Value)).ToList();
        if (mots.Count < 2) return titre;
        var m = mots.OrderByDescending(x => x.Value.Length).ThenBy(x => x.Index).First();
        return titre[..m.Index] + "*" + m.Value + "*" + titre[(m.Index + m.Length)..];
    }
}

/// <summary>Les couleurs « #RRGGBB » ou « #AARRGGBB ».</summary>
public static class Couleurs
{
    static readonly Regex Hex = new(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);
    public static string Valide(string? c, string defaut) => c != null && Hex.IsMatch(c.Trim()) ? c.Trim().ToUpperInvariant() : defaut;
    public static (byte A, byte R, byte G, byte B) Lire(string c)
    {
        c = Valide(c, "#000000");
        uint v = uint.Parse(c[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return c.Length == 7 ? ((byte)255, (byte)(v >> 16), (byte)(v >> 8), (byte)v) : ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}

/// <summary>Un modèle de miniature : une mise en page toute prête, remplie avec l'image, le titre et le logo.</summary>
public sealed record ModeleMiniature(string Id, string Nom, string Description);

/// <summary>
/// Les modèles (prédication, direct, louange, verset, annonce, témoignage). Chacun est construit à partir de ce que l'on a :
/// l'image (souvent envoyée depuis Montage), le titre, le sous-titre et le logo de l'église.
/// </summary>
public static class ModelesMiniature
{
    public static readonly ModeleMiniature[] Tous =
    {
        new("predication", "Prédication", "Ombre à gauche, gros titre, nom du prédicateur sur un bandeau"),
        new("direct", "Culte en direct", "Badge « EN DIRECT », titre en bas"),
        new("louange", "Louange", "Titre au centre, bords assombris"),
        new("verset", "Verset", "Image floutée, citation et référence"),
        new("annonce", "Annonce", "Bandeau incliné en couleur"),
        new("temoignage", "Témoignage", "Texte à gauche sur fond sombre, image à droite"),
    };

    const int W = ProjetMiniature.Largeur, H = ProjetMiniature.Hauteur;

    static Calque Fond(string? image, double luminosite = 0, double flou = 0) => new()
    {
        Type = Calque.TypeImage, Nom = "Image de fond", Role = "fond", Chemin = image ?? "", X = 0, Y = 0, L = W, H = H, Luminosite = luminosite, Flou = flou,
        Verrouille = false,
    };

    static Calque Ombre(string direction, double force, double etendue, string couleur = "#000000") => new()
    {
        Type = Calque.TypeDegrade, Nom = "Dégradé", Direction = direction, Force = force, Etendue = etendue, Remplissage = couleur, X = 0, Y = 0, L = W, H = H,
    };

    static Calque Titre(string texte, double x, double y, double l, double h, double taille, string alignement, string police = "Impact") => new()
    {
        Type = Calque.TypeTexte, Nom = "Titre", Role = "titre", Texte = Calque.AccentAuto(texte), X = x, Y = y, L = l, H = h, Taille = taille, Police = police,
        Poids = 800, Alignement = alignement, Couleur = "#FFFFFF", Accent = "#FFD400", Contour = "#000000", Epaisseur = 7, Ombre = true, OmbreFlou = 22,
        OmbreDistance = 7, Majuscules = true, Interligne = 0.92,
    };

    static Calque SousTitre(string texte, double x, double y, double l, double h, double taille, string fond, string alignement = "gauche") => new()
    {
        Type = Calque.TypeTexte, Nom = "Sous-titre", Role = "sous", Texte = texte, X = x, Y = y, L = l, H = h, Taille = taille, Police = "Bahnschrift",
        Poids = 700, Alignement = alignement, Couleur = "#FFFFFF", Epaisseur = 0, Ombre = fond.Length == 0, OmbreFlou = 12, OmbreDistance = 3,
        Majuscules = true, FondTexte = fond, Marge = 18, Arrondi = 10,
    };

    static Calque Logo(string chemin, double x, double y, double taille) => new()
    {
        Type = Calque.TypeImage, Nom = "Logo", Role = "logo", Chemin = chemin, Ajustement = "ajuster", X = x, Y = y, L = taille, H = taille,
    };

    /// <summary>Construit le modèle <paramref name="id"/> avec ce que l'on a (chaque élément est facultatif).</summary>
    public static ProjetMiniature Creer(string id, string? image, string titre, string sousTitre, string? logo, string nom = "Miniature")
    {
        var p = new ProjetMiniature { Nom = nom };
        var c = p.Calques;
        bool avecLogo = !string.IsNullOrWhiteSpace(logo);
        titre = string.IsNullOrWhiteSpace(titre) ? "Titre de la vidéo" : titre.Trim();
        sousTitre = sousTitre?.Trim() ?? "";
        switch (id)
        {
            case "direct":
                c.Add(Fond(image));
                c.Add(Ombre("bas", 0.92, 0.62));
                c.Add(new Calque
                {
                    Type = Calque.TypeTexte, Nom = "Badge", Role = "badge", Texte = "● EN DIRECT", X = 48, Y = 44, L = 330, H = 82, Taille = 46, Police = "Bahnschrift",
                    Poids = 700, Alignement = "centre", Couleur = "#FFFFFF", Epaisseur = 0, Ombre = false, Majuscules = true, FondTexte = "#E1251B", Marge = 12, Arrondi = 12,
                });
                c.Add(Titre(titre, 60, 410, 1160, 220, 104, "centre"));
                if (sousTitre.Length > 0) c.Add(SousTitre(sousTitre, 240, 628, 800, 64, 40, "", "centre"));
                if (avecLogo) c.Add(Logo(logo!, W - 48 - 110, 40, 110));
                break;
            case "louange":
                c.Add(Fond(image, -0.08));
                c.Add(Ombre("vignette", 0.9, 0.7));
                c.Add(Titre(titre, 80, 190, 1120, 300, 112, "centre", "Unbounded"));
                c[^1].Epaisseur = 0; c[^1].OmbreFlou = 45; c[^1].OmbreDistance = 0; c[^1].Accent = "#FFD400"; c[^1].Taille = 92;
                if (sousTitre.Length > 0) c.Add(SousTitre(sousTitre, 240, 500, 800, 70, 44, "", "centre"));
                if (avecLogo) c.Add(Logo(logo!, (W - 100) / 2.0, 46, 100));
                break;
            case "verset":
                c.Add(Fond(image, -0.38, 7));
                c.Add(Ombre("vignette", 0.75, 0.6));
                c.Add(new Calque
                {
                    Type = Calque.TypeTexte, Nom = "Guillemets", Texte = "«", X = 70, Y = 40, L = 220, H = 230, Taille = 260, Police = "Georgia", Poids = 700,
                    Couleur = "#FFD400", Epaisseur = 0, Ombre = false, Majuscules = false, Alignement = "gauche",
                });
                c.Add(new Calque
                {
                    Type = Calque.TypeTexte, Nom = "Citation", Role = "titre", Texte = titre, X = 150, Y = 170, L = 980, H = 340, Taille = 62, Police = "Literata",
                    Poids = 600, Couleur = "#FFFFFF", Accent = "#FFD400", Epaisseur = 0, Ombre = true, OmbreFlou = 20, OmbreDistance = 2, Majuscules = false,
                    Alignement = "centre", Interligne = 1.15,
                });
                c.Add(SousTitre(sousTitre.Length > 0 ? sousTitre : "Jean 3:16", 150, 540, 980, 80, 50, "", "centre"));
                c[^1].Couleur = "#FFD400";
                if (avecLogo) c.Add(Logo(logo!, W - 48 - 96, H - 48 - 96 - 60, 96));
                break;
            case "annonce":
                c.Add(Fond(image, -0.1));
                c.Add(new Calque { Type = Calque.TypeForme, Nom = "Bandeau", FormeType = "rectangle", Remplissage = "#E1251B", X = -80, Y = 245, L = W + 160, H = 240, Rotation = -5, Opacite = 0.94 });
                c.Add(Titre(titre, 70, 250, 1140, 230, 112, "centre"));
                c[^1].Rotation = -5; c[^1].Epaisseur = 0; c[^1].OmbreFlou = 10; c[^1].OmbreDistance = 4; c[^1].Accent = "#FFE45C";
                if (sousTitre.Length > 0) { c.Add(SousTitre(sousTitre, 360, 510, 560, 76, 42, "#111111", "centre")); c[^1].Rotation = -5; }
                if (avecLogo) c.Add(Logo(logo!, 48, 40, 110));
                break;
            case "temoignage":
                c.Add(Fond(image));
                c[^1].X = 470; c[^1].L = W - 470;
                c.Add(new Calque { Type = Calque.TypeForme, Nom = "Fond du texte", FormeType = "rectangle", Remplissage = "#121212", X = 0, Y = 0, L = 560, H = H });
                c.Add(new Calque { Type = Calque.TypeDegrade, Nom = "Fondu", Direction = "gauche", Force = 1, Etendue = 1, Remplissage = "#121212", X = 560, Y = 0, L = 260, H = H });
                c.Add(Titre(titre, 50, 120, 480, 380, 92, "gauche"));
                if (sousTitre.Length > 0) c.Add(SousTitre(sousTitre, 50, 540, 470, 72, 40, "#E1251B"));
                if (avecLogo) c.Add(Logo(logo!, 50, 34, 84));
                break;
            default: // prédication
                c.Add(Fond(image));
                c.Add(Ombre("gauche", 0.9, 0.78));
                c.Add(Titre(titre, 60, 140, 760, 380, 116, "gauche"));
                if (sousTitre.Length > 0) c.Add(SousTitre(sousTitre, 64, 548, 600, 80, 44, "#E1251B"));
                if (avecLogo) c.Add(Logo(logo!, 64, 40, 100));
                break;
        }
        if (string.IsNullOrEmpty(image)) c.RemoveAll(x => x.Role == "fond");
        p.Normaliser();
        return p;
    }
}
