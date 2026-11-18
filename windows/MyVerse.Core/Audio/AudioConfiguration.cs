using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVerse.Audio;

// ======================================================================================================================
// Siloé Stream 1.3 : le son, simple comme une voiture automatique.
// Ce que l'utilisateur voit et règle : des SONS (un micro, un instrument, la table de mixage, l'ordinateur, les vidéos,
// une musique) et les endroits OÙ VA LE SON (l'église, l'enregistrement, Zoom ou OBS, le casque).
// Tout le reste (pistes d'OBS, monitoring, filtres, mono) est décidé par Siloé Stream : voir AudioConfiguration.Planifier.
// ======================================================================================================================

/// <summary>Comment le moteur capte un son (caché à l'utilisateur). Les numéros sont ceux des réglages enregistrés depuis la 3.0.</summary>
public enum AudioInputKind { Microphone, Sortie, Media, Asio }
public enum AudioFilterKind { NoiseSuppression, NoiseGate, Compressor, Limiter }
/// <summary>Le monitoring d'OBS : Siloé Stream s'en sert pour faire entendre un son dans l'église (haut-parleurs de l'ordinateur).</summary>
public enum AudioMonitoring { Off, Only, AndOutput }

/// <summary>Ce qu'est un son. Il décide des réglages faits tout seuls : voix nettoyée et régulière, mono, son dans l'église.</summary>
public enum GenreSon { Voix, Instrument, Table, Ordinateur, Video, Fichier }

/// <summary>
/// Un son de l'église, tel que l'utilisateur le voit : un nom, ce que c'est, où il est branché, son volume, Couper,
/// « Dans l'église », et deux aides pour la voix (enlever le bruit, volume régulier).
/// </summary>
public sealed class Son
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("nom")] public string Nom { get; set; } = "Son";
    [JsonPropertyName("genre")] public GenreSon Genre { get; set; }
    /// <summary>Comment le moteur le capte : micro ou carte son (WASAPI), sortie de l'ordinateur, fichier, pilote ASIO.</summary>
    [JsonPropertyName("capture")] public AudioInputKind Capture { get; set; }
    /// <summary>L'appareil (identifiant Windows), le pilote ASIO, ou « default » (celui de Windows).</summary>
    [JsonPropertyName("appareil")] public string Appareil { get; set; } = "default";
    [JsonPropertyName("nomAppareil")] public string NomAppareil { get; set; } = "";
    /// <summary>Carte son à plusieurs entrées : 0 = toutes ensemble (stéréo), 1, 2, 3… = cette entrée seule (des deux côtés).</summary>
    [JsonPropertyName("entree")] public int Entree { get; set; }
    /// <summary>Pilote ASIO : l'entrée (0 = Input 1) ou les deux entrées (gauche, droite) ; −1 = muet.</summary>
    [JsonPropertyName("entreesAsio")] public List<int> EntreesAsio { get; set; } = new() { 0 };
    [JsonPropertyName("fichier")] public string Fichier { get; set; } = "";
    [JsonPropertyName("boucle")] public bool Boucle { get; set; }
    /// <summary>Volume en dB (0 = normal, « 100 % » à l'écran), de −60 (silence) à +6.</summary>
    [JsonPropertyName("volumeDb")] public double VolumeDb { get; set; }
    [JsonPropertyName("coupe")] public bool Coupe { get; set; }
    /// <summary>On l'entend dans l'église : il sort par les haut-parleurs de l'ordinateur (le monitoring d'OBS).</summary>
    [JsonPropertyName("eglise")] public bool Eglise { get; set; }
    /// <summary>Enlever le bruit de fond (RNNoise).</summary>
    [JsonPropertyName("bruit")] public bool EnleverBruit { get; set; }
    /// <summary>Volume régulier : les passages faibles montent, les cris ne saturent pas (compresseur et limiteur).</summary>
    [JsonPropertyName("regulier")] public bool VolumeRegulier { get; set; }

    /// <summary>Un nouveau son avec les réglages intelligents de son genre.</summary>
    public static Son Nouveau(GenreSon genre, string nom)
    {
        var s = new Son { Genre = genre, Nom = nom };
        AudioConfiguration.AppliquerDefauts(s);
        return s;
    }

    /// <summary>Une voix se nettoie et se régularise ; la musique, non (cela l'abîmerait).</summary>
    [JsonIgnore] public bool EstMicro => Genre is GenreSon.Voix or GenreSon.Instrument or GenreSon.Table;
}

/// <summary>Les haut-parleurs de l'église : la sortie de l'ordinateur où passent les sons « Dans l'église ».</summary>
public sealed class SortieEglise
{
    [JsonPropertyName("appareil")] public string Appareil { get; set; } = "default";
    [JsonPropertyName("nomAppareil")] public string NomAppareil { get; set; } = "";
}

/// <summary>Le casque de la régie : on y entend tout le son, comme il part en ligne et dans l'enregistrement.</summary>
public sealed class CasqueReglages
{
    [JsonPropertyName("actif")] public bool Actif { get; set; }
    [JsonPropertyName("appareil")] public string Appareil { get; set; } = "default";
    [JsonPropertyName("nomAppareil")] public string NomAppareil { get; set; } = "";
}

/// <summary>Un filtre d'OBS posé sur une source du moteur. Choisis par Siloé Stream d'après « Enlever le bruit » et « Volume régulier ».</summary>
public sealed class AudioFilter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AudioFilterKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    // compresseur
    public double Ratio { get; set; } = 4;
    public double ThresholdDb { get; set; } = -18;
    public double AttackMs { get; set; } = 6;
    public double ReleaseMs { get; set; } = 60;
    public double OutputGainDb { get; set; }
    // noise gate
    public double OpenDb { get; set; } = -26;
    public double CloseDb { get; set; } = -32;
    public double HoldMs { get; set; } = 200;
    // réduction du bruit : « rnnoise » ou « speex »
    public string Method { get; set; } = "rnnoise";
    public double SuppressDb { get; set; } = -30;
}

