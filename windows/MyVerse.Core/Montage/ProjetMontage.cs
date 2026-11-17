using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVerse.Montages;

// ======================================================================================================================
// Siloé Stream 2.2.0 : le montage. Un projet de montage ne modifie jamais les enregistrements : il ne garde que des
// références (fichier, début, fin) et des réglages. La piste vidéo principale met les séquences bout à bout (avec, au
// besoin, un fondu entre deux séquences), entre un générique de début et un générique de fin ; les titres, le logo et les
// pistes son supplémentaires se posent par-dessus. Le rendu (RenduMontage) lit ces réglages ; il copie les images telles
// quelles quand rien ne l'oblige à réencoder.
// ======================================================================================================================

/// <summary>Un projet de montage (fichier « .siloemontage », du JSON lisible).</summary>
public sealed class ProjetMontage
{
    public const string Extension = ".siloemontage";
    public const string Aucune = "aucune", Fondu = "fondu", Noir = "noir";

    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("nom")] public string Nom { get; set; } = "Montage du culte";
    /// <summary>La piste vidéo principale : les séquences, bout à bout, dans l'ordre.</summary>
    [JsonPropertyName("sequences")] public List<SequenceMontage> Sequences { get; set; } = new();
    /// <summary>Les sons ajoutés (musique, micro enregistré à part…), posés librement sur les pistes son A1 à A4.</summary>
    [JsonPropertyName("audios")] public List<ClipAudio> Audios { get; set; } = new();
    [JsonPropertyName("titres")] public List<TitreMontage> Titres { get; set; } = new();
    [JsonPropertyName("logo")] public LogoMontage Logo { get; set; } = new();
    [JsonPropertyName("intro")] public GeneriqueMontage Intro { get; set; } = new() { Texte = "Bienvenue" };
    [JsonPropertyName("outro")] public GeneriqueMontage Outro { get; set; } = new() { Texte = "Merci de nous avoir suivis", SousTexte = "Que Dieu vous bénisse" };
    /// <summary>Le volume du son des séquences (1 = tel quel).</summary>
    [JsonPropertyName("volume")] public double VolumePrincipal { get; set; } = 1;
    [JsonPropertyName("sortie")] public SortieMontage Sortie { get; set; } = new();
    /// <summary>Les fichiers importés dans ce montage (la liste de gauche), utilisés ou pas.</summary>
    [JsonPropertyName("medias")] public List<string> Medias { get; set; } = new();

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string NouvelId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Remet chaque valeur dans ses limites (fichier modifié à la main, ancienne version).</summary>
    public void Normaliser()
    {
        Nom = string.IsNullOrWhiteSpace(Nom) ? "Montage" : Nom.Trim();
        Sequences = (Sequences ?? new()).Where(s => s != null).ToList();
        Audios = (Audios ?? new()).Where(a => a != null).ToList();
        Titres = (Titres ?? new()).Where(t => t != null).ToList();
        Medias = (Medias ?? new()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var s in Sequences) s.Normaliser();
        foreach (var a in Audios) a.Normaliser();
        foreach (var t in Titres) t.Normaliser();
        Logo ??= new(); Logo.Normaliser();
        Intro ??= new(); Intro.Normaliser();
        Outro ??= new(); Outro.Normaliser();
        Sortie ??= new(); Sortie.Normaliser();
        VolumePrincipal = Borne(VolumePrincipal, 0, 4, 1);
        // identifiants en double (copier-coller d'un fichier) : on les sépare
        var vus = new HashSet<string>();
        foreach (var s in Sequences) if (!vus.Add(s.Id)) { s.Id = NouvelId(); vus.Add(s.Id); }
        foreach (var a in Audios) if (!vus.Add(a.Id)) { a.Id = NouvelId(); vus.Add(a.Id); }
        foreach (var t in Titres) if (!vus.Add(t.Id)) { t.Id = NouvelId(); vus.Add(t.Id); }
    }

    public static double Borne(double v, double min, double max, double defaut) => double.IsFinite(v) ? Math.Clamp(v, min, max) : defaut;

    public string VersJson() => JsonSerializer.Serialize(this, Options);

    public static ProjetMontage DepuisJson(string json)
    {
        var p = JsonSerializer.Deserialize<ProjetMontage>(json) ?? throw new InvalidDataException("Projet de montage illisible.");
        p.Normaliser();
        return p;
    }

    public ProjetMontage Copie() => DepuisJson(VersJson());

    /// <summary>Écrit le projet (d'abord à côté, puis remplace : un fichier coupé en deux n'existe jamais).</summary>
    public void Enregistrer(string chemin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(chemin))!);
        var tmp = chemin + ".tmp";
        File.WriteAllText(tmp, VersJson());
        File.Move(tmp, chemin, true);
    }

    public static ProjetMontage Ouvrir(string chemin) => DepuisJson(File.ReadAllText(chemin));

    /// <summary>Tous les fichiers utilisés (séquences, sons, logo).</summary>
    public IEnumerable<string> Fichiers()
    {
        var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Sequences) if (vus.Add(s.Source)) yield return s.Source;
        foreach (var a in Audios) if (vus.Add(a.Source)) yield return a.Source;
    }

    // ------------------------------------------------------------ la piste vidéo principale, placée sur la ligne de temps
    /// <summary>
    /// La piste vidéo telle qu'elle passe : générique de début, séquences, générique de fin, chacun à sa place sur la ligne de
    /// temps (secondes). Un fondu fait chevaucher deux séquences : la suivante commence avant la fin de la précédente.
    /// </summary>
    public List<ElementPiste> Piste()
    {
        var l = new List<ElementPiste>();
        double t = 0;
        if (Intro.Actif) { l.Add(new ElementPiste(TypeElement.Intro, -1, 0, Intro.Duree, 0, Aucune)); t = Intro.Duree; }
        for (int i = 0; i < Sequences.Count; i++)
        {
            var s = Sequences[i];
            double longueur = s.Longueur;
            double recouvrement = 0;
            string transition = Aucune;
            // un fondu seulement entre deux séquences (les génériques ont leur propre fondu au noir)
            if (i > 0 && s.Transition != Aucune)
            {
                recouvrement = Math.Min(s.DureeTransition, Math.Min(Sequences[i - 1].Longueur, longueur) / 2);
                if (recouvrement >= 0.04) transition = s.Transition; else recouvrement = 0;
            }
            t -= recouvrement;
            l.Add(new ElementPiste(TypeElement.Sequence, i, t, longueur, recouvrement, transition));
            t += longueur;
        }
        if (Outro.Actif) l.Add(new ElementPiste(TypeElement.Outro, -1, t, Outro.Duree, 0, Aucune));
        return l;
    }

    /// <summary>La durée du montage (secondes).</summary>
    public double Duree => Piste() is { Count: > 0 } p ? p[^1].Debut + p[^1].Longueur : 0;

    /// <summary>Ce qui passe à l'instant <paramref name="t"/> de la ligne de temps (pendant un fondu : la séquence qui arrive).</summary>
    public ElementPiste? ElementA(double t)
    {
        ElementPiste? r = null;
        foreach (var e in Piste()) if (t >= e.Debut - 1e-9) r = e;
        return r is { } x && t <= x.Debut + x.Longueur + 1e-6 ? x : null;
    }

    /// <summary>Le début (ligne de temps) de la séquence <paramref name="index"/>.</summary>
    public double DebutSequence(int index) => Piste().FirstOrDefault(e => e.Type == TypeElement.Sequence && e.Index == index)?.Debut ?? 0;

    /// <summary>
    /// Coupe la séquence qui passe à l'instant <paramref name="t"/> en deux (outil « Couper », touche S). Renvoie la nouvelle
    /// séquence (la seconde moitié), ou null si l'instant est trop près d'un bord.
    /// </summary>
    public SequenceMontage? Couper(double t)
    {
        if (ElementA(t) is not { Type: TypeElement.Sequence } e) return null;
        var s = Sequences[e.Index];
        double dans = s.Debut + (t - e.Debut);
        if (dans <= s.Debut + 0.1 || dans >= s.Fin - 0.1) return null;
        var n = new SequenceMontage { Source = s.Source, Debut = dans, Fin = s.Fin, Volume = s.Volume, Muet = s.Muet, DureeTransition = s.DureeTransition, DureeSource = s.DureeSource };
        s.Fin = dans;
        Sequences.Insert(e.Index + 1, n);
        return n;
    }

    /// <summary>« 1:02:03.25 » : heures facultatives, centièmes facultatifs.</summary>
    public static string Temps(double s, bool centiemes = true)
    {
        if (!double.IsFinite(s) || s < 0) s = 0;
        var t = TimeSpan.FromSeconds(s);
        string b = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        return centiemes ? b + "." + (t.Milliseconds / 10).ToString("00", CultureInfo.InvariantCulture) : b;
    }

    /// <summary>Lit « 1:02:03.25 », « 62:03 », « 95.5 » ou « 95,5 » (secondes) ; null si ce n'est pas un temps.</summary>
    public static double? LireTemps(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return null;
        var parties = texte.Trim().Replace(',', '.').Split(':');
        if (parties.Length > 3) return null;
        double total = 0;
        for (int i = 0; i < parties.Length; i++)
        {
            if (!double.TryParse(parties[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return null;
            if (i < parties.Length - 1 && (v != Math.Floor(v))) return null;
            if (i > 0 && v >= 60) return null;
            total = total * 60 + v;
        }
        return total;
    }
}

public enum TypeElement { Intro, Sequence, Outro }

/// <summary>Un élément de la piste vidéo placé sur la ligne de temps (secondes).</summary>
/// <param name="Index">L'index de la séquence dans <see cref="ProjetMontage.Sequences"/> (−1 pour un générique).</param>
/// <param name="Recouvrement">Le fondu avec l'élément précédent (secondes, 0 : coupe franche).</param>
public sealed record ElementPiste(TypeElement Type, int Index, double Debut, double Longueur, double Recouvrement, string Transition)
{
    public double Fin => Debut + Longueur;
}

/// <summary>Une séquence de la piste vidéo : un morceau d'un enregistrement (de <see cref="Debut"/> à <see cref="Fin"/>, secondes dans le fichier).</summary>
public sealed class SequenceMontage
{
    [JsonPropertyName("id")] public string Id { get; set; } = ProjetMontage.NouvelId();
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("debut")] public double Debut { get; set; }
    [JsonPropertyName("fin")] public double Fin { get; set; }
    /// <summary>La durée du fichier (connue à l'ajout) : la fin ne la dépasse pas.</summary>
    [JsonPropertyName("dureeSource")] public double DureeSource { get; set; }
    [JsonPropertyName("volume")] public double Volume { get; set; } = 1;
    [JsonPropertyName("muet")] public bool Muet { get; set; }
    /// <summary>La transition depuis la séquence précédente : « aucune », « fondu » (fondu enchaîné) ou « noir » (par le noir).</summary>
    [JsonPropertyName("transition")] public string Transition { get; set; } = ProjetMontage.Aucune;
    [JsonPropertyName("dureeTransition")] public double DureeTransition { get; set; } = 1;

    [JsonIgnore] public double Longueur => Math.Max(0, Fin - Debut);
    [JsonIgnore] public string Nom => Path.GetFileNameWithoutExtension(Source);

    public void Normaliser()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? ProjetMontage.NouvelId() : Id;
        Source ??= "";
        DureeSource = ProjetMontage.Borne(DureeSource, 0, 1e7, 0);
        Debut = ProjetMontage.Borne(Debut, 0, 1e7, 0);
        Fin = ProjetMontage.Borne(Fin, 0, 1e7, 0);
        if (DureeSource > 0) { Fin = Math.Min(Fin, DureeSource); Debut = Math.Min(Debut, Math.Max(0, DureeSource - 0.1)); }
        if (Fin < Debut + 0.1) Fin = Debut + 0.1;
        Volume = ProjetMontage.Borne(Volume, 0, 4, 1);
        if (Transition is not (ProjetMontage.Aucune or ProjetMontage.Fondu or ProjetMontage.Noir)) Transition = ProjetMontage.Aucune;
        DureeTransition = ProjetMontage.Borne(DureeTransition, 0.1, 5, 1);
    }
}

