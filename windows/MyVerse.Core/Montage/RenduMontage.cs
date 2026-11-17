using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MyVerse.Montages;

/// <summary>Un encodeur vidéo de FFmpeg (carte graphique ou processeur).</summary>
public sealed record EncodeurVideo(string Nom, string Libelle, bool Materiel, string Codec);

/// <summary>
/// Les encodeurs vidéo : NVIDIA NVENC, Intel Quick Sync, AMD AMF, puis x264 / x265 (processeur) en secours. Chacun est essayé
/// pour de vrai (quelques images à la taille voulue) : une carte qui ne sait pas faire de la 4K n'est pas choisie pour de la 4K.
/// </summary>
public static class EncodeursVideo
{
    public static readonly EncodeurVideo[] Tous =
    {
        new("h264_nvenc", "NVIDIA NVENC · H.264", true, "h264"),
        new("hevc_nvenc", "NVIDIA NVENC · HEVC", true, "hevc"),
        new("h264_qsv", "Intel Quick Sync · H.264", true, "h264"),
        new("hevc_qsv", "Intel Quick Sync · HEVC", true, "hevc"),
        new("h264_amf", "AMD AMF · H.264", true, "h264"),
        new("hevc_amf", "AMD AMF · HEVC", true, "hevc"),
        new("libx264", "Processeur (x264) · H.264", false, "h264"),
        new("libx265", "Processeur (x265) · HEVC", false, "hevc"),
    };

    static readonly Dictionary<string, bool> essais = new();

    static string Q(int q) => q.ToString(CultureInfo.InvariantCulture);

    /// <summary>Les réglages de qualité constante de chaque encodeur (<paramref name="q"/> : 14 presque sans perte, 30 léger).</summary>
    public static string[] Options(string encodeur, int q) => encodeur switch
    {
        "libx264" => new[] { "-preset", "fast", "-crf", Q(q), "-profile:v", "high", "-pix_fmt", "yuv420p" },
        "libx265" => new[] { "-preset", "fast", "-crf", Q(q + 2), "-pix_fmt", "yuv420p" },
        "h264_nvenc" => new[] { "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-cq", Q(q), "-b:v", "0", "-spatial-aq", "1", "-profile:v", "high", "-pix_fmt", "yuv420p" },
        "hevc_nvenc" => new[] { "-preset", "p5", "-tune", "hq", "-rc", "vbr", "-cq", Q(q + 1), "-b:v", "0", "-spatial-aq", "1", "-pix_fmt", "yuv420p" },
        "h264_qsv" => new[] { "-preset", "medium", "-global_quality", Q(q), "-profile:v", "high" },
        "hevc_qsv" => new[] { "-preset", "medium", "-global_quality", Q(q + 1) },
        "h264_amf" => new[] { "-quality", "quality", "-rc", "cqp", "-qp_i", Q(q), "-qp_p", Q(q + 2) },
        "hevc_amf" => new[] { "-quality", "quality", "-rc", "cqp", "-qp_i", Q(q), "-qp_p", Q(q + 2) },
        _ => Array.Empty<string>(),
    };

    /// <summary>L'encodeur marche vraiment sur ce PC à cette taille (essai gardé pour la séance).</summary>
    public static async Task<bool> MarcheAsync(string ffmpeg, string encodeur, int l, int h, CancellationToken annuler = default)
    {
        string cle = $"{ffmpeg}|{encodeur}|{l}x{h}";
        lock (essais) if (essais.TryGetValue(cle, out var connu)) return connu;
        bool ok;
        try
        {
            var args = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", $"color=c=black:s={l}x{h}:r=30", "-frames:v", "5", "-c:v", encodeur };
            args.AddRange(Options(encodeur, 20));
            args.AddRange(new[] { "-f", "null", "-" });
            var (code, _, _) = await Processus.ExecuterAsync(ffmpeg, args, TimeSpan.FromSeconds(40), annuler);
            ok = code == 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { ok = false; }
        lock (essais) essais[cle] = ok;
        return ok;
    }

    /// <summary>Les encodeurs qui marchent sur ce PC à cette taille, les cartes graphiques d'abord.</summary>
    public static async Task<List<EncodeurVideo>> DetecterAsync(string ffmpeg, int l, int h, CancellationToken annuler = default)
    {
        var r = new List<EncodeurVideo>();
        foreach (var e in Tous) if (await MarcheAsync(ffmpeg, e.Nom, l, h, annuler)) r.Add(e);
        return r;
    }

    /// <summary>L'encodeur choisi dans les réglages, ou le meilleur qui marche (carte graphique d'abord, le codec voulu d'abord).</summary>
    public static async Task<EncodeurVideo> ChoisirAsync(string ffmpeg, SortieMontage s, int l, int h, CancellationToken annuler = default)
    {
        if (s.Encodeur != "auto" && Tous.FirstOrDefault(e => e.Nom == s.Encodeur) is { } choisi)
        {
            if (await MarcheAsync(ffmpeg, choisi.Nom, l, h, annuler)) return choisi;
            throw new IOException($"L'encodeur « {choisi.Libelle} » ne marche pas sur ce PC en {l} × {h} : choisissez « Automatique ».");
        }
        foreach (var e in Tous.Where(e => e.Codec == s.Codec)) if (await MarcheAsync(ffmpeg, e.Nom, l, h, annuler)) return e;
        foreach (var e in Tous.Where(e => e.Codec != s.Codec)) if (await MarcheAsync(ffmpeg, e.Nom, l, h, annuler)) return e;
        throw new IOException($"Aucun encodeur vidéo ne marche sur ce PC en {l} × {h}.");
    }
}

/// <summary>
/// Le rendu d'un montage, sans fichier final intermédiaire : le résultat sort de FFmpeg au fil de l'eau (vers l'envoi YouTube,
/// ou vers un fichier si on exporte).
/// <list type="bullet">
/// <item>Copie (« -c copy ») quand rien n'oblige à réencoder : mêmes formats, pas de titre, de logo, de fondu, de générique ni de
/// son changé. Les images d'origine sont gardées telles quelles (qualité, résolution, codec, débit) ; chaque coupe se cale sur
/// l'image clé qui la précède.</item>
/// <item>Réencodage sinon, à l'image près : chaque séquence est décodée à son tour par son propre FFmpeg (deux à la fois pendant un
/// fondu, jamais plus : la mémoire reste bornée même pour des heures de montage), Siloé Stream compte les images et les
/// échantillons de son de chaque séquence (le son et l'image restent calés au 1/48000 s près, quel que soit le nombre de coupes),
/// et un seul encodeur (carte graphique si possible) reçoit l'image et le son par deux tubes, pose les titres et le logo,
/// mélange les pistes son ajoutées et écrit le résultat.</item>
/// </list>
/// </summary>
public sealed class RenduMontage : IDisposable
{
    public const int Frequence = 48000;
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    readonly ProjetMontage p;
    readonly IReadOnlyDictionary<string, InfoMedia> infos;
    readonly string ffmpeg;
    /// <summary>Le dossier de travail (petits fichiers : liste, textes des titres, police, filtres). Supprimé à la fin.</summary>
    public string Dossier { get; }
    readonly CancellationTokenSource arret = new();
    readonly List<Process> processus = new();
    readonly StringBuilder journal = new();
    readonly TaskCompletionSource<bool> fin = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Process? sortie;
    long imagesFaites, imagesTotal, microsFaites;
    volatile string? erreur;
    int demarre;
    bool police;

