using System.Globalization;
using System.Text.Json.Nodes;

namespace MyVerse;

/// <summary>
/// MyCam (Siloé Stream 4.0) : les téléphones jumelés qui servent de caméra, comme DroidCam.
/// Le téléphone envoie ses images par sa connexion WebSocket déjà authentifiée (messages binaires « MC ») : de la vidéo H.264
/// depuis Siloé Stream 4.1 (décodée ici par le FFmpeg d'OBS), sinon des images JPEG une à une.
/// Ce registre garde la dernière image et l'état de chaque téléphone (nom, image, batterie, lien).
/// La source Display « mycam » (Peripherique = identifiant du téléphone) affiche ces images.
/// </summary>
public sealed class MyCams
{
    /// <summary>Ce que le téléphone dit de lui (message « cam.info »), et ce qu'on mesure ici.</summary>
    public sealed record Info(
        string DeviceId, string Nom, string Appareil, string Taille, int Fps, int FpsCible, int Batterie, bool Charge,
        double Temperature, string Lien, bool Avant, bool Diffuse, long Depuis, DateTime VuLe, int FpsRecu)
    {
        /// <summary>Le téléphone envoie des images en ce moment (une image reçue dans les 3 dernières secondes).</summary>
        public bool EnDirect => Diffuse && DateTime.UtcNow - VuLe < TimeSpan.FromSeconds(3);

        /// <summary>Format des images reçues : « H.264 » ou « JPEG » (vide avant la première image).</summary>
        public string Codec { get; init; } = "";

        /// <summary>« 1280 × 720 · 30 i/s · H.264 · Wi-Fi · batterie 78 % ».</summary>
        public string Resume => string.Join(" · ", new[]
        {
            Taille, FpsRecu > 0 ? $"{FpsRecu} i/s" : "", Codec, Lien == "usb" ? "câble USB" : "Wi-Fi",
            Batterie >= 0 ? $"batterie {Batterie} %" + (Charge ? " (en charge)" : "") : "",
        }.Where(s => s.Length > 0));
    }

    /// <summary>Une image : le JPEG (ou, en H.264, les pixels décodés), le quart de tour à appliquer (0 à 3) et le miroir.</summary>
    public sealed record Cadre(byte[] Jpeg, int Rotation, bool Miroir, long Numero)
    {
        /// <summary>
        /// Siloé Stream 1.2 : l'instant où le téléphone a pris l'image, sur l'horloge du PC (<see cref="Horloge"/>, ns) :
        /// réception − trajet sur le Wi-Fi (moitié de l'aller-retour « cam.ping ») − âge de l'image dit par le téléphone.
        /// L'enregistrement s'en sert pour mettre chaque image en face du son du même instant.
        /// </summary>
        public long Capture { get; init; }
        /// <summary>Image H.264 décodée : pixels BGRA, <see cref="Largeur"/> × 4 octets par ligne ; null pour une image JPEG.</summary>
        public byte[]? Pixels { get; init; }
        public int Largeur { get; init; }
        public int Hauteur { get; init; }
    }

    /// <summary>Le flux H.264 d'un téléphone : son décodeur et l'attente d'une image clé.</summary>
    sealed class Flux
    {
        public DecodeurH264? Decodeur;
        /// <summary>Le décodeur n'a pas encore d'image clé (début, reprise, erreur) : les images P sont ignorées.</summary>
        public bool AttendCle = true;
        public bool Ferme;
        public long DemandeCle, DemandeJpeg;
        /// <summary>Unités d'accès reçues depuis la dernière image décodée.</summary>
        public int SansImage;
    }

    readonly object verrou = new();
    readonly Dictionary<string, Info> infos = new();
    readonly Dictionary<string, Cadre> cadres = new();
    readonly Dictionary<string, (long Seconde, int N)> compteurs = new();
    readonly Dictionary<string, Flux> flux = new();
    long numero;