/// <summary>Un son posé sur une piste son (A1 à A4) : un morceau d'un fichier, à une place de la ligne de temps.</summary>
public sealed class ClipAudio
{
    public const int Pistes = 4;
    [JsonPropertyName("id")] public string Id { get; set; } = ProjetMontage.NouvelId();
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    /// <summary>La piste son : 1 à 4.</summary>
    [JsonPropertyName("piste")] public int Piste { get; set; } = 1;
    /// <summary>Où il commence sur la ligne de temps (secondes).</summary>
    [JsonPropertyName("position")] public double Position { get; set; }
    [JsonPropertyName("debut")] public double Debut { get; set; }
    [JsonPropertyName("fin")] public double Fin { get; set; }
    [JsonPropertyName("dureeSource")] public double DureeSource { get; set; }
    [JsonPropertyName("volume")] public double Volume { get; set; } = 1;
    [JsonPropertyName("muet")] public bool Muet { get; set; }
    [JsonPropertyName("fonduEntree")] public double FonduEntree { get; set; } = 0.5;
    [JsonPropertyName("fonduSortie")] public double FonduSortie { get; set; } = 1;

    [JsonIgnore] public double Longueur => Math.Max(0, Fin - Debut);
    [JsonIgnore] public string Nom => Path.GetFileNameWithoutExtension(Source);