    /// <summary>Un élément de la piste, compté en images et en échantillons de son.</summary>
    sealed record Morceau(ElementPiste Element, long DebutImage, int Images, int Recouvrement, long Echantillons, long RecouvrementSon);
    readonly List<Morceau> morceaux = new();
    readonly List<(string Fichier, double Entree, double Sortie)> copies = new();

    public bool Copie { get; private set; }
    /// <summary>Pourquoi il faut réencoder (vide en copie).</summary>
    public string Raison { get; private set; } = "";
    public int Largeur { get; private set; }
    public int Hauteur { get; private set; }
    public (int Num, int Den) Ips { get; private set; } = (30, 1);
    public EncodeurVideo? Encodeur { get; private set; }
    /// <summary>La durée du résultat (secondes).</summary>
    public double Duree { get; private set; }
    /// <summary>« mp4 » (MP4 fragmenté : lisible pendant qu'il s'écrit) ou « matroska ».</summary>
    public string Conteneur { get; private set; } = "mp4";
    public string TypeMime => Conteneur == "mp4" ? "video/mp4" : "video/x-matroska";
    public string ExtensionConseillee => Conteneur == "mp4" ? ".mp4" : ".mkv";
    /// <summary>La taille probable du résultat (pour vérifier la place sur le disque).</summary>
    public long OctetsEstimes { get; private set; }
    public List<string> Avertissements { get; } = new();
    public string? Erreur => erreur;
    /// <summary>Vrai quand FFmpeg a fini sans erreur (le résultat est complet).</summary>
    public Task<bool> Termine => fin.Task;
    /// <summary>Le résultat, au fil de l'eau (quand il n'est pas écrit dans un fichier).</summary>
    public Stream? Flux { get; private set; }
    public double Progression => Copie
        ? (Duree > 0 ? Math.Clamp(Interlocked.Read(ref microsFaites) / 1e6 / Duree, 0, 1) : 0)
        : (imagesTotal > 0 ? Math.Clamp((double)Interlocked.Read(ref imagesFaites) / imagesTotal, 0, 1) : 0);
    public double SecondesFaites => Progression * Duree;
    public string Description => Copie
        ? $"Copie sans réencodage : qualité d'origine ({Largeur} × {Hauteur})"
        : $"Réencodage {Largeur} × {Hauteur} · {Encodeur?.Libelle} · qualité {p.Sortie.Qualite}";

    RenduMontage(ProjetMontage p, IReadOnlyDictionary<string, InfoMedia> infos, string ffmpeg, string dossier)
    {
        this.p = p; this.infos = infos; this.ffmpeg = ffmpeg; Dossier = dossier;
    }

    // ================================================================================================ préparation
    /// <summary>
    /// Analyse les fichiers, choisit la copie ou le réencodage (et l'encodeur), prépare le dossier de travail. Rien n'est encore
    /// lancé : <see cref="Demarrer"/> le fait.
    /// </summary>
    public static async Task<RenduMontage> PreparerAsync(ProjetMontage projet, string ffmpeg, string dossierTravail, Action<string>? etat = null, CancellationToken annuler = default)
    {
        var p = projet.Copie();
        if (p.Sequences.Count == 0) throw new InvalidOperationException("Le montage est vide : ajoutez au moins une séquence vidéo.");
        var infos = new Dictionary<string, InfoMedia>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in p.Fichiers())
        {
            etat?.Invoke($"Analyse de « {Path.GetFileName(f)} »…");
            infos[f] = await AnalyseMedia.LireAsync(ffmpeg, f, annuler);
        }
        foreach (var s in p.Sequences)
        {
            var i = infos[s.Source];
            if (!i.Video) throw new InvalidDataException($"« {s.Nom} » n'a pas d'image : mettez-le sur une piste son.");
            if (i.Duree > 0 && s.Fin > i.Duree) s.Fin = i.Duree;
            if (s.Fin < s.Debut + 0.1) throw new InvalidDataException($"La séquence « {s.Nom} » est vide (début après la fin du fichier ?).");
        }
        foreach (var a in p.Audios.ToList())
        {
            var i = infos[a.Source];
            if (!i.Audio) { p.Audios.Remove(a); continue; }
            if (i.Duree > 0 && a.Fin > i.Duree) a.Fin = i.Duree;
            if (a.Fin < a.Debut + 0.05) p.Audios.Remove(a);
        }
        Directory.CreateDirectory(dossierTravail);
        var r = new RenduMontage(p, infos, ffmpeg, dossierTravail);
        var premiere = infos[p.Sequences[0].Source];
        (r.Largeur, r.Hauteur) = p.Sortie.Taille(premiere.Largeur, premiere.Hauteur);
        r.Ips = premiere.IpsExactes;
        var (copie, raison) = CopiePossible(p, infos);
        r.Copie = copie; r.Raison = raison;
        if (copie)
        {
            r.Largeur = premiere.Largeur; r.Hauteur = premiere.Hauteur;
            r.Conteneur = premiere.CodecVideo is "h264" or "hevc" && (!premiere.Audio || premiere.CodecAudio == "aac") ? "mp4" : "matroska";
            etat?.Invoke("Recherche des images clés pour couper sans réencoder…");
            await r.PlanifierCopieAsync(annuler);
        }
        else
        {
            etat?.Invoke("Choix de l'encodeur…");
            r.Encodeur = await EncodeursVideo.ChoisirAsync(ffmpeg, p.Sortie, r.Largeur, r.Hauteur, annuler);
            r.Conteneur = "mp4";
            r.Planifier();
        }
        return r;
    }

