using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MyVerse;
using MyVerse.Montages;

namespace MyVerse.Tests;

/// <summary>
/// Siloé Stream 2.2.0 : le montage. Le projet (piste, fondus, coupe, sauvegarde), l'analyse des fichiers, l'envoi « resumable »
/// contre un faux serveur d'envoi de Google (coupures, réception partielle, session expirée, rendu raté), et — avec FFmpeg
/// (SILOE_FFMPEG) — de vrais rendus : réencodage avec générique, titre, logo, fondu et son ajouté (son et image vérifiés calés),
/// copie sans réencodage en 4K, rendu au fil de l'eau (sans fichier), export.
/// </summary>
public static class MontageTests
{
    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public delegate void Verif(bool condition, string quoi, string? detail = null);

    public static async Task RunAsync(Action<bool, string, string?> verifier)
    {
        Verif ok = (c, q, d) => verifier(c, q, d);
        Projet(ok);
        Analyse(ok);
        await EnvoiAsync(ok);
        var ffmpeg = Environment.GetEnvironmentVariable("SILOE_FFMPEG");
        if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)) { Console.WriteLine("  FFmpeg non configuré : rendus réels ignorés (SILOE_FFMPEG)."); return; }
        await RendusAsync(ok, ffmpeg);
    }

    // ================================================================================================ projet
    static void Projet(Verif ok)
    {
        var p = new ProjetMontage();
        p.Intro.Actif = true; p.Intro.Duree = 4;
        p.Sequences.Add(new SequenceMontage { Source = "a.mkv", Debut = 10, Fin = 70, DureeSource = 100 });
        p.Sequences.Add(new SequenceMontage { Source = "b.mkv", Debut = 0, Fin = 30, DureeSource = 40, Transition = ProjetMontage.Fondu, DureeTransition = 2 });
        p.Sequences.Add(new SequenceMontage { Source = "a.mkv", Debut = 80, Fin = 81, DureeSource = 100, Transition = ProjetMontage.Noir, DureeTransition = 3 });
        p.Outro.Actif = true; p.Outro.Duree = 5;
        var piste = p.Piste();
        ok(piste.Count == 5 && piste[1].Debut == 4 && piste[2].Debut == 62 && piste[2].Recouvrement == 2, "piste : générique puis séquences ; un fondu de 2 s fait chevaucher les séquences", string.Join(" ", piste.Select(e => $"{e.Type}@{e.Debut}")));
        ok(Math.Abs(piste[3].Recouvrement - 0.5) < 1e-9, "un fondu ne dépasse pas la moitié de la séquence la plus courte", F(piste[3].Recouvrement));
        ok(Math.Abs(p.Duree - (4 + 60 + 30 - 2 + 1 - 0.5 + 5)) < 1e-9, "durée du montage avec fondus et génériques", F(p.Duree));
        ok(p.ElementA(5) is { Type: TypeElement.Sequence, Index: 0 } && p.ElementA(63) is { Index: 1 } && p.ElementA(1) is { Type: TypeElement.Intro }, "ce qui passe à un instant donné");
        var n = p.Couper(4 + 20);
        ok(n != null && p.Sequences.Count == 4 && p.Sequences[0].Fin == 30 && n.Debut == 30 && n.Fin == 70 && Math.Abs(p.Duree - (4 + 60 + 30 - 2 + 1 - 0.5 + 5)) < 1e-9,
            "couper à la tête de lecture : deux séquences, même durée totale");
        ok(p.Couper(4.05) == null, "couper tout près d'un bord : refusé");
        var dossier = Path.Combine(Path.GetTempPath(), "siloestream-montage-test-" + Guid.NewGuid().ToString("N")[..8]);
        var chemin = Path.Combine(dossier, "culte" + ProjetMontage.Extension);
        p.Titres.Add(new TitreMontage { Texte = "Pasteur Jean", SousTexte = "Prédication", Debut = 10, Duree = 6 });
        p.Enregistrer(chemin);
        var r = ProjetMontage.Ouvrir(chemin);
        ok(r.Sequences.Count == 4 && r.Sequences[2].Transition == ProjetMontage.Fondu && r.Titres[0].SousTexte == "Prédication" && r.Intro.Actif && Math.Abs(r.Duree - p.Duree) < 1e-9,
            "projet enregistré puis relu à l'identique");
        File.WriteAllText(chemin, File.ReadAllText(chemin).Replace("\"fondu\"", "\"explosion\"").Replace("\"qualite\": 19", "\"qualite\": 99"));
        r = ProjetMontage.Ouvrir(chemin);
        ok(r.Sequences[2].Transition == ProjetMontage.Aucune && r.Sortie.Qualite == 30, "projet modifié à la main : valeurs impossibles corrigées");
        ok(ProjetMontage.LireTemps("1:02:03.5") == 3723.5 && ProjetMontage.LireTemps("62:03") == 3723 && ProjetMontage.LireTemps("95,5") == 95.5 && ProjetMontage.LireTemps("1:75") == null
            && ProjetMontage.Temps(3723.5) == "1:02:03.50", "temps : lecture et écriture (h:mm:ss.cc)");
        ok(new SortieMontage { Resolution = "2160" }.Taille(1920, 1080) == (3840, 2160) && new SortieMontage().Taille(1279, 721) == (1278, 720), "taille de sortie : 4K demandée, ou celle de la source (paire)");
        try { Directory.Delete(dossier, true); } catch (Exception) { }
    }

    // ================================================================================================ analyse
    static void Analyse(Verif ok)
    {
        const string obs = """
            Input #0, matroska,webm, from 'C:\Users\x\Videos\2026-11-15 10-02-11.mkv':
              Metadata:
                ENCODER         : Lavf61.1.100
              Duration: 01:52:07.43, start: 0.000000, bitrate: 41234 kb/s
              Stream #0:0: Video: h264 (High), yuv420p(tv, bt709, progressive), 3840x2160 [SAR 1:1 DAR 16:9], 29.97 fps, 29.97 tbr, 1k tbn (default)
              Stream #0:1: Audio: aac (LC), 48000 Hz, stereo, fltp (default)
            At least one output file must be specified
            """;
        var i = AnalyseMedia.Interpreter("x.mkv", obs, 123);
        ok(i is { Video: true, CodecVideo: "h264", Largeur: 3840, Hauteur: 2160, Audio: true, CodecAudio: "aac", Frequence: 48000, Canaux: 2, FormatPixels: "yuv420p" }
            && Math.Abs(i.Duree - 6727.43) < 0.001 && i.IpsExactes == (30000, 1001), "enregistrement OBS 4K lu (H.264, AAC, 29,97 i/s)", i?.ToString());
        const string mp3 = """
            Input #0, mp3, from 'chant.mp3':
              Duration: 00:04:01.20, start: 0.025057, bitrate: 320 kb/s
              Stream #0:0: Audio: mp3 (mp3float), 44100 Hz, stereo, fltp, 320 kb/s
              Stream #0:1: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 600x600 [SAR 1:1 DAR 1:1], 90k tbr, 90k tbn (attached pic)
            """;
        var m = AnalyseMedia.Interpreter("chant.mp3", mp3);
        ok(m is { Video: false, Audio: true, CodecAudio: "mp3", Frequence: 44100 } && Math.Abs(m.DebutFlux - 0.025057) < 1e-9, "MP3 avec pochette : du son seulement (la pochette n'est pas une vidéo)", m?.ToString());
        ok(AnalyseMedia.Interpreter("x.txt", "Invalid data found when processing input") == null, "fichier illisible : rien");
        var infos = new InfosVideo { Titre = "Culte <dimanche>", Confidentialite = "public", PublierLe = DateTimeOffset.UtcNow.AddDays(1), Tags = Enumerable.Range(0, 200).Select(k => "étiquette" + k).ToList() };
        var res = infos.Ressource();
        ok(res["status"]?["privacyStatus"]?.GetValue<string>() == "private" && res["status"]?["publishAt"] != null && res["snippet"]?["title"]?.GetValue<string>() == "Culte ‹dimanche›"
            && res["snippet"]!["tags"]!.AsArray().Sum(t => t!.GetValue<string>().Length + 1) <= 500, "publication programmée : privée jusqu'à la date ; titre et tags nettoyés pour YouTube");
    }

    // ================================================================================================ envoi resumable
    /// <summary>Un faux serveur d'envoi de Google (Resumable Upload) : il garde les octets reçus, peut couper, ne garder qu'une partie, oublier la session.</summary>
    sealed class FauxEnvoi : IDisposable
    {
        readonly HttpListener ecoute = new();
        public readonly string Adresse;
        public readonly MemoryStream Recu = new();
        public readonly List<string> Plages = new();
        public int Puts, Couper = -1, Partiel = -1, Expirer = -1;
        public string? MetaRecu, TypeRecu;
        public bool Fini;
        public long? Total;
        public FauxEnvoi()
        {
            var t = new TcpListener(IPAddress.Loopback, 0); t.Start();
            int port = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop();
            Adresse = $"http://127.0.0.1:{port}";
            ecoute.Prefixes.Add(Adresse + "/");
            ecoute.Start();
            _ = Task.Run(Boucle);
        }
        async Task Boucle()
        {
            while (ecoute.IsListening)
            {
                HttpListenerContext c;
                try { c = await ecoute.GetContextAsync(); } catch (Exception) { return; }
                try { await Traiter(c); }
                catch (Exception) { try { c.Response.Abort(); } catch (Exception) { } }
            }
        }
        async Task Traiter(HttpListenerContext c)
        {
            var corps = new MemoryStream();
            await c.Request.InputStream.CopyToAsync(corps);
            var octets = corps.ToArray();
            var chemin = c.Request.Url!.AbsolutePath;
            if (c.Request.HttpMethod == "POST" && chemin == "/token") { await Json(c, 200, "{\"access_token\":\"acces\",\"expires_in\":3599}"); return; }
            if (c.Request.HttpMethod == "POST" && chemin == "/upload/youtube/v3/videos")
            {
                MetaRecu = Encoding.UTF8.GetString(octets);
                TypeRecu = c.Request.Headers["X-Upload-Content-Type"];
                c.Response.Headers["Location"] = Adresse + "/upload/session/s1";
                await Json(c, 200, "{}");
                return;
            }
            if (c.Request.HttpMethod != "PUT" || chemin != "/upload/session/s1") { await Json(c, 404, "{}"); return; }
            int n;
            lock (this) n = ++Puts;
            if (n == Expirer) { await Json(c, 404, "{\"error\":{\"code\":404,\"message\":\"Not Found\"}}"); return; }
            var plage = c.Request.Headers["Content-Range"] ?? "";
            lock (Plages) Plages.Add(plage + " (" + octets.Length + ")");
            if (n == Couper) { c.Response.Abort(); return; }    // Internet coupé : rien n'est gardé
            var m = Regex.Match(plage, @"bytes (?:(\d+)-(\d+)|\*)/(\d+|\*)");
            if (!m.Success) { await Json(c, 400, "{}"); return; }
            long? total = m.Groups[3].Value == "*" ? null : long.Parse(m.Groups[3].Value);
            if (m.Groups[1].Success)
            {
                long debut = long.Parse(m.Groups[1].Value);
                if (debut != Recu.Length) { await Json(c, 400, "{\"error\":{\"code\":400,\"message\":\"mauvais début " + debut + " au lieu de " + Recu.Length + "\"}}"); return; }
                int garder = octets.Length;
                if (n == Partiel) garder = octets.Length / 2 / EnvoiYouTube.Quantum * EnvoiYouTube.Quantum;   // une partie seulement
                Recu.Write(octets, 0, garder);
            }
            if (total is { } t && Recu.Length == t)
            {
                Fini = true; Total = t;
                await Json(c, 200, "{\"id\":\"video-42\",\"status\":{\"uploadStatus\":\"uploaded\",\"privacyStatus\":\"public\"}}");
                return;
            }
            c.Response.StatusCode = 308;
            if (Recu.Length > 0) c.Response.Headers["Range"] = "bytes=0-" + (Recu.Length - 1);
            c.Response.Close();
        }
        static async Task Json(HttpListenerContext c, int statut, string json)
        {
            var o = Encoding.UTF8.GetBytes(json);
            c.Response.StatusCode = statut;
            c.Response.ContentType = "application/json";
            await c.Response.OutputStream.WriteAsync(o);
            c.Response.Close();
        }
        public void Dispose() { try { ecoute.Stop(); ecoute.Close(); } catch (Exception) { } }
    }

    static YouTubeCompte CompteFaux(string adresse, string dossier)
    {
        var coffre = new Coffre(dossier);
        coffre.Ecrire(YouTubeCompte.CleJeton, "rafraichir");
        return new YouTubeCompte(coffre, () => ("client", "secret")) { AdresseJeton = adresse + "/token", AdresseApi = adresse + "/youtube/v3", AdresseEnvoi = adresse + "/upload/youtube/v3" };
    }

    /// <summary>Un flux qui donne ses octets par petits bouts irréguliers (comme FFmpeg).</summary>
    sealed class FluxLent : Stream
    {
        readonly byte[] donnees; int pos; readonly Random hasard = new(7);
        public FluxLent(byte[] d) { donnees = d; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, donnees.Length - pos), hasard.Next(1, 300_000));
            Array.Copy(donnees, pos, buffer, offset, n); pos += n; return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => donnees.Length; public override long Position { get => pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    static async Task EnvoiAsync(Verif ok)
    {
        var dossier = Path.Combine(Path.GetTempPath(), "siloestream-envoi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dossier);
        var donnees = RandomNumberGenerator.GetBytes(EnvoiYouTube.Quantum * 13 + 12345);   // 13 quanta et un bout : plusieurs morceaux
        var infos = new InfosVideo { Titre = "Culte du 15 novembre", Description = "Prédication", Tags = new() { "culte", "église" }, Confidentialite = "unlisted" };
        static Task Rien(TimeSpan t, CancellationToken c) => Task.CompletedTask;

        // ---------- un envoi avec une coupure d'Internet et un morceau reçu à moitié
        using (var g = new FauxEnvoi { Couper = 2, Partiel = 4 })
        {
            var envoi = new EnvoiYouTube(CompteFaux(g.Adresse, dossier)) { TailleMorceau = 4 * EnvoiYouTube.Quantum, Attendre = Rien };
            var etats = new List<string>();
            envoi.Etat += etats.Add;
            var session = await envoi.OuvrirSessionAsync(infos, "video/mp4");
            var meta = JsonNode.Parse(g.MetaRecu!)!;
            ok(session.EndsWith("/upload/session/s1") && g.TypeRecu == "video/mp4" && meta["status"]?["privacyStatus"]?.GetValue<string>() == "unlisted" && meta["snippet"]?["description"]?.GetValue<string>() == "Prédication",
                "session d'envoi ouverte (titre, description, visibilité ; taille inconnue)", g.MetaRecu);
            long dernier = 0;
            bool completDemande = false;
            var video = await envoi.EnvoyerAsync(session, new FluxLent(donnees), 0, () => { completDemande = true; return Task.FromResult(true); }, new Progression(o => dernier = o));
            ok(video["id"]?.GetValue<string>() == "video-42" && g.Fini && g.Recu.ToArray().AsSpan().SequenceEqual(donnees), "toute la vidéo est reçue, octet pour octet, malgré la coupure et le morceau reçu à moitié",
                string.Join(" | ", g.Plages));
            ok(completDemande && g.Plages.Count(x => !x.Contains("/*")) >= 1 && g.Plages.Where(x => x.Contains("/*") && !x.StartsWith("bytes */")).All(x => long.Parse(Regex.Match(x, @"\((\d+)\)").Groups[1].Value) % EnvoiYouTube.Quantum == 0),
                "morceaux de 256 Kio (taille inconnue) ; la taille totale seulement au dernier, après la fin du rendu", string.Join(" | ", g.Plages));
            ok(etats.Any(e => e.Contains("nouvel essai")) && etats.Any(e => e.Contains("reprend")) && dernier == donnees.Length, "coupure dite simplement, puis reprise ; progrès jusqu'au bout", string.Join(" / ", etats));
        }

        // ---------- le rendu échoue : le dernier morceau ne part jamais, rien n'est publié
        using (var g = new FauxEnvoi())
        {
            var envoi = new EnvoiYouTube(CompteFaux(g.Adresse, dossier)) { TailleMorceau = 4 * EnvoiYouTube.Quantum, Attendre = Rien };
            var session = await envoi.OuvrirSessionAsync(infos, "video/mp4");
            try
            {
                await envoi.EnvoyerAsync(session, new MemoryStream(donnees), 0, () => Task.FromResult(false));
                ok(false, "rendu raté : l'envoi s'arrête sans publier", null);
            }
            catch (IOException e) { ok(!g.Fini && g.Plages.All(x => x.Contains("/*")), "rendu raté : l'envoi s'arrête sans publier (aucun morceau final)", e.Message); }
        }

        // ---------- la session a expiré chez YouTube
        using (var g = new FauxEnvoi { Expirer = 2 })
        {
            var envoi = new EnvoiYouTube(CompteFaux(g.Adresse, dossier)) { TailleMorceau = 4 * EnvoiYouTube.Quantum, Attendre = Rien };
            var session = await envoi.OuvrirSessionAsync(infos, "video/mp4");
            try { await envoi.EnvoyerAsync(session, new MemoryStream(donnees)); ok(false, "session expirée : erreur claire", null); }
            catch (ErreurYouTube e) { ok(e.Raison == "sessionExpiree" && e.Message.Contains("expiré"), "session expirée : erreur claire", e.Message); }
        }

        // ---------- reprise plus tard depuis une copie complète : YouTube dit où il en est, on envoie la suite
        using (var g = new FauxEnvoi())
        {
            var compte = CompteFaux(g.Adresse, dossier);
            var envoi = new EnvoiYouTube(compte) { TailleMorceau = 4 * EnvoiYouTube.Quantum, Attendre = Rien };
            var session = await envoi.OuvrirSessionAsync(infos, "video/mp4", donnees.Length);
            g.Recu.Write(donnees, 0, 5 * EnvoiYouTube.Quantum);   // déjà reçu avant la fermeture de Siloé Stream
            var etat = await envoi.EtatSessionAsync(session, donnees.Length);
            var copie = new MemoryStream(donnees) { Position = etat.Recus };
            var video = await envoi.EnvoyerAsync(session, copie, etat.Recus);
            ok(!etat.Fini && etat.Recus == 5 * EnvoiYouTube.Quantum && video["id"] != null && g.Recu.ToArray().AsSpan().SequenceEqual(donnees), "reprise depuis la copie : seulement la suite est envoyée");
        }
        try { Directory.Delete(dossier, true); } catch (Exception) { }
    }

    sealed class Progression : IProgress<long> { readonly Action<long> a; public Progression(Action<long> a) { this.a = a; } public void Report(long v) => a(v); }

    // ================================================================================================ rendus réels
    static async Task<int> Ffmpeg(string ffmpeg, params string[] args)
    {
        var (code, err, _) = await Processus.ExecuterAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" }.Concat(args), TimeSpan.FromMinutes(5));
        if (code != 0) Console.WriteLine("    ffmpeg : " + err.Trim());
        return code;
    }

    /// <summary>Un clip de test : noir, un éclair blanc et un bip au même instant <paramref name="eclair"/> (pour vérifier que le son et l'image restent calés).</summary>
    static Task<int> Clip(string ffmpeg, string fichier, int l, int h, double duree, double eclair, string codec = "libx264") => Ffmpeg(ffmpeg,
        "-f", "lavfi", "-i", $"color=c=black:s={l}x{h}:r=30:d={F(duree)},drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:enable='between(t,{F(eclair)},{F(eclair + 0.1)})'",
        "-f", "lavfi", "-i", $"aevalsrc='if(between(t,{F(eclair)},{F(eclair + 0.1)}),0.8*sin(2*PI*1000*t),0)':s=48000:d={F(duree)}",
        "-c:v", codec, "-preset", "ultrafast", "-g", "30", "-sc_threshold", "0", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-shortest", fichier);

    /// <summary>Les instants des éclairs (image blanche) et des bips (son fort) d'un fichier rendu.</summary>
    static async Task<(List<double> Eclairs, List<double> Bips)> Reperes(string ffmpeg, string fichier)
    {
        var (_, err, _) = await Processus.ExecuterAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", fichier, "-vf", "scale=64:36,signalstats,metadata=print:key=lavfi.signalstats.YAVG",
            "-af", "silencedetect=n=-30dB:d=0.05", "-f", "null", "-" }, TimeSpan.FromMinutes(3));
        var eclairs = new List<double>();
        double t = -1;
        foreach (var ligne in err.Split('\n'))
        {
            var mt = Regex.Match(ligne, @"pts_time:([\d.]+)");
            if (mt.Success) t = double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
            var my = Regex.Match(ligne, @"YAVG=([\d.]+)");
            if (my.Success && double.Parse(my.Groups[1].Value, CultureInfo.InvariantCulture) > 180 && (eclairs.Count == 0 || t - eclairs[^1] > 0.5)) eclairs.Add(t);
        }
        var bips = Regex.Matches(err, @"silence_end: ([\d.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        return (eclairs, bips);
    }

    static async Task RendusAsync(Verif ok, string ffmpeg)
    {
        var dossier = Path.Combine(Path.GetTempPath(), "siloestream-rendu-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dossier);
        string A = Path.Combine(dossier, "culte-a.mkv"), B = Path.Combine(dossier, "culte-b.mp4"), K4 = Path.Combine(dossier, "camera-4k.mkv");
        string Musique = Path.Combine(dossier, "musique.mp3"), Logo = Path.Combine(dossier, "logo.png");
        ok(await Clip(ffmpeg, A, 640, 360, 6, 2.0) == 0 && await Clip(ffmpeg, B, 640, 360, 6, 3.0) == 0
            && await Ffmpeg(ffmpeg, "-f", "lavfi", "-i", "sine=f=440:d=8:sample_rate=44100", "-c:a", "libmp3lame", "-b:a", "128k", Musique) == 0
            && await Ffmpeg(ffmpeg, "-f", "lavfi", "-i", "color=c=red:s=200x120:d=1", "-frames:v", "1", Logo) == 0, "clips de test fabriqués (vidéo, musique, logo)");
        var infoA = await AnalyseMedia.LireAsync(ffmpeg, A);
        ok(infoA is { Video: true, Largeur: 640, Hauteur: 360, Audio: true } && Math.Abs(infoA.Duree - 6) < 0.1, "analyse d'un vrai fichier (MKV)", infoA.Resume);

        // ---------- réencodage : générique, deux séquences avec fondu, titre, logo, musique ajoutée
        var p = new ProjetMontage();
        p.Intro.Actif = true; p.Intro.Duree = 2; p.Intro.Texte = "Bienvenue"; p.Intro.SousTexte = "Culte du dimanche";
        p.Sequences.Add(new SequenceMontage { Source = A, Debut = 1, Fin = 4, DureeSource = 6 });
        p.Sequences.Add(new SequenceMontage { Source = B, Debut = 2, Fin = 5, DureeSource = 6, Transition = ProjetMontage.Fondu, DureeTransition = 0.5 });
        p.Titres.Add(new TitreMontage { Texte = "Pasteur Jean", SousTexte = "Prédication", Debut = 2.5, Duree = 2 });
        p.Logo = new LogoMontage { Actif = true, Chemin = Logo, Coin = "hd" };
        p.Sortie.Encodeur = "libx264";
        p.Normaliser();
        var sortie = Path.Combine(dossier, "montage.mp4");
        var sw = Stopwatch.StartNew();
        using (var r = await RenduMontage.PreparerAsync(p, ffmpeg, Path.Combine(dossier, "travail1")))
        {
            ok(!r.Copie && r.Raison == "générique" && r.Encodeur?.Nom == "libx264" && Math.Abs(r.Duree - 7.5) < 0.05, "réencodage nécessaire (générique), encodeur choisi", r.Raison + " " + r.Duree);
            r.Demarrer(sortie);
            bool fini = await r.Termine.WaitAsync(TimeSpan.FromMinutes(5));
            ok(fini && r.Progression == 1, "rendu réencodé terminé", r.Erreur);
        }
        var info = await AnalyseMedia.LireAsync(ffmpeg, sortie);
        ok(info is { Video: true, CodecVideo: "h264", Largeur: 640, Hauteur: 360, Audio: true, CodecAudio: "aac" } && Math.Abs(info.Duree - 7.5) < 0.12, $"montage relu : 7,5 s, H.264 + AAC ({sw.Elapsed.TotalSeconds:0.0} s de rendu)", info.Resume + " " + info.Duree);
        var (eclairs, bips) = await Reperes(ffmpeg, sortie);
        // l'éclair de A (2,0 s du fichier, séquence depuis 1,0) arrive à 2 + 1 = 3,0 s ; celui de B (3,0 s, séquence depuis 2,0, fondu 0,5) à 2 + 3 − 0,5 + 1 = 5,5 s
        bool cale = eclairs.Count == 2 && bips.Count >= 2 && Math.Abs(eclairs[0] - 3.0) < 0.07 && Math.Abs(eclairs[1] - 5.5) < 0.07
            && Math.Abs(bips[0] - eclairs[0]) < 0.04 && Math.Abs(bips[1] - eclairs[1]) < 0.04;
        ok(cale, "le son et l'image restent calés après les coupes et le fondu (écart < 1 image)", $"éclairs {string.Join(",", eclairs.Select(F))} bips {string.Join(",", bips.Select(F))}");

        // ---------- musique ajoutée sur une piste son : elle s'entend, la durée ne change pas
        p.Audios.Add(new ClipAudio { Source = Musique, Piste = 1, Position = 0.5, Debut = 0, Fin = 6, DureeSource = 8, Volume = 0.5 });
        var avecMusique = Path.Combine(dossier, "montage-musique.mkv");
        using (var r = await RenduMontage.PreparerAsync(p, ffmpeg, Path.Combine(dossier, "travail2")))
        {
            r.Demarrer(avecMusique);
            ok(await r.Termine.WaitAsync(TimeSpan.FromMinutes(5)), "rendu avec une piste son ajoutée", r.Erreur);
        }
        var (_, vol, _) = await Processus.ExecuterAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-ss", "1", "-t", "1", "-i", avecMusique, "-af", "volumedetect", "-vn", "-f", "null", "-" }, TimeSpan.FromMinutes(1));
        var moyen = Regex.Match(vol, @"mean_volume: (-?[\d.]+) dB");
        var infoM = await AnalyseMedia.LireAsync(ffmpeg, avecMusique);
        ok(moyen.Success && double.Parse(moyen.Groups[1].Value, CultureInfo.InvariantCulture) > -40 && Math.Abs(infoM.Duree - 7.5) < 0.12, "la musique ajoutée s'entend pendant le générique ; durée inchangée", moyen.Value + " " + infoM.Duree);

        // ---------- au fil de l'eau : le résultat sort par un flux (comme vers YouTube), sans fichier
        p.Audios.Clear();
        p.Titres.Clear();
        long octets = 0;
        var recu = Path.Combine(dossier, "flux.mp4");
        using (var r = await RenduMontage.PreparerAsync(p, ffmpeg, Path.Combine(dossier, "travail3")))
        {
            r.Demarrer(null);
            await using (var f = File.Create(recu)) { var t = new byte[65536]; int n; while ((n = await r.Flux!.ReadAsync(t)) > 0) { octets += n; await f.WriteAsync(t.AsMemory(0, n)); } }
            ok(await r.Termine && r.TypeMime == "video/mp4", "rendu au fil de l'eau : MP4 fragmenté lu jusqu'au bout", r.Erreur);
        }
        var infoF = await AnalyseMedia.LireAsync(ffmpeg, recu);
        ok(octets > 10000 && infoF is { Video: true, Audio: true } && Math.Abs(infoF.Duree - 7.5) < 0.15, "le flux est une vidéo complète et lisible", infoF.Resume + " " + infoF.Duree);

        // ---------- copie sans réencodage, en 4K
        ok(await Clip(ffmpeg, K4, 3840, 2160, 4, 1.5) == 0, "clip 4K fabriqué");
        var k = new ProjetMontage();
        k.Sequences.Add(new SequenceMontage { Source = K4, Debut = 1.0, Fin = 2.5, DureeSource = 4 });
        k.Sequences.Add(new SequenceMontage { Source = K4, Debut = 3.0, Fin = 4.0, DureeSource = 4 });
        var sortieK = Path.Combine(dossier, "montage-4k.mp4");
        sw.Restart();
        using (var r = await RenduMontage.PreparerAsync(k, ffmpeg, Path.Combine(dossier, "travail4")))
        {
            ok(r.Copie && r.Largeur == 3840 && r.Hauteur == 2160 && Math.Abs(r.Duree - 2.5) < 0.1, "4K : copie sans réencodage possible (mêmes formats, coupes sur images clés)", r.Raison + " " + r.Duree);
            r.Demarrer(sortieK);
            ok(await r.Termine.WaitAsync(TimeSpan.FromMinutes(2)), "copie 4K terminée", r.Erreur);
        }
        var infoK = await AnalyseMedia.LireAsync(ffmpeg, sortieK);
        ok(infoK is { CodecVideo: "h264", Largeur: 3840, Hauteur: 2160, Audio: true } && Math.Abs(infoK.Duree - 2.5) < 0.15, $"4K gardée telle quelle (H.264 3840 × 2160, {sw.Elapsed.TotalSeconds:0.0} s)", infoK.Resume + " " + infoK.Duree);

        // ---------- 4K réencodée avec l'encodeur choisi automatiquement (carte graphique si elle sait faire de la 4K)
        k.Titres.Add(new TitreMontage { Texte = "Sainte Cène", Debut = 0.2, Duree = 1.5, Position = TitreMontage.Centre });
        k.Sequences[1].Transition = ProjetMontage.Noir;
        k.Sequences[1].DureeTransition = 0.4;
        var encodeurs = await EncodeursVideo.DetecterAsync(ffmpeg, 3840, 2160);
        var sortieK2 = Path.Combine(dossier, "montage-4k-titre.mp4");
        sw.Restart();
        using (var r = await RenduMontage.PreparerAsync(k, ffmpeg, Path.Combine(dossier, "travail5")))
        {
            ok(!r.Copie && r.Encodeur != null && r.Encodeur.Nom == encodeurs[0].Nom, "4K avec titre : réencodée, meilleur encodeur du PC (" + r.Encodeur?.Libelle + ")", string.Join(", ", encodeurs.Select(e => e.Nom)));
            r.Demarrer(sortieK2);
            ok(await r.Termine.WaitAsync(TimeSpan.FromMinutes(5)), "rendu 4K terminé", r.Erreur);
        }
        var infoK2 = await AnalyseMedia.LireAsync(ffmpeg, sortieK2);
        ok(infoK2 is { Largeur: 3840, Hauteur: 2160, Video: true, Audio: true } && Math.Abs(infoK2.Duree - 2.1) < 0.12, $"4K réencodée relue ({sw.Elapsed.TotalSeconds:0.0} s)", infoK2.Resume + " " + infoK2.Duree);

        // ---------- un fichier illisible : erreur claire avant tout rendu
        var abime = Path.Combine(dossier, "abime.mkv");
        File.WriteAllBytes(abime, RandomNumberGenerator.GetBytes(200_000));
        var x = new ProjetMontage();
        x.Sequences.Add(new SequenceMontage { Source = abime, Debut = 0, Fin = 3, DureeSource = 6 });
        try
        {
            using var r = await RenduMontage.PreparerAsync(x, ffmpeg, Path.Combine(dossier, "travail6"));
            ok(false, "fichier illisible : refusé avec un message", "accepté ?");
        }
        catch (Exception e) { ok(e is InvalidDataException && e.Message.Contains("abime"), "fichier illisible : refusé avec un message", e.Message); }

        // ---------- un enregistrement coupé dont l'en-tête annonce encore 6 s (il n'en reste que 2) : en réencodage, erreur claire
        // plutôt qu'une image figée pendant 4 s
        var coupe = Path.Combine(dossier, "coupe.mkv");
        File.WriteAllBytes(coupe, File.ReadAllBytes(A).Take((int)(new FileInfo(A).Length * 0.5)).ToArray());
        x.Sequences[0] = new SequenceMontage { Source = coupe, Debut = 0, Fin = 6, DureeSource = 6 };
        x.Sortie.Mode = "exact";
        using (var r = await RenduMontage.PreparerAsync(x, ffmpeg, Path.Combine(dossier, "travail7")))
        {
            r.Demarrer(Path.Combine(dossier, "coupe-sortie.mp4"));
            bool reussi = await r.Termine.WaitAsync(TimeSpan.FromMinutes(2));
            ok(!reussi && r.Erreur != null && r.Erreur.Contains("avant la fin prévue"), "enregistrement coupé : erreur claire au lieu d'une image figée", r.Erreur);
        }
        try { Directory.Delete(dossier, true); } catch (Exception) { }
    }
}