/// <summary>
/// Une source du moteur (une source d'OBS), préparée par <see cref="AudioConfiguration.Planifier"/> à partir d'un <see cref="Son"/>.
/// Jusqu'à la 1.2, c'était aussi ce que l'utilisateur réglait (« obsSources ») : ces réglages sont relus une fois pour être repris.
/// </summary>
public sealed class AudioSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Source";
    public AudioInputKind Kind { get; set; }
    public string DeviceId { get; set; } = "default";
    public string DeviceName { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public bool FollowProjection { get; set; }
    public bool Loop { get; set; }
    public int Channel { get; set; }
    public double VolumeDb { get; set; }
    public bool Muted { get; set; }
    public bool Mono { get; set; }
    /// <summary>Pistes d'OBS où va la source (bit 0 = piste 1).</summary>
    public int Tracks { get; set; } = AudioConfiguration.ToutesLesPistes;
    public AudioMonitoring Monitoring { get; set; } = AudioMonitoring.Off;
    public List<AudioFilter> Filters { get; set; } = new();
    public double Balance { get; set; } = 0.5;
    public int SyncOffsetMs { get; set; }
    public bool? UseDeviceTiming { get; set; }
    public List<int> AsioCanaux { get; set; } = new() { 0 };
    /// <summary>Un fichier de musique : il attend « Lire » (il ne part pas tout seul quand on l'ajoute ou au démarrage).</summary>
    [JsonIgnore] public bool DemarrerArrete { get; set; }

    [JsonIgnore] public bool Horodatage => UseDeviceTiming ?? Kind == AudioInputKind.Sortie;
    public bool DansPiste(int piste) => piste is >= 1 and <= AudioConfiguration.Pistes && (Tracks & (1 << (piste - 1))) != 0;
}

/// <summary>Ce qui reçoit du son : un enregistrement, un envoi (Zoom, OBS…) ou le casque. Il reçoit « tout le son » ou les sons choisis.</summary>
public interface IDestinationAudio
{
    string Id { get; }
    string Name { get; }
    /// <summary>Les sons reçus ; null = tout le son.</summary>
    List<string>? ChoixSons { get; set; }
    /// <summary>La piste d'OBS qui le nourrit (1 à 6), donnée par Siloé Stream à chaque <see cref="AudioConfiguration.Planifier"/>.</summary>
    int Track { get; set; }
}

public sealed class RecordingVisual
{
    public string Kind { get; set; } = "projection"; // projection, camera, image, video
    public string Path { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public bool Visible { get; set; } = true;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
    public bool Crop { get; set; }
    /// <summary>Kind « annonce » : l'identifiant de la source Display (bibliothèque).</summary>
    public string Annonce { get; set; } = "";
}

/// <summary>Un enregistrement : le son (MP3 léger ou WAV studio) ou une vidéo (ce qui est à l'écran, des caméras) avec le son.</summary>
public sealed class RecordingProfile : IDestinationAudio
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Enregistrement";
    public string Color { get; set; } = "#B3261E";
    public string Directory { get; set; } = "";
    public bool AudioOnly { get; set; } = true;
    /// <summary>Son seul : « mp3 » (léger, à partager), « wav » (studio, sans perte) ; « flac » et « m4a » sont encore lus. Vidéo : « mkv ».</summary>
    public string Format { get; set; } = "mp3";
    [JsonPropertyName("sons")] public List<string>? ChoixSons { get; set; }
    [JsonIgnore] public int Track { get; set; } = 1;
    /// <summary>Jusqu'à la 1.2 : la piste d'OBS choisie à la main. Reprise une fois en sons choisis, puis oubliée.</summary>
    [JsonPropertyName("track")] public int? PisteV6 { get; set; }
    /// <summary>Avant la 4.2 : les sources choisies. Reprises une fois, puis oubliées.</summary>
    public List<string>? SourceIds { get; set; }
    /// <summary>Sources Display filmées, dans l'ordre (la 1re en grand) : identifiants de la bibliothèque Display, ou <see cref="Projection"/>.</summary>
    public List<string> DisplayIds { get; set; } = new();
    /// <summary>Ce qui est à l'écran de projection (Bible, chants, Display, Duo).</summary>
    public const string Projection = "projection";
    /// <summary>1.6.0 : le format d'un direct (YouTube, un autre service) : FLV vers une adresse RTMP(S).</summary>
    public const string FormatDirect = "flv";
    /// <summary>Plusieurs sources Display : « incrustation » (la 1re en grand, les autres en petit) ou « cote » (côte à côte).</summary>
    public string Layout { get; set; } = "incrustation";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 30;
    public int VideoKbps { get; set; } = 5000;
    public int AudioKbps { get; set; } = 192;
    /// <summary>« auto » : encodeur de la carte graphique (NVIDIA, Intel, AMD) s'il marche, sinon x264, sinon MJPEG.</summary>
    public string Encoder { get; set; } = "auto";
    public List<RecordingVisual> Visuals { get; set; } = new() { new() };

    /// <summary>Les cadres (proportions de l'image) de n sources Display : incrustation (la 1re en plein écran, les autres en petit en bas à droite) ou côte à côte.</summary>
    public static List<(double X, double Y, double W, double H)> Cadres(int n, string layout)
    {
        var cadres = new List<(double, double, double, double)>();
        if (n <= 0) return cadres;
        if (layout == "cote")
        {
            int colonnes = n <= 3 ? n : (int)Math.Ceiling(Math.Sqrt(n));
            int lignes = (int)Math.Ceiling(n / (double)colonnes);
            double t = 1.0 / colonnes, y0 = (1 - lignes * t) / 2;
            for (int i = 0; i < n; i++)
                cadres.Add((i % colonnes * t, y0 + i / colonnes * t, t, t));
            return cadres;
        }
        const double petit = 0.3, marge = 0.025;
        cadres.Add((0, 0, 1, 1));
        for (int k = 0; k < n - 1; k++)
        {
            int colonne = k % 3, rang = k / 3;
            double x = 1 - marge - (colonne + 1) * petit - colonne * marge, y = 1 - marge - (rang + 1) * petit - rang * marge;
            cadres.Add((Math.Max(0, x), Math.Max(0, y), petit, petit));
        }
        return cadres;
    }