    /// <summary>La copie sans réencodage est-elle possible ? Sinon, pourquoi (en mots simples).</summary>
    public static (bool Possible, string Raison) CopiePossible(ProjetMontage p, IReadOnlyDictionary<string, InfoMedia> infos)
    {
        if (p.Sequences.Count == 0) return (false, "montage vide");
        if (p.Sortie.Mode == "exact") return (false, "coupes à l'image près demandées");
        if (p.Intro.Actif || p.Outro.Actif) return (false, "générique");
        if (p.Titres.Count > 0) return (false, "titres");
        if (p.Logo.Visible) return (false, "logo");
        if (p.Audios.Any(a => !a.Muet && a.Volume > 0)) return (false, "pistes son ajoutées");
        if (p.Sequences.Skip(1).Any(s => s.Transition != ProjetMontage.Aucune)) return (false, "fondus entre les séquences");
        if (Math.Abs(p.VolumePrincipal - 1) > 0.001 || p.Sequences.Any(s => s.Muet || Math.Abs(s.Volume - 1) > 0.001)) return (false, "volume changé");
        var l = p.Sequences.Select(s => infos.TryGetValue(s.Source, out var i) ? i : null).ToList();
        if (l.Any(i => i is not { Video: true })) return (false, "fichier sans image");
        var a = l[0]!;
        foreach (var i in l)
            if (i!.CodecVideo != a.CodecVideo || i.Largeur != a.Largeur || i.Hauteur != a.Hauteur || Math.Abs(i.Ips - a.Ips) > 0.01 || i.FormatPixels != a.FormatPixels
                || i.Audio != a.Audio || i.CodecAudio != a.CodecAudio || i.Frequence != a.Frequence || i.Canaux != a.Canaux)
                return (false, "enregistrements de formats différents");
        var (tl, th) = p.Sortie.Taille(a.Largeur, a.Hauteur);
        if (tl != a.Largeur / 2 * 2 || th != a.Hauteur / 2 * 2) return (false, $"taille changée ({tl} × {th})");
        if (a.CodecVideo is not ("h264" or "hevc" or "av1" or "vp9" or "mpeg2video" or "prores")) return (false, "codec vidéo " + a.CodecVideo);
        return (true, "");
    }

    /// <summary>
    /// L'image clé où couper sans réencoder pour commencer à <paramref name="t"/> (secondes depuis le début du fichier) : celle qui
    /// suit de très près (moins de 0,1 s : l'amorce du son AAC décale souvent le début du fichier de 21 ms), sinon celle d'avant ;
    /// null si FFmpeg n'en voit pas.
    /// </summary>
    public static async Task<double?> ImageCleAvantAsync(string ffmpeg, InfoMedia info, double t, CancellationToken annuler = default)
    {
        if (t <= 0.05) return 0;
        double depuis = Math.Max(0, t - 15);
        var args = new[] { "-hide_banner", "-nostdin", "-skip_frame", "nokey", "-ss", F(depuis), "-t", F(t - depuis + 0.3), "-copyts", "-i", info.Chemin,
            "-map", "0:v:0", "-vf", "showinfo", "-an", "-sn", "-f", "null", "-" };
        var (_, err, _) = await Processus.ExecuterAsync(ffmpeg, args, TimeSpan.FromSeconds(90), annuler);
        double? avant = null, apres = null;
        foreach (var ligne in err.Split('\n'))
        {
            if (!ligne.Contains("iskey:1")) continue;
            var m = Regex.Match(ligne, @"pts_time:\s*(-?[\d.]+)");
            if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
            v -= info.DebutFlux;   // les instants de FFmpeg (-copyts) comptent depuis le début du flux
            if (v <= t + 0.0005) { if (avant == null || v > avant) avant = Math.Max(0, v); }
            else if (v <= t + 0.1 && (apres == null || v < apres)) apres = v;
        }
        return apres ?? avant;
    }

