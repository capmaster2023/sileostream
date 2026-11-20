using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyVerse.Audio;
using OpenCvSharp;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;

namespace MyVerse.Ui.Audio;

/// <summary>
/// La composition vidéo d'un enregistrement (Siloé Stream 1.2), à cadence exacte et hors du fil de l'interface, comme le rendu d'OBS.
/// <para>
/// WPF ne dessine plus chaque image : quelques fois par seconde (quand le contenu change), il rend la mise en page des sources Display
/// (fond, texte, calques) deux fois, les images vivantes remplacées par une clé noire puis blanche. La différence des deux rendus dit,
/// pour chaque pixel, combien l'image vivante s'y voit (texte par-dessus, demi-transparence du fond flou compris).
/// </para>
/// <para>
/// À chaque image, sur le fil vidéo de l'enregistrement, les images vivantes (MyCam, caméra, vidéo) sont posées par OpenCV à leur place
/// (quart de tour, miroir, mise à l'échelle) : image = rendu clé noire + différence × images vivantes. MyCam est lue directement
/// (sans l'interface) et l'image prise au plus près de l'instant demandé est choisie : le mouvement reste fluide (30 i/s)
/// et la voix tombe sur les lèvres.
/// </para>
/// </summary>
public sealed class RecordingCompositor : IRecordingFrameProvider, IDisposable
{
    // ------------------------------------------------------------ fournisseurs d'images vivantes
    /// <summary>Ce qui donne les images vivantes d'un contenu (MyCam, caméra, vidéo, fenêtre…) à l'enregistrement.</summary>
    abstract class Fournisseur : IDisposable
    {
        /// <summary>Taille de l'image telle qu'affichée (après le quart de tour), pour la mise en page ; null tant qu'il n'y a pas d'image.</summary>
        public abstract (int L, int H)? Taille { get; }
        /// <summary>L'image de l'instant <paramref name="monde"/> : pixels BGRA (taille brute), quarts de tour (sens horaire), puis miroir.</summary>
        public abstract (Mat Image, int Rotation, bool Miroir)? Lire(long monde);
        /// <summary>Retard des images sur le monde (ns) : MyCam seulement.</summary>
        public virtual long Retard => 0;
        /// <summary>Pourquoi il n'y a pas d'image (pour l'erreur au démarrage).</summary>
        public virtual string Statut => "La source Display ne fournit pas d’image.";
        public virtual void Dispose() { }
    }

    /// <summary>MyCam : les images décodées du téléphone, lues directement (pas d'interface), choisies selon l'instant de leur prise de vue.</summary>
    sealed class FournisseurMyCam : Fournisseur
    {
        readonly MyCams cams;
        readonly string id;
        readonly Mat image = new();
        long numero = -1;
        int rotation;
        bool miroir;
        public FournisseurMyCam(MyCams cams, string id) { this.cams = cams; this.id = id; }
        public override (int L, int H)? Taille
        {
            get
            {
                if (cams.Dernier(id) is not { } c) return null;
                (int L, int H)? t = c.Pixels != null && c.Largeur > 0 ? (c.Largeur, c.Hauteur) : TailleJpeg(c.Jpeg);
                if (t is not { } d) return null;
                return c.Rotation % 2 == 1 ? (d.H, d.L) : d;
            }
        }
        public override long Retard => cams.Retard(id);
        public override string Statut
        {
            get
            {
                var info = cams.InfoDe(id);
                return info == null ? "MyCam : le téléphone n'envoie pas d'image (Siloé Stream › MyCam › Diffuser)."
                    : !info.Diffuse ? $"MyCam : « {info.Nom} » ne diffuse pas (touchez Diffuser sur le téléphone)."
                    : $"MyCam : « {info.Nom} » n'envoie plus d'image (Wi-Fi ?).";
            }
        }
        public override (Mat Image, int Rotation, bool Miroir)? Lire(long monde)
        {
            var c = cams.Proche(id, monde);
            if (c == null) return null;
            if (c.Numero != numero)
            {
                if (c.Pixels is { } px && c.Largeur > 0 && c.Hauteur > 0 && px.Length >= c.Largeur * c.Hauteur * 4)
                {
                    image.Create(c.Hauteur, c.Largeur, MatType.CV_8UC4);
                    Marshal.Copy(px, 0, image.Data, c.Largeur * c.Hauteur * 4);
                }
                else if (c.Jpeg.Length > 0)
                {
                    using var bgr = Cv2.ImDecode(c.Jpeg, ImreadModes.Color);
                    if (bgr.Empty()) return numero < 0 ? null : (image, rotation, miroir);
                    Cv2.CvtColor(bgr, image, ColorConversionCodes.BGR2BGRA);
                }
                else return null;
                numero = c.Numero; rotation = c.Rotation; miroir = c.Miroir;
            }
            return (image, rotation, miroir);
        }
        public override void Dispose() => image.Dispose();
    }

