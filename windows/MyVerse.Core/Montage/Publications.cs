using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MyVerse.Audio;

namespace MyVerse.Montages;

/// <summary>
/// Une publication (envoi sur YouTube) ou un export (fichier) d'un montage. Elle tourne en arrière-plan : fermer la fenêtre de
/// publication ne l'arrête pas. Son état est gardé sur le disque (reprise de l'envoi après une coupure ou une fermeture).
/// </summary>
public sealed class Publication
{
    public const string YouTube = "youtube", Export = "fichier";
    [JsonPropertyName("id")] public string Id { get; set; } = ProjetMontage.NouvelId();
    [JsonPropertyName("type")] public string Type { get; set; } = YouTube;
    [JsonPropertyName("titre")] public string Titre { get; set; } = "";
    [JsonPropertyName("debut")] public DateTime Debut { get; set; } = DateTime.Now;
    [JsonPropertyName("fin")] public DateTime? Fin { get; set; }
    /// <summary>« preparation », « rendu », « envoi », « termine », « erreur », « annule » ou « interrompu ».</summary>
    [JsonPropertyName("statut")] public string Statut { get; set; } = "preparation";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("avertissements")] public List<string> Avertissements { get; set; } = new();
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("videoId")] public string VideoId { get; set; } = "";
    [JsonPropertyName("session")] public string Session { get; set; } = "";
    [JsonPropertyName("mime")] public string Mime { get; set; } = "video/mp4";
    [JsonPropertyName("infos")] public InfosVideo? Infos { get; set; }
    [JsonPropertyName("miniature")] public string Miniature { get; set; } = "";
    [JsonPropertyName("playlist")] public string Playlist { get; set; } = "";
    /// <summary>Export : le fichier écrit.</summary>
    [JsonPropertyName("fichier")] public string Fichier { get; set; } = "";
    /// <summary>La copie gardée sur le PC (facultative), et si elle est complète.</summary>
    [JsonPropertyName("copie")] public string Copie { get; set; } = "";
    [JsonPropertyName("copieComplete")] public bool CopieComplete { get; set; }
    [JsonPropertyName("octetsEnvoyes")] public long OctetsEnvoyes { get; set; }
    [JsonPropertyName("duree")] public double DureeMontage { get; set; }

    /// <summary>Le rendu (0 à 1).</summary>
    [JsonIgnore] public double ProgressionRendu { get; set; }
    /// <summary>Octets rendus (taille du résultat jusqu'ici).</summary>
    [JsonIgnore] public long OctetsRendus { get; set; }
    /// <summary>Le débit de l'envoi (octets par seconde, moyenne glissante).</summary>
    [JsonIgnore] public double Debit { get; set; }
    /// <summary>L'état du réseau (« Internet coupé : nouvel essai dans 8 s… »), vide quand tout va bien.</summary>
    [JsonIgnore] public string Reseau { get; set; } = "";
    [JsonIgnore] public bool EnCours => Statut is "preparation" or "rendu" or "envoi";
    [JsonIgnore] public bool PeutReprendre => !EnCours && Type == YouTube && VideoId.Length == 0 && CopieComplete && File.Exists(Copie) && Infos != null;
    [JsonIgnore] public string Lien => VideoId.Length > 0 ? "https://youtu.be/" + VideoId : "";
    [JsonIgnore] public string LienStudio => VideoId.Length > 0 ? $"https://studio.youtube.com/video/{VideoId}/edit" : "";

    [JsonIgnore] internal CancellationTokenSource? Annulation;
    /// <summary>Un changement (état, progression) : appelé depuis un fil de travail.</summary>
    public event Action? Change;

    internal void Notifier() { try { Change?.Invoke(); } catch (Exception e) { Journal.Erreur("publication (affichage)", e); } }
    internal void Etat(string statut, string message) { Statut = statut; Message = message; Notifier(); }
    internal void Avertir(string texte) { lock (Avertissements) Avertissements.Add(texte); Journal.Info("publication : " + texte); Notifier(); }

    /// <summary>Arrête la publication (le rendu et l'envoi ; rien n'est publié).</summary>
    public void Annuler() { try { Annulation?.Cancel(); } catch (ObjectDisposedException) { } }
}

/// <summary>Les publications et exports en cours et récents (gardés dans « montage/publications.json »).</summary>
public static class Publications
{
    static readonly List<Publication> liste = new();
    static string fichier = "";
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    /// <summary>La liste a changé (une publication ajoutée, finie, retirée) : appelé depuis n'importe quel fil.</summary>
    public static event Action? Change;