    async Task PlanifierCopieAsync(CancellationToken annuler)
    {
        copies.Clear();
        double total = 0, recule = 0;
        long octets = 0;
        foreach (var s in p.Sequences)
        {
            var i = infos[s.Source];
            double entree = await ImageCleAvantAsync(ffmpeg, i, s.Debut, annuler) ?? s.Debut;
            recule = Math.Max(recule, s.Debut - entree);
            copies.Add((s.Source, i.DebutFlux + entree, i.DebutFlux + s.Fin));
            total += s.Fin - entree;
            if (i.Duree > 0) octets += (long)(i.Taille * Math.Min(1, (s.Fin - entree) / i.Duree));
        }
        if (recule > 0.04) Avertissements.Add($"Copie sans réencodage : chaque coupe de début se cale sur l'image clé qui la précède (jusqu'à {recule:0.#} s plus tôt). Pour des coupes à l'image près, choisissez « Réencoder (coupes exactes) ».");
        Duree = total;
        OctetsEstimes = octets;
        var sb = new StringBuilder("ffconcat version 1.0\n");
        foreach (var (f, e, s) in copies)
        {
            sb.Append("file '").Append(f.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            if (e > 0.0005) sb.Append("inpoint ").Append(F(e)).Append('\n');
            sb.Append("outpoint ").Append(F(s)).Append('\n');
        }
        File.WriteAllText(Path.Combine(Dossier, "liste.ffconcat"), sb.ToString(), new UTF8Encoding(false));
    }

    long Echantillon(long image) => image * Frequence * Ips.Den / Ips.Num;

    void Planifier()
    {
        morceaux.Clear();
        double ips = (double)Ips.Num / Ips.Den;
        long fin = 0;
        int precedent = 0;
        foreach (var e in p.Piste())
        {
            int n = Math.Max(1, (int)Math.Round(e.Longueur * ips));
            int o = morceaux.Count > 0 && e.Recouvrement > 0 ? Math.Clamp((int)Math.Round(e.Recouvrement * ips), 0, Math.Min(precedent, n) / 2) : 0;
            long debut = fin - o;
            morceaux.Add(new Morceau(e, debut, n, o, Echantillon(debut + n) - Echantillon(debut), o > 0 ? Echantillon(debut + o) - Echantillon(debut) : 0));
            fin = debut + n;
            precedent = n;
        }
        imagesTotal = fin;
        Duree = fin / ips;
        double mbps = (Hauteur >= 2000 ? 40 : Hauteur >= 1400 ? 22 : Hauteur >= 1000 ? 12 : 6) * Math.Pow(2, (19 - p.Sortie.Qualite) / 6.0);
        OctetsEstimes = (long)((mbps * 1e6 + 256e3) / 8 * Duree);

        // la police des titres (copiée dans le dossier de travail : FFmpeg la lit sans souci de chemin)
        police = false;
        foreach (var f in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeuib.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        })
        {
            if (f.Length > 0 && File.Exists(f))
            {
                try { File.Copy(f, Path.Combine(Dossier, "police.ttf"), true); police = true; break; } catch (IOException) { }
            }
        }
    }

    // ================================================================================================ lancement
    /// <summary>
    /// Lance le rendu. <paramref name="fichier"/> : écrire dans ce fichier (exporter) ; null : le résultat sort par <see cref="Flux"/>
    /// (envoi direct à YouTube, sans fichier sur le disque).
    /// </summary>
    public void Demarrer(string? fichier = null)
    {
        if (Interlocked.Exchange(ref demarre, 1) == 1) throw new InvalidOperationException("Rendu déjà lancé.");
        try
        {
            if (Copie) DemarrerCopie(fichier); else DemarrerReencodage(fichier);
        }
        catch (Exception e)
        {
            erreur = e.Message;
            Annuler();
            fin.TrySetResult(false);
            throw;
        }
    }

