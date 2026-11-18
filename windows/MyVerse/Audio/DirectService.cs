using System.Reflection;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

public enum EtatDirect { Arrete, Preparation, EnDirect, Reconnexion, Arret, Erreur }

/// <summary>
/// Siloé Stream 1.6.0 : le direct. L'image de MyStream (la même que Zoom) et le son de « Où va le son › YouTube » partent vers YouTube
/// (ou un autre service) par ffmpeg, en RTMPS. Comme OBS : si Internet coupe, Siloé Stream se reconnecte tout seul ; avec le compte
/// connecté, Siloé Stream crée le direct sur YouTube, le démarre, le suit (en direct, santé de l'image) et le termine.
/// </summary>
public sealed class DirectService : IDisposable
{
    readonly ChurchAudioService audio;
    readonly Donnees donnees;
    public Coffre Coffre { get; }
    public YouTubeCompte Compte { get; }
    public EtatDirect Etat { get; private set; } = EtatDirect.Arrete;
    /// <summary>Ce qui se passe, en mots (« En direct sur YouTube », « Reconnexion… (essai 2) »).</summary>
    public string Message { get; private set; } = "";
    /// <summary>L'état du direct chez YouTube (compte connecté) : « en attente », « en direct », « terminé »…</summary>
    public string StatutYouTube { get; private set; } = "";
    /// <summary>La santé de l'image reçue par YouTube : « good », « ok », « bad », « noData » (compte connecté).</summary>
    public string Sante { get; private set; } = "";
    public DateTime? Debut { get; private set; }
    public int Reconnexions { get; private set; }
    /// <summary>Le direct YouTube en cours (compte connecté) : son identifiant, qui est aussi celui de la vidéo.</summary>
    public string? Video { get; private set; }
    public string Encodeur { get; private set; } = "";
    public event Action? Change;

    RecordingSession? session;
    /// <summary>Quand la session actuelle a démarré, et si une session a déjà tenu (plus de 15 s).</summary>
    long sessionDepuis;
    bool aTenu;
    Action<float[], long>? consommateur;
    int piste;
    string? adresse;
    string? flux;
    RecordingProfile? profil;
    CancellationTokenSource? fin;
    readonly SemaphoreSlim operation = new(1, 1);
    static readonly Dictionary<string, string> encodeurs = new();

    public DirectService(ChurchAudioService audio, Donnees donnees)
    {
        this.audio = audio; this.donnees = donnees;
        Coffre = new Coffre(donnees.Dossier);
        Compte = new YouTubeCompte(Coffre, Identifiants);
        _ = ChargerIdentifiantsSiteAsync();
    }

    ReglagesDirect R => donnees.Reglages.Direct;
    public bool Actif => Etat is EtatDirect.Preparation or EtatDirect.EnDirect or EtatDirect.Reconnexion or EtatDirect.Arret;
    public bool EnDirect => Etat is EtatDirect.EnDirect or EtatDirect.Reconnexion;
    public TimeSpan Duree => Debut is { } d ? DateTime.Now - d : TimeSpan.Zero;

