using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les caméras USB et les cartes de capture HDMI (Cam Link, « USB Video »…), partagées par l'écran, les calques, MyStream et l'enregistrement :
/// une seule ouverture par appareil, rendue quand plus personne ne s'en sert.
/// <para>
/// 1.6.5 : fluide. Les cartes de capture USB 2.0 (« USB Video ») ne donnent que 5 images par seconde en 1080p non compressé :
/// Siloé Stream demande le format compressé MJPG, aux images par seconde voulues (60 par défaut, ou 40, ou 30 : réglage de la source),
/// et mesure ce qui arrive vraiment. Si l'appareil n'y arrive pas en 1080p, il essaie 720p (les images par seconde d'abord),
/// sinon il garde le meilleur essai. Ce qui a marché est retenu pour la prochaine ouverture. Les images partent dès qu'elles arrivent.
/// </para>
/// <para>
/// 1.6.6 : quand plus personne ne s'en sert (un verset à l'écran), l'appareil reste ouvert 10 minutes, prêt (deux images par seconde
/// gardées fraîches) : la caméra revient tout de suite, sans écran noir ni attente.
/// </para>
/// </summary>
public static class CameraFrames
{
    static readonly object gate = new();
    static readonly Dictionary<string, Capture> captures = new();
    /// <summary>Le réglage qui a marché, par appareil et par demande (images par seconde, taille).</summary>
    static readonly Dictionary<string, Reglage> trouves = new();
    /// <summary>Les appareils dont on a déjà dit qu'ils donnent moins d'images que demandé (une fois par ouverture de Siloé Stream).</summary>
    static readonly HashSet<string> dits = new();
    /// <summary>Combien de temps un appareil reste ouvert, prêt, après sa dernière utilisation.</summary>
    const long Garde = 10 * 60 * 1000;

    /// <summary>Un réglage de l'appareil : la taille, le format compressé MJPG ou non, les images par seconde demandées et mesurées.</summary>
    public sealed record Reglage(int Largeur, int Hauteur, bool Mjpg, int Demande, double Mesure)
    {
        public override string ToString() => $"{Largeur} × {Hauteur} · {(Mjpg ? "MJPG" : "non compressé")} · {Math.Round(Mesure)} i/s";
    }