    /// <summary>Nouvelle image d'un téléphone (thread quelconque) : l'identifiant du téléphone.</summary>
    public event Action<string>? NouveauCadre;
    /// <summary>Un téléphone MyCam est apparu, a changé (diffuse, batterie…) ou est parti (thread quelconque).</summary>
    public event Action? Change;
    /// <summary>
    /// Message pour un téléphone (thread quelconque) : « cam.cle » (le décodeur attend une image clé) ou « cam.format »
    /// (ce PC ne décode pas le H.264 : le téléphone repasse en JPEG).
    /// </summary>
    public event Action<string, JsonObject>? Demande;

    /// <summary>Drapeau de l'en-tête (Siloé Stream 1.2) : l'âge de l'image suit, sur 4 octets (microsecondes, petit-boutiste).</summary>
    public const int DrapeauAge = 0x10;

    /// <summary>
    /// En-tête d'une image MyCam : « M », « C », version, drapeaux (bits 0-1 : quarts de tour, bit 2 : miroir, bit 3 : image clé H.264,
    /// bit 4 : âge de l'image), puis, avec le bit 4, l'âge de l'image au moment de l'envoi (µs, 4 octets), puis le JPEG (version 1, FF D8)
    /// ou une unité d'accès H.264 (version 2, Annex B : 00 00 01 ou 00 00 00 01). Âge −1 : non donné. Null si ce n'est pas une image MyCam.
    /// </summary>
    public static (int Rotation, bool Miroir, int Debut, bool H264, bool Cle, long AgeUs)? LireEntete(ReadOnlySpan<byte> b)
    {
        if (b.Length < 8 || b[0] != (byte)'M' || b[1] != (byte)'C') return null;
        int debut = 4;
        long age = -1;
        if ((b[3] & DrapeauAge) != 0)
        {
            if (b.Length < 12) return null;
            age = (uint)(b[4] | b[5] << 8 | b[6] << 16 | b[7] << 24);
            debut = 8;
        }
        if (b[2] == 1)
        {
            if (b[debut] != 0xFF || b[debut + 1] != 0xD8) return null;
            return (b[3] & 3, (b[3] & 4) != 0, debut, false, false, age);
        }
        if (b[2] == 2)
        {
            if (b[debut] != 0 || b[debut + 1] != 0 || !(b[debut + 2] == 1 || (b[debut + 2] == 0 && b[debut + 3] == 1))) return null;
            return (b[3] & 3, (b[3] & 4) != 0, debut, true, (b[3] & 8) != 0, age);
        }
        return null;
    }

    // ------------------------------------------------------------ Siloé Stream 1.2 : l'heure de chaque image, pour la synchro du son
    /// <summary>Retard d'un téléphone qui ne dit pas l'âge de ses images (Siloé Stream 1.1 et avant) : caméra et encodeur, environ.</summary>
    public const long AgeParDefaut = 100 * Horloge.Milliseconde;
    /// <summary>Images gardées par téléphone : l'enregistrement prend celle prise au plus près de l'instant voulu.</summary>
    const int Historique = 8;

    /// <summary>Le lien avec un téléphone : aller-retour sur le Wi-Fi et retard mesuré des images (capture → image prête sur le PC).</summary>
    sealed class Liaison
    {
        public long RttMin = long.MaxValue, Rtt = -1;
        public double Retard = -1, Ecart;
        /// <summary>Trajet téléphone → PC : la moitié de l'aller-retour le plus court, plus l'attente du moment (file d'envoi du téléphone).</summary>
        public long Trajet => Rtt < 0 ? 5 * Horloge.Milliseconde : Math.Clamp(Rtt - RttMin / 2, RttMin / 2, 500 * Horloge.Milliseconde);
    }
    readonly Dictionary<string, Liaison> liaisons = new();
    readonly Dictionary<string, Cadre[]> historiques = new();

    Liaison LiaisonDe(string deviceId)
    {
        if (!liaisons.TryGetValue(deviceId, out var l)) liaisons[deviceId] = l = new Liaison();
        return l;
    }

    /// <summary>Réponse « cam.pong » d'un téléphone : l'aller-retour mesuré (ns).</summary>
    public void AllerRetour(string deviceId, long rtt)
    {
        if (rtt <= 0 || rtt > 5 * Horloge.Seconde) return;
        lock (verrou)
        {
            var l = LiaisonDe(deviceId);
            l.RttMin = Math.Min(l.RttMin, rtt);
            l.Rtt = l.Rtt < 0 ? rtt : (l.Rtt * 3 + rtt) / 4;
        }
    }

