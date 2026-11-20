using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 2.5.0 : les images vivantes des calques (caméras, fenêtres, vidéos, images, MyCam). Une capture par source : deux
/// calques de la même caméra, ou un calque de la caméra déjà montrée en grand, partagent la même (une caméra ne s'ouvre jamais deux
/// fois). Chaque capture écrit aussi son image pour la page OBS (« display-calque-N.jpg », nommée dans etat.json).
/// </summary>
public sealed class CalquesVivants : IDisposable
{
    sealed class Lecteur
    {
        public required SourcesDisplay Capture;
        public required string Fichier;
    }

    readonly Dictionary<SourceDisplay, Lecteur> lecteurs = new();
    readonly Dictionary<string, SourceDisplay> sourceDe = new();
    readonly SourcesDisplay principal;
    int numero;

    /// <summary>L'image d'un calque a changé (identifiant du calque, image).</summary>
    public event Action<string, ImageSource?>? Image;

    public CalquesVivants(SourcesDisplay principal)
    {
        this.principal = principal;
        principal.Change += () =>
        {
            foreach (var (id, s) in sourceDe.ToList())
                if (s == principal.Source) Image?.Invoke(id, principal.Image);
        };
    }

    /// <summary>Les sources qui donnent une image vivante (un texte se dessine sans capture).</summary>
    public static bool Vivante(SourceDisplay? s) => s?.Type is "image" or "camera" or "mycam" or "fenetre" or "video";

    /// <summary>
    /// Les calques à capter : ceux de toutes les sorties (église, MyStream, OBS). Les captures qui ne servent plus s'arrêtent,
    /// les nouvelles démarrent ; chaque calque reçoit tout de suite la dernière image de sa source.
    /// </summary>
    public void Choisir(IEnumerable<CalqueEcran> calques)
    {
        sourceDe.Clear();
        foreach (var c in calques)
            if (c.Media is { } s && Vivante(s)) sourceDe[c.Id] = s;
        var voulues = sourceDe.Values.Where(s => s != principal.Source).ToHashSet();
        foreach (var s in lecteurs.Keys.Where(k => !voulues.Contains(k)).ToList())
        {
            lecteurs[s].Capture.Dispose();
            lecteurs.Remove(s);
        }
        foreach (var s in voulues)
        {
            if (lecteurs.ContainsKey(s)) continue;
            var fichier = $"display-calque-{++numero}.jpg";
            var l = new Lecteur { Capture = new SourcesDisplay(fichier), Fichier = fichier };
            var source = s;
            l.Capture.Change += () =>
            {
                foreach (var (id, x) in sourceDe.ToList())
                    if (x == source) Image?.Invoke(id, l.Capture.Image);
            };
            lecteurs[s] = l;
            l.Capture.ChoisirSource(s);
        }
        foreach (var (id, _) in sourceDe.ToList()) Image?.Invoke(id, ImageDe(id));
    }

    /// <summary>La dernière image d'un calque (null : pas encore d'image, ou un texte).</summary>
    public ImageSource? ImageDe(string calque) => LecteurDe(calque)?.Image;

    /// <summary>La capture qui donne l'image d'un calque (celle de l'écran principal si c'est la même source).</summary>
    public SourcesDisplay? LecteurDe(string calque)
    {
        if (!sourceDe.TryGetValue(calque, out var s)) return null;
        if (s == principal.Source) return principal;
        return lecteurs.TryGetValue(s, out var l) ? l.Capture : null;
    }

    /// <summary>Le fichier de l'image du calque pour la page OBS (null : un texte, ou pas d'image).</summary>
    public string? FichierObs(string calque)
    {
        if (!sourceDe.TryGetValue(calque, out var s)) return null;
        if (s == principal.Source) return "display.jpg";
        return lecteurs.TryGetValue(s, out var l) ? l.Fichier : null;
    }

    /// <summary>« Reconnecter » : les captures des calques repartent (caméra débranchée puis rebranchée).</summary>
    public void Reconnecter()
    {
        foreach (var (s, l) in lecteurs) l.Capture.ReconnecterSource(s);
    }

    public void Dispose()
    {
        foreach (var l in lecteurs.Values) l.Capture.Dispose();
        lecteurs.Clear();
        sourceDe.Clear();
    }
}