    public sealed class Lease : IDisposable
    {
        readonly Capture capture;
        readonly bool vignettes;
        bool disposed;
        internal Lease(Capture capture, bool vignettes)
        {
            this.capture = capture;
            this.vignettes = vignettes;
        }
        public BitmapSource? Image => Volatile.Read(ref capture.Image);
        /// <summary>1.7.0 : la même image en petit (384 px de large, 10 par seconde au plus), pour une tuile de la grille.</summary>
        public BitmapSource? Vignette => Volatile.Read(ref capture.Vignette);
        public string? Error => capture.Error;
        /// <summary>Finie quand la prochaine image arrive (pour la montrer tout de suite, sans attendre un tour de minuterie).</summary>
        public Task Prochaine => Volatile.Read(ref capture.Prochaine).Task;
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                if (vignettes) capture.Vignettes--;
                else capture.Pleines--;
                // l'appareil reste ouvert, prêt, pendant la garde (il revient tout de suite)
                if (--capture.Users == 0) capture.Libere = Environment.TickCount64;
            }
        }
    }

    internal sealed class Capture
    {
        public string Id = "";
        public int Users;
        public BitmapSource? Image;
        public BitmapSource? Vignette;
        /// <summary>Parts qui veulent aussi la vignette (tuiles de la grille) ; la dernière vignette faite (horloge).</summary>
        public int Vignettes;
        public long DerniereVignette;
        /// <summary>1.8.0 : parts qui montrent l'image en grand (écran, MyStream, enregistrement).</summary>
        public int Pleines;
        public volatile string? Error;
        public readonly CancellationTokenSource Cancel = new();
        public TaskCompletionSource Prochaine = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Ce qui est demandé (images par seconde, taille) et sa version : quand ça change, l'appareil se rouvre avec.</summary>
        public int Ips = 60;
        public string Taille = "1080p";
        public int Version;
        /// <summary>Ce que donne l'appareil en ce moment (« 1280 × 720 · MJPG · 60 i/s »).</summary>
        public volatile string Info = "";
        /// <summary>Quand la dernière part a été rendue (l'appareil reste ouvert pendant la garde).</summary>
        public long Libere;
        /// <summary>Le fil de l'appareil est fini (garde passée, erreur) : une nouvelle demande rouvre l'appareil.</summary>
        public bool Fini;
    }

    /// <summary>Ce que donne l'appareil en ce moment (« 1920 × 1080 · MJPG · 60 i/s ») ; vide s'il n'est pas ouvert.</summary>
    public static string Info(string id) { lock (gate) return captures.TryGetValue(id, out var c) ? c.Info : ""; }

    /// <summary>
    /// Une part de l'appareil <paramref name="id"/>. <paramref name="source"/> : la source qui le demande (ses images par seconde et sa taille) ;
    /// null (l'enregistrement) : comme il est déjà ouvert, sinon 60 images par seconde en 1080p.
    /// </summary>
    public static Lease Acquire(string id, SourceDisplay? source = null, bool vignettes = false)
    {
        lock (gate)
        {
            int ips = source?.IpsVoulues ?? 60;
            string taille = source?.TailleVoulue ?? "1080p";
            if (captures.TryGetValue(id, out var capture) && !capture.Fini)
            {
                // la source a changé ses images par seconde ou sa taille : l'appareil se rouvre avec (les autres parts suivent)
                if (source != null && (capture.Ips != ips || capture.Taille != taille)) { capture.Ips = ips; capture.Taille = taille; capture.Version++; }
            }
            else
            {
                var device = SourcesDisplay.Peripheriques(true).FirstOrDefault(d => d.Id == id) ?? throw new IOException("Caméra absente.");
                capture = new() { Id = id, Ips = ips, Taille = taille };
                captures[id] = capture;
                var state = capture;
                new Thread(() => Boucle(state, device)) { IsBackground = true, Name = "caméra " + device.Nom, Priority = ThreadPriority.AboveNormal }.Start();
            }
            capture.Users++;
            if (vignettes) capture.Vignettes++;
            else capture.Pleines++;
            return new(capture, vignettes);
        }
    }

    /// <summary>
    /// « Reconnecter la source » : les réglages trouvés sont oubliés et les appareils ouverts se rouvrent en cherchant de nouveau
    /// (une caméra réglée autrement entre-temps, en 60p par exemple).
    /// </summary>
    public static void Oublier()
    {
        lock (gate)
        {
            trouves.Clear();
            dits.Clear();
            foreach (var c in captures.Values) c.Version++;
        }
    }

    // ------------------------------------------------------------ le fil de l'appareil
    static void Boucle(Capture state, SourcesDisplay.Peripherique device)
    {
        // 2.5.1 : les attentes entre deux images à la milliseconde près (sinon Windows réveille le fil par pas de 15,6 ms)
        bool precision = Minuterie(true);
        try
        {
            int sansImage = 0, coupures = 0;
            while (!state.Cancel.IsCancellationRequested)
            {
                int version, ips; string taille;
                lock (gate) { version = state.Version; ips = state.Ips; taille = state.Taille; }
                // 2.5.1 : deux ouvertures de suite sans aucune image avec le réglage retenu (le signal HDMI a changé : la caméra est passée
                // du 4K au 1080p, ou de 60 à 30 images par seconde) : le bon réglage est cherché de nouveau
                if (sansImage >= 2) { lock (gate) trouves.Remove(device.Id + "|" + ips + "|" + taille); sansImage = 0; }
                using var camera = Ouvrir(device, ips, taille, state.Cancel.Token, out var r);
                state.Info = r.ToString();
                if (coupures == 0) Journal.Info($"caméra « {device.Nom} » : {r} (demandé : {ips} i/s, {taille})");
                Prevenir(device, ips, r);
                Conseiller(device, r);
                var fin = Lire(state, camera, ips, version);
                if (fin == FinLecture.Changement) { sansImage = coupures = 0; continue; }
                // 2.5.1 : une coupure (plus d'image depuis 3 s alors qu'on s'en sert) : l'appareil est rouvert ; la dernière image reste
                // à l'écran, sur Zoom et sur YouTube en attendant (pas de noir)
                sansImage = fin == FinLecture.SansImage ? sansImage + 1 : 0;
                if (coupures++ % 20 == 0)
                    Journal.Info($"caméra « {device.Nom} » : plus d'image depuis {Coupure / 1000} s (câble HDMI, caméra éteinte ou en veille ?) : Siloé Stream la rouvre");
                camera.Dispose();
                state.Cancel.Token.WaitHandle.WaitOne(300);
                // l'appareil a pu changer de place dans la liste de Windows (une autre caméra branchée entre-temps)
                try { if (SourcesDisplay.Peripheriques(true).FirstOrDefault(d => d.Id == device.Id) is { } maj) device = maj; }
                catch (Exception e) when (e is not OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { state.Error = e.Message; Journal.Erreur("caméra partagée", e); }
        finally
        {
            if (precision) Minuterie(false);
            state.Info = "";
            lock (gate)
            {
                state.Fini = true;
                if (captures.TryGetValue(state.Id, out var c) && c == state) captures.Remove(state.Id);
            }
            state.Cancel.Dispose();
        }
    }

    /// <summary>Pourquoi la lecture s'arrête : un autre réglage demandé (ou la fin), une coupure, ou aucune image depuis l'ouverture.</summary>
    enum FinLecture { Changement, Coupure, SansImage }

    /// <summary>2.5.1 : plus d'image depuis ce temps (ms) alors qu'on s'en sert : l'appareil est rouvert.</summary>
    const int Coupure = 3000;

    /// <summary>
    /// Lit les images et les publie, au plus <paramref name="ips"/> par seconde (40 d'une caméra à 60 : une sur trois est sautée).
    /// 2.5.1 : avec DirectShow (OpenCV), Grab répond tout de suite et c'est Retrieve qui attend l'image suivante (une seconde au plus) :
    /// entre deux images voulues, le fil dort jusqu'à la suivante. Avant, il tournait à vide : un cœur du processeur occupé à plein,
    /// en priorité haute, pris à Zoom, à l'encodeur du direct et à MyStream (la caméra coupait sur Internet).
    /// </summary>
    static FinLecture Lire(Capture state, VideoCapture camera, int ips, int version)
    {
        using var frame = new Mat();
        using var bgra = new Mat();
        double intervalle = 1000.0 / ips, tolerance = intervalle * 0.25, prochain = 0;
        var horloge = Stopwatch.StartNew();
        int ratees = 0;
        double libreDepuis = -1000, derniere = 0;
        bool recue = false;
        while (!state.Cancel.IsCancellationRequested && Volatile.Read(ref state.Version) == version)
        {
            // personne ne s'en sert : l'appareil est rendu après la garde (sous le verrou : une demande peut arriver en même temps)
            bool libre;
            lock (gate)
            {
                libre = state.Users == 0;
                if (libre && Environment.TickCount64 - state.Libere > Garde)
                {
                    state.Fini = true;
                    if (captures.TryGetValue(state.Id, out var c) && c == state) captures.Remove(state.Id);
                    Journal.Info($"caméra rendue après {Garde / 60000} minutes sans utilisation");
                    throw new OperationCanceledException();
                }
            }
            // Grab (DirectShow) répond tout de suite : faux seulement si l'appareil a disparu (débranché) ; Retrieve attend l'image
            if (!camera.Grab())
            {
                // coupée seulement après 3 essais de suite
                if (++ratees >= 3) throw new IOException("La caméra ne fournit plus d’image.");
                Thread.Sleep(50);
                continue;
            }
            ratees = 0;
            double t = horloge.Elapsed.TotalMilliseconds;
            bool pleines = Volatile.Read(ref state.Pleines) > 0;
            // le moment de la prochaine image voulue : en garde, deux par seconde (elle reste fraîche pour quand la caméra revient) ;
            // 1.8.0 : seulement des tuiles (vignettes), dix petites par seconde ; sinon les images par seconde demandées
            double attente = libre ? libreDepuis + 500 - t
                : !pleines ? state.DerniereVignette + 90 - Environment.TickCount64
                : prochain - tolerance - t;
            if (attente > 0)
            {
                // 2.5.1 : le fil dort jusque-là (Retrieve donnera alors la dernière image arrivée)
                Thread.Sleep((int)Math.Clamp(Math.Ceiling(attente), 1, 100));
                continue;
            }
            bool ok;
            if (libre)
            {
                libreDepuis = t;
                ok = Recuperer(camera, frame);
                if (ok) Publier(state, frame, bgra, true);
            }
            else if (!pleines)
            {
                ok = Recuperer(camera, frame);
                if (ok) Publier(state, frame, bgra, false);
            }
            else
            {
                prochain = Math.Max(prochain + intervalle, t);
                ok = Recuperer(camera, frame);
                if (ok) Publier(state, frame, bgra, true);
            }
            double apres = horloge.Elapsed.TotalMilliseconds;
            if (ok) { derniere = apres; recue = true; }
            // 2.5.1 : plus d'image depuis 3 s alors qu'on s'en sert (signal HDMI perdu ou changé, carte de capture figée) : à rouvrir
            else if (!libre && apres - derniere > Coupure) return recue ? FinLecture.Coupure : FinLecture.SansImage;
        }
        return FinLecture.Changement;
    }

    /// <summary>2.5.1 : la minuterie de Windows à la milliseconde pendant qu'un appareil est ouvert (les attentes entre les images).</summary>
    static bool Minuterie(bool active)
    {
        try { if (active) timeBeginPeriod(1); else timeEndPeriod(1); return true; }
        catch (Exception) { return false; }
    }
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    static bool Recuperer(VideoCapture camera, Mat frame) => camera.Retrieve(frame) && !frame.Empty();

    /// <summary>
    /// 1.8.0 : la plus grande image utile : 1920 de large (l'écran de l'église, MyStream en 1080p), 3840 si le direct part en 4K.
    /// Une caméra 4K montrée en 1080p ne fabrique plus des images de 33 Mo (soixante fois par seconde) : l'interface reste vive.
    /// </summary>
    public static Func<int> LargeurMax { get; set; } = () => 1920;

    static BitmapSource Bitmap(Mat m)
    {
        var b = BitmapSource.Create(m.Width, m.Height, 96, 96, PixelFormats.Bgra32, null, m.Data, (int)(m.Step() * m.Height), (int)m.Step());
        b.Freeze();
        return b;
    }

    static void Publier(Capture state, Mat frame, Mat bgra, bool pleine)
    {
        // 1.7.0 : la tuile de la grille reste en direct sans pousser l'image en grand vers l'écran
        if (Volatile.Read(ref state.Vignettes) > 0 && Environment.TickCount64 - state.DerniereVignette >= 90 && frame.Width > 0)
        {
            state.DerniereVignette = Environment.TickCount64;
            int l = Math.Min(384, frame.Width), h = Math.Max(1, (int)Math.Round(frame.Height * (double)l / frame.Width));
            using var petite = new Mat();
            using var petiteBgra = new Mat();
            Cv2.Resize(frame, petite, new OpenCvSharp.Size(l, h), 0, 0, InterpolationFlags.Area);
            if (petite.Channels() == 4) petite.CopyTo(petiteBgra); else Cv2.CvtColor(petite, petiteBgra, ColorConversionCodes.BGR2BGRA);
            Volatile.Write(ref state.Vignette, Bitmap(petiteBgra));
        }
        if (pleine)
        {
            int max = Math.Max(640, LargeurMax());
            if (frame.Width > max)
            {
                using var reduite = new Mat();
                Cv2.Resize(frame, reduite, new OpenCvSharp.Size(max, Math.Max(1, (int)Math.Round(frame.Height * (double)max / frame.Width))), 0, 0, InterpolationFlags.Area);
                if (reduite.Channels() == 4) reduite.CopyTo(bgra); else Cv2.CvtColor(reduite, bgra, ColorConversionCodes.BGR2BGRA);
            }
            else if (frame.Channels() == 4) frame.CopyTo(bgra);
            else Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
            Volatile.Write(ref state.Image, Bitmap(bgra));
        }
        var avant = Interlocked.Exchange(ref state.Prochaine, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        avant.TrySetResult();
    }

    // ------------------------------------------------------------ trouver le bon réglage
    /// <summary>
    /// Ouvre l'appareil au meilleur réglage pour <paramref name="ips"/> images par seconde et la taille voulue : le réglage retenu s'il marche
    /// encore, sinon des essais mesurés, les images par seconde d'abord (60, puis 30), la plus grande taille d'abord, MJPG d'abord.
    /// </summary>
    static VideoCapture Ouvrir(SourcesDisplay.Peripherique device, int ips, string taille, CancellationToken token, out Reglage reglage)
    {
        string cle = device.Id + "|" + ips + "|" + taille;
        Reglage? connu;
        lock (gate) trouves.TryGetValue(cle, out connu);
        if (connu != null && Configurer(device.Index, connu.Largeur, connu.Hauteur, connu.Mjpg, connu.Demande) is { } deja)
        {
            reglage = connu;
            return deja;
        }
        var tailles = taille switch
        {
            "4k" => new[] { (3840, 2160), (1920, 1080), (1280, 720) },
            "720p" => new[] { (1280, 720) },
            _ => new[] { (1920, 1080), (1280, 720) },
        };
        // une carte de capture donne 60 ou 30 images par seconde : 40 se fait en sautant des images d'un appareil à 60
        var demandes = ips >= 40 ? new[] { 60, 30 } : new[] { 30 };
        Reglage? mieux = null;
        foreach (var demande in demandes)
            foreach (var (l, h) in tailles)
                foreach (var mjpg in new[] { true, false })
                {
                    token.ThrowIfCancellationRequested();
                    var cam = Configurer(device.Index, l, h, mjpg, demande);
                    if (cam == null) continue;
                    double mesure;
                    try { mesure = Mesurer(cam, token); }
                    catch { cam.Dispose(); throw; }
                    var r = new Reglage(l, h, mjpg, demande, mesure);
                    Journal.Info($"caméra « {device.Nom} » : essai {r}");
                    // assez d'images : on le garde, déjà ouvert (les essais vont du meilleur au moins bon)
                    if (mesure >= 0.85 * Math.Min(ips, demande))
                    {
                        lock (gate) trouves[cle] = r;
                        reglage = r;
                        return cam;
                    }
                    cam.Dispose();
                    Thread.Sleep(150);
                    if (mieux == null || Meilleur(r, mieux)) mieux = r;
                }
        // rien d'assez rapide : le meilleur essai ; sinon l'appareil comme il vient (sans rien demander)
        if (mieux != null && Configurer(device.Index, mieux.Largeur, mieux.Hauteur, mieux.Mjpg, mieux.Demande) is { } meilleur)
        {
            lock (gate) trouves[cle] = mieux;
            reglage = mieux;
            return meilleur;
        }
        var brut = OuvrirAppareil(device.Index);
        reglage = new Reglage((int)brut.Get(VideoCaptureProperties.FrameWidth), (int)brut.Get(VideoCaptureProperties.FrameHeight), false, 0, brut.Get(VideoCaptureProperties.Fps));
        return brut;
    }

    /// <summary>Le meilleur de deux essais : plus d'images par seconde (par paliers de 5), puis la plus grande image.</summary>
    static bool Meilleur(Reglage a, Reglage b)
    {
        int pa = (int)Math.Round(a.Mesure / 5), pb = (int)Math.Round(b.Mesure / 5);
        return pa != pb ? pa > pb : a.Largeur * a.Hauteur > b.Largeur * b.Hauteur;
    }

    static readonly int Mjpg = VideoWriter.FourCC('M', 'J', 'P', 'G');

    /// <summary>
    /// Ouvre l'appareil et le règle : les images par seconde d'abord, puis la taille, puis le format MJPG (dans cet ordre avec DirectShow :
    /// changer les images par seconde après le format le ferait oublier). Null si l'appareil ne prend pas ce réglage exactement.
    /// </summary>
    static VideoCapture? Configurer(int index, int l, int h, bool mjpg, int demande)
    {
        var cam = OuvrirAppareil(index);
        try
        {
            if (demande > 0) cam.Set(VideoCaptureProperties.Fps, demande);
            cam.Set(VideoCaptureProperties.FrameWidth, l);
            cam.Set(VideoCaptureProperties.FrameHeight, h);
            if (mjpg) cam.Set(VideoCaptureProperties.FourCC, Mjpg);
            bool ok = (int)cam.Get(VideoCaptureProperties.FrameWidth) == l && (int)cam.Get(VideoCaptureProperties.FrameHeight) == h
                && (!mjpg || (int)cam.Get(VideoCaptureProperties.FourCC) == Mjpg);
            if (ok) return cam;
        }
        catch (Exception e) when (e is not OperationCanceledException) { Journal.Info($"caméra : réglage {l} × {h} refusé ({e.Message})"); }
        cam.Dispose();
        return null;
    }

    /// <summary>
    /// 1.6.7 : ouvre l'appareil ; juste après une fermeture (les essais de réglage), certains pilotes refusent un instant :
    /// trois essais espacés avant de dire « occupée ».
    /// </summary>
    static VideoCapture OuvrirAppareil(int index)
    {
        for (int essai = 1; ; essai++)
        {
            var cam = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (cam.IsOpened()) return cam;
            cam.Dispose();
            if (essai >= 4) throw new IOException("Caméra occupée ou indisponible (une autre application s'en sert peut-être)");
            Thread.Sleep(400 * essai);
        }
    }

    /// <summary>Les images par seconde qui arrivent vraiment (les 3 premières ne comptent pas : l'appareil démarre), en 0,7 seconde.</summary>
    static double Mesurer(VideoCapture cam, CancellationToken token)
    {
        using var frame = new Mat();
        for (int i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!cam.Read(frame) || frame.Empty()) return 0;
        }
        var horloge = Stopwatch.StartNew();
        int n = 0;
        while (horloge.ElapsedMilliseconds < 700)
        {
            token.ThrowIfCancellationRequested();
            if (!cam.Read(frame) || frame.Empty()) break;
            n++;
        }
        double s = horloge.Elapsed.TotalSeconds;
        return s > 0 ? n / s : 0;
    }

    /// <summary>Moins d'images que demandé : on le dit une fois, avec quoi faire.</summary>
    static void Prevenir(SourcesDisplay.Peripherique device, int ips, Reglage r)
    {
        if (r.Mesure >= 0.85 * ips) return;
        lock (gate) if (!dits.Add(device.Id + "|" + ips)) return;
        string message = $"« {device.Nom} » donne {Math.Round(r.Mesure)} images par seconde ({r.Largeur} × {r.Hauteur}) au lieu de {ips} : "
            + "cette carte de capture ou cette caméra n'en donne pas plus. Pour une Sony : mode vidéo, « Réglage d'enreg. » en 60p ; sinon choisissez 30 images par seconde (clic droit sur la source).";
        Journal.Info(message);
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => (Application.Current.MainWindow as MainWindow)?.Toast(message)));
    }

    /// <summary>
    /// 2.5.1 : la caméra arrive en 4K alors que rien n'en a besoin (le direct et l'écran en 1080p) : l'ordinateur réduit chaque image et
    /// une carte de capture comme la Cam Link 4K est à la limite de l'USB 3. On le dit une fois, avec quoi faire.
    /// </summary>
    static void Conseiller(SourcesDisplay.Peripherique device, Reglage r)
    {
        if (r.Largeur < 3840 || LargeurMax() >= 3840) return;
        lock (gate) if (!dits.Add(device.Id + "|4k")) return;
        string message = $"« {device.Nom} » arrive en 4K alors que le direct et l'écran sont en 1080p : l'ordinateur réduit chaque image et la carte de capture est à la limite de l'USB. "
            + "Plus léger et plus stable : sur la Sony, Réglages HDMI › Résolution HDMI : 1080p ; puis clic droit sur la source › Taille de l'image : 1080p.";
        Journal.Info(message);
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => (Application.Current.MainWindow as MainWindow)?.Toast(message)));
    }
}