    /// <summary>
    /// L'identifiant Google : celui des réglages avancés du direct s'il y en a un, sinon (2.4.2) celui gardé sur siloestream.tech
    /// (api/config.php, sur Hostinger : rien à retaper après une réinstallation), sinon celui donné à la compilation.
    /// </summary>
    public (string Id, string Secret) Identifiants()
    {
        var id = Coffre.Lire("google.id").Trim();
        if (id.Length > 0) return (id, Coffre.Lire("google.secret").Trim());
        if (site is { } s) return s;
        var sid = Coffre.Lire("google.site.id").Trim();
        if (sid.Length > 0) return (sid, Coffre.Lire("google.site.secret").Trim());
        var meta = typeof(DirectService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        return (meta.FirstOrDefault(x => x.Key == "YouTubeClientId")?.Value ?? "", meta.FirstOrDefault(x => x.Key == "YouTubeClientSecret")?.Value ?? "");
    }

    static (string Id, string Secret)? site;
    static readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public const string AdresseIdentifiants = "https://siloestream.tech/api/google.php";

    /// <summary>2.4.2 : lit l'identifiant Google gardé sur le site (et le garde chiffré sur ce PC, pour quand Internet manque).</summary>
    public async Task ChargerIdentifiantsSiteAsync()
    {
        if (site != null) return;
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, AdresseIdentifiants);
            req.Headers.Add("X-Siloe-Stream", "1");
            using var r = await http.SendAsync(req);
            if (!r.IsSuccessStatusCode) return;
            using var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            string id = j.RootElement.TryGetProperty("id", out var a) ? a.GetString()?.Trim() ?? "" : "";
            string secret = j.RootElement.TryGetProperty("secret", out var b) ? b.GetString()?.Trim() ?? "" : "";
            if (id.Length == 0 || secret.Length == 0) return;
            site = (id, secret);
            Coffre.Ecrire("google.site.id", id);
            Coffre.Ecrire("google.site.secret", secret);
        }
        catch (Exception e) { Journal.Info("identifiant Google du site : " + e.Message); }
    }

    void Statut(EtatDirect e, string message)
    {
        Etat = e; Message = adresse != null ? AdresseDirect.Masquer(message, adresse) : message;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Change?.Invoke()));
    }

    // ------------------------------------------------------------ démarrer
    /// <summary>Clé de stream ou autre service : l'adresse vient des réglages et du coffre.</summary>
    /// <param name="qualite">« 720p », « 1080p », « 2160p » ; null : celle des réglages (un direct automatique peut avoir la sienne).</param>
    public Task DemarrerAvecCleAsync(string? qualite = null)
    {
        var a = AdresseDirect.Construire(R, Coffre);
        if (a == null) { Statut(EtatDirect.Erreur, R.Mode == ReglagesDirect.Autre ? "Donnez l'adresse du serveur (rtmp:// ou rtmps://) et la clé." : "Collez d'abord la clé de stream de YouTube Studio."); return Task.CompletedTask; }
        return DemarrerAsync(a, null, null, qualite);
    }

    /// <summary>
    /// « Créer et démarrer le direct » (ou seulement le créer, pour plus tard) : rend l'identifiant du direct YouTube.
    /// <paramref name="qualite"/> : celle d'un direct automatique (null : celle des réglages) ; <paramref name="garder"/> : faux pour
    /// un direct automatique (ses réglages ne remplacent pas ceux retenus dans la fenêtre du direct).
    /// </summary>
    public async Task<string?> CreerAsync(ModeleDirect m, DateTimeOffset? plusTard, string? qualite = null, bool garder = true)
    {
        if (!await operation.WaitAsync(0)) return null;
        try
        {
            if (Actif) return null;
            Statut(EtatDirect.Preparation, "Création du direct sur YouTube…");
            // YouTube ne diffuse le 4K qu'en latence normale (sinon il le ramène à 1080p)
            if ((qualite ?? R.Qualite) == "2160p" && m.Latence != "normal") { m.Latence = "normal"; Journal.Info("direct : 4K, latence mise à normale"); }
            var id = await Compte.CreerDirectAsync(m, plusTard ?? DateTimeOffset.Now);
            var avertissements = new List<string>();
            try { await Compte.CategorieAsync(id, m); } catch (ErreurYouTube e) { avertissements.Add("catégorie : " + e.Message); }
            if (m.Miniature.Length > 0)
            {
                try { await Compte.MiniatureAsync(id, m.Miniature); } catch (Exception e) when (e is ErreurYouTube or IOException) { avertissements.Add(e.Message); }
            }
            var f = await Compte.FluxAsync(R.Flux);
            R.Flux = f.Id;
            await Compte.LierAsync(id, f.Id);
            R.DernierDirect = id;
            if (garder && R.Retenir)
            {
                var garde = AudioConfiguration.Copy(m);
                R.Modele = garde;
            }
            donnees.SauverReglages();
            if (avertissements.Count > 0) Journal.Info("direct créé, avec remarques : " + string.Join(" | ", avertissements));
            if (plusTard != null)
            {
                Statut(EtatDirect.Arrete, $"Direct programmé le {plusTard.Value.LocalDateTime:d MMMM 'à' HH:mm}. Le jour venu : « Direct déjà prévu », puis Démarrer."
                    + (avertissements.Count > 0 ? " " + string.Join(" ", avertissements) : ""));
                return id;
            }
            operation.Release();
            try { await DemarrerAsync(AdresseDirect.Pour(f), id, f.Id, qualite); }
            finally { await operation.WaitAsync(); }
            if (avertissements.Count > 0 && EnDirect) Statut(Etat, Message + " · " + string.Join(" ", avertissements));
            return id;
        }
        catch (Exception e) when (e is ErreurYouTube or HttpRequestException or TaskCanceledException or IOException)
        {
            Statut(EtatDirect.Erreur, Simple(e));
            Journal.Erreur("direct YouTube : création", e);
            return null;
        }
        finally { operation.Release(); }
    }

    /// <summary>Démarre un direct déjà prévu sur YouTube (lié au flux de Siloé Stream) ; <paramref name="qualite"/> : null pour celle des réglages.</summary>
    public async Task DemarrerPrevuAsync(DirectPrevu d, string? qualite = null)
    {
        if (!await operation.WaitAsync(0)) return;
        FluxYouTube f;
        try
        {
            if (Actif) return;
            Statut(EtatDirect.Preparation, $"Préparation de « {d.Titre} »…");
            f = await Compte.FluxAsync(R.Flux);
            R.Flux = f.Id;
            if (d.FluxLie != f.Id) await Compte.LierAsync(d.Id, f.Id);
            R.DernierDirect = d.Id;
            donnees.SauverReglages();
        }
        catch (Exception e) when (e is ErreurYouTube or HttpRequestException or TaskCanceledException)
        {
            Statut(EtatDirect.Erreur, Simple(e));
            Journal.Erreur("direct YouTube : direct prévu", e);
            return;
        }
        finally { operation.Release(); }
        await DemarrerAsync(AdresseDirect.Pour(f), d.Id, f.Id, qualite);
    }

    static string Simple(Exception e) => e switch
    {
        ErreurYouTube y => y.Message,
        HttpRequestException or TaskCanceledException => "YouTube ne répond pas : vérifiez la connexion Internet.",
        _ => e.Message,
    };

    /// <summary>Envoie MyStream et le son vers <paramref name="vers"/> (RTMP ou RTMPS) ; <paramref name="qualite"/> : null pour celle des réglages.</summary>
    public async Task DemarrerAsync(string vers, string? video, string? idFlux, string? qualite = null)
    {
        if (!await operation.WaitAsync(0)) return;
        try
        {
            if (Etat is EtatDirect.EnDirect or EtatDirect.Reconnexion or EtatDirect.Arret) return;
            adresse = vers; Video = video; flux = idFlux; Reconnexions = 0; StatutYouTube = ""; Sante = ""; aTenu = false;
            Statut(EtatDirect.Preparation, "Préparation du direct…");
            if (!audio.Running) throw new IOException("le moteur du son n'est pas démarré (le direct a besoin du son).");
            if (!audio.TracksActive) throw new IOException("cet ordinateur ne peut pas mélanger le son pour le direct (pilote de la carte graphique).");
            if (!File.Exists(audio.Ffmpeg)) throw new IOException("le programme d'envoi (dossier « ffmpeg » de Siloé Stream) est introuvable : réinstallez Siloé Stream.");
            var moteur = App.MyStream ?? throw new IOException("MyStream n'a pas démarré (voir Réglages › Journal).");
            var q = qualite ?? R.Qualite;
            var (l, h, kbps) = ReglagesDirect.TailleDe(q);
            moteur.ChangerTaille(l, h);
            moteur.Direct(true);
            moteur.Attendre();
            Encodeur = await EncodeurAsync(audio.Ffmpeg, l, h);
            if (q == "2160p" && Encodeur == "libx264")
                throw new IOException("le 4K demande une carte graphique capable d'encoder (NVIDIA, Intel Quick Sync ou AMD) : choisissez 1080p dans « Image et son du direct »");
            profil = new RecordingProfile
            {
                Id = AudioConfiguration.IdDirect, Name = "Direct", Format = RecordingProfile.FormatDirect, AudioOnly = false, Directory = "",
                Width = l, Height = h, Fps = 30, VideoKbps = kbps, AudioKbps = 160, Encoder = Encodeur,
            };
            audio.DestinationsActives.Add(AudioConfiguration.IdDirect);
            audio.Apply(false);
            await OuvrirSessionAsync();
            Debut = DateTime.Now;
            Statut(EtatDirect.EnDirect, Video != null ? "Envoi en cours : YouTube démarre le direct dès qu'il reçoit l'image…" : "En direct : l'image et le son partent.");
            fin = new CancellationTokenSource();
            var jeton = fin.Token;
            _ = Task.Run(() => SurveillerAsync(jeton));
        }
        catch (Exception e)
        {
            await FermerSessionAsync(false);
            App.MyStream?.Direct(false);
            audio.DestinationsActives.Remove(AudioConfiguration.IdDirect);
            audio.Apply(false);
            Statut(EtatDirect.Erreur, "Impossible de démarrer le direct : " + Simple(e).TrimEnd('.') + ".");
            Journal.Erreur("direct : démarrage", new IOException(AdresseDirect.Masquer(e.Message, adresse)));
        }
        finally { operation.Release(); }
    }

    /// <summary>Une session ffmpeg (au démarrage, puis à chaque reconnexion).</summary>
    async Task OuvrirSessionAsync()
    {
        var s = new RecordingSession(profil!, adresse!, audio.Ffmpeg, Encodeur, App.MyStream);
        try
        {
            await s.PrepareAsync();
            var protection = new ProtectionSaturation(AudioBlock.Rate);
            var copie = new float[AudioBlock.Samples];
            var c = new Action<float[], long>((bloc, instant) =>
            {
                if (bloc.Length != copie.Length) return;
                Array.Copy(bloc, copie, copie.Length);
                protection.Traiter(copie, copie.Length);
                Volatile.Read(ref session)?.Accept(copie, instant);
            });
            Volatile.Write(ref session, s);
            sessionDepuis = Environment.TickCount64;
            consommateur = c;
            piste = audio.PisteDe(AudioConfiguration.IdDirect);
            audio.AddTrackConsumer(piste - 1, c);
        }
        catch { s.Dispose(); throw; }
    }

    /// <summary>Ferme la session ffmpeg (proprement : fin du flux FLV) ; <paramref name="vite"/> : sans attendre (Internet coupé).</summary>
    async Task FermerSessionAsync(bool proprement)
    {
        if (consommateur != null && piste > 0) audio.RemoveTrackConsumer(piste - 1, consommateur);
        consommateur = null; piste = 0;
        var s = Interlocked.Exchange(ref session, null);
        if (s == null) return;
        try { if (proprement) await s.StopAsync().WaitAsync(TimeSpan.FromSeconds(12)); }
        catch (Exception e) { Journal.Info("direct : fin de session : " + AdresseDirect.Masquer(e.Message, adresse)); }
        finally { s.Dispose(); }
    }

    /// <summary>
    /// Le direct tourne : une session en erreur (Internet coupé, YouTube injoignable) est refaite après 2, 4, 8 puis 10 s, sans fin,
    /// jusqu'à « Arrêter ». Avec le compte connecté, l'état chez YouTube est suivi (et le direct passe en « live » s'il attend).
    /// </summary>
    async Task SurveillerAsync(CancellationToken jeton)
    {
        long prochainYouTube = 0, essai = 0;
        while (!jeton.IsCancellationRequested)
        {
            try { await Task.Delay(500, jeton); } catch (OperationCanceledException) { return; }
            try
            {
                // le son du direct a changé de piste (sons choisis modifiés pendant le direct)
                int p = audio.PisteDe(AudioConfiguration.IdDirect);
                if (consommateur != null && p != piste && p > 0)
                {
                    audio.RemoveTrackConsumer(piste - 1, consommateur);
                    piste = p;
                    audio.AddTrackConsumer(piste - 1, consommateur);
                }
                var s = Volatile.Read(ref session);
                if (s != null && s.Error == null && !aTenu && Environment.TickCount64 - sessionDepuis > 15_000) aTenu = true;
                if (s == null || s.Error != null)
                {
                    if (s != null) Journal.Info("direct coupé : " + AdresseDirect.Masquer(s.Error ?? "", adresse));
                    await FermerSessionAsync(false);
                    // refusé dès le départ (mauvaise clé, pas d'Internet) : on le dit au lieu de réessayer sans fin
                    if (!aTenu && Reconnexions == 0)
                    {
                        var refus = Video != null
                            ? "YouTube n'a pas accepté l'image : vérifiez la connexion Internet, puis réessayez."
                            : "Le serveur refuse l'image : vérifiez la clé de stream (et la connexion Internet), puis réessayez.";
                        _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            await ArreterAsync(false);
                            Statut(EtatDirect.Erreur, refus);
                        }));
                        return;
                    }
                    essai++;
                    Reconnexions++;
                    int attente = (int)Math.Min(10, Math.Pow(2, Math.Min(essai, 4)));
                    Statut(EtatDirect.Reconnexion, $"Connexion perdue : reconnexion dans {attente} s (essai {essai})…");
                    await Task.Delay(attente * 1000, jeton);
                    await operation.WaitAsync(jeton);
                    try
                    {
                        if (jeton.IsCancellationRequested) return;
                        await OuvrirSessionAsync();
                        essai = 0;
                        Statut(EtatDirect.EnDirect, "Reconnecté : le direct continue.");
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Statut(EtatDirect.Reconnexion, "Reconnexion impossible pour l'instant : " + Simple(e));
                    }
                    finally { operation.Release(); }
                    continue;
                }
                if (Video != null && Environment.TickCount64 >= prochainYouTube)
                {
                    bool live = StatutYouTube == "live";
                    prochainYouTube = Environment.TickCount64 + (live ? 60_000 : 10_000);
                    await SuivreYouTubeAsync(jeton);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Journal.Erreur("direct : surveillance", new IOException(AdresseDirect.Masquer(e.Message, adresse))); }
        }
    }

    /// <summary>L'état du direct chez YouTube ; un direct qui attend (pas de démarrage automatique) passe en « live » dès que l'image arrive.</summary>
    async Task SuivreYouTubeAsync(CancellationToken jeton)
    {
        try
        {
            var d = await Compte.DirectAsync(Video!, jeton);
            if (d == null) { StatutYouTube = "absent"; Statut(Etat, "Ce direct n'existe plus sur YouTube : l'image part toujours (Arrêter pour finir)."); return; }
            if (flux != null) { var (st, sante) = await Compte.EtatFluxAsync(flux, jeton); Sante = sante; if (st == "active" && d.Statut is "ready" or "testing") await Compte.TransitionAsync(d.Id, "live", jeton); }
            var avant = StatutYouTube;
            StatutYouTube = d.Statut;
            if (d.Statut == "live" && avant != "live" && Etat == EtatDirect.EnDirect) Statut(EtatDirect.EnDirect, "En direct sur YouTube.");
            else if (d.Statut is "complete" or "revoked") Statut(Etat, "YouTube a terminé ce direct : l'image ne s'y voit plus (Arrêter).");
            else System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Change?.Invoke()));
        }
        catch (ErreurYouTube e) { Journal.Info("suivi YouTube : " + e.Message); }
        catch (HttpRequestException) { }
    }

    // ------------------------------------------------------------ arrêter
    /// <summary>Arrête le direct : l'image s'arrête, puis le direct YouTube est terminé (compte connecté).</summary>
    public async Task ArreterAsync(bool terminerYouTube = true)
    {
        fin?.Cancel();
        await operation.WaitAsync();
        try
        {
            if (Etat is EtatDirect.Arrete or EtatDirect.Erreur && session == null) return;
            var duree = Duree;
            Statut(EtatDirect.Arret, "Fin du direct…");
            await FermerSessionAsync(true);
            App.MyStream?.Direct(false);
            audio.DestinationsActives.Remove(AudioConfiguration.IdDirect);
            audio.Apply(false);
            string fini = $"Direct terminé ({(int)duree.TotalHours}:{duree.Minutes:00}:{duree.Seconds:00}).";
            if (Video != null && terminerYouTube)
            {
                try { await Compte.TransitionAsync(Video, "complete").WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception e) when (e is ErreurYouTube or HttpRequestException or TimeoutException or TaskCanceledException)
                {
                    fini += " YouTube le terminera de lui-même (" + Simple(e).TrimEnd('.') + ").";
                }
            }
            Debut = null; Video = null; flux = null; StatutYouTube = ""; Sante = "";
            Statut(EtatDirect.Arrete, fini);
            adresse = null;
        }
        finally { operation.Release(); }
    }

    // ------------------------------------------------------------ l'encodeur
    /// <summary>L'encodeur vidéo du direct : la carte graphique (NVIDIA, Intel, AMD) si elle marche, sinon x264 ; essayé pour de vrai, une fois.</summary>
    static async Task<string> EncodeurAsync(string ffmpeg, int l, int h)
    {
        string cle = ffmpeg + "|" + l + "x" + h;
        lock (encodeurs) if (encodeurs.TryGetValue(cle, out var connu)) return connu;
        foreach (var e in new[] { "h264_nvenc", "h264_qsv", "h264_amf", "libx264" })
        {
            try
            {
                var args = new List<string> { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", $"color=c=black:s={l}x{h}:r=30", "-frames:v", "3", "-c:v", e, "-pix_fmt", "yuv420p", "-b:v", "3000k" };
                args.AddRange(RecordingSession.OptionsDirect(e));
                args.AddRange(new[] { "-f", "null", "-" });
                await ChurchRecordingService.TestAsync(ffmpeg, args);
                lock (encodeurs) encodeurs[cle] = e;
                return e;
            }
            catch (Exception) when (e != "libx264") { }
        }
        return "libx264";
    }

    /// <summary>Fermeture de Siloé Stream : le flux se termine proprement et le direct YouTube est terminé (15 s au plus).</summary>
    public void Dispose()
    {
        fin?.Cancel();
        if (consommateur != null && piste > 0) audio.RemoveTrackConsumer(piste - 1, consommateur);
        consommateur = null;
        var s = Interlocked.Exchange(ref session, null);
        var video = Video;
        if (s == null && video == null) return;
        try
        {
            Task.Run(async () =>
            {
                if (s != null) { try { await s.StopAsync().WaitAsync(TimeSpan.FromSeconds(8)); } catch (Exception) { } s.Dispose(); }
                if (video != null) { try { await Compte.TransitionAsync(video, "complete").WaitAsync(TimeSpan.FromSeconds(6)); } catch (Exception) { } }
            }).Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception) { }
    }
}