    /// <summary>Caméra USB (DirectShow) : sa capture partagée, lue sur son propre fil.</summary>
    sealed class FournisseurCamera : Fournisseur
    {
        readonly CameraFrames.Lease lease;
        readonly Mat image = new();
        BitmapSource? vue;
        public FournisseurCamera(string id) { lease = CameraFrames.Acquire(id); }
        public override (int L, int H)? Taille => lease.Image is { } b ? (b.PixelWidth, b.PixelHeight) : null;
        public override string Statut => lease.Error ?? "La caméra ne fournit pas d’image.";
        public override (Mat Image, int Rotation, bool Miroir)? Lire(long monde)
        {
            if (lease.Error is { } e) throw new IOException(e);
            var b = lease.Image;
            if (b == null) return null;
            if (!ReferenceEquals(b, vue)) { Copier(b, image); vue = b; }
            return (image, 0, false);
        }
        public override void Dispose() { lease.Dispose(); image.Dispose(); }
    }

    /// <summary>Vidéo, fenêtre, image : la dernière image publiée par le lecteur Display.</summary>
    sealed class FournisseurLecteur : Fournisseur
    {
        readonly SourcesDisplay lecteur;
        readonly Mat image = new();
        BitmapSource? vue;
        public FournisseurLecteur(SourcesDisplay lecteur) { this.lecteur = lecteur; }
        public override (int L, int H)? Taille => lecteur.Image is { } b ? (b.PixelWidth, b.PixelHeight) : null;
        public override string Statut => lecteur.Statut is { Length: > 0 } st && st != "Connexion à la source…" ? st : base.Statut;
        public override (Mat Image, int Rotation, bool Miroir)? Lire(long monde)
        {
            var b = lecteur.Image;
            if (b == null) return null;
            if (!ReferenceEquals(b, vue)) { Copier(b, image); vue = b; }
            return (image, 0, false);
        }
        public override void Dispose() => image.Dispose();
    }

    /// <summary>Une image WPF (gelée, lisible de tout fil) → pixels BGRA.</summary>
    static void Copier(BitmapSource b, Mat m)
    {
        BitmapSource s = b.Format == PixelFormats.Bgra32 || b.Format == PixelFormats.Bgr32 || b.Format == PixelFormats.Pbgra32 ? b : new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
        m.Create(s.PixelHeight, s.PixelWidth, MatType.CV_8UC4);
        s.CopyPixels(Int32Rect.Empty, m.Data, s.PixelWidth * s.PixelHeight * 4, s.PixelWidth * 4);
    }

