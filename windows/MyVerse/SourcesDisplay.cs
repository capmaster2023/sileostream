using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace MyVerse.Ui;

/// <summary>Only the explicitly selected device/window is captured. No audio is recorded.
/// Une instance par contenu vivant : App.Sources (écran principal) et une par source superposée (App.Calques, 2.5.0).</summary>
public sealed class SourcesDisplay : IDisposable
{
    /// <summary>Nom de l'image écrite pour la page OBS : display.jpg (écran principal) ou display-calque-N.jpg (un calque) ;
    /// vide pour un lecteur qui n'écrit rien (vidéo des enregistrements).</summary>
    readonly string export;
    public SourcesDisplay(string export = "display.jpg") { this.export = export; }

    /// <summary>
    /// 1.7.0 : lecteur d'une tuile de la grille (« Médias ») : la caméra donne sa vignette (petite image) dix fois par seconde au
    /// plus, une fenêtre est capturée quatre fois par seconde ; il garde aussi la dernière image (une fenêtre montrée ensuite à
    /// l'écran apparaît tout de suite).
    /// </summary>
    public bool Tuile { get; init; }

    volatile bool pause;
    /// <summary>1.8.0 : tuile cachée (on n'est pas sur la page Médias) : la fenêtre n'est plus capturée, la caméra reste ouverte sans rien montrer.</summary>
    public bool Pause { get => pause; set => pause = value; }

    public sealed record Peripherique(int Index, string Nom, string Id);
    /// <summary>Une fenêtre ouverte : son programme (« Zoom.exe ») et sa classe servent à la retrouver quand elle est rouverte (1.3.1).</summary>
    public sealed record Fenetre(long Handle, int Processus, string Titre, string Exe = "", string Classe = "")
    {
        /// <summary>« [Zoom.exe] : Zoom Meeting », comme dans OBS.</summary>
        public string Libelle => Exe.Length > 0 ? $"[{Exe}] : {Titre}" : Titre;
    }
    public BitmapSource? Image { get; private set; }
    /// <summary>La source capturée (null : aucune) : l'enregistrement lit MyCam et les caméras directement, sans passer par l'interface.</summary>
    public SourceDisplay? Source => source;
    public string Statut { get; private set; } = "";
    public event Action? Change;
    CancellationTokenSource? fin;
    Task travail = Task.CompletedTask;
    SourceDisplay? source;
    int generation;
    long dernierExport;
    int exportEnCours;

    public void Choisir(EtatEcran etat) => ChoisirSource(etat.Mode == "media" ? etat.Media : null);