    /// <summary>
    /// Retard des images d'un téléphone (ns) : de la prise de vue à l'image prête sur le PC, avec une marge pour ses variations.
    /// L'enregistrement attend ce temps avant de prendre l'image d'un instant, pour qu'elle soit arrivée.
    /// </summary>
    public long Retard(string deviceId)
    {
        lock (verrou)
        {
            if (!liaisons.TryGetValue(deviceId, out var l) || l.Retard < 0) return AgeParDefaut + 50 * Horloge.Milliseconde;
            return (long)Math.Clamp(l.Retard + 2 * l.Ecart + 5 * Horloge.Milliseconde, 0, 1.5 * Horloge.Seconde);
        }
    }

    /// <summary>L'image d'un téléphone prise au plus près de <paramref name="instant"/> (horloge du PC), parmi les dernières reçues.</summary>
    public Cadre? Proche(string deviceId, long instant)
    {
        lock (verrou)
        {
            if (!historiques.TryGetValue(deviceId, out var h)) return cadres.TryGetValue(deviceId, out var d) ? d : null;
            Cadre? meilleur = null;
            long ecart = long.MaxValue;
            foreach (var c in h)
            {
                if (c == null) continue;
                long e = Math.Abs(c.Capture - instant);
                if (e < ecart) { ecart = e; meilleur = c; }
            }
            return meilleur;
        }
    }

    /// <summary>Le PC sait décoder le H.264 (FFmpeg d'OBS) : il l'annonce aux téléphones.</summary>
    public static bool H264 => DecodeurH264.Disponible;

    /// <summary>Une image reçue d'un téléphone (message binaire). Faux si ce n'est pas une image MyCam.</summary>
    /// <param name="recu">Instant de la réception (<see cref="Horloge"/>) ; 0 : maintenant.</param>
    public bool Recevoir(string deviceId, string nomTelephone, byte[] message, int longueur, long recu = 0)
    {
        if (recu == 0) recu = Horloge.Ns();
        if (longueur > message.Length) return false;
        var e = LireEntete(message.AsSpan(0, longueur));
        if (e is not { } en) return false;
        long trajet;
        lock (verrou) trajet = LiaisonDe(deviceId).Trajet;
        long capture = recu - trajet - (en.AgeUs >= 0 ? en.AgeUs * 1000 : AgeParDefaut);
        Cadre c;
        if (en.H264)
        {
            if (DecoderH264(deviceId, message, en.Debut, longueur - en.Debut, en.Cle) is not { } d) return true;
            c = new Cadre(Array.Empty<byte>(), en.Rotation, en.Miroir, 0) { Pixels = d.Pixels, Largeur = d.Largeur, Hauteur = d.Hauteur, Capture = capture };
        }
        else c = new Cadre(message.AsSpan(en.Debut, longueur - en.Debut).ToArray(), en.Rotation, en.Miroir, 0) { Capture = capture };
        Publier(deviceId, nomTelephone, c, en.H264 ? "H.264" : "JPEG");
        return true;
    }