    /// <summary>Largeur et hauteur d'un JPEG (en-tête SOF), sans le décoder.</summary>
    internal static (int L, int H)? TailleJpeg(byte[] j)
    {
        int i = 2;
        while (i + 9 < j.Length)
        {
            if (j[i] != 0xFF) { i++; continue; }
            int m = j[i + 1];
            if (m == 0xFF) { i++; continue; }
            if (m is 0xD8 or 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
            {
                int h = j[i + 5] << 8 | j[i + 6], l = j[i + 7] << 8 | j[i + 8];
                return l > 0 && h > 0 ? (l, h) : null;
            }
            i += 2 + (j[i + 2] << 8 | j[i + 3]);
        }
        return null;
    }

    // ------------------------------------------------------------ calque (rendu WPF) et trous (images vivantes)
    /// <summary>Une image vivante à poser : son fournisseur, flou ou net, l'image dans le canevas (origine et axes) et le rognage.</summary>
    sealed record Trou(Fournisseur Fournisseur, bool Flou, Point O, Point X, Point Y, OpenCvSharp.Rect Rognage);

    /// <summary>Le rendu WPF du moment : clé noire (C1), différence clé blanche − clé noire (D), et les images vivantes à poser.</summary>
    sealed class Calque : IDisposable
    {
        public required Mat C1, D;
        public required List<Trou> Trous;
        public void Dispose() { C1.Dispose(); D.Dispose(); }
    }

    /// <summary>Une source Display filmée : son dessin, et ses lecteurs (vidéo, fenêtre, image) s'il s'agit d'une source de la bibliothèque.</summary>
    sealed class Vue
    {
        public required Rendu Rendu;
        public required FrameworkElement Cadre;
        public Annonce? Annonce;
        public EtatEcran? Etat;
        public SourcesDisplay? Premier;
        public Fournisseur? Principal;
        /// <summary>2.5.0 : les fournisseurs des calques montrés (null : texte ou image fixe, dessinés par WPF).</summary>
        public readonly Dictionary<string, Fournisseur?> Calques = new();
    }

    readonly Canvas canvas = new() { Background = Brushes.Black };
    readonly RenderTargetBitmap bitmap;
    readonly byte[] pixels;
    readonly List<Vue> vues = new();
    readonly Dictionary<string, Fournisseur> fournisseurs = new();
    readonly DispatcherTimer timer;
    readonly int width, height;
    readonly object verrouCalque = new();
    Calque? calque;
    List<object?> signature = new();
    long derniereReconstruction;
    // tampons du fil vidéo
    readonly Mat media, produit, sortie, flouPetit, flouGrand;
    bool disposed;
    volatile string? erreurImage;
    public string? Error { get; private set; }

    public RecordingCompositor(RecordingProfile profile)
    {
        width = profile.Width; height = profile.Height;
        canvas.Width = width; canvas.Height = height;
        bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        pixels = new byte[width * height * 4];
        media = new Mat(height, width, MatType.CV_8UC4);
        produit = new Mat(height, width, MatType.CV_8UC4);
        sortie = new Mat(height, width, MatType.CV_8UC4);
        flouPetit = new Mat(Math.Max(1, height / 4), Math.Max(1, width / 4), MatType.CV_8UC4);
        flouGrand = new Mat(height, width, MatType.CV_8UC4);
        // quelques fois par seconde seulement : la mise en page (texte, calques, fond) ; les images vivantes sont posées à chaque image, ailleurs
        timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(200) };
        try
        {
            foreach (var visual in profile.Visuals.Where(v => v.Visible))
            {
                var rendu = new Rendu { Transitions = false, SansEffets = true };
                var vue = new Vue { Rendu = rendu, Cadre = rendu };
                if (visual.Kind == "annonce")
                {
                    vue.Annonce = App.Donnees.Annonces.FirstOrDefault(x => x.Id == visual.Annonce) ?? throw new IOException("Une source Display de cet enregistrement a été supprimée : « Modifier » pour la retirer.");
                    if (App.Regie.EtatAnnonce(vue.Annonce) == null) throw new IOException($"La source Display « {vue.Annonce.Titre} » est vide.");
                }
                else if (visual.Kind != "projection") throw new InvalidDataException("Type de contenu inconnu : " + visual.Kind);
                vues.Add(vue);
                var clip = new Grid { Width = width * visual.Width, Height = height * visual.Height, ClipToBounds = true };
                clip.Children.Add(rendu);
                vue.Cadre = clip;
                Canvas.SetLeft(clip, visual.X * width); Canvas.SetTop(clip, visual.Y * height); canvas.Children.Add(clip);
            }
            canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
            timer.Tick += (_, _) => Reconstruire(false);
            Reconstruire(true);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Les sources de la bibliothèque doivent donner une image avant le départ (8 s au plus).</summary>
    public async Task ValidateAsync()
    {
        Reconstruire(true);
        foreach (var v in vues.Where(v => v.Annonce != null))
            foreach (var f in v.Calques.Values.Append(v.Principal).OfType<Fournisseur>())
            {
                var until = Stopwatch.StartNew();
                while (f.Taille == null && until.ElapsedMilliseconds < 8000) await Task.Delay(50);
                if (f.Taille == null) throw new IOException(f.Statut);
            }
        Reconstruire(true);
        if (Error != null) throw new IOException(Error);
    }

    public void Start() => timer.Start();

    /// <summary>
    /// Le fournisseur des images vivantes d'un contenu : le principal (<paramref name="calque"/> null) ou un calque (null : texte,
    /// image fixe d'un calque, ou rien à l'écran).
    /// </summary>
    Fournisseur? FournisseurPour(Vue v, string? calque)
    {
        SourceDisplay? source;
        SourcesDisplay? lecteur;
        if (v.Annonce == null)
        {
            lecteur = calque == null ? App.Sources : App.Calques.LecteurDe(calque);
            source = lecteur?.Source;
            // 2.5.0 : une image fixe dans un calque (un logo) est dessinée par WPF, avec sa transparence
            if (calque != null && source?.Type == "image") return null;
        }
        else
        {
            if (calque != null) return null;
            source = v.Etat is { Mode: "media", Media: { } m } ? m : null;
            lecteur = null;
            if (source != null && source.Type is ("video" or "fenetre" or "image"))
            {
                // vidéo, fenêtre ou image d'une source de la bibliothèque : son propre lecteur, sans toucher à l'écran
                v.Premier ??= new SourcesDisplay("");
                v.Premier.ChoisirSource(source);
                lecteur = v.Premier;
            }
        }
        // seules les images vivantes ont un fournisseur ; le reste est dessiné par WPF
        if (source == null || source.Type is not ("mycam" or "camera" or "video" or "fenetre" or "image")) return null;
        string cle = source.Type switch
        {
            "mycam" => "mycam:" + source.Peripherique,
            "camera" => "camera:" + source.Peripherique,
            _ => "lecteur:" + RuntimeHelpers.GetHashCode(lecteur),
        };
        if (fournisseurs.TryGetValue(cle, out var f)) return f;
        f = source.Type switch
        {
            "mycam" when App.Telephone?.Cameras is { } cams => new FournisseurMyCam(cams, source.Peripherique),
            "mycam" => null,
            "camera" => new FournisseurCamera(source.Peripherique),
            _ when lecteur != null => new FournisseurLecteur(lecteur),
            _ => null,
        };
        if (f != null) fournisseurs[cle] = f;
        return f;
    }

    /// <summary>Le contenu d'un état d'écran, pour savoir s'il a changé (un état relu est un nouvel objet).</summary>
    static string Empreinte(EtatEcran? e)
    {
        if (e == null) return "";
        try { return JsonSerializer.Serialize(e); }
        catch (Exception) { return RuntimeHelpers.GetHashCode(e).ToString(); }
    }

    /// <summary>Clé noire ou blanche à la forme de l'image vivante (même mise en page que l'image elle-même).</summary>
    static ImageSource? Cle((int L, int H)? taille, bool blanc)
    {
        if (taille is not { } t || t.L <= 0 || t.H <= 0) return null;
        var d = new DrawingImage(new GeometryDrawing(blanc ? Brushes.White : Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, t.L, t.H))));
        d.Freeze();
        return d;
    }