    public void Normaliser()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? ProjetMontage.NouvelId() : Id;
        Source ??= "";
        Piste = Math.Clamp(Piste, 1, Pistes);
        Position = ProjetMontage.Borne(Position, 0, 1e7, 0);
        DureeSource = ProjetMontage.Borne(DureeSource, 0, 1e7, 0);
        Debut = ProjetMontage.Borne(Debut, 0, 1e7, 0);
        Fin = ProjetMontage.Borne(Fin, 0, 1e7, 0);
        if (DureeSource > 0) { Fin = Math.Min(Fin, DureeSource); Debut = Math.Min(Debut, Math.Max(0, DureeSource - 0.1)); }
        if (Fin < Debut + 0.1) Fin = Debut + 0.1;
        Volume = ProjetMontage.Borne(Volume, 0, 4, 1);
        FonduEntree = ProjetMontage.Borne(FonduEntree, 0, 10, 0.5);
        FonduSortie = ProjetMontage.Borne(FonduSortie, 0, 10, 1);
    }
}

/// <summary>Un titre posé sur l'image (nom du prédicateur, référence biblique, annonce…).</summary>
public sealed class TitreMontage
{
    public const string Bas = "bas", Centre = "centre", Haut = "haut";
    [JsonPropertyName("id")] public string Id { get; set; } = ProjetMontage.NouvelId();
    [JsonPropertyName("texte")] public string Texte { get; set; } = "Titre";
    [JsonPropertyName("sousTexte")] public string SousTexte { get; set; } = "";
    /// <summary>Début sur la ligne de temps (secondes).</summary>
    [JsonPropertyName("debut")] public double Debut { get; set; }
    [JsonPropertyName("duree")] public double Duree { get; set; } = 6;
    /// <summary>« bas » (bandeau en bas à gauche), « centre » ou « haut ».</summary>
    [JsonPropertyName("position")] public string Position { get; set; } = Bas;
    /// <summary>Taille du texte en pixels pour une image de 1080 lignes (le reste suit).</summary>
    [JsonPropertyName("taille")] public double Taille { get; set; } = 54;