    /// <summary>La source à capturer (null : aucune). Sans effet si c'est déjà elle.</summary>
    public void ChoisirSource(SourceDisplay? nouvelle)
    {
        if (nouvelle == source) return;
        source = nouvelle;
        try { fin?.Cancel(); } catch (ObjectDisposedException) { }
        var avant = travail;
        var token = new CancellationTokenSource();
        fin = token;
        int g = ++generation;
        // 1.6.6 : la dernière image de cette source, tout de suite (au lieu d'un écran noir) ; la capture reprend derrière
        Image = Derniere(nouvelle);
        ViderExport();
        Statut = nouvelle == null ? "" : Image != null ? "En direct" : "Connexion à la source…";
        Change?.Invoke();
        travail = Task.Run(async () =>
        {
            try
            {
                await avant.ConfigureAwait(false);
                token.Token.ThrowIfCancellationRequested();
                if (nouvelle == null) return;
                if (nouvelle.Type == "image")
                {
                    var image = new BitmapImage();
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 1920;
                    image.UriSource = new Uri(nouvelle.Chemin, UriKind.Absolute);
                    image.EndInit(); image.Freeze();
                    await Publier(image, g, token.Token);
                }
                else if (nouvelle.Type == "camera")
                {
                    // 1.6.5 : la source dit ses images par seconde et sa taille ; chaque image est montrée dès qu'elle arrive.
                    // 1.6.7 : une caméra occupée, débranchée ou qui hésite à l'ouverture n'arrête plus la source : Siloé Stream le dit et
                    // réessaie toutes les 2 secondes (MyStream restait noir pour toujours si sa première ouverture avait raté)
                    string attente = "";
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            using var lease = Audio.CameraFrames.Acquire(nouvelle.Peripherique, nouvelle, Tuile);
                            BitmapSource? previous = null;
                            while (!token.IsCancellationRequested)
                            {
                                if (lease.Error is { } error) throw new IOException(error);
                                var prochaine = lease.Prochaine;
                                if (!(Tuile && pause) && (Tuile ? lease.Vignette : lease.Image) is { } b && !ReferenceEquals(b, previous))
                                {
                                    await Publier(b, g, token.Token);
                                    previous = b;
                                    attente = "";
                                    if (Tuile) await Task.Delay(90, token.Token);
                                    continue;
                                }
                                await Task.WhenAny(prochaine, Task.Delay(500, token.Token));
                                token.Token.ThrowIfCancellationRequested();
                            }
                        }
                        catch (IOException ex)
                        {
                            var message = ex.Message.TrimEnd('.') + " : Siloé Stream réessaie toutes les 2 secondes.";
                            // une tuile (1.7.0) n'écrit qu'une ligne au journal tant que la raison ne change pas (caméra débranchée des heures)
                            if (!Tuile || message != attente) Journal.Info($"caméra « {nouvelle.Peripherique} » : {ex.Message} (nouvel essai toutes les 2 s)");
                            attente = await Attendre(g, attente, message, false);
                            await Task.Delay(2000, token.Token);
                        }
                    }
                }
                else if (nouvelle.Type == "mycam")
                {
                    // MyCam : les images JPEG du téléphone arrivent par sa connexion ; on montre toujours la plus récente
                    var cams = App.Telephone?.Cameras ?? throw new IOException("MyCam indisponible : la connexion des téléphones est fermée.");
                    var signal = new SemaphoreSlim(0, 1);
                    void Recue(string id)
                    {
                        if (id != nouvelle.Peripherique) return;
                        try { signal.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
                    }
                    cams.NouveauCadre += Recue;
                    try
                    {
                        long dernier = -1;
                        bool attente = false, erreurSignalee = false;
                        var depuis = Stopwatch.StartNew();
                        while (!token.IsCancellationRequested)
                        {
                            if (cams.Dernier(nouvelle.Peripherique) is { } c && c.Numero != dernier)
                            {
                                dernier = c.Numero;
                                // une image ratée ne coupe pas la caméra : l'image suivante arrive dans quelques millisecondes
                                try
                                {
                                    if (DecoderMyCam(c) is { } b) { await Publier(b, g, token.Token); attente = false; }
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    if (!erreurSignalee) { erreurSignalee = true; Journal.Erreur("MyCam (image sautée)", ex); }
                                }
                                depuis.Restart();
                            }
                            else if (!attente && depuis.ElapsedMilliseconds > (dernier < 0 ? 300 : 2500))
                            {
                                attente = true;
                                var info = cams.InfoDe(nouvelle.Peripherique);
                                var texte = info == null ? "En attente de MyCam : sur le téléphone, Siloé Stream › MyCam › Diffuser."
                                    : !info.Diffuse ? $"« {info.Nom} » ne diffuse pas : touchez Diffuser dans MyCam sur le téléphone."
                                    : $"« {info.Nom} » : images interrompues (Wi-Fi ?). Reprise automatique.";
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (g != generation) return;
                                    Statut = texte; Change?.Invoke();
                                });
                            }
                            await signal.WaitAsync(500, token.Token);
                        }
                    }
                    finally
                    {
                        cams.NouveauCadre -= Recue;
                        signal.Dispose();
                    }
                }
                else if (nouvelle.Type == "video")
                {
                    using var video = new VideoCapture(nouvelle.Chemin);
                    if (!video.IsOpened()) throw new IOException("Vidéo illisible.");
                    using var frame = new Mat(); using var bgra = new Mat();
                    double fps = video.Fps; if (!double.IsFinite(fps) || fps <= 0) fps = 30;
                    var clock = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested)
                    {
                        double wanted = clock.Elapsed.TotalSeconds * fps;
                        if (video.PosFrames > wanted) { await Task.Delay(5, token.Token); continue; }
                        if (wanted - video.PosFrames > 3) video.PosFrames = (int)Math.Floor(wanted);
                        if (!video.Read(frame) || frame.Empty()) break;
                        Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
                        var b = BitmapSource.Create(bgra.Width, bgra.Height, 96, 96, PixelFormats.Bgra32, null, bgra.Data, (int)(bgra.Step() * bgra.Height), (int)bgra.Step()); b.Freeze();
                        await Publier(b, g, token.Token);
                    }
                }
                else if (nouvelle.Type == "fenetre")
                {
                    // 1.3.1 : une fenêtre fermée puis rouverte (Zoom qui change de réunion, un programme relancé) est reprise toute seule,
                    // une fenêtre réduite revient dès qu'elle est restaurée : la source attend au lieu de s'arrêter
                    var h = new IntPtr(nouvelle.Fenetre);
                    int pid = nouvelle.Processus;
                    string attente = "";
                    while (!token.IsCancellationRequested)
                    {
                        if (Tuile && pause) { await Task.Delay(400, token.Token); continue; }
                        if (!Ouverte(h, pid))
                        {
                            if (Retrouver(nouvelle) is { } f) { h = new IntPtr(f.Handle); pid = f.Processus; }
                            else
                            {
                                attente = await Attendre(g, attente, "La fenêtre a été fermée. Siloé Stream la reprendra dès qu'elle sera rouverte ; sinon, choisissez-la de nouveau (clic droit › Modifier).", true);
                                await Task.Delay(1000, token.Token);
                                continue;
                            }
                        }
                        if (IsIconic(h))
                        {
                            attente = await Attendre(g, attente, "La fenêtre est réduite : restaurez-la pour la voir à l'écran.", false);
                            await Task.Delay(500, token.Token);
                            continue;
                        }
                        BitmapSource image;
                        // une fenêtre qui se ferme ou change de taille pendant la capture : on réessaie un peu plus tard
                        try { image = Capturer(h, nouvelle.ZoneClient); }
                        catch (IOException ex)
                        {
                            attente = await Attendre(g, attente, ex.Message, false);
                            await Task.Delay(1000, token.Token);
                            continue;
                        }
                        attente = "";
                        await Publier(image, g, token.Token);
                        await Task.Delay(Tuile ? 250 : 66, token.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Journal.Erreur("Display", ex);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (g != generation) return;
                    Image = null; ViderExport(); Statut = ex.Message; Change?.Invoke();
                    if (Application.Current.MainWindow is MainWindow w) w.Toast(ex.Message);
                });
            }
            finally { token.Dispose(); }
        });
    }

    /// <summary>
    /// Image MyCam : les pixels H.264 décodés (Siloé Stream 4.1) ou le JPEG décodé, tournés d'un quart de tour au besoin
    /// (téléphone en hauteur ou à l'horizontale), en miroir au besoin ; réduits à <paramref name="largeur"/> pour les vignettes.
    /// </summary>
    public static BitmapSource? DecoderMyCam(MyCams.Cadre c, int largeur = 0)
    {
        try
        {
            BitmapSource b;
            if (c.Pixels is { } px)
            {
                b = BitmapSource.Create(c.Largeur, c.Hauteur, 96, 96, PixelFormats.Bgr32, null, px, c.Largeur * 4);
                double finale = c.Rotation % 2 == 1 ? c.Hauteur : c.Largeur;
                if (largeur > 0 && largeur < finale) b = new TransformedBitmap(b, new ScaleTransform(largeur / finale, largeur / finale));
                if (c.Rotation != 0) b = new TransformedBitmap(b, new RotateTransform(90 * c.Rotation));
            }
            else
            {
                using var flux = new MemoryStream(c.Jpeg, false);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = flux;
                if (largeur > 0) image.DecodePixelWidth = largeur;
                image.Rotation = c.Rotation switch { 1 => Rotation.Rotate90, 2 => Rotation.Rotate180, 3 => Rotation.Rotate270, _ => Rotation.Rotate0 };
                image.EndInit();
                b = image;
            }
            if (c.Miroir) b = new TransformedBitmap(b, new ScaleTransform(-1, 1));
            b.Freeze();
            return b;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 1.6.6 : la dernière image des dernières sources montrées (caméra, MyCam, fenêtre, image ; pas les vidéos, qui repartent du début),
    /// pour tous les lecteurs (église, MyStream, Duo, tuiles…) : une source montrée de nouveau apparaît tout de suite. Douze sources au plus
    /// (une image 1080p pèse 8 Mo). Sur le fil de l'interface seulement.
    /// </summary>
    static readonly List<(string Cle, BitmapSource Image)> dernieres = new();

    static string? CleDerniere(SourceDisplay? s) => s?.Type switch
    {
        "camera" => "c:" + s.Peripherique,
        "mycam" => "m:" + s.Peripherique,
        "fenetre" => $"f:{s.Fenetre}:{s.Processus}:{s.ZoneClient}",
        "image" => "i:" + s.Chemin,
        _ => null,
    };

    static BitmapSource? Derniere(SourceDisplay? s)
    {
        if (CleDerniere(s) is not { } cle) return null;
        foreach (var (c, image) in dernieres) if (c == cle) return image;
        return null;
    }

    static void Garder(SourceDisplay s, BitmapSource b)
    {
        if (CleDerniere(s) is not { } cle) return;
        int i = dernieres.FindIndex(x => x.Cle == cle);
        if (i == 0) { dernieres[0] = (cle, b); return; }
        if (i > 0) dernieres.RemoveAt(i);
        dernieres.Insert(0, (cle, b));
        if (dernieres.Count > 12) dernieres.RemoveAt(dernieres.Count - 1);
    }

    async Task Publier(BitmapSource b, int g, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (g != generation) return;
            Image = b; Statut = "En direct";
            // la vignette d'une caméra (petite) ne remplace pas sa dernière image en grand
            if (source != null && !(Tuile && source.Type == "camera")) Garder(source, b);
            Change?.Invoke();
        });
        // 1.3.1 : une source « Dans l'église » ne part pas vers la page OBS
        if (export.Length == 0 || g != generation || source?.EgliseSeulement == true || !App.SortieObs.Prete || Environment.TickCount64 - dernierExport < 100) return;
        // 4.1 : l'image de la page OBS s'écrit à côté, sans retenir l'image suivante de la caméra ; si la précédente s'écrit encore, celle-ci est sautée
        if (Interlocked.CompareExchange(ref exportEnCours, 1, 0) != 0) return;
        dernierExport = Environment.TickCount64;
        _ = Task.Run(async () =>
        {
            try { await Exporter(b, g); }
            finally { Volatile.Write(ref exportEnCours, 0); }
        });
    }

    /// <summary>
    /// La source attend (fenêtre fermée ou réduite) : plus d'image, un message utile, et une bulle la première fois pour la fenêtre fermée
    /// (seulement pour l'écran principal et le Duo, pas pour les lecteurs des enregistrements). Renvoie le message affiché.
    /// </summary>
    async Task<string> Attendre(int g, string actuel, string message, bool bulle)
    {
        if (message == actuel) return actuel;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (g != generation) return;
            Image = null; ViderExport(); Statut = message; Change?.Invoke();
            if (bulle && export.Length > 0 && Application.Current.MainWindow is MainWindow w) w.Toast(message);
        });
        return message;
    }

    /// <summary>
    /// L'image de la page OBS (display.jpg), écrite à côté puis mise à la place. OBS (ou l'antivirus) tient parfois le fichier ouvert
    /// au moment du remplacement : Windows répond « accès refusé ». On réessaie un instant, sinon cette image est sautée ;
    /// la source, elle, continue (avant la 4.0.1, cette erreur arrêtait la caméra : écran noir ou image figée).
    /// </summary>
    async Task Exporter(BitmapSource b, int g)
    {
        try
        {
            BitmapSource sortie = b.PixelWidth > 1280 ? new TransformedBitmap(b, new ScaleTransform(1280.0 / b.PixelWidth, 1280.0 / b.PixelWidth)) : b;
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(sortie));
            var path = Path.Combine(App.SortieObs.Dossier, export);
            using (var file = File.Create(path + ".tmp")) encoder.Save(file);
            for (int essai = 0; essai < 3 && g == generation; essai++)
            {
                try { File.Move(path + ".tmp", path, true); return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(10); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException) { }
    }

    void ViderExport()
    {
        if (export.Length == 0 || !App.SortieObs.Prete) return;
        try
        {
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[3], 3)));
            var path = Path.Combine(App.SortieObs.Dossier, export);
            // Distinct temp path: an in-flight old source may still finish encoding its last frame.
            using (var f = File.Create(path + ".blank")) encoder.Save(f);
            File.Move(path + ".blank", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException) { }
    }

    public void Reconnecter(EtatEcran e) { source = null; Choisir(e); }
    public void ReconnecterSource(SourceDisplay? s) { source = null; ChoisirSource(s); }
    public void Dispose() { ++generation; try { fin?.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>
    /// Les caméras DirectShow (webcams, cartes de capture). MyVirtualCam (l'image de MyStream) n'y est que si
    /// <paramref name="avecMyVirtualCam"/> (1.6.8 : on peut la choisir comme média ; l'index reste celui de DirectShow).
    /// </summary>
    public static List<Peripherique> Peripheriques(bool avecMyVirtualCam = false)
    {
        var liste = new List<Peripherique>();
        object? obj = null;
        IEnumMoniker? enumeration = null;
        try
        {
            obj = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86"))!);
            var categorie = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
            if (((ICreateDevEnum)obj!).CreateClassEnumerator(ref categorie, out enumeration, 0) != 0 || enumeration == null) return liste;
            var monikers = new IMoniker[1];
            int index = 0;
            while (enumeration.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var m = monikers[0]; object? sac = null;
                try
                {
                    var iid = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
                    m.BindToStorage(null!, null!, ref iid, out sac);
                    ((IPropertyBag)sac).Read("FriendlyName", out var nom, IntPtr.Zero);
                    m.GetDisplayName(null!, null!, out var id);
                    if (avecMyVirtualCam || !EstMyVirtualCam(nom?.ToString()))
                        liste.Add(new Peripherique(index, nom?.ToString() ?? $"Caméra {index + 1}", id));
                }
                finally { if (sac != null) Marshal.ReleaseComObject(sac); Marshal.ReleaseComObject(m); index++; }
            }
        }
        finally { if (enumeration != null) Marshal.ReleaseComObject(enumeration); if (obj != null) Marshal.ReleaseComObject(obj); }
        return liste;
    }

    public static bool EstMyVirtualCam(string? nom) => string.Equals(nom, "MyVirtualCam", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Les fenêtres qu'on peut capturer, comme la liste d'OBS (1.3.1) : visibles, avec un titre, ni fenêtre enfant, ni petite fenêtre
    /// d'outils, ni fenêtre cachée par Windows (autre bureau virtuel, application en veille), ni le bureau ou la barre des tâches,
    /// ni Siloé Stream lui-même. Rangées par programme puis par titre. <paramref name="reduites"/> : aussi les fenêtres réduites.
    /// </summary>
    public static List<Fenetre> Fenetres(bool reduites = false)
    {
        var list = new List<Fenetre>();
        var exes = new Dictionary<int, string>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            bool reduite = IsIconic(h);
            if (reduite && !reduites) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId) return true;
            long style = Style(h, -16 /* GWL_STYLE */), etendu = Style(h, -20 /* GWL_EXSTYLE */);
            if ((style & 0x40000000L /* WS_CHILD */) != 0 || (etendu & 0x80L /* WS_EX_TOOLWINDOW */) != 0) return true;
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cachee, sizeof(int)) == 0 && cachee != 0) return true;
            if (!reduite && (!GetClientRect(h, out var c) || c.Right <= 0 || c.Bottom <= 0)) return true;
            var titre = new StringBuilder(512); GetWindowText(h, titre, titre.Capacity);
            if (titre.Length == 0) return true;
            var nomClasse = new StringBuilder(256); GetClassName(h, nomClasse, nomClasse.Capacity);
            string classe = nomClasse.ToString();
            if (classe is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (!exes.TryGetValue((int)pid, out var exe)) exes[(int)pid] = exe = NomExe((int)pid);
            list.Add(new Fenetre(h.ToInt64(), (int)pid, titre.ToString(), exe, classe));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(f => f.Exe, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Titre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// La fenêtre d'une source qui a été fermée puis rouverte : même programme et même titre, sinon même programme et même type
    /// de fenêtre (« Faire correspondre le titre, sinon trouver une fenêtre du même type » d'OBS). Null si rien ne correspond,
    /// ou pour une source d'avant la 1.3.1 (sans programme connu).
    /// </summary>
    public static Fenetre? Retrouver(SourceDisplay s)
    {
        if (string.IsNullOrEmpty(s.Exe)) return null;
        var memes = Fenetres(true).Where(f => string.Equals(f.Exe, s.Exe, StringComparison.OrdinalIgnoreCase)).ToList();
        return memes.FirstOrDefault(f => f.Titre == s.Titre)
            ?? (string.IsNullOrEmpty(s.Classe) ? null : memes.FirstOrDefault(f => f.Classe == s.Classe));
    }

    /// <summary>La fenêtre existe toujours et appartient au même programme (un numéro de fenêtre peut être réutilisé par Windows).</summary>
    public static bool Ouverte(IntPtr h, int processus)
    {
        if (!IsWindow(h)) return false;
        GetWindowThreadProcessId(h, out uint pid);
        return processus > 0 && pid == (uint)processus;
    }

    public static bool Reduite(IntPtr h) => IsIconic(h);

    /// <summary>Le nom du programme d'un processus (« Zoom.exe »), vide s'il ne se lit pas (programme protégé).</summary>
    static string NomExe(int pid)
    {
        var p = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (p == IntPtr.Zero) return "";
        try
        {
            var chemin = new StringBuilder(1024);
            int n = chemin.Capacity;
            return QueryFullProcessImageName(p, 0, chemin, ref n) && n > 0 ? Path.GetFileName(chemin.ToString(0, n)) : "";
        }
        catch (Exception) { return ""; }
        finally { CloseHandle(p); }
    }

    static long Style(IntPtr h, int index) => IntPtr.Size == 8 ? GetWindowLongPtr(h, index).ToInt64() : GetWindowLong(h, index);

    /// <summary>
    /// Une image de la fenêtre (PrintWindow, rendu complet : marche aussi pour les fenêtres dessinées par la carte graphique).
    /// <paramref name="zoneClient"/> (1.3.1) : seulement l'intérieur, sans la barre de titre ni les bords, comme « Zone client » d'OBS.
    /// </summary>
    public static BitmapSource Capturer(IntPtr h, bool zoneClient = false)
    {
        if (!GetWindowRect(h, out var r)) throw new IOException("Fenêtre inaccessible.");
        int w = r.Right - r.Left, height = r.Bottom - r.Top;
        if (w <= 0 || height <= 0 || w > 7680 || height > 4320) throw new IOException("Dimensions de fenêtre non prises en charge.");
        var dc = GetDC(IntPtr.Zero); var mem = CreateCompatibleDC(dc); IntPtr bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var info = new BitmapInfo { Size = 40, Width = w, Height = -height, Planes = 1, Bits = 32 };
            bmp = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bmp == IntPtr.Zero) throw new IOException("Capture impossible (mémoire graphique).");
            old = SelectObject(mem, bmp);
            if (!PrintWindow(h, mem, 2)) throw new IOException("Cette fenêtre refuse la capture. Essayez une fenêtre classique, non protégée.");
            var b = BitmapSource.Create(w, height, 96, 96, PixelFormats.Bgr32, null, bits, w * height * 4, w * 4);
            b.Freeze();
            return zoneClient ? ZoneClient(h, r, b) : b;
        }
        finally { if (old != IntPtr.Zero) SelectObject(mem, old); if (bmp != IntPtr.Zero) DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, dc); }
    }

    /// <summary>L'intérieur de la fenêtre, découpé dans son image entière (l'image entière si la zone ne se lit pas).</summary>
    static BitmapSource ZoneClient(IntPtr h, Rectangle fenetre, BitmapSource entiere)
    {
        if (!GetClientRect(h, out var c) || c.Right <= 0 || c.Bottom <= 0) return entiere;
        var coin = new Point32();
        if (!ClientToScreen(h, ref coin)) return entiere;
        int x = coin.X - fenetre.Left, y = coin.Y - fenetre.Top;
        if (x < 0 || y < 0) return entiere;
        int l = Math.Min(c.Right, entiere.PixelWidth - x), ht = Math.Min(c.Bottom, entiere.PixelHeight - y);
        if (l <= 0 || ht <= 0 || (l == entiere.PixelWidth && ht == entiere.PixelHeight)) return entiere;
        var zone = new CroppedBitmap(entiere, new Int32Rect(x, y, l, ht));
        zone.Freeze();
        return zone;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum { [PreserveSig] int CreateClassEnumerator(ref Guid type, out IEnumMoniker result, int flags); }
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value, IntPtr log);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    }
    delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] struct Rectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Used, Important; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rectangle r);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    // 1.3.1 : la liste des fenêtres comme OBS, la zone client, le programme d'une fenêtre
    [StructLayout(LayoutKind.Sequential)] struct Point32 { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Rectangle r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Point32 p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribut, out int valeur, int taille);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint acces, bool heriter, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    static extern bool QueryFullProcessImageName(IntPtr processus, int options, StringBuilder chemin, ref int taille);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