    void Rendre(Mat dans)
    {
        canvas.UpdateLayout();
        bitmap.Clear();
        bitmap.Render(canvas);
        bitmap.CopyPixels(pixels, width * 4, 0);
        Marshal.Copy(pixels, 0, dans.Data, pixels.Length);
    }

    /// <summary>
    /// Le rendu WPF (fil de l'interface), refait quand le contenu change (au plus 5 fois par seconde) et toutes les 2 s :
    /// clé noire, clé blanche, et la place de chaque image vivante.
    /// </summary>
    void Reconstruire(bool force)
    {
        if (disposed) return;
        try
        {
            var sig = new List<object?>();
            foreach (var v in vues)
            {
                v.Etat = v.Annonce == null ? App.Regie.Etat : App.Regie.EtatAnnonce(v.Annonce) ?? v.Etat;
                v.Principal = FournisseurPour(v, null);
                v.Calques.Clear();
                foreach (var c in v.Etat?.Calques ?? Array.Empty<CalqueEcran>())
                    if (c.Visible) v.Calques[c.Id] = FournisseurPour(v, c.Id);
                sig.Add(Empreinte(v.Etat)); sig.Add(v.Principal); sig.Add(v.Principal?.Taille);
                foreach (var (id, f) in v.Calques) { sig.Add(id); sig.Add(f); sig.Add(f?.Taille); }
            }
            if (!force && sig.SequenceEqual(signature) && Environment.TickCount64 - derniereReconstruction < 2000) return;
            signature = sig;
            derniereReconstruction = Environment.TickCount64;

            foreach (var v in vues)
            {
                if (v.Etat != null) v.Rendu.Afficher(v.Etat);
                v.Rendu.ImageDisplay(Cle(v.Principal?.Taille, false));
                foreach (var c in v.Etat?.Calques ?? Array.Empty<CalqueEcran>())
                    v.Rendu.ImageCalque(c.Id, v.Calques.TryGetValue(c.Id, out var f) && f != null ? Cle(f.Taille, false) : App.Calques.ImageDe(c.Id));
            }
            canvas.UpdateLayout();
            // la place de chaque image vivante dans le canevas, rognée par son dessin et par sa source Display
            var trous = new List<Trou>();
            var images = new List<Rendu.ImageVivante>();
            var tout = new Rect(0, 0, width, height);
            foreach (var v in vues)
            {
                images.Clear();
                v.Rendu.ImagesVivantes(images);
                var cadreVue = v.Cadre.TransformToAncestor(canvas).TransformBounds(new Rect(v.Cadre.RenderSize));
                foreach (var iv in images)
                {
                    var f = iv.Calque == null ? v.Principal : v.Calques.TryGetValue(iv.Calque, out var fc) ? fc : null;
                    var el = iv.Element;
                    double w = el.RenderSize.Width, h = el.RenderSize.Height;
                    if (f == null || w <= 0 || h <= 0) continue;
                    var t = el.TransformToAncestor(canvas);
                    var rog = iv.Cadre.TransformToAncestor(canvas).TransformBounds(new Rect(iv.Cadre.RenderSize));
                    rog.Intersect(cadreVue);
                    rog.Intersect(tout);
                    if (rog.IsEmpty) continue;
                    int x0 = (int)Math.Floor(rog.X), y0 = (int)Math.Floor(rog.Y);
                    int x1 = Math.Min(width, (int)Math.Ceiling(rog.Right)), y1 = Math.Min(height, (int)Math.Ceiling(rog.Bottom));
                    if (x1 <= x0 || y1 <= y0) continue;
                    trous.Add(new Trou(f, iv.Flou, t.Transform(new Point(0, 0)), t.Transform(new Point(w, 0)), t.Transform(new Point(0, h)),
                        new OpenCvSharp.Rect(x0, y0, x1 - x0, y1 - y0)));
                }
            }
            var c1 = new Mat(height, width, MatType.CV_8UC4);
            var d = new Mat(height, width, MatType.CV_8UC4);
            Rendre(c1);
            if (trous.Count > 0)
            {
                foreach (var v in vues)
                {
                    v.Rendu.ImageDisplay(Cle(v.Principal?.Taille, true));
                    foreach (var (id, f) in v.Calques)
                        if (f != null) v.Rendu.ImageCalque(id, Cle(f.Taille, true));
                }
                using var c2 = new Mat(height, width, MatType.CV_8UC4);
                Rendre(c2);
                Cv2.Subtract(c2, c1, d);
            }
            else d.SetTo(Scalar.All(0));
            Calque? ancien;
            var nouveau = new Calque { C1 = c1, D = d, Trous = trous };
            lock (verrouCalque) { ancien = calque; calque = nouveau; }
            ancien?.Dispose();
            // les fournisseurs qui ne servent plus (caméra changée) sont rendus
            var utiles = vues.SelectMany(v => v.Calques.Values.Append(v.Principal)).OfType<Fournisseur>().ToHashSet();
            foreach (var k in fournisseurs.Where(kv => !utiles.Contains(kv.Value)).Select(kv => kv.Key).ToList())
            {
                var f = fournisseurs[k];
                fournisseurs.Remove(k);
                lock (verrouCalque) f.Dispose();
            }
        }
        catch (Exception e) { Error = e.Message; timer.Stop(); Journal.Erreur("enregistrement : composition", e); }
    }