    /// <summary>Les contenus visuels de la vidéo, d'après les sources Display choisies et la disposition.</summary>
    public List<RecordingVisual> VisuelsDisplay()
    {
        var cadres = Cadres(DisplayIds.Count, Layout);
        return DisplayIds.Select((id, i) => new RecordingVisual
        {
            Kind = id == Projection ? "projection" : "annonce", Annonce = id == Projection ? "" : id,
            X = cadres[i].X, Y = cadres[i].Y, Width = cadres[i].W, Height = cadres[i].H,
        }).ToList();
    }
}

/// <summary>
/// Un envoi du son vers un autre logiciel (Zoom, Teams, OBS, Facebook dans le navigateur) par un câble audio virtuel
/// (VB-CABLE : ils choisissent le micro « CABLE Output »), ou vers une sortie de l'ordinateur (une autre table de mixage, une salle).
/// JSON « sortiesVirtuelles » : les sorties MyVirtualSound des versions précédentes.
/// </summary>
public sealed class VirtualSoundSettings : IDestinationAudio
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Zoom";
    public bool Enabled { get; set; }
    /// <summary>Vide = le câble audio virtuel installé (automatique) ; « default » = sortie Windows par défaut ; sinon une sortie choisie.</summary>
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    [JsonPropertyName("sons")] public List<string>? ChoixSons { get; set; }
    [JsonIgnore] public int Track { get; set; } = 1;
    /// <summary>Jusqu'à la 1.2 : piste choisie à la main, volume et sourdine propres à la sortie. Repris une fois, puis oubliés.</summary>
    [JsonPropertyName("track")] public int? PisteV6 { get; set; }
    [JsonPropertyName("volumeDb")] public double? AncienVolumeDb { get; set; }
    [JsonPropertyName("muted")] public bool? AncienneSourdine { get; set; }
    /// <summary>Avant la 4.2 : les sources envoyées. Reprises une fois, puis oubliées.</summary>
    public List<string>? SourceIds { get; set; }

    /// <summary>L'envoi passe par le câble audio virtuel (le cas de Zoom, OBS, Teams).</summary>
    [JsonIgnore] public bool ParCable => string.IsNullOrEmpty(DeviceId);
}

/// <summary>1.6.0 : le son du direct (YouTube, un autre service) : tout le son, ou les sons choisis.</summary>
public sealed class SonDirect : IDestinationAudio
{
    public string Id { get; set; } = AudioConfiguration.IdDirect;
    public string Name { get; set; } = "YouTube";
    [JsonPropertyName("sons")] public List<string>? ChoixSons { get; set; }
    [JsonIgnore] public int Track { get; set; } = 1;
}

/// <summary>Ce que Siloé Stream demande au moteur, recalculé à chaque changement : les sources d'OBS et la piste de chaque destination.</summary>
public sealed class PlanAudio
{
    public List<AudioSource> Moteur { get; } = new();
    /// <summary>Destination (enregistrement, envoi, <see cref="AudioConfiguration.IdCasque"/>) → piste d'OBS (1 à 6).</summary>
    public Dictionary<string, int> Pistes { get; } = new();
    /// <summary>Les sons de chaque piste utilisée (piste → identifiants des sons).</summary>
    public Dictionary<int, List<string>> SonsDesPistes { get; } = new();
    /// <summary>Ce qu'il faut dire à l'utilisateur (trop de mélanges différents…).</summary>
    public List<string> Alertes { get; } = new();
    public int PisteDe(string id) => Pistes.TryGetValue(id, out int p) ? p : 1;
}

public sealed class AudioConfiguration
{
    /// <summary>7 : Siloé Stream 1.3, le son simple (sons, « où va le son », pistes attribuées par Siloé Stream).
    /// 6 : 1.0 à 1.2 (comme OBS : chaque source coche ses pistes). 5 : réglages d'OBS 32. 4 : enregistrements choisis par l'utilisateur.</summary>
    public const int Actuelle = 7;
    public const int Pistes = 6;
    public const int ToutesLesPistes = (1 << Pistes) - 1;
    public const int MaxEnregistrements = 5;
    public const int MaxSortiesVirtuelles = 4;
    public const int MaxSources = 32;
    public const int MaxEntrees = 32;
    /// <summary>La destination « casque de la régie » dans <see cref="PlanAudio.Pistes"/>.</summary>
    public const string IdCasque = "casque";
    /// <summary>La destination « direct » (YouTube) dans <see cref="PlanAudio.Pistes"/>.</summary>
    public const string IdDirect = "direct";

    public int Version { get; set; } = Actuelle;
    [JsonPropertyName("sons")] public List<Son> Sons { get; set; } = new();
    [JsonPropertyName("eglise")] public SortieEglise Eglise { get; set; } = new();
    [JsonPropertyName("casque")] public CasqueReglages Casque { get; set; } = new();
    [JsonPropertyName("enregistrements")] public List<RecordingProfile> Profiles { get; set; } = new();
    /// <summary>Les envois (Zoom, OBS…) : 4 au plus, actifs en même temps si besoin.</summary>
    [JsonPropertyName("sortiesVirtuelles")] public List<VirtualSoundSettings> VirtualSounds { get; set; } = new();
    /// <summary>1.6.0 : le son du direct (YouTube).</summary>
    [JsonPropertyName("direct")] public SonDirect Direct { get; set; } = new();