    IEnumerable<string> ArgsConteneur(string? fichier)
    {
        if (fichier == null)
            return Conteneur == "mp4"
                ? new[] { "-f", "mp4", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", "pipe:1" }
                : new[] { "-f", "matroska", "pipe:1" };
        return Path.GetExtension(fichier).ToLowerInvariant() == ".mkv"
            ? new[] { "-f", "matroska", fichier }
            : new[] { "-f", "mp4", "-movflags", "+faststart", fichier };
    }

    void DemarrerCopie(string? fichier)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-progress", "pipe:2", "-nostats", "-y",
            "-f", "concat", "-safe", "0", "-i", "liste.ffconcat", "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-avoid_negative_ts", "make_zero" };
        if (Conteneur == "matroska" && fichier != null && Path.GetExtension(fichier).ToLowerInvariant() != ".mkv")
            fichier = Path.ChangeExtension(fichier, ".mkv");
        args.AddRange(ArgsConteneur(fichier));
        LancerSortie(args, fichier == null, null);
    }

    void DemarrerReencodage(string? fichier)
    {
        var tubeImage = Tube(out var cheminImage);
        var tubeSon = Tube(out var cheminSon);
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pixel_format", "yuv420p", "-video_size", $"{Largeur}x{Hauteur}", "-framerate", $"{Ips.Num}/{Ips.Den}",
            "-probesize", "32", "-analyzeduration", "0", "-i", cheminImage,
            "-f", "f32le", "-ar", Frequence.ToString(CultureInfo.InvariantCulture), "-ac", "2", "-probesize", "32", "-analyzeduration", "0", "-i", cheminSon };
        int entree = 2;
        var clips = p.Audios.Where(a => !a.Muet && a.Volume > 0).ToList();
        var entreesClips = new List<int>();
        foreach (var c in clips)
        {
            args.AddRange(new[] { "-ss", F(c.Debut), "-t", F(c.Longueur), "-i", c.Source });
            entreesClips.Add(entree++);
        }
        int entreeLogo = -1;
        if (p.Logo.Visible) { args.AddRange(new[] { "-i", p.Logo.Chemin }); entreeLogo = entree++; }

        // ---------- les filtres de l'encodeur : titres, logo, pistes son ajoutées
        var g = new StringBuilder();
        string v = "0:v", a = "1:a";
        var titres = new List<string>();
        int n = 0;
        foreach (var t in p.Titres.Where(t => t.Texte.Trim().Length > 0 || t.SousTexte.Trim().Length > 0))
        {
            int taille = Pair(t.Taille * Hauteur / 1080.0), sous = Pair(t.Taille * 0.62 * Hauteur / 1080.0);
            double y1, y2;
            string x;
            bool boite = t.Position == TitreMontage.Bas;
            switch (t.Position)
            {
                case TitreMontage.Haut: x = "(w-text_w)/2"; y1 = Hauteur * 0.07; y2 = y1 + taille * 1.4; break;
                case TitreMontage.Centre: x = "(w-text_w)/2"; y1 = Hauteur / 2.0 - taille * (t.SousTexte.Trim().Length > 0 ? 1.0 : 0.5); y2 = Hauteur / 2.0 + taille * 0.45; break;
                default: x = F(Largeur * 0.06); y1 = Hauteur * (t.SousTexte.Trim().Length > 0 ? 0.74 : 0.8); y2 = y1 + taille * 1.45; break;
            }
            var fenetre = (t.Debut, t.Debut + t.Duree);
            if (t.Texte.Trim().Length > 0) { EcrireTexte($"titre-{n}.txt", t.Texte.Trim()); titres.Add(Texte($"titre-{n}.txt", taille, x, F(y1), fenetre, boite)); }
            if (t.SousTexte.Trim().Length > 0) { EcrireTexte($"sous-{n}.txt", t.SousTexte.Trim()); titres.Add(Texte($"sous-{n}.txt", sous, x, F(y2), fenetre, boite)); }
            n++;
        }
        if (titres.Count > 0) { g.Append($"[{v}]").Append(string.Join(",", titres)).Append("[vt];"); v = "vt"; }
        if (entreeLogo >= 0)
        {
            var seq = morceaux.Where(m => m.Element.Type == TypeElement.Sequence).ToList();
            double fps = (double)Ips.Num / Ips.Den;
            double t0 = seq.Count > 0 ? seq[0].DebutImage / fps : 0, t1 = seq.Count > 0 ? (seq[^1].DebutImage + seq[^1].Images) / fps : Duree;
            int lw = Pair(Largeur * p.Logo.Taille), marge = Pair(Largeur * 0.03);
            string pos = p.Logo.Coin switch
            {
                "hg" => $"x={marge}:y={marge}",
                "bd" => $"x=main_w-overlay_w-{marge}:y=main_h-overlay_h-{marge}",
                "bg" => $"x={marge}:y=main_h-overlay_h-{marge}",
                _ => $"x=main_w-overlay_w-{marge}:y={marge}",
            };
            g.Append($"[{entreeLogo}:v]scale={lw}:-2,format=rgba,colorchannelmixer=aa={F(p.Logo.Opacite)}[lg];[{v}][lg]overlay={pos}:enable='between(t,{F(t0)},{F(t1)})'[vl];");
            v = "vl";
        }
        if (clips.Count > 0)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                var f = new List<string> { "aresample=48000", "aformat=sample_fmts=fltp:channel_layouts=stereo", $"volume={F(c.Volume)}" };
                if (c.FonduEntree > 0) f.Add($"afade=t=in:st=0:d={F(Math.Min(c.FonduEntree, c.Longueur / 2))}");
                if (c.FonduSortie > 0) { double d = Math.Min(c.FonduSortie, c.Longueur / 2); f.Add($"afade=t=out:st={F(c.Longueur - d)}:d={F(d)}"); }
                f.Add($"adelay=delays={Math.Round(c.Position * 1000).ToString(CultureInfo.InvariantCulture)}:all=1");
                g.Append($"[{entreesClips[i]}:a]").Append(string.Join(",", f)).Append($"[c{i}];");
            }
            g.Append($"[{a}]").Append(string.Concat(Enumerable.Range(0, clips.Count).Select(i => $"[c{i}]")))
                .Append($"amix=inputs={clips.Count + 1}:duration=first:dropout_transition=0:normalize=0[am];");
            a = "am";
        }
        if (g.Length > 0)
        {
            File.WriteAllText(Path.Combine(Dossier, "filtre-sortie.txt"), g.ToString().TrimEnd(';'), new UTF8Encoding(false));
            args.AddRange(new[] { "-/filter_complex", "filtre-sortie.txt" });
        }
        args.AddRange(new[] { "-map", v.Contains(':') ? v + ":0" : $"[{v}]", "-map", a.Contains(':') ? a + ":0" : $"[{a}]" });
        args.AddRange(new[] { "-c:v", Encodeur!.Nom });
        args.AddRange(EncodeursVideo.Options(Encodeur.Nom, p.Sortie.Qualite));
        args.AddRange(new[] { "-g", Math.Max(1, (int)Math.Round(2.0 * Ips.Num / Ips.Den)).ToString(CultureInfo.InvariantCulture) });
        args.AddRange(new[] { "-c:a", "aac", "-b:a", "256k", "-ar", Frequence.ToString(CultureInfo.InvariantCulture), "-ac", "2" });
        args.AddRange(ArgsConteneur(fichier));
        LancerSortie(args, fichier == null, new[]
        {
            Task.Factory.StartNew(() => Pompe("image", tubeImage, PomperImages), TaskCreationOptions.LongRunning),
            Task.Factory.StartNew(() => Pompe("son", tubeSon, PomperSon), TaskCreationOptions.LongRunning),
        });
    }

    static int Pair(double v) => Math.Max(2, (int)Math.Round(v / 2) * 2);

    void EcrireTexte(string nom, string texte) => File.WriteAllText(Path.Combine(Dossier, nom), texte.Replace("\r\n", "\n"), new UTF8Encoding(false));

    /// <summary>Un texte dessiné par FFmpeg (drawtext), avec un fondu d'entrée et de sortie s'il a une fenêtre de temps.</summary>
    string Texte(string fichier, int taille, string x, string y, (double A, double B)? fenetre, bool boite)
    {
        var s = new StringBuilder("drawtext=");
        if (police) s.Append("fontfile=police.ttf:");
        s.Append($"textfile={fichier}:expansion=none:fontsize={taille}:fontcolor=white:x={x}:y={y}:line_spacing={Pair(taille * 0.2)}");
        s.Append(boite ? $":box=1:boxcolor=black@0.5:boxborderw={Pair(taille * 0.35)}" : ":shadowx=2:shadowy=2:shadowcolor=black@0.7");
        if (fenetre is { } w)
        {
            double f = Math.Min(0.4, (w.B - w.A) / 4);
            s.Append($":enable='between(t,{F(w.A)},{F(w.B)})':alpha='clip(min((t-{F(w.A)})/{F(f)},({F(w.B)}-t)/{F(f)}),0,1)'");
        }
        return s.ToString();
    }

    static NamedPipeServerStream Tube(out string chemin)
    {
        string nom = "siloestream-montage-" + Guid.NewGuid().ToString("N");
        var t = new NamedPipeServerStream(nom, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 1 << 20);
        chemin = OperatingSystem.IsWindows() ? @"\\.\pipe\" + nom : "unix:" + Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + nom);
        return t;
    }

    Process Lancer(ProcessStartInfo info, StringBuilder? erreurs)
    {
        var pr = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (erreurs != null)
            pr.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (erreurs) { if (erreurs.Length < 8000) erreurs.AppendLine(e.Data); }
            };
        if (!pr.Start()) throw new IOException("FFmpeg n'a pas démarré.");
        if (erreurs != null) pr.BeginErrorReadLine();
        lock (processus) processus.Add(pr);
        return pr;
    }

    void LancerSortie(List<string> args, bool flux, Task[]? pompes)
    {
        Journal.Info("montage : ffmpeg " + string.Join(" ", args.Select(x => x.Contains(' ') ? "\"" + x + "\"" : x)));
        var info = Processus.Info(ffmpeg, args, Dossier, false, flux);
        sortie = Lancer(info, null);
        if (flux) Flux = sortie.StandardOutput.BaseStream;
        var lecture = Task.Run(async () =>
        {
            string? ligne;
            while ((ligne = await sortie.StandardError.ReadLineAsync()) != null)
            {
                if (ligne.StartsWith("out_time_us=", StringComparison.Ordinal) || ligne.StartsWith("out_time_ms=", StringComparison.Ordinal))
                {
                    if (long.TryParse(ligne.AsSpan(ligne.IndexOf('=') + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0) Interlocked.Exchange(ref microsFaites, us);
                }
                else if (!ligne.Contains('=') || ligne.Contains("rror"))
                    lock (journal) { if (journal.Length < 20000) journal.AppendLine(ligne); }
            }
        });
        _ = Task.Run(async () =>
        {
            try { await sortie.WaitForExitAsync(); } catch (Exception) { }
            try { await lecture; } catch (Exception) { }
            if (pompes != null) { try { await Task.WhenAll(pompes).WaitAsync(TimeSpan.FromSeconds(20)); } catch (Exception) { } }
            int code = -1;
            try { code = sortie.ExitCode; } catch (Exception) { }
            bool ok = code == 0 && erreur == null && !arret.IsCancellationRequested;
            if (!ok && erreur == null) erreur = arret.IsCancellationRequested ? "Annulé." : MessageErreur();
            if (ok) { Interlocked.Exchange(ref imagesFaites, imagesTotal); Interlocked.Exchange(ref microsFaites, (long)(Duree * 1e6)); }
            else Journal.Erreur("montage : " + erreur + " | " + JournalTexte());
            TuerTout();
            fin.TrySetResult(ok);
        });
    }

    string JournalTexte() { lock (journal) return journal.ToString(); }

    string MessageErreur()
    {
        var j = JournalTexte();
        if (j.Contains("No space left", StringComparison.OrdinalIgnoreCase) || j.Contains("not enough space", StringComparison.OrdinalIgnoreCase))
            return "Disque plein : libérez de la place puis recommencez.";
        if (j.Contains("Broken pipe", StringComparison.OrdinalIgnoreCase) || j.Contains("Invalid argument") && j.Contains("pipe:"))
            return "L'envoi s'est arrêté avant la fin du rendu.";
        var dernier = Processus.Fin(j);
        return "FFmpeg s'est arrêté : " + (dernier.Length > 0 ? dernier : "erreur inconnue") + ".";
    }

    void Pompe(string quoi, NamedPipeServerStream tube, Action<Stream> travail)
    {
        try
        {
            tube.WaitForConnectionAsync(arret.Token).GetAwaiter().GetResult();
            travail(tube);
            tube.Flush();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (!arret.IsCancellationRequested && erreur == null && e is not IOException) erreur = $"Rendu ({quoi}) : {e.Message}";
            // un tube cassé : l'encodeur s'est arrêté, son code de sortie dira pourquoi
        }
        catch (Exception e)
        {
            Journal.Erreur($"montage ({quoi})", e);
            erreur ??= $"Rendu ({quoi}) : {e.Message}";
            Processus.Tuer(sortie);
        }
        finally
        {
            try { tube.Dispose(); } catch (Exception) { }
        }
    }

    // ================================================================================================ l'image
    int TailleImage => Largeur * Hauteur * 3 / 2;

    void PomperImages(Stream tube)
    {
        var melange = new byte[TailleImage];
        LecteurImages? courant = null;
        try
        {
            for (int k = 0; k < morceaux.Count; k++)
            {
                arret.Token.ThrowIfCancellationRequested();
                var m = morceaux[k];
                courant ??= new LecteurImages(this, m);
                int suivantRec = k + 1 < morceaux.Count ? morceaux[k + 1].Recouvrement : 0;
                for (int f = m.Recouvrement; f < m.Images - suivantRec; f++)
                {
                    courant.Lire();
                    tube.Write(courant.Image, 0, TailleImage);
                    Interlocked.Increment(ref imagesFaites);
                }
                if (suivantRec > 0)
                {
                    var suivant = new LecteurImages(this, morceaux[k + 1]);
                    bool noir = morceaux[k + 1].Element.Transition == ProjetMontage.Noir;
                    for (int j = 0; j < suivantRec; j++)
                    {
                        courant.Lire();
                        suivant.Lire();
                        Fondre(courant.Image, suivant.Image, melange, (j + 0.5) / suivantRec, noir, Largeur * Hauteur);
                        tube.Write(melange, 0, TailleImage);
                        Interlocked.Increment(ref imagesFaites);
                    }
                    courant.Fermer();
                    courant = suivant;
                }
                else { courant.Fermer(); courant = null; }
            }
        }
        finally { courant?.Fermer(); }
    }

    /// <summary>Le fondu de deux images YUV 4:2:0 (« par le noir » : la première s'éteint, puis la seconde s'allume).</summary>
    public static void Fondre(byte[] a, byte[] b, byte[] sortie, double alpha, bool parLeNoir, int luma)
    {
        int n = Math.Min(Math.Min(a.Length, b.Length), sortie.Length);
        if (!parLeNoir)
        {
            int w = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 256), iw = 256 - w;
            for (int i = 0; i < n; i++) sortie[i] = (byte)((a[i] * iw + b[i] * w + 128) >> 8);
            return;
        }
        var src = alpha < 0.5 ? a : b;
        double force = alpha < 0.5 ? 1 - alpha * 2 : (alpha - 0.5) * 2;
        int k = (int)Math.Round(Math.Clamp(force, 0, 1) * 256), ik = 256 - k;
        // le noir en YUV « TV » : Y = 16, U = V = 128
        for (int i = 0; i < n; i++)
        {
            int noir = i < luma ? 16 : 128;
            sortie[i] = (byte)((src[i] * k + noir * ik + 128) >> 8);
        }
    }

    static int LireTout(Stream s, byte[] dest, int voulu)
    {
        int n = 0;
        while (n < voulu)
        {
            int r = s.Read(dest, n, voulu - n);
            if (r <= 0) break;
            n += r;
        }
        return n;
    }

    /// <summary>Les images d'un élément de la piste, lues d'un FFmpeg à lui (exactement le nombre prévu : la dernière se répète si le fichier finit avant).</summary>
    sealed class LecteurImages
    {
        readonly RenduMontage r;
        readonly Morceau m;
        readonly StringBuilder erreurs = new();
        Process? proc;
        Stream? flux;
        bool materiel, fini;
        public byte[] Image;
        byte[] tampon;
        long lues;

        public LecteurImages(RenduMontage r, Morceau m)
        {
            this.r = r; this.m = m;
            Image = new byte[r.TailleImage];
            tampon = new byte[r.TailleImage];
            Array.Fill(Image, (byte)16, 0, r.Largeur * r.Hauteur);
            Array.Fill(Image, (byte)128, r.Largeur * r.Hauteur, r.TailleImage - r.Largeur * r.Hauteur);
            materiel = m.Element.Type == TypeElement.Sequence && r.p.Sortie.DecodageMateriel;
            Demarrer();
        }

        void Demarrer()
        {
            var info = m.Element.Type == TypeElement.Sequence ? r.ArgsSequence(r.p.Sequences[m.Element.Index], m.Images, materiel)
                : r.ArgsGenerique(m.Element.Type == TypeElement.Intro ? r.p.Intro : r.p.Outro, m.Element.Type == TypeElement.Intro ? "intro" : "outro", m.Images);
            proc = r.Lancer(info, erreurs);
            flux = proc.StandardOutput.BaseStream;
        }

        public void Lire()
        {
            if (fini) return;
            int n = LireTout(flux!, tampon, tampon.Length);
            if (n == tampon.Length) { (Image, tampon) = (tampon, Image); lues++; return; }
            if (lues == 0 && materiel)
            {
                // la carte graphique refuse ce fichier : le processeur le décode
                Journal.Info("montage : décodage matériel refusé, processeur à la place : " + erreurs.ToString().Trim());
                Processus.Tuer(proc);
                materiel = false;
                Demarrer();
                n = LireTout(flux!, tampon, tampon.Length);
                if (n == tampon.Length) { (Image, tampon) = (tampon, Image); lues++; return; }
            }
            fini = true;
            if (lues == 0)
            {
                string nom = m.Element.Type == TypeElement.Sequence ? r.p.Sequences[m.Element.Index].Nom : "générique";
                string detail;
                lock (erreurs) detail = Processus.Fin(erreurs.ToString(), 300);
                throw new InvalidDataException($"« {nom} » ne se lit pas{(detail.Length > 0 ? " : " + detail : "")}.");
            }
            // quelques images de moins (arrondi à la fin du fichier) : la dernière se répète ; beaucoup moins : le fichier s'arrête
            // avant la fin prévue (enregistrement coupé), on ne fige pas l'image en silence
            double ips = (double)r.Ips.Num / r.Ips.Den;
            if (lues < m.Images - Math.Max(ips / 2, m.Images / 50.0))
            {
                string nom = m.Element.Type == TypeElement.Sequence ? r.p.Sequences[m.Element.Index].Nom : "générique";
                throw new InvalidDataException($"« {nom} » s'arrête {ProjetMontage.Temps((m.Images - lues) / ips, false)} avant la fin prévue de la séquence (fichier coupé ?) : raccourcissez la séquence.");
            }
            if (lues < m.Images) Journal.Info($"montage : élément {m.Element.Type} {m.Element.Index} : {lues} images lues sur {m.Images} (la dernière est répétée)");
        }

        public void Fermer() => Processus.Tuer(proc);
    }

    ProcessStartInfo ArgsSequence(SequenceMontage s, int images, bool materiel)
    {
        double fps = (double)Ips.Num / Ips.Den;
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
        if (materiel) args.AddRange(new[] { "-hwaccel", "auto" });
        args.AddRange(new[] { "-ss", F(s.Debut), "-t", F(images / fps + 1), "-i", s.Source, "-map", "0:v:0",
            "-vf", $"scale={Largeur}:{Hauteur}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={Largeur}:{Hauteur}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps={Ips.Num}/{Ips.Den},format=yuv420p",
            "-frames:v", images.ToString(CultureInfo.InvariantCulture), "-f", "rawvideo", "-pix_fmt", "yuv420p", "pipe:1" });
        return Processus.Info(ffmpeg, args, Dossier);
    }

    ProcessStartInfo ArgsGenerique(GeneriqueMontage gen, string nom, int images)
    {
        double fps = (double)Ips.Num / Ips.Den, d = images / fps;
        bool logo = p.Logo.Chemin.Length > 0 && File.Exists(p.Logo.Chemin);
        var sb = new StringBuilder("[0:v]format=yuv420p[b0];");
        string b = "b0";
        int taille = Pair(66 * Hauteur / 1080.0), sous = Pair(38 * Hauteur / 1080.0);
        double y = logo ? Hauteur * 0.6 : Hauteur * 0.42;
        if (logo)
        {
            sb.Append($"[1:v]scale=-2:{Pair(Hauteur * 0.26)},format=rgba[lg];[{b}][lg]overlay=x=(main_w-overlay_w)/2:y={F(Hauteur * 0.2)}[b1];");
            b = "b1";
        }
        var f = new List<string>();
        if (gen.Texte.Trim().Length > 0) { EcrireTexte($"gen-{nom}-texte.txt", gen.Texte.Trim()); f.Add(Texte($"gen-{nom}-texte.txt", taille, "(w-text_w)/2", F(y), null, false)); }
        if (gen.SousTexte.Trim().Length > 0) { EcrireTexte($"gen-{nom}-sous.txt", gen.SousTexte.Trim()); f.Add(Texte($"gen-{nom}-sous.txt", sous, "(w-text_w)/2", F(y + taille * 1.6), null, false)); }
        double fondu = Math.Min(0.8, d / 3);
        f.Add($"fade=t=in:st=0:d={F(fondu)}");
        f.Add($"fade=t=out:st={F(Math.Max(0, d - fondu))}:d={F(fondu)}");
        f.Add("format=yuv420p");
        sb.Append($"[{b}]").Append(string.Join(",", f)).Append("[g]");
        File.WriteAllText(Path.Combine(Dossier, $"gen-{nom}.txt"), sb.ToString(), new UTF8Encoding(false));
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error",
            "-f", "lavfi", "-i", $"color=c=black:s={Largeur}x{Hauteur}:r={Ips.Num}/{Ips.Den}:d={F(d + 1)}" };
        if (logo) args.AddRange(new[] { "-i", p.Logo.Chemin });
        args.AddRange(new[] { "-/filter_complex", $"gen-{nom}.txt", "-map", "[g]", "-frames:v", images.ToString(CultureInfo.InvariantCulture), "-f", "rawvideo", "-pix_fmt", "yuv420p", "pipe:1" });
        return Processus.Info(ffmpeg, args, Dossier);
    }

    // ================================================================================================ le son
    void PomperSon(Stream tube)
    {
        const int Bloc = 4800;
        var a = new float[Bloc * 2];
        var b = new float[Bloc * 2];
        var octets = new byte[Bloc * 8];
        LecteurSon? courant = null;
        void Ecrire(float[] s, int ech)
        {
            var o = MemoryMarshal.AsBytes(s.AsSpan(0, ech * 2));
            tube.Write(o);
        }
        try
        {
            for (int k = 0; k < morceaux.Count; k++)
            {
                arret.Token.ThrowIfCancellationRequested();
                var m = morceaux[k];
                courant ??= new LecteurSon(this, m);
                long suivantRec = k + 1 < morceaux.Count ? morceaux[k + 1].RecouvrementSon : 0;
                for (long reste = m.Echantillons - m.RecouvrementSon - suivantRec; reste > 0;)
                {
                    int c = (int)Math.Min(Bloc, reste);
                    courant.Lire(a, c);
                    Ecrire(a, c);
                    reste -= c;
                }
                if (suivantRec > 0)
                {
                    var suivant = new LecteurSon(this, morceaux[k + 1]);
                    for (long j = 0; j < suivantRec;)
                    {
                        int c = (int)Math.Min(Bloc, suivantRec - j);
                        courant.Lire(a, c);
                        suivant.Lire(b, c);
                        for (int i = 0; i < c; i++)
                        {
                            float al = (float)((j + i + 0.5) / suivantRec);
                            a[2 * i] = a[2 * i] * (1 - al) + b[2 * i] * al;
                            a[2 * i + 1] = a[2 * i + 1] * (1 - al) + b[2 * i + 1] * al;
                        }
                        Ecrire(a, c);
                        j += c;
                    }
                    courant.Fermer();
                    courant = suivant;
                }
                else { courant.Fermer(); courant = null; }
            }
        }
        finally { courant?.Fermer(); }
    }

    /// <summary>Le son d'un élément de la piste (silence pour un générique, une séquence muette ou sans son), au volume voulu.</summary>
    sealed class LecteurSon
    {
        readonly Process? proc;
        readonly Stream? flux;
        readonly float gain;
        byte[] octets = new byte[4800 * 8];
        bool fini;

        public LecteurSon(RenduMontage r, Morceau m)
        {
            if (m.Element.Type != TypeElement.Sequence) { fini = true; return; }
            var s = r.p.Sequences[m.Element.Index];
            gain = (float)(s.Muet ? 0 : s.Volume * r.p.VolumePrincipal);
            if (gain <= 0 || !r.infos[s.Source].Audio) { fini = true; return; }
            double secondes = (double)m.Echantillons / Frequence;
            var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-ss", F(s.Debut), "-t", F(secondes + 1), "-i", s.Source, "-map", "0:a:0",
                "-af", "aresample=48000:async=1:first_pts=0", "-f", "f32le", "-acodec", "pcm_f32le", "-ar", "48000", "-ac", "2", "pipe:1" };
            proc = r.Lancer(Processus.Info(r.ffmpeg, args, r.Dossier), new StringBuilder());
            flux = proc.StandardOutput.BaseStream;
        }

        public void Lire(float[] dest, int echantillons)
        {
            int voulu = echantillons * 8;
            if (octets.Length < voulu) octets = new byte[voulu];
            int n = fini || flux == null ? 0 : LireTout(flux, octets, voulu);
            if (n < voulu) fini = true;
            int nf = n / 4;
            Buffer.BlockCopy(octets, 0, dest, 0, nf * 4);
            Array.Clear(dest, nf, echantillons * 2 - nf);
            if (gain != 1f) for (int i = 0; i < nf; i++) dest[i] *= gain;
        }

        public void Fermer() => Processus.Tuer(proc);
    }

    // ================================================================================================ arrêt
    void TuerTout()
    {
        List<Process> l;
        lock (processus) l = processus.ToList();
        foreach (var pr in l) Processus.Tuer(pr);
    }

    /// <summary>Arrête tout (FFmpeg et les lecteurs). Le résultat est alors incomplet : <see cref="Termine"/> rend faux.</summary>
    public void Annuler()
    {
        try { arret.Cancel(); } catch (ObjectDisposedException) { }
        TuerTout();
    }

    public void Dispose()
    {
        Annuler();
        lock (processus) { foreach (var pr in processus) { try { pr.Dispose(); } catch (Exception) { } } processus.Clear(); }
        // seulement nos petits fichiers de travail : jamais un enregistrement
        try { if (Directory.Exists(Dossier)) Directory.Delete(Dossier, true); } catch (Exception) { }
    }
}