    // ------------------------------------------------------------ fil vidéo de l'enregistrement
    /// <summary>Le retard des images vivantes (MyCam) : l'enregistrement attend ce temps avant de demander l'image d'un instant.</summary>
    public long Retard
    {
        get
        {
            lock (verrouCalque)
            {
                long r = 0;
                if (calque != null) foreach (var t in calque.Trous) r = Math.Max(r, t.Fournisseur.Retard);
                return r;
            }
        }
    }

    /// <summary>L'image de l'instant <paramref name="monde"/> : le rendu WPF, avec les images vivantes de cet instant posées à leur place.</summary>
    public void CopyFrame(byte[] destination, long monde)
    {
        lock (verrouCalque)
        {
            var c = calque;
            if (c == null || disposed) { Array.Clear(destination); return; }
            if (c.Trous.Count == 0) { Marshal.Copy(c.C1.Data, destination, 0, Math.Min(destination.Length, pixels.Length)); return; }
            try
            {
                media.SetTo(Scalar.All(0));
                foreach (var t in c.Trous)
                {
                    if (t.Fournisseur.Lire(monde) is not { } img || img.Image.Empty()) continue;
                    var a = Affine(t, img.Image.Cols, img.Image.Rows, img.Rotation, img.Miroir);
                    if (t.Flou)
                    {
                        // le fond flou d'une caméra en hauteur : flouté au quart de la taille (rapide), puis agrandi
                        using var petit = OpenCv(a, 0.25, 0, 0);
                        Cv2.WarpAffine(img.Image, flouPetit, petit, flouPetit.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
                        double sigma = Rendu.RayonFlou / 3 / 4;
                        Cv2.GaussianBlur(flouPetit, flouPetit, new OpenCvSharp.Size(0, 0), sigma);
                        Cv2.Resize(flouPetit, flouGrand, new OpenCvSharp.Size(width, height), 0, 0, InterpolationFlags.Linear);
                        using var de = new Mat(flouGrand, t.Rognage);
                        using var vers = new Mat(media, t.Rognage);
                        de.CopyTo(vers);
                    }
                    else
                    {
                        using var m = OpenCv(a, 1, t.Rognage.X, t.Rognage.Y);
                        using var vers = new Mat(media, t.Rognage);
                        Cv2.WarpAffine(img.Image, vers, m, t.Rognage.Size, InterpolationFlags.Linear, BorderTypes.Transparent);
                    }
                }
                // image = clé noire + différence × images vivantes (la différence vaut 0 là où il n'y a pas d'image vivante)
                Cv2.Multiply(media, c.D, produit, 1.0 / 255);
                Cv2.Add(c.C1, produit, sortie);
                Marshal.Copy(sortie.Data, destination, 0, Math.Min(destination.Length, pixels.Length));
                erreurImage = null;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // une image ratée ne coupe pas l'enregistrement : le rendu WPF seul, et la cause au journal (une fois)
                if (erreurImage != e.Message) { erreurImage = e.Message; Journal.Erreur("enregistrement : image vivante", e); }
                if (e is IOException) Error ??= e.Message;
                Marshal.Copy(c.C1.Data, destination, 0, Math.Min(destination.Length, pixels.Length));
            }
        }
    }

    /// <summary>
    /// Pixels de l'image brute (coordonnées continues) → canevas : quart de tour (sens horaire), miroir, puis la place de l'élément Image
    /// (son origine et ses axes dans le canevas, pour toute sa taille affichée).
    /// </summary>
    static double[] Affine(Trou t, int largeur, int hauteur, int rotation, bool miroir)
    {
        // brut (x, y) → affiché (u, v) : u = a0 x + a1 y + a2 ; v = a3 x + a4 y + a5
        double W = largeur, H = hauteur;
        double[] r = (rotation & 3) switch
        {
            1 => new[] { 0, -1, H, 1, 0, 0 },
            2 => new[] { -1, 0, W, 0, -1, H },
            3 => new[] { 0, 1, 0, -1, 0, W },
            _ => new[] { 1d, 0, 0, 0, 1, 0 },
        };
        double dw = (rotation & 1) == 1 ? H : W, dh = (rotation & 1) == 1 ? W : H;
        if (miroir) r = new[] { -r[0], -r[1], dw - r[2], r[3], r[4], r[5] };
        // affiché (u, v) → canevas : O + (X − O) u / dw + (Y − O) v / dh
        double cxu = (t.X.X - t.O.X) / dw, cxv = (t.Y.X - t.O.X) / dh, cyu = (t.X.Y - t.O.Y) / dw, cyv = (t.Y.Y - t.O.Y) / dh;
        return new[]
        {
            cxu * r[0] + cxv * r[3], cxu * r[1] + cxv * r[4], cxu * r[2] + cxv * r[5] + t.O.X,
            cyu * r[0] + cyv * r[3], cyu * r[1] + cyv * r[4], cyu * r[2] + cyv * r[5] + t.O.Y,
        };
    }

    /// <summary>La matrice d'OpenCV (centres des pixels aux entiers) pour une affine en coordonnées continues, mise à l'échelle et décalée.</summary>
    static Mat OpenCv(double[] a, double echelle, int dx, int dy)
    {
        double m00 = a[0] * echelle, m01 = a[1] * echelle, m10 = a[3] * echelle, m11 = a[4] * echelle;
        double tx = a[2] * echelle + 0.5 * (m00 + m01) - 0.5 - dx;
        double ty = a[5] * echelle + 0.5 * (m10 + m11) - 0.5 - dy;
        var m = new Mat(2, 3, MatType.CV_64FC1);
        m.Set(0, 0, m00); m.Set(0, 1, m01); m.Set(0, 2, tx);
        m.Set(1, 0, m10); m.Set(1, 1, m11); m.Set(1, 2, ty);
        return m;
    }

    public void Dispose()
    {
        if (disposed) return;
        timer.Stop();
        lock (verrouCalque)
        {
            disposed = true;
            calque?.Dispose(); calque = null;
            foreach (var f in fournisseurs.Values) { try { f.Dispose(); } catch (Exception) { } }
            fournisseurs.Clear();
            media.Dispose(); produit.Dispose(); sortie.Dispose(); flouPetit.Dispose(); flouGrand.Dispose();
        }
        foreach (var v in vues) v.Premier?.Dispose();
        vues.Clear();
    }
}