    // ------------------------------------------------------------ réglages des versions précédentes (relus une fois, puis oubliés)
    /// <summary>1.0 à 1.2 (et 3.x, 4.x) : les sources du mélangeur « comme OBS ». Reprises en <see cref="Sons"/>.</summary>
    [JsonPropertyName("obsSources")] public List<AudioSource>? AnciennesSources { get; set; }
    /// <summary>1.0 à 1.2 : le périphérique de monitoring d'OBS ; il devient les haut-parleurs de l'église.</summary>
    [JsonPropertyName("monitorDeviceId")] public string? AncienMonitoring { get; set; }
    [JsonPropertyName("monitorDeviceName")] public string? AncienMonitoringNom { get; set; }
    /// <summary>3.0.0 : une seule sortie MyVirtualSound.</summary>
    [JsonPropertyName("virtualSound")] public VirtualSoundSettings? AncienneSortie { get; set; }

    // ------------------------------------------------------------ mots simples
    /// <summary>Le nom d'un genre, tel qu'on le dit à l'église.</summary>
    public static string NomGenre(GenreSon g) => g switch
    {
        GenreSon.Voix => "Micro",
        GenreSon.Instrument => "Instrument",
        GenreSon.Table => "Table de mixage",
        GenreSon.Ordinateur => "Son de l'ordinateur",
        GenreSon.Video => "Vidéos de Display",
        _ => "Musique",
    };

    /// <summary>Une phrase pour expliquer un genre (assistant « Ajouter un son »).</summary>
    public static string AideGenre(GenreSon g) => g switch
    {
        GenreSon.Voix => "Pour parler ou chanter : pasteur, chorale, micro sans fil.",
        GenreSon.Instrument => "Piano, clavier, guitare, basse, batterie…",
        GenreSon.Table => "Tout le son de l'église, déjà mélangé par la table de mixage.",
        GenreSon.Ordinateur => "YouTube, une musique, tout ce que l'ordinateur joue.",
        GenreSon.Video => "Le son des vidéos que vous projetez avec Display.",
        _ => "Une musique d'attente, un chant en MP3 : un fichier à lire.",
    };

    /// <summary>« Micro · UMC404HD · entrée 1 » : ce qu'est un son et où il est branché.</summary>
    public static string Description(Son s)
    {
        string genre = NomGenre(s.Genre);
        switch (s.Genre)
        {
            case GenreSon.Video: return "Le son des vidéos projetées";
            case GenreSon.Fichier: return s.Fichier.Length > 0 ? Path.GetFileName(s.Fichier) + (s.Boucle ? " · en boucle" : "") : "aucun fichier choisi";
            case GenreSon.Ordinateur: return s.Appareil is "" or "default" ? "Tout ce que joue l'ordinateur" : "Ce qui sort de « " + NomCourt(s.NomAppareil) + " »";
        }
        if (s.Capture == AudioInputKind.Asio)
            return genre + " · " + (s.Appareil is "" or "default" ? "aucun pilote" : s.Appareil) + " · " + string.Join(" + ", s.EntreesAsio.Select(k => k < 0 ? "muet" : $"entrée {k + 1}"));
        string appareil = s.Appareil == "default" || s.NomAppareil.Length == 0 ? "micro de Windows" : NomCourt(s.NomAppareil);
        return genre + " · " + appareil + (s.Entree > 0 ? $" · entrée {s.Entree}" : "");
    }

    /// <summary>Les réglages intelligents d'un genre (comment le capter, nettoyer la voix, l'entendre dans l'église).</summary>
    public static void AppliquerDefauts(Son s)
    {
        switch (s.Genre)
        {
            case GenreSon.Voix:
                if (s.Capture is not (AudioInputKind.Microphone or AudioInputKind.Asio)) s.Capture = AudioInputKind.Microphone;
                s.EnleverBruit = true; s.VolumeRegulier = true; s.Eglise = false;
                break;
            case GenreSon.Instrument:
            case GenreSon.Table:
                if (s.Capture is not (AudioInputKind.Microphone or AudioInputKind.Asio)) s.Capture = AudioInputKind.Microphone;
                s.EnleverBruit = false; s.VolumeRegulier = false; s.Eglise = false;
                break;
            case GenreSon.Ordinateur:
                s.Capture = AudioInputKind.Sortie; s.EnleverBruit = false; s.VolumeRegulier = false; s.Eglise = false;
                break;
            default:
                s.Capture = AudioInputKind.Media; s.EnleverBruit = false; s.VolumeRegulier = false; s.Eglise = true;
                break;
        }
    }

    /// <summary>
    /// Le son des vidéos de Display, s'il a été ajouté (2.4.0 : il n'est plus ajouté tout seul ; on le choisit dans « Ajouter un son »,
    /// et il se retire comme les autres).
    /// </summary>
    [JsonIgnore] public Son? SonVideos => Sons.FirstOrDefault(s => s.Genre == GenreSon.Video);

    /// <summary>« Microphone (Focusrite USB) » → « Focusrite USB » : un nom court pour un appareil.</summary>
    public static string NomCourt(string nom)
    {
        nom ??= "";
        int a = nom.IndexOf('('), b = nom.LastIndexOf(')');
        return a >= 0 && b > a + 1 ? nom[(a + 1)..b].Trim() : nom.Trim();
    }

    /// <summary>Un nom pas encore pris : « Micro », « Micro 2 »…</summary>
    public string NomLibre(string nom)
    {
        if (string.IsNullOrWhiteSpace(nom)) nom = "Son";
        nom = nom.Trim();
        if (Sons.All(s => !string.Equals(s.Nom, nom, StringComparison.OrdinalIgnoreCase))) return nom;
        for (int i = 2; ; i++)
            if (Sons.All(s => !string.Equals(s.Nom, $"{nom} {i}", StringComparison.OrdinalIgnoreCase))) return $"{nom} {i}";
    }