    public void Normaliser()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? ProjetMontage.NouvelId() : Id;
        Texte ??= ""; SousTexte ??= "";
        Debut = ProjetMontage.Borne(Debut, 0, 1e7, 0);
        Duree = ProjetMontage.Borne(Duree, 0.5, 3600, 6);
        if (Position is not (Bas or Centre or Haut)) Position = Bas;
        Taille = ProjetMontage.Borne(Taille, 16, 200, 54);
    }
}

/// <summary>Le logo de l'église, dans un coin de l'image pendant les séquences.</summary>
public sealed class LogoMontage
{
    [JsonPropertyName("actif")] public bool Actif { get; set; }
    [JsonPropertyName("chemin")] public string Chemin { get; set; } = "";
    /// <summary>« hd » (en haut à droite), « hg », « bd » ou « bg ».</summary>
    [JsonPropertyName("coin")] public string Coin { get; set; } = "hd";
    /// <summary>Largeur du logo, en part de la largeur de l'image.</summary>
    [JsonPropertyName("taille")] public double Taille { get; set; } = 0.1;
    [JsonPropertyName("opacite")] public double Opacite { get; set; } = 0.9;

    /// <summary>Le logo sera vraiment dessiné (coché, et le fichier existe).</summary>
    [JsonIgnore] public bool Visible => Actif && Chemin.Length > 0 && File.Exists(Chemin);

