using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>
/// Siloé Stream 2.5.0 : un calque de l'écran, comme une source dans une scène d'OBS : une source des Médias (image, caméra, fenêtre,
/// vidéo, MyCam, texte) posée sur l'écran, à sa place et à sa taille (en parts de l'écran). Les calques vont du dessous vers le
/// dessus ; celui du dessus passe devant. Le contenu principal de la régie (versets, chants, média montré en grand) est l'un des
/// étages de la pile, à son rang (<see cref="EtatEcran.Rang"/>). Remplace le Duo (un seul contenu épinglé dans un coin).
/// </summary>
public sealed record CalqueEcran
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    /// <summary>La source des Médias (<see cref="Annonce.Id"/>) qu'il montre.</summary>
    [JsonPropertyName("annonce")] public string Annonce { get; init; } = "";
    [JsonPropertyName("libelle")] public string Libelle { get; init; } = "";
    /// <summary>Ce qu'il montre : un média (Mode « media ») ou un texte (Mode « chant »).</summary>
    [JsonPropertyName("etat")] public EtatEcran Etat { get; init; } = new();
    /// <summary>Coin haut gauche, largeur et hauteur, en parts de l'écran (0 à 1).</summary>
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("l")] public double L { get; init; } = 1;
    [JsonPropertyName("h")] public double H { get; init; } = 1;
    /// <summary>Montré à l'écran (la case d'OBS) ; caché, il reste dans la liste.</summary>
    [JsonPropertyName("visible")] public bool Visible { get; init; } = true;
    /// <summary>Pour la page OBS : le fichier de son image vivante (« display-calque-N.jpg »), donné par l'interface.</summary>
    [JsonPropertyName("image"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Image { get; init; }
    [JsonIgnore] public SourceDisplay? Media => Etat.Mode == "media" ? Etat.Media : null;
}

public sealed partial class Regie
{
    /// <summary>Les calques de l'écran, du dessous vers le dessus (vide : aucun).</summary>
    public IReadOnlyList<CalqueEcran> Calques => Etat.Calques ?? Array.Empty<CalqueEcran>();

    /// <summary>Plus petite part de l'écran pour un calque (5 %).</summary>
    public const double TailleMinCalque = 0.05;

    /// <summary>Ce que montre une source des Médias dans un calque : son média, ou la première diapositive de son texte.</summary>
    EtatEcran? ContenuCalque(Annonce a)
    {
        var b = new EtatEcran { Theme = Etat.Theme, Taille = Etat.Taille, Presentation = "plein" };
        if (a.Source is { } s) return b with { Mode = "media", Media = s, Ref = a.Titre };
        var blocs = BlocsTexte(a.Texte);
        if (blocs.Count == 0) return null;
        return b with { Mode = "chant", Lignes = blocs[0].Split('\n').Where(l => l.Trim().Length > 0).ToArray() };
    }

    /// <summary>
    /// « Superposer » : la source des Médias part à l'écran par-dessus ce qui y est déjà (un nouveau calque, tout en haut).
    /// Sur un écran vide, elle prend tout l'écran ; sinon, un coin (en haut à droite, puis les autres coins), à déplacer et agrandir.
    /// </summary>
    public CalqueEcran? Superposer(Annonce a)
    {
        if (ContenuCalque(a) is not { } contenu) { Dire($"« {a.Titre} » est vide."); return null; }
        var liste = Calques.ToList();
        bool vide = Etat.Vide && !liste.Any(c => c.Visible);
        double l = 0.35, h = 0.35, m = 0.03;
        var coins = new (double X, double Y)[] { (1 - l - m, m), (m, m), (1 - l - m, 1 - h - m), (m, 1 - h - m) };
        // un coin après l'autre (ceux qui prennent tout l'écran ne comptent pas)
        var (x, y) = coins[liste.Count(c => c.L < 0.999 || c.H < 0.999) % coins.Length];
        var c = new CalqueEcran
        {
            Id = Guid.NewGuid().ToString("N")[..10], Annonce = a.Id, Libelle = a.Titre.Trim().Length > 0 ? a.Titre.Trim() : "Source", Etat = contenu,
            X = vide ? 0 : x, Y = vide ? 0 : y, L = vide ? 1 : l, H = vide ? 1 : h,
        };
        liste.Add(c);
        Etat = Etat with { Calques = liste };
        Oeil?.Invoke(true);
        if (a.Source is { } s) AdopterCameraStream(a.Id, a.Titre, s);
        Signaler();
        return c;
    }

    /// <summary>Change un calque (sa place, sa taille, montré ou caché) ; faux s'il n'existe plus.</summary>
    bool ChangerCalque(string id, Func<CalqueEcran, CalqueEcran> changer)
    {
        var liste = Calques.ToList();
        int i = liste.FindIndex(c => c.Id == id);
        if (i < 0) return false;
        var n = changer(liste[i]);
        if (n == liste[i]) return true;
        bool montre = n.Visible != liste[i].Visible;
        liste[i] = n;
        Etat = Etat with { Calques = liste };
        // la source OBS suit : quelque chose à l'écran, ou plus rien
        if (montre) Oeil?.Invoke(!Etat.Vide || Etat.AvecCalques);
        Signaler();
        return true;
    }

    /// <summary>Place et taille d'un calque (parts de l'écran), gardées dans l'écran.</summary>
    public bool PlacerCalque(string id, double x, double y, double l, double h) => ChangerCalque(id, c =>
    {
        l = Math.Clamp(double.IsFinite(l) ? l : c.L, TailleMinCalque, 1);
        h = Math.Clamp(double.IsFinite(h) ? h : c.H, TailleMinCalque, 1);
        x = Math.Clamp(double.IsFinite(x) ? x : c.X, 0, 1 - l);
        y = Math.Clamp(double.IsFinite(y) ? y : c.Y, 0, 1 - h);
        return c with { X = Math.Round(x, 4), Y = Math.Round(y, 4), L = Math.Round(l, 4), H = Math.Round(h, 4) };
    });

    /// <summary>Le calque prend tout l'écran.</summary>
    public bool PleinEcranCalque(string id) => PlacerCalque(id, 0, 0, 1, 1);

    /// <summary>La case d'OBS : le calque est montré ou caché (il reste dans la liste).</summary>
    public bool MontrerCalque(string id, bool visible) => ChangerCalque(id, c => c with { Visible = visible });

    /// <summary>Retire un calque de l'écran.</summary>
    public bool RetirerCalque(string id)
    {
        var liste = Calques.ToList();
        int i = liste.FindIndex(c => c.Id == id);
        if (i < 0) return false;
        liste.RemoveAt(i);
        int rang = i < Etat.Rang ? Etat.Rang - 1 : Etat.Rang;
        Etat = Etat with { Calques = liste.Count > 0 ? liste : null, Rang = Math.Clamp(rang, 0, liste.Count) };
        if (Etat.Vide && !Etat.AvecCalques) Oeil?.Invoke(false);
        Signaler();
        return true;
    }

    /// <summary>Retire tous les calques (le contenu principal reste).</summary>
    public void RetirerCalques()
    {
        if (Calques.Count == 0) return;
        Etat = Etat with { Calques = null, Rang = 0 };
        if (Etat.Vide) Oeil?.Invoke(false);
        Signaler();
    }

    /// <summary>
    /// Les flèches d'OBS : le calque monte (sens 1, il passe devant celui du dessus) ou descend (sens -1). Le contenu principal compte
    /// comme un étage de la pile : un calque peut passer dessous (les paroles par-dessus une caméra) ou dessus.
    /// </summary>
    public bool MonterCalque(string id, int sens)
    {
        var liste = Calques.ToList();
        int i = liste.FindIndex(c => c.Id == id);
        if (i < 0 || sens == 0) return false;
        int rang = Etat.Rang;
        // la pile entière : les calques, avec le contenu principal entre eux (à son rang)
        int pos = i < rang ? i : i + 1;
        int cible = pos + Math.Sign(sens);
        if (cible < 0 || cible > liste.Count) return false;
        if (cible == rang)
        {
            // il passe de l'autre côté du contenu principal
            rang += sens > 0 ? -1 : 1;
        }
        else
        {
            int j = cible < rang ? cible : cible - 1;
            (liste[i], liste[j]) = (liste[j], liste[i]);
        }
        Etat = Etat with { Calques = liste, Rang = Math.Clamp(rang, 0, liste.Count) };
        Signaler();
        return true;
    }

    /// <summary>Le contenu principal (versets, chants, média en grand) monte ou descend dans la pile des calques.</summary>
    public bool MonterPrincipal(int sens)
    {
        int rang = Math.Clamp(Etat.Rang + Math.Sign(sens), 0, Calques.Count);
        if (rang == Etat.Rang) return false;
        Etat = Etat with { Rang = rang };
        Signaler();
        return true;
    }

    /// <summary>
    /// 2.5.1 : ▲ ▼ d'un média de la liste (à gauche), comme dans « Sources à l'écran » : il passe devant (sens 1) ou derrière (sens -1)
    /// les autres sources à l'écran. Superposé, c'est son calque qui bouge (le plus devant, s'il est superposé plusieurs fois) ;
    /// montré en grand, c'est le contenu principal. <paramref name="calque"/> : le calque qui a bougé (null : aucun).
    /// Null : le média n'est pas à l'écran ; faux : il est déjà tout devant (ou tout derrière) ; vrai : il a bougé.
    /// </summary>
    public bool? MonterMedia(string annonce, int sens, out string? calque)
    {
        calque = null;
        if (Calques.LastOrDefault(c => c.Annonce == annonce) is { } c)
        {
            calque = c.Id;
            return MonterCalque(c.Id, sens);
        }
        if (MediaPrincipal(annonce)) return MonterPrincipal(sens);
        return null;
    }

    /// <summary>2.5.1 : le média est le contenu principal de l'écran (montré en grand, ou une diapositive de son texte).</summary>
    public bool MediaPrincipal(string annonce) => Cible switch
    {
        CibleDisplay d => d.Id == annonce,
        CibleTexte t => t.Element == annonce,
        _ => false,
    };

    /// <summary>Une source des Médias a changé (texte, fenêtre, nom) : ses calques suivent ; supprimée, ils partent.</summary>
    void CalquesSuivent(Annonce? a, string id)
    {
        if (Calques.Count == 0) return;
        var liste = new List<CalqueEcran>();
        int rang = Etat.Rang;
        bool change = false;
        for (int i = 0; i < Calques.Count; i++)
        {
            var c = Calques[i];
            if (c.Annonce != id) { liste.Add(c); continue; }
            change = true;
            if (a == null || ContenuCalque(a) is not { } contenu) { if (i < Etat.Rang) rang--; continue; }
            liste.Add(c with { Etat = contenu, Libelle = a.Titre.Trim().Length > 0 ? a.Titre.Trim() : c.Libelle });
        }
        if (!change) return;
        Etat = Etat with { Calques = liste.Count > 0 ? liste : null, Rang = Math.Clamp(rang, 0, liste.Count) };
        Signaler();
    }

    /// <summary>
    /// L'écran sans les calques dont la source ne va pas là (<paramref name="exclure"/> : « Dans l'église seulement » pour Internet,
    /// « MyStream seulement » pour la salle) ; le rang du contenu principal suit.
    /// </summary>
    static EtatEcran CalquesPermis(EtatEcran e, Func<SourceDisplay, bool> exclure)
    {
        if (e.Calques is not { Count: > 0 } l) return e;
        var garde = new List<CalqueEcran>();
        int rang = e.Rang;
        for (int i = 0; i < l.Count; i++)
        {
            if (l[i].Media is { } m && exclure(m)) { if (i < e.Rang) rang--; continue; }
            garde.Add(l[i]);
        }
        return garde.Count == l.Count ? e : e with { Calques = garde.Count > 0 ? garde : null, Rang = Math.Clamp(rang, 0, garde.Count) };
    }
}