    /// <summary>Ajoute un son à la liste (avant le son des vidéos, qui reste le dernier).</summary>
    public void Ajouter(Son s)
    {
        int i = Sons.FindIndex(x => x.Genre == GenreSon.Video);
        if (i < 0 || s.Genre == GenreSon.Video) Sons.Add(s);
        else Sons.Insert(i, s);
    }

    /// <summary>Retire un son : il n'est plus reçu nulle part (2.4.0 : le son des vidéos aussi).</summary>
    public bool Retirer(Son s)
    {
        if (!Sons.Remove(s)) return false;
        foreach (var d in Destinations()) d.ChoixSons?.Remove(s.Id);
        return true;
    }

    /// <summary>Les enregistrements et les envois.</summary>
    public IEnumerable<IDestinationAudio> Destinations() => Profiles.Cast<IDestinationAudio>().Concat(VirtualSounds).Append(Direct);

    /// <summary>Ce que reçoit une destination, en mots : « Tout le son », « Pasteur et Chorale », « Aucun son ».</summary>
    public string Recoit(IDestinationAudio d)
    {
        if (d.ChoixSons == null) return "Tout le son";
        var noms = Sons.Where(s => d.ChoixSons.Contains(s.Id)).Select(s => s.Nom).ToList();
        return noms.Count switch
        {
            0 => "Aucun son",
            1 => noms[0] + " seulement",
            _ => string.Join(", ", noms.Take(noms.Count - 1)) + " et " + noms[^1],
        };
    }

    /// <summary>2.1.0 : le dossier des enregistrements choisi dans Réglages › Son (vide : Vidéos\Siloé Stream).</summary>
    [JsonPropertyName("dossierEnregistrements")] public string DossierChoisi { get; set; } = "";

    /// <summary>2.1.0 : le dossier choisi, repris de <see cref="DossierChoisi"/> au démarrage et à chaque choix.</summary>
    public static string DossierParDefautChoisi { get; set; } = "";