    public static IReadOnlyList<Publication> Toutes { get { lock (liste) return liste.ToList(); } }
    public static bool EnCours { get { lock (liste) return liste.Any(p => p.EnCours); } }

    /// <summary>Le dossier de travail d'une publication (petits fichiers de FFmpeg ; supprimé à la fin).</summary>
    public static string DossierTravail(string id) => Path.Combine(Path.GetTempPath(), "SiloeStream-montage", id);

    /// <summary>Relit les publications (au démarrage) ; celles qui tournaient quand Siloé Stream s'est fermé sont « interrompues ».</summary>
    public static void Charger(string dossierDonnees)
    {
        fichier = Path.Combine(dossierDonnees, "montage", "publications.json");
        lock (liste)
        {
            liste.Clear();
            try
            {
                if (File.Exists(fichier) && JsonSerializer.Deserialize<List<Publication>>(File.ReadAllText(fichier)) is { } l)
                    foreach (var p in l.Where(p => p != null))
                    {
                        p.Avertissements ??= new();
                        if (p.EnCours)
                        {
                            p.Statut = "interrompu";
                            p.Message = p.CopieComplete ? "Siloé Stream a été fermé pendant l'envoi : la copie est complète, l'envoi peut reprendre." : "Siloé Stream a été fermé pendant le rendu ou l'envoi.";
                        }
                        liste.Add(p);
                    }
            }
            catch (Exception e) { Journal.Erreur("publications (illisibles : remises à zéro)", e); }
        }
        // les dossiers de travail laissés par une fermeture brutale (nos petits fichiers seulement)
        try
        {
            var racine = Path.Combine(Path.GetTempPath(), "SiloeStream-montage");
            if (Directory.Exists(racine)) foreach (var d in Directory.GetDirectories(racine)) { try { Directory.Delete(d, true); } catch (Exception) { } }
        }
        catch (Exception) { }
    }