    public void Normaliser()
    {
        Chemin ??= "";
        if (Coin is not ("hd" or "hg" or "bd" or "bg")) Coin = "hd";
        Taille = ProjetMontage.Borne(Taille, 0.03, 0.5, 0.1);
        Opacite = ProjetMontage.Borne(Opacite, 0.1, 1, 0.9);
    }
}

/// <summary>Un générique (début ou fin) : un fond noir, le logo s'il y en a un, un texte et un sous-texte, un fondu au noir.</summary>
public sealed class GeneriqueMontage
{
    [JsonPropertyName("actif")] public bool Actif { get; set; }
    [JsonPropertyName("texte")] public string Texte { get; set; } = "";
    [JsonPropertyName("sousTexte")] public string SousTexte { get; set; } = "";
    [JsonPropertyName("duree")] public double Duree { get; set; } = 5;

    public void Normaliser()
    {
        Texte ??= ""; SousTexte ??= "";
        Duree = ProjetMontage.Borne(Duree, 1, 60, 5);
    }
}

/// <summary>Comment le montage est rendu : taille, codec, encodeur, qualité, et s'il peut copier les images sans les réencoder.</summary>
public sealed class SortieMontage
{
    /// <summary>« source » (la taille des enregistrements), « 1080 », « 1440 » ou « 2160 » (4K).</summary>
    [JsonPropertyName("resolution")] public string Resolution { get; set; } = "source";
    /// <summary>« h264 » (lisible partout, conseillé pour YouTube) ou « hevc » (H.265 : fichiers plus légers, même qualité).</summary>
    [JsonPropertyName("codec")] public string Codec { get; set; } = "h264";
    /// <summary>« auto » (le meilleur encodeur qui marche sur ce PC) ou un encodeur de FFmpeg (h264_nvenc, h264_qsv, libx264…).</summary>
    [JsonPropertyName("encodeur")] public string Encodeur { get; set; } = "auto";
    /// <summary>Qualité constante : 14 (presque sans perte, gros fichier) à 30 (léger) ; 18 à 20 : excellent pour YouTube.</summary>
    [JsonPropertyName("qualite")] public int Qualite { get; set; } = 19;
    /// <summary>« auto » : copier les images telles quelles quand c'est possible (coupes sur les images clés) ; « exact » : toujours réencoder, coupes à l'image près.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "auto";
    /// <summary>Décoder les enregistrements avec la carte graphique (plus rapide en 4K) ; le processeur prend le relais si elle refuse.</summary>
    [JsonPropertyName("decodageMateriel")] public bool DecodageMateriel { get; set; } = true;

    public void Normaliser()
    {
        if (Resolution is not ("source" or "1080" or "1440" or "2160")) Resolution = "source";
        if (Codec is not ("h264" or "hevc")) Codec = "h264";
        if (string.IsNullOrWhiteSpace(Encodeur) || (Encodeur != "auto" && EncodeursVideo.Tous.All(e => e.Nom != Encodeur))) Encodeur = "auto";
        Qualite = Math.Clamp(Qualite, 14, 30);
        if (Mode is not ("auto" or "exact")) Mode = "auto";
    }

    /// <summary>La taille de l'image rendue, à partir de celle de la première séquence (toujours paire).</summary>
    public (int L, int H) Taille(int sourceL, int sourceH)
    {
        (int l, int h) = Resolution switch
        {
            "1080" => (1920, 1080),
            "1440" => (2560, 1440),
            "2160" => (3840, 2160),
            _ => (sourceL > 0 ? sourceL : 1920, sourceH > 0 ? sourceH : 1080),
        };
        return (l / 2 * 2, h / 2 * 2);
    }
}