    /// <summary>Dossier des enregistrements par défaut : celui choisi dans Réglages › Son (2.1.0), sinon Vidéos\Siloé Stream.</summary>
    public static string DossierEnregistrements
    {
        get
        {
            if (DossierParDefautChoisi.Length > 0) return DossierParDefautChoisi;
            string v = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(v)) v = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            return Path.Combine(v, "Siloé Stream");
        }
    }

    public static T Copy<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Donnees.Json), Donnees.Json)!;
    public static double Bound(double x, double min, double max, double fallback = 0) => double.IsFinite(x) ? Math.Clamp(x, min, max) : fallback;

    // ------------------------------------------------------------ le plan du moteur
    /// <summary>Même appareil Windows (« default » ne vaut que « default »).</summary>
    static bool MemeAppareil(string a, string b) => string.Equals(string.IsNullOrEmpty(a) ? "default" : a, string.IsNullOrEmpty(b) ? "default" : b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ce que Siloé Stream demande à OBS. Personne ne choisit de piste : la piste 1 reçoit tout le son, et chaque destination
    /// qui veut d'autres sons reçoit sa piste (2 à 6), partagée avec celles qui veulent les mêmes. Les pistes ne bougent pas
    /// quand un enregistrement démarre ou s'arrête (OBS garde jusqu'à une seconde de son en réserve : une piste réattribuée
    /// ferait entendre l'ancien mélange au début du fichier) ; seulement s'il y a plus de mélanges que de pistes, les
    /// destinations actives passent en premier. Un envoi vers une sortie de l'ordinateur ne reçoit jamais le « son de
    /// l'ordinateur » de cette même sortie (sinon le son tournerait en rond et sifflerait). Le casque entend tout le son.
    /// </summary>
    /// <param name="videoProjetee">La vidéo à l'écran (Display) : le son des vidéos la joue.</param>
    /// <param name="actives">Les destinations qui tournent (enregistrements en cours) : prioritaires s'il manque des pistes.</param>
    public PlanAudio Planifier(string videoProjetee = "", ISet<string>? actives = null)
    {
        var plan = new PlanAudio();
        var tous = Sons.Select(s => s.Id).ToList();
        var destinations = new List<(string Id, string Nom, List<string>? Choix, string? Appareil, bool Actif)>
        {
            (IdCasque, "Casque", null, Casque.Appareil, Casque.Actif),
        };
        foreach (var v in VirtualSounds) destinations.Add((v.Id, v.Name, v.ChoixSons, v.ParCable ? null : v.DeviceId, v.Enabled));
        foreach (var p in Profiles) destinations.Add((p.Id, p.Name, p.ChoixSons, null, actives?.Contains(p.Id) == true));
        destinations.Add((Direct.Id, Direct.Name, Direct.ChoixSons, null, actives?.Contains(Direct.Id) == true));
        // les sons de chaque destination, et son mélange (null = tout le son)
        var voulus = destinations.Select(d =>
        {
            var sons = (d.Choix ?? tous).Where(tous.Contains).ToHashSet();
            // jamais le son de l'ordinateur d'une sortie renvoyé vers cette même sortie (larsen)
            if (d.Appareil != null)
                sons.RemoveWhere(id => Sons.First(s => s.Id == id) is { Genre: GenreSon.Ordinateur } o && MemeAppareil(o.Appareil, d.Appareil));
            return (D: d, Sons: sons, Cle: sons.SetEquals(tous) ? null : string.Join("|", sons.OrderBy(x => x, StringComparer.Ordinal)));
        }).ToList();
        bool manque = voulus.Where(v => v.Cle != null).Select(v => v.Cle).Distinct().Count() > Pistes - 1;
        var cles = new Dictionary<string, int>();
        int suivante = 2;
        foreach (var (d, sons, cle) in manque ? voulus.OrderBy(v => v.D.Actif ? 0 : 1).ToList() : voulus)
        {
            int piste = 1;
            if (cle != null)
            {
                if (cles.TryGetValue(cle, out int p)) piste = p;
                else if (suivante <= Pistes) { piste = suivante++; cles[cle] = piste; plan.SonsDesPistes[piste] = sons.ToList(); }
                else plan.Alertes.Add($"Trop de mélanges différents : « {d.Nom} » reçoit tout le son.");
            }
            plan.Pistes[d.Id] = piste;
        }
        plan.SonsDesPistes[1] = tous.ToList();
        foreach (var s in Sons)
        {
            int masque = 1;
            foreach (var (piste, ids) in plan.SonsDesPistes) if (piste > 1 && ids.Contains(s.Id)) masque |= 1 << (piste - 1);
            plan.Moteur.Add(SourceMoteur(s, masque, videoProjetee));
        }
        foreach (var d in Destinations()) d.Track = plan.PisteDe(d.Id);
        return plan;
    }

    /// <summary>La source d'OBS d'un son : comment le capter, ses pistes, l'église (monitoring) et ses aides (filtres d'OBS).</summary>
    static AudioSource SourceMoteur(Son s, int pistes, string videoProjetee)
    {
        var a = new AudioSource
        {
            Id = s.Id, Name = s.Nom, Kind = s.Capture, DeviceId = string.IsNullOrEmpty(s.Appareil) ? "default" : s.Appareil, DeviceName = s.NomAppareil,
            Channel = s.Capture == AudioInputKind.Microphone ? s.Entree : 0, AsioCanaux = s.EntreesAsio.ToList(),
            MediaPath = s.Genre == GenreSon.Video ? videoProjetee ?? "" : s.Fichier, FollowProjection = s.Genre == GenreSon.Video,
            Loop = s.Genre == GenreSon.Fichier && s.Boucle, DemarrerArrete = s.Genre == GenreSon.Fichier,
            VolumeDb = s.VolumeDb, Muted = s.Coupe,
            // une voix est mono : un micro branché sur la seule entrée gauche s'entend des deux côtés
            Mono = s.Genre == GenreSon.Voix,
            Tracks = pistes, Monitoring = s.Eglise ? AudioMonitoring.AndOutput : AudioMonitoring.Off,
        };
        if (s.EnleverBruit)
            a.Filters.Add(new AudioFilter { Id = s.Id + "-bruit", Kind = AudioFilterKind.NoiseSuppression, Method = "rnnoise" });
        if (s.VolumeRegulier)
        {
            // les passages faibles remontent (seuil bas, gain de 6 dB) et les pointes sont rattrapées par le limiteur
            a.Filters.Add(new AudioFilter { Id = s.Id + "-regulier", Kind = AudioFilterKind.Compressor, Ratio = 3, ThresholdDb = -24, AttackMs = 5, ReleaseMs = 100, OutputGainDb = 6 });
            a.Filters.Add(new AudioFilter { Id = s.Id + "-limite", Kind = AudioFilterKind.Limiter, ThresholdDb = -2, ReleaseMs = 60 });
        }
        return a;
    }

    // ------------------------------------------------------------ bornes et reprise des anciens réglages
    /// <summary>Remet les valeurs dans leurs bornes, reprend les réglages des versions précédentes, et garde le son des vidéos.</summary>
    public void Normalize()
    {
        Sons ??= new(); Profiles ??= new(); VirtualSounds ??= new();
        Eglise ??= new(); Casque ??= new();
        Direct ??= new();
        Direct.Id = IdDirect;
        if (string.IsNullOrWhiteSpace(Direct.Name)) Direct.Name = "YouTube";
        Sons.RemoveAll(s => s == null);
        Profiles.RemoveAll(p => p == null);
        VirtualSounds.RemoveAll(v => v == null);
        Reprendre();
        var ids = new HashSet<string>();
        Sons.RemoveAll(s => string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id));
        // un seul son des vidéos (le premier)
        var premiereVideo = Sons.FirstOrDefault(s => s.Genre == GenreSon.Video);
        Sons.RemoveAll(s => s.Genre == GenreSon.Video && s != premiereVideo);
        if (Sons.Count > MaxSources) Sons.RemoveRange(MaxSources, Sons.Count - MaxSources);
        // 2.4.0 : le son des vidéos de Display n'est plus ajouté tout seul ; s'il a été choisi, il reste le dernier
        int iv = Sons.FindIndex(s => s.Genre == GenreSon.Video);
        if (iv >= 0 && iv != Sons.Count - 1) { var v = Sons[iv]; Sons.RemoveAt(iv); Sons.Add(v); }
        foreach (var s in Sons)
        {
            s.Nom = string.IsNullOrWhiteSpace(s.Nom) ? NomGenre(s.Genre) : s.Nom.Trim();
            if (!Enum.IsDefined(s.Genre)) s.Genre = GenreSon.Voix;
            if (!Enum.IsDefined(s.Capture)) s.Capture = AudioInputKind.Microphone;
            // la capture suit le genre
            if (s.Genre == GenreSon.Ordinateur) s.Capture = AudioInputKind.Sortie;
            else if (s.Genre is GenreSon.Video or GenreSon.Fichier) s.Capture = AudioInputKind.Media;
            else if (s.Capture is not (AudioInputKind.Microphone or AudioInputKind.Asio)) s.Capture = AudioInputKind.Microphone;
            if (string.IsNullOrEmpty(s.Appareil)) s.Appareil = "default";
            s.NomAppareil ??= ""; s.Fichier ??= "";
            s.EntreesAsio ??= new();
            s.Entree = s.Capture == AudioInputKind.Microphone ? Math.Clamp(s.Entree, 0, MaxEntrees) : 0;
            if (s.Capture == AudioInputKind.Asio)
            {
                if (s.EntreesAsio.Count == 0) s.EntreesAsio.Add(0);
                if (s.EntreesAsio.Count > 2) s.EntreesAsio.RemoveRange(2, s.EntreesAsio.Count - 2);
                for (int k = 0; k < s.EntreesAsio.Count; k++) s.EntreesAsio[k] = Math.Clamp(s.EntreesAsio[k], -1, MaxEntrees - 1);
            }
            if (s.Genre != GenreSon.Fichier) s.Boucle = false;
            if (s.Genre == GenreSon.Video) { s.Fichier = ""; s.Appareil = "default"; s.NomAppareil = ""; }
            // seule une voix se « nettoie » : la réduction du bruit abîmerait la musique (instrument, table de mixage, fichiers)
            if (s.Genre != GenreSon.Voix) s.EnleverBruit = false;
            s.VolumeDb = Bound(s.VolumeDb, -60, 6);
        }
        var known = Sons.Select(s => s.Id).ToHashSet();
        var vids = new HashSet<string>();
        VirtualSounds.RemoveAll(v => string.IsNullOrEmpty(v.Id) || !vids.Add(v.Id));
        if (VirtualSounds.Count > MaxSortiesVirtuelles) VirtualSounds.RemoveRange(MaxSortiesVirtuelles, VirtualSounds.Count - MaxSortiesVirtuelles);
        for (int i = 0; i < VirtualSounds.Count; i++)
        {
            var v = VirtualSounds[i];
            v.Name = string.IsNullOrWhiteSpace(v.Name) ? (i == 0 ? "Zoom" : "Envoi " + (i + 1)) : v.Name.Trim();
            v.DeviceId ??= ""; v.DeviceName ??= "";
            v.ChoixSons = v.ChoixSons?.Where(known.Contains).Distinct().ToList();
            v.SourceIds = null; v.PisteV6 = null; v.AncienVolumeDb = null; v.AncienneSourdine = null;
        }
        Direct.ChoixSons = Direct.ChoixSons?.Where(known.Contains).Distinct().ToList();
        var pids = new HashSet<string>();
        Profiles.RemoveAll(p => string.IsNullOrEmpty(p.Id) || !pids.Add(p.Id));
        if (Profiles.Count > MaxEnregistrements) Profiles.RemoveRange(MaxEnregistrements, Profiles.Count - MaxEnregistrements);
        for (int i = 0; i < Profiles.Count; i++)
        {
            var p = Profiles[i];
            p.SourceIds = null; p.PisteV6 = null;
            p.ChoixSons = p.ChoixSons?.Where(known.Contains).Distinct().ToList();
            p.Name = string.IsNullOrWhiteSpace(p.Name) ? "Enregistrement " + (i + 1) : p.Name.Trim();
            p.Color ??= "#B3261E"; p.Visuals ??= new(); p.Encoder ??= "auto"; p.Layout = p.Layout == "cote" ? "cote" : "incrustation";
            p.DisplayIds = (p.DisplayIds ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(6).ToList();
            if (p.DisplayIds.Count > 0) { p.AudioOnly = false; p.Format = "mkv"; }
            else
            {
                p.AudioOnly = true;
                // FLAC (sans perte) devient WAV (sans perte, lisible partout) ; le reste en MP3
                p.Format = p.Format switch { "wav" or "flac" => "wav", "m4a" => "m4a", _ => "mp3" };
            }
            if (string.IsNullOrWhiteSpace(p.Directory)) p.Directory = Path.Combine(DossierEnregistrements, RecordingFiles.SafeName(p.Name));
        }
        if (string.IsNullOrEmpty(Eglise.Appareil)) Eglise.Appareil = "default";
        Eglise.NomAppareil ??= "";
        if (string.IsNullOrEmpty(Casque.Appareil)) Casque.Appareil = "default";
        Casque.NomAppareil ??= "";
        Version = Actuelle;
    }

    /// <summary>
    /// Les réglages des versions précédentes : la sortie unique de la 3.0.0, les sources choisies par destination (avant la 4.2),
    /// puis les pistes cochées « comme OBS » (1.0 à 1.2). Tout devient des sons et des destinations qui reçoivent « tout le son »
    /// ou les sons qu'elles recevaient. Le monitoring d'OBS devient « Dans l'église », et son périphérique les haut-parleurs de l'église.
    /// </summary>
    void Reprendre()
    {
        if (AnciennesSources == null && AncienneSortie == null && AncienMonitoring == null) return;
        var anciennes = (AnciennesSources ?? new()).Where(s => s != null).ToList();
        var ids = new HashSet<string>();
        anciennes.RemoveAll(s => string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id));
        if (AncienneSortie is { } ancienne)
        {
            // 3.0.0 : une seule sortie, qui recevait une piste choisie à la main
            if (Version < 4 && (ancienne.Enabled || !string.IsNullOrEmpty(ancienne.DeviceId)))
            {
                int piste = Math.Clamp(ancienne.PisteV6 ?? 1, 1, Pistes);
                ancienne.SourceIds = anciennes.Where(x => (x.Tracks & (1 << (piste - 1))) != 0).Select(x => x.Id).ToList();
                ancienne.Name = "Zoom";
                ancienne.Id = Guid.NewGuid().ToString("N");
                VirtualSounds.Insert(0, ancienne);
            }
            AncienneSortie = null;
        }
        // avant la 4.2 : chaque destination choisissait ses sources ; on en fait des pistes, comme le faisait la 4.2
        if (Version < 6) MigrerPistes(anciennes);
        // 1.0 à 1.2 : les sources deviennent des sons
        foreach (var a in anciennes)
        {
            if (Sons.Any(s => s.Id == a.Id)) continue;
            var genre = a.Kind switch
            {
                AudioInputKind.Media when a.FollowProjection => GenreSon.Video,
                AudioInputKind.Media => GenreSon.Fichier,
                AudioInputKind.Sortie => GenreSon.Ordinateur,
                _ => DevinerGenre(a.Name),
            };
            var f = (a.Filters ?? new()).Where(x => x != null && x.Enabled).ToList();
            Sons.Add(new Son
            {
                Id = a.Id, Nom = a.Name ?? "", Genre = genre, Capture = a.Kind, Appareil = a.DeviceId ?? "default", NomAppareil = a.DeviceName ?? "",
                Entree = a.Channel, EntreesAsio = a.AsioCanaux?.ToList() ?? new() { 0 }, Fichier = a.FollowProjection ? "" : a.MediaPath ?? "", Boucle = a.Loop,
                // le son des vidéos s'entend maintenant dans l'église (avant la 1.3, la vidéo de Display était muette dans la salle)
                VolumeDb = a.VolumeDb, Coupe = a.Muted, Eglise = genre == GenreSon.Video || a.Monitoring != AudioMonitoring.Off,
                // ses filtres d'OBS deviennent les deux aides : enlever le bruit, volume régulier
                EnleverBruit = f.Any(x => x.Kind == AudioFilterKind.NoiseSuppression),
                VolumeRegulier = f.Any(x => x.Kind is AudioFilterKind.Compressor or AudioFilterKind.Limiter),
            });
        }
        // chaque destination reçoit les sources de sa piste (toutes = « tout le son »)
        var tous = anciennes.Select(s => s.Id).ToHashSet();
        foreach (var d in Profiles.Cast<IDestinationAudio>().Concat(VirtualSounds))
        {
            int? piste = d switch { RecordingProfile p => p.PisteV6, VirtualSoundSettings v => v.PisteV6, _ => null };
            if (piste is not { } t || t < 1 || t > Pistes) continue;
            var recus = anciennes.Where(s => (s.Tracks & (1 << (t - 1))) != 0 && s.Monitoring != AudioMonitoring.Only).Select(s => s.Id).ToList();
            d.ChoixSons = recus.ToHashSet().SetEquals(tous) ? null : recus;
        }
        foreach (var v in VirtualSounds)
        {
            // le volume et la sourdine propres à une sortie ont disparu : une sortie coupée est simplement désactivée
            if (v.AncienneSourdine == true) v.Enabled = false;
        }
        if (!string.IsNullOrEmpty(AncienMonitoring))
        {
            Eglise.Appareil = AncienMonitoring;
            Eglise.NomAppareil = AncienMonitoringNom ?? "";
        }
        AnciennesSources = null; AncienMonitoring = null; AncienMonitoringNom = null;
    }

    /// <summary>Un micro des versions précédentes : voix, instrument ou table de mixage, d'après son nom.</summary>
    public static GenreSon DevinerGenre(string? nom)
    {
        string n = (nom ?? "").ToLowerInvariant();
        string[] instruments = { "piano", "clavier", "keyboard", "guitare", "guitar", "basse", "bass", "batterie", "drum", "orgue", "synth", "instrument", "violon", "saxo", "trompette", "musique", "music" };
        string[] tables = { "table", "console", "mixage", "mixer", "sono", "xr18", "x32" };
        if (tables.Any(n.Contains)) return GenreSon.Table;
        if (instruments.Any(n.Contains)) return GenreSon.Instrument;
        return GenreSon.Voix;
    }

    static List<string>? AnciennesSourcesDe(IDestinationAudio d) => d switch
    {
        RecordingProfile p => p.SourceIds,
        VirtualSoundSettings v => v.SourceIds,
        _ => null,
    };

    /// <summary>Avant la 4.2 : chaque destination choisissait ses sources. La 4.2 en faisait des pistes cochées : un mélange par piste.</summary>
    void MigrerPistes(List<AudioSource> anciennes)
    {
        var connues = anciennes.Select(s => s.Id).ToHashSet();
        var pisteDe = new Dictionary<string, int>();
        var contenu = new Dictionary<int, HashSet<string>>();
        foreach (var d in Profiles.Cast<IDestinationAudio>().Concat(VirtualSounds))
        {
            var liste = (AnciennesSourcesDe(d) ?? new()).Where(connues.Contains).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            string cle = string.Join("|", liste);
            if (!pisteDe.TryGetValue(cle, out int t) && pisteDe.Count < Pistes)
            {
                t = pisteDe.Count + 1;
                pisteDe[cle] = t;
                contenu[t] = liste.ToHashSet();
            }
            if (d is RecordingProfile p) p.PisteV6 = t > 0 ? t : 1;
            else if (d is VirtualSoundSettings v) v.PisteV6 = t > 0 ? t : 1;
        }
        foreach (var s in anciennes)
        {
            int bits = 0;
            for (int t = 1; t <= Pistes; t++)
                if (!contenu.TryGetValue(t, out var liste) || liste.Contains(s.Id)) bits |= 1 << (t - 1);
            s.Tracks = bits;
        }
        foreach (var p in Profiles) p.SourceIds = null;
        foreach (var v in VirtualSounds) v.SourceIds = null;
    }
}

/// <summary>
/// Blocs remis à l'enregistrement et aux sorties : 10 ms, stéréo entrelacée, à la fréquence du moteur (48 kHz, comme OBS par défaut).
/// </summary>
public static class AudioBlock
{
    public static int Rate { get; private set; } = 48000;
    public static int Frames => Rate / 100;
    public static int Samples => Frames * 2;
    public static void Configurer(int rate) => Rate = rate == 44100 ? 44100 : 48000;
}
public static class AudioMath
{
    public static double Linear(double db) => db <= -60 ? 0 : Math.Pow(10, db / 20);
    public static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));
}