    /// <summary>
    /// Décode une unité d'accès H.264 du téléphone. Sans image clé reçue, les images P sont ignorées et une image clé est demandée
    /// (au plus une fois par seconde) ; sans FFmpeg, le téléphone est prié de repasser en JPEG.
    /// </summary>
    (byte[] Pixels, int Largeur, int Hauteur)? DecoderH264(string deviceId, byte[] message, int debut, int longueur, bool cle)
    {
        Flux f;
        lock (verrou)
        {
            if (!flux.TryGetValue(deviceId, out var existant)) flux[deviceId] = existant = new Flux();
            f = existant;
        }
        string? demande = null;
        (byte[], int, int)? sortie = null;
        lock (f)
        {
            if (f.Ferme) return null;
            long maintenant = Environment.TickCount64;
            f.Decodeur ??= DecodeurH264.Creer();
            if (f.Decodeur == null)
            {
                if (maintenant - f.DemandeJpeg > 5000) { f.DemandeJpeg = maintenant; demande = "cam.format"; }
            }
            else if (f.AttendCle && !cle)
            {
                if (maintenant - f.DemandeCle > 1000) { f.DemandeCle = maintenant; demande = "cam.cle"; }
            }
            else
            {
                var px = f.Decodeur.Decoder(message, debut, longueur, out var l, out var h);
                if (px != null)
                {
                    f.AttendCle = false;
                    f.SansImage = 0;
                    sortie = (px, l, h);
                }
                else if (++f.SansImage >= 3 || f.Decodeur.Erreurs > 0)
                {
                    // le décodeur ne sort plus rien (flux repris en route, données perdues) : on repart de la prochaine image clé
                    f.AttendCle = true;
                    if (maintenant - f.DemandeCle > 1000) { f.DemandeCle = maintenant; demande = "cam.cle"; }
                }
            }
        }
        if (demande != null)
        {
            var m = new JsonObject { ["t"] = demande };
            if (demande == "cam.format") m["codec"] = "jpeg";
            try { Demande?.Invoke(deviceId, m); } catch (Exception ex) { Journal.Erreur("MyCam", ex); }
        }
        return sortie;
    }

    void FermerFlux(string deviceId)
    {
        Flux? f;
        lock (verrou)
        {
            if (!flux.Remove(deviceId, out f)) return;
        }
        lock (f)
        {
            f.Ferme = true;
            f.Decodeur?.Dispose();
            f.Decodeur = null;
        }
    }

    void Publier(string deviceId, string nomTelephone, Cadre image, string codec)
    {
        bool nouveau;
        long pret = Horloge.Ns();
        lock (verrou)
        {
            var c = image with { Numero = ++numero };
            cadres[deviceId] = c;
            if (!historiques.TryGetValue(deviceId, out var h)) historiques[deviceId] = h = new Cadre[Historique];
            Array.Copy(h, 1, h, 0, h.Length - 1);
            h[^1] = c;
            // retard mesuré : de la prise de vue à l'image prête ici (décodée), moyenne et écart glissants
            if (c.Capture > 0)
            {
                var l = LiaisonDe(deviceId);
                double r = pret - c.Capture;
                if (l.Retard < 0) { l.Retard = r; l.Ecart = 10 * Horloge.Milliseconde; }
                else { l.Ecart = l.Ecart * 0.95 + Math.Abs(r - l.Retard) * 0.05; l.Retard = l.Retard * 0.95 + r * 0.05; }
            }
            var s = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            infos.TryGetValue(deviceId, out var i);
            int fps = i?.FpsRecu ?? 0;
            if (compteurs.TryGetValue(deviceId, out var k) && k.Seconde == s) compteurs[deviceId] = (s, k.N + 1);
            else
            {
                if (compteurs.ContainsKey(deviceId)) fps = k.Seconde == s - 1 ? k.N : 0;
                compteurs[deviceId] = (s, 1);
            }
            nouveau = i == null || !i.EnDirect || i.Codec != codec;
            infos[deviceId] = (i ?? NouvelleInfo(deviceId, nomTelephone)) with { Diffuse = true, VuLe = DateTime.UtcNow, FpsRecu = fps, Codec = codec };
        }
        try { NouveauCadre?.Invoke(deviceId); } catch (Exception ex) { Journal.Erreur("MyCam", ex); }
        if (nouveau) Prevenir();
    }

    static Info NouvelleInfo(string deviceId, string nom) =>
        new(deviceId, nom.Length > 0 ? nom : "MyCam", "", "", 0, 30, -1, false, 0, "wifi", false, false, 0, DateTime.MinValue, 0);