    public static void Sauver()
    {
        if (fichier.Length == 0) return;
        try
        {
            List<Publication> copie;
            lock (liste) copie = liste.TakeLast(40).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
            var tmp = fichier + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(copie, Options));
            File.Move(tmp, fichier, true);
        }
        catch (Exception e) { Journal.Erreur("publications (sauvegarde)", e); }
    }

    static Publication Ajouter(Publication p)
    {
        p.Annulation = new CancellationTokenSource();
        lock (liste) liste.Add(p);
        Sauver();
        Change?.Invoke();
        return p;
    }

    static void Terminer(Publication p, string statut, string message)
    {
        p.Fin = DateTime.Now;
        p.Reseau = "";
        p.Etat(statut, message);
        Sauver();
        Change?.Invoke();
    }

    /// <summary>Retire une publication finie de la liste (ses fichiers restent où ils sont).</summary>
    public static void Oublier(Publication p)
    {
        if (p.EnCours) return;
        lock (liste) liste.Remove(p);
        Sauver();
        Change?.Invoke();
    }

    /// <summary>Supprime la copie gardée sur le PC (seulement quand l'utilisateur le demande). Jamais un enregistrement d'origine.</summary>
    public static bool SupprimerCopie(Publication p)
    {
        if (p.EnCours || p.Copie.Length == 0) return false;
        try { if (File.Exists(p.Copie)) File.Delete(p.Copie); }
        catch (Exception e) { Journal.Erreur("suppression de la copie", e); return false; }
        p.Copie = ""; p.CopieComplete = false;
        Sauver();
        p.Notifier();
        return true;
    }

    public static string Go(long octets) => octets >= 1L << 30 ? $"{octets / (double)(1L << 30):0.0} Go" : $"{octets / (double)(1L << 20):0} Mo";

    static long Libre(string dossier)
    {
        try { return RecordingFiles.FreeSpace(dossier); } catch (Exception) { return long.MaxValue; }
    }

    static string NomLibre(string dossier, string nom, string extension)
    {
        string baseNom = RecordingFiles.SafeName(nom);
        for (int i = 0; i < 1000; i++)
        {
            var c = Path.Combine(dossier, baseNom + (i == 0 ? "" : " (" + (i + 1) + ")") + extension);
            if (!File.Exists(c)) return c;
        }
        return Path.Combine(dossier, baseNom + " " + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    static string MessageErreur(Exception e, RenduMontage? rendu)
    {
        if (rendu?.Erreur is { Length: > 0 } r && r != "Annulé.") return "Rendu : " + r;
        return e switch
        {
            ErreurYouTube y => y.Message,
            HttpRequestException => "Internet ne répond pas : " + e.Message,
            _ => e.Message,
        };
    }

    // ================================================================================================ YouTube
    /// <summary>
    /// Publie le montage sur YouTube : rendu (copie ou réencodage) et envoi en même temps. <paramref name="dossierCopie"/> : garder
    /// aussi une copie sur le PC (le rendu va alors à pleine vitesse dans le fichier, l'envoi le suit, et il peut reprendre plus tard) ;
    /// null : aucun fichier sur le disque, la vidéo part directement de FFmpeg vers YouTube.
    /// </summary>
    public static Publication PublierYouTube(ProjetMontage projet, InfosVideo infos, string? miniature, string? playlist, string? dossierCopie, YouTubeCompte compte, string ffmpeg)
    {
        var p = Ajouter(new Publication
        {
            Type = Publication.YouTube, Titre = infos.Titre, Infos = infos, Miniature = miniature ?? "", Playlist = playlist ?? "",
            Message = "Préparation…",
        });
        var copieProjet = projet.Copie();
        _ = Task.Run(() => ExecuterYouTube(p, copieProjet, infos, dossierCopie, compte, ffmpeg));
        return p;
    }

    /// <summary>Le progrès de l'envoi : octets confirmés par YouTube, et le débit.</summary>
    sealed class Suivi : IProgress<long>
    {
        readonly Publication p;
        readonly Queue<(DateTime T, long O)> points = new();
        public Suivi(Publication p) { this.p = p; }
        public void Report(long octets)
        {
            var maintenant = DateTime.UtcNow;
            lock (points)
            {
                points.Enqueue((maintenant, octets));
                while (points.Count > 2 && (maintenant - points.Peek().T).TotalSeconds > 20) points.Dequeue();
                var premier = points.Peek();
                double s = (maintenant - premier.T).TotalSeconds;
                p.Debit = s > 0.5 ? (octets - premier.O) / s : p.Debit;
            }
            p.OctetsEnvoyes = octets;
            p.Reseau = "";
            p.Notifier();
        }
    }

    static async Task ExecuterYouTube(Publication p, ProjetMontage projet, InfosVideo infos, string? dossierCopie, YouTubeCompte compte, string ffmpeg)
    {
        var jeton = p.Annulation!.Token;
        RenduMontage? rendu = null;
        CopieQuiGrandit? copie = null;
        using var suivi = new CancellationTokenSource();
        try
        {
            rendu = await RenduMontage.PreparerAsync(projet, ffmpeg, DossierTravail(p.Id), m => p.Etat("preparation", m), jeton);
            p.Description = rendu.Description;
            p.DureeMontage = rendu.Duree;
            p.Mime = rendu.TypeMime;
            foreach (var a in rendu.Avertissements) p.Avertir(a);

            if (dossierCopie != null)
            {
                Directory.CreateDirectory(dossierCopie);
                long libre = Libre(dossierCopie);
                if (libre < rendu.OctetsEstimes + (2L << 30))
                    p.Avertir($"Pas assez de place pour garder une copie sur le PC (il faut environ {Go(rendu.OctetsEstimes)}, il reste {Go(libre)}) : la vidéo part directement, sans copie.");
                else
                    p.Copie = NomLibre(dossierCopie, infos.Titre.Length > 0 ? infos.Titre : "Montage", rendu.ExtensionConseillee);
            }

            p.Etat("envoi", "Ouverture de l'envoi sur YouTube…");
            var envoi = new EnvoiYouTube(compte);
            envoi.Etat += m => { p.Reseau = m; p.Notifier(); };
            p.Session = await envoi.OuvrirSessionAsync(infos, rendu.TypeMime, null, jeton);
            Sauver();

            rendu.Demarrer(null);
            _ = SuivreRendu(p, rendu, suivi.Token);
            Stream source = rendu.Flux!;
            Func<Task<bool>> complet = () => rendu.Termine;
            if (p.Copie.Length > 0)
            {
                copie = new CopieQuiGrandit(source, p.Copie);
                source = copie.Lecteur();
                complet = async () => await copie.Termine && await rendu.Termine;
                _ = copie.Termine.ContinueWith(t =>
                {
                    if (t.Result && rendu.Termine.IsCompletedSuccessfully && rendu.Termine.Result) { p.CopieComplete = true; Sauver(); p.Notifier(); }
                }, TaskScheduler.Default);
            }
            p.Etat("envoi", rendu.Copie ? "Copie et envoi en cours…" : "Rendu et envoi en cours…");
            var video = await envoi.EnvoyerAsync(p.Session, source, 0, complet, new Suivi(p), jeton);
            if (copie != null) p.CopieComplete = await copie.Termine && await rendu.Termine;
            await Finir(p, video, compte, jeton);
        }
        catch (OperationCanceledException) when (jeton.IsCancellationRequested) { Terminer(p, "annule", "Annulé : rien n'a été publié."); }
        catch (Exception e)
        {
            Journal.Erreur("publication YouTube", e);
            Terminer(p, "erreur", MessageErreur(e, rendu) + (p.CopieComplete ? " La copie est complète : « Reprendre l'envoi » continue sans refaire le rendu." : ""));
        }
        finally
        {
            suivi.Cancel();
            rendu?.Dispose();
            copie?.Dispose();
            Sauver();
        }
    }

    static async Task SuivreRendu(Publication p, RenduMontage r, CancellationToken jeton)
    {
        try
        {
            while (!jeton.IsCancellationRequested && !r.Termine.IsCompleted)
            {
                p.ProgressionRendu = r.Progression;
                p.Notifier();
                await Task.WhenAny(r.Termine, Task.Delay(500, jeton));
            }
            p.ProgressionRendu = r.Termine.IsCompletedSuccessfully && r.Termine.Result ? 1 : r.Progression;
            p.Notifier();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Après l'envoi : la visibilité réelle, la miniature, la playlist.</summary>
    static async Task Finir(Publication p, JsonNode video, YouTubeCompte compte, CancellationToken jeton)
    {
        p.VideoId = video["id"]?.GetValue<string>() ?? "";
        if (p.VideoId.Length == 0) throw new ErreurYouTube("YouTube n'a pas donné l'identifiant de la vidéo.");
        Sauver();
        string visibilite = video["status"]?["privacyStatus"]?.GetValue<string>() ?? "";
        if (p.Infos is { PublierLe: null } i && i.Confidentialite != "private" && visibilite == "private")
            p.Avertir("YouTube a gardé la vidéo en privé : les vidéos envoyées par une application Google non vérifiée restent privées. Rendez-la publique dans YouTube Studio, ou faites vérifier l'application Google de l'église.");
        if (p.Miniature.Length > 0 && File.Exists(p.Miniature))
        {
            p.Etat("envoi", "Miniature…");
            try { await compte.MiniatureAsync(p.VideoId, p.Miniature, jeton); }
            catch (ErreurYouTube e) { p.Avertir("Miniature : " + e.Message); }
        }
        if (p.Playlist.Length > 0)
        {
            p.Etat("envoi", "Ajout à la playlist…");
            try { await compte.AjouterAPlaylistAsync(p.Playlist, p.VideoId, jeton); }
            catch (ErreurYouTube e) { p.Avertir("Playlist : " + e.Message); }
        }
        Terminer(p, "termine", p.Infos?.PublierLe is { } d ? $"Envoyée : publication prévue le {d.LocalDateTime:dddd d MMMM à HH:mm}." : "Publiée sur YouTube.");
    }

    /// <summary>Reprend l'envoi d'une publication interrompue à partir de sa copie complète (sans refaire le rendu).</summary>
    public static Publication? Reprendre(Publication ancienne, YouTubeCompte compte)
    {
        if (!ancienne.PeutReprendre) return null;
        lock (liste) liste.Remove(ancienne);
        ancienne.Fin = null;
        ancienne.Debut = DateTime.Now;
        ancienne.Statut = "envoi";
        ancienne.Message = "Reprise de l'envoi…";
        var p = Ajouter(ancienne);
        _ = Task.Run(() => ExecuterReprise(p, compte));
        return p;
    }

    static async Task ExecuterReprise(Publication p, YouTubeCompte compte)
    {
        var jeton = p.Annulation!.Token;
        try
        {
            await using var fs = new FileStream(p.Copie, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var envoi = new EnvoiYouTube(compte);
            envoi.Etat += m => { p.Reseau = m; p.Notifier(); };
            long depart = 0;
            JsonNode? video = null;
            if (p.Session.Length > 0)
            {
                try
                {
                    var e = await envoi.EtatSessionAsync(p.Session, fs.Length, jeton);
                    if (e.Fini) video = e.Video; else depart = e.Recus;
                }
                catch (ErreurYouTube e) when (e.Raison == "sessionExpiree") { p.Session = ""; }
            }
            if (video == null)
            {
                if (p.Session.Length == 0)
                {
                    p.Session = await envoi.OuvrirSessionAsync(p.Infos!, p.Mime, fs.Length, jeton);
                    depart = 0;
                    Sauver();
                }
                p.ProgressionRendu = 1;
                p.Etat("envoi", depart > 0 ? $"Reprise à {Go(depart)} sur {Go(fs.Length)}…" : "Envoi de la copie…");
                fs.Position = depart;
                video = await envoi.EnvoyerAsync(p.Session, fs, depart, null, new Suivi(p), jeton);
            }
            if (video == null) throw new ErreurYouTube("YouTube n'a pas décrit la vidéo.");
            await Finir(p, video, compte, jeton);
        }
        catch (OperationCanceledException) when (jeton.IsCancellationRequested) { Terminer(p, "annule", "Annulé : la copie reste sur le PC, l'envoi peut reprendre."); }
        catch (Exception e)
        {
            Journal.Erreur("reprise de l'envoi YouTube", e);
            Terminer(p, "erreur", MessageErreur(e, null));
        }
    }

    // ================================================================================================ fichier
    /// <summary>Exporte le montage dans un fichier (MP4, ou MKV si les enregistrements ne vont pas en MP4).</summary>
    public static Publication Exporter(ProjetMontage projet, string fichierSortie, string ffmpeg)
    {
        var p = Ajouter(new Publication { Type = Publication.Export, Titre = Path.GetFileNameWithoutExtension(fichierSortie), Fichier = fichierSortie, Message = "Préparation…" });
        var copieProjet = projet.Copie();
        _ = Task.Run(() => ExecuterExport(p, copieProjet, fichierSortie, ffmpeg));
        return p;
    }

    static async Task ExecuterExport(Publication p, ProjetMontage projet, string fichierSortie, string ffmpeg)
    {
        var jeton = p.Annulation!.Token;
        RenduMontage? rendu = null;
        try
        {
            rendu = await RenduMontage.PreparerAsync(projet, ffmpeg, DossierTravail(p.Id), m => p.Etat("preparation", m), jeton);
            p.Description = rendu.Description;
            p.DureeMontage = rendu.Duree;
            foreach (var a in rendu.Avertissements) p.Avertir(a);
            if (rendu.Conteneur == "matroska" && Path.GetExtension(fichierSortie).ToLowerInvariant() != ".mkv")
            {
                fichierSortie = Path.ChangeExtension(fichierSortie, ".mkv");
                p.Avertir("Ces enregistrements ne vont pas dans un MP4 sans réencodage : le fichier est un MKV.");
            }
            p.Fichier = fichierSortie;
            var dossier = Path.GetDirectoryName(Path.GetFullPath(fichierSortie))!;
            Directory.CreateDirectory(dossier);
            long libre = Libre(dossier);
            if (libre < rendu.OctetsEstimes / 2)
                throw new IOException($"Pas assez de place sur le disque : il faut environ {Go(rendu.OctetsEstimes)}, il reste {Go(libre)}.");
            if (libre < rendu.OctetsEstimes + (1L << 30))
                p.Avertir($"La place est juste sur le disque (environ {Go(rendu.OctetsEstimes)} à écrire, {Go(libre)} libres).");
            p.Etat("rendu", rendu.Copie ? "Copie sans réencodage…" : "Rendu…");
            rendu.Demarrer(fichierSortie);
            using (jeton.Register(() => rendu.Annuler()))
            {
                while (!rendu.Termine.IsCompleted)
                {
                    p.ProgressionRendu = rendu.Progression;
                    try { p.OctetsRendus = File.Exists(fichierSortie) ? new FileInfo(fichierSortie).Length : 0; } catch (Exception) { }
                    p.Notifier();
                    await Task.WhenAny(rendu.Termine, Task.Delay(500, CancellationToken.None));
                }
            }
            jeton.ThrowIfCancellationRequested();
            if (!await rendu.Termine) throw new IOException(rendu.Erreur ?? "Le rendu a échoué.");
            p.ProgressionRendu = 1;
            Terminer(p, "termine", "Exporté : " + Path.GetFileName(fichierSortie));
        }
        catch (OperationCanceledException) when (jeton.IsCancellationRequested) { Terminer(p, "annule", "Annulé." + (File.Exists(fichierSortie) ? " Le fichier incomplet est resté sur le disque." : "")); }
        catch (Exception e)
        {
            Journal.Erreur("export du montage", e);
            Terminer(p, "erreur", MessageErreur(e, rendu) + (File.Exists(fichierSortie) ? " Le fichier incomplet est resté sur le disque." : ""));
        }
        finally { rendu?.Dispose(); Sauver(); }
    }

    /// <summary>Supprime le fichier incomplet d'un export raté ou annulé (sur demande).</summary>
    public static bool SupprimerFichierIncomplet(Publication p)
    {
        if (p.EnCours || p.Type != Publication.Export || p.Statut == "termine" || p.Fichier.Length == 0) return false;
        try { if (File.Exists(p.Fichier)) File.Delete(p.Fichier); return true; }
        catch (Exception e) { Journal.Erreur("suppression de l'export incomplet", e); return false; }
    }
}

/// <summary>
/// La copie gardée sur le PC pendant la publication : la sortie de FFmpeg est écrite dans le fichier à pleine vitesse (le rendu
/// n'attend pas Internet), et l'envoi lit ce fichier pendant qu'il grandit.
/// </summary>
public sealed class CopieQuiGrandit : IDisposable
{
    readonly FileStream ecriture;
    readonly string chemin;
    long ecrits;
    volatile bool fini;
    readonly SemaphoreSlim signal = new(0, int.MaxValue);
    readonly CancellationTokenSource arret = new();
    public Task<bool> Termine { get; }
    public string? Erreur { get; private set; }

    public CopieQuiGrandit(Stream source, string chemin)
    {
        this.chemin = chemin;
        ecriture = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 1 << 20, FileOptions.Asynchronous);
        Termine = Task.Run(async () =>
        {
            var t = new byte[1 << 20];
            try
            {
                while (true)
                {
                    int n = await source.ReadAsync(t, arret.Token);
                    if (n <= 0) break;
                    await ecriture.WriteAsync(t.AsMemory(0, n), arret.Token);
                    await ecriture.FlushAsync(arret.Token);
                    Interlocked.Add(ref ecrits, n);
                    signal.Release();
                }
                return true;
            }
            catch (Exception e)
            {
                Erreur = e is IOException && (e.HResult & 0xFFFF) is 0x70 or 0x27 ? "Disque plein : la copie sur le PC s'est arrêtée." : e.Message;
                Journal.Erreur("copie du montage", e);
                return false;
            }
            finally
            {
                fini = true;
                try { await ecriture.FlushAsync(); } catch (Exception) { }
                signal.Release();
            }
        });
    }

    /// <summary>Un flux qui lit la copie pendant qu'elle s'écrit (il attend la suite, et finit quand la copie est finie).</summary>
    public Stream Lecteur() => new LecteurCopie(this);

    sealed class LecteurCopie : Stream
    {
        readonly CopieQuiGrandit c;
        readonly FileStream f;
        long position;
        public LecteurCopie(CopieQuiGrandit c)
        {
            this.c = c;
            f = new FileStream(c.chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.Asynchronous);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken annuler = default)
        {
            while (true)
            {
                long dispo = Interlocked.Read(ref c.ecrits) - position;
                if (dispo > 0)
                {
                    f.Position = position;
                    int n = await f.ReadAsync(buffer[..(int)Math.Min(buffer.Length, dispo)], annuler);
                    position += n;
                    if (n > 0) return n;
                }
                if (c.fini && Interlocked.Read(ref c.ecrits) <= position)
                {
                    if (!await c.Termine) throw new IOException(c.Erreur ?? "La copie sur le PC s'est arrêtée.");
                    return 0;
                }
                await c.signal.WaitAsync(TimeSpan.FromMilliseconds(250), annuler);
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) f.Dispose(); base.Dispose(disposing); }
    }

    public void Dispose()
    {
        try { arret.Cancel(); } catch (ObjectDisposedException) { }
        try { Termine.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        try { ecriture.Dispose(); } catch (Exception) { }
    }
}