    /// <summary>Message « cam.info » du téléphone : nom de la source, image, batterie, lien, diffusion.</summary>
    public void Infos(string deviceId, string nomTelephone, JsonObject m)
    {
        string Ch(string k, string d = "") => m[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : d;
        int Ent(string k, int d)
        {
            if (m[k] is not JsonValue v) return d;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return (int)l;
            return v.TryGetValue<double>(out var x) ? (int)x : d;
        }
        double Reel(string k) => m[k] is JsonValue v && v.TryGetValue<double>(out var x) ? x : 0;
        bool Oui(string k) => m[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        long Long(string k) => m[k] is JsonValue v && v.TryGetValue<long>(out var l) ? l : m[k] is JsonValue w && w.TryGetValue<double>(out var x) ? (long)x : 0;
        lock (verrou)
        {
            var avant = infos.TryGetValue(deviceId, out var i) ? i : NouvelleInfo(deviceId, nomTelephone);
            var nom = Ch("nom").Trim();
            infos[deviceId] = avant with
            {
                Nom = nom.Length > 0 ? (nom.Length > 40 ? nom[..40] : nom) : avant.Nom,
                Appareil = Ch("appareil", avant.Appareil),
                Taille = Ch("taille", avant.Taille),
                Fps = Ent("fps", avant.Fps),
                FpsCible = Ent("fpsCible", avant.FpsCible),
                Batterie = Ent("batterie", -1),
                Charge = Oui("charge"),
                Temperature = Reel("temperature"),
                Lien = Ch("lien", "wifi"),
                Avant = Oui("avant"),
                Diffuse = Oui("diffuse"),
                Depuis = Long("depuis"),
            };
            if (!Oui("diffuse")) { cadres.Remove(deviceId); historiques.Remove(deviceId); }
        }
        // diffusion arrêtée : le décodeur est libéré ; à la reprise, le téléphone recommence par une image clé
        if (!Oui("diffuse")) FermerFlux(deviceId);
        Prevenir();
    }

    /// <summary>Le téléphone s'est déconnecté : il reste connu (nom), sans image.</summary>
    public void Deconnecte(string deviceId)
    {
        FermerFlux(deviceId);
        lock (verrou)
        {
            if (!infos.TryGetValue(deviceId, out var i)) return;
            infos[deviceId] = i with { Diffuse = false };
            cadres.Remove(deviceId);
            historiques.Remove(deviceId);
            liaisons.Remove(deviceId);
        }
        Prevenir();
    }

    public Cadre? Dernier(string deviceId) { lock (verrou) return cadres.TryGetValue(deviceId, out var c) ? c : null; }

    public Info? InfoDe(string deviceId) { lock (verrou) return infos.TryGetValue(deviceId, out var i) ? i : null; }

    /// <summary>Les téléphones MyCam vus depuis le lancement : ceux qui diffusent d'abord.</summary>
    public List<Info> Liste() { lock (verrou) return infos.Values.OrderByDescending(i => i.EnDirect).ThenBy(i => i.Nom, StringComparer.CurrentCultureIgnoreCase).ToList(); }

    void Prevenir() { try { Change?.Invoke(); } catch (Exception e) { Journal.Erreur("MyCam", e); } }

    /// <summary>« 42:18 » depuis une heure Unix en millisecondes (0 : rien).</summary>
    public static string Duree(long depuisMs)
    {
        if (depuisMs <= 0) return "";
        var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(depuisMs);
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : d.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}

public sealed partial class Regie
{
    /// <summary>L'écran noir de la régie (touche B) : la fenêtre principale le tient, le téléphone le demande.</summary>
    public Func<bool> EstNoir { get; set; } = () => false;
    /// <summary>Bascule l'écran noir (fenêtre principale) ; null sans interface.</summary>
    public Action? BasculerNoir { get; set; }

    static bool EstMyCam(SourceDisplay? s, string deviceId) => s is { Type: "mycam" } && s.Peripherique == deviceId;

    /// <summary>
    /// Témoin d'un téléphone MyCam : « live » s'il est à l'écran (en grand ou dans un calque montré), « next » s'il passe avec Suivant, sinon « off ».
    /// L'écran noir éteint le témoin rouge.
    /// </summary>
    public string TemoinMyCam(string deviceId)
    {
        if (!EstNoir() && (EstMyCam(Etat.Media, deviceId) || Calques.Any(c => c.Visible && EstMyCam(c.Media, deviceId)))) return "live";
        try
        {
            var n = Prochaine();
            if (n != null && EtatCible(n.Cible, ElementCourant) is { } e && EstMyCam(e.Media, deviceId)) return "next";
        }
        catch (Exception ex) { Journal.Erreur("témoin MyCam", ex); }
        return "off";
    }
}
