using System.Diagnostics;
using System.Runtime.InteropServices;
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

namespace MyVerse.Ui;

/// <summary>
/// MyStream (Siloé Stream 1.6.0) : l'image envoyée sur Internet, la même pour Zoom (MyVirtualCam) et pour YouTube (le direct).
/// <para>
/// Tout se fait hors du fil de l'interface, sur le fil « MyStream » (le clic reste immédiat) :
/// le dessin WPF (texte, fond, bandeau, calques) n'est refait que quand le contenu change, deux fois : les images vivantes remplacées
/// par une clé noire puis blanche ; la différence des deux dit, pour chaque pixel, combien l'image vivante s'y voit.
/// À chaque image d'une caméra, MyCam ou vidéo, OpenCV la pose à sa place : image = clé noire + différence × image vivante
/// (les paroles en bandeau restent par-dessus la caméra). Une caméra seule, sans rien par-dessus, est posée telle quelle.
/// </para>
/// </summary>
public sealed class MyStreamMoteur : IRecordingFrameProvider, IDisposable
{
    readonly MyVirtualCam? camera;
    Dispatcher? fil;
    // le dessin et son image (fil MyStream)
    Canvas canvas = null!;
    Rendu rendu = null!;
    RenderTargetBitmap bitmap = null!;
    byte[] pixels = Array.Empty<byte>();
    DispatcherTimer minuterie = null!;
    public int Largeur { get; private set; } = 1280;
    public int Hauteur { get; private set; } = 720;
    volatile bool dispose;

    // ce qu'on doit montrer (fil MyStream)
    EtatEcran etat = new();
    BitmapSource? image, imageVue;
    (int L, int H)? tailleImage;
    // 2.5.0 : les images des calques (par identifiant de calque)
    readonly Dictionary<string, BitmapSource?> imagesCalques = new(), vuesCalques = new();
    readonly Dictionary<string, (int L, int H)?> taillesCalques = new();
    readonly Dictionary<string, Mat> matsCalques = new();
    bool sale = true, aComposer;
    long dernierDessin, dernierBattement, derniereComposition;

    // le calque : clé noire (C1), différence (D) et la place de chaque image vivante
    /// <summary>La place d'une image vivante : <paramref name="Calque"/> null pour le contenu principal.</summary>
    sealed record Trou(string? Calque, bool Flou, Point O, Point X, Point Y, OpenCvSharp.Rect Rognage);
    sealed class Calque : IDisposable
    {
        public required Mat C1, D;
        public required List<Trou> Trous;
        /// <summary>Une seule image vivante, rien par-dessus : posée telle quelle (sans mélange).</summary>
        public bool Direct;
        public void Dispose() { C1.Dispose(); D.Dispose(); }
    }
    Calque? calque;
    Mat media = new(), produit = new(), sortie = new(), flouPetit = new(), flouGrand = new();
    readonly Mat vivante = new();

    // la dernière image composée, lue par le direct (autre fil)
    readonly object verrouImage = new();
    byte[] derniere = Array.Empty<byte>();
    /// <summary>Images composées depuis le démarrage (pour les essais et le diagnostic).</summary>
    public long Images => Interlocked.Read(ref images);
    long images;
    /// <summary>Le direct tourne : MyStream compose même sans MyVirtualCam.</summary>
    volatile bool direct;

    public MyStreamMoteur(MyVirtualCam? camera, int largeur = 1280, int hauteur = 720)
    {
        this.camera = camera;
        Largeur = largeur; Hauteur = hauteur;
        // les polices du dessin se lisent dans les ressources de l'application, sur le fil de l'interface (une seule fois)
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Rendu).TypeHandle);
        using var pret = new ManualResetEventSlim();
        Exception? erreur = null;
        var t = new Thread(() =>
        {
            try
            {
                fil = Dispatcher.CurrentDispatcher;
                Construire(Largeur, Hauteur);
                minuterie = new DispatcherTimer(DispatcherPriority.Background, fil) { Interval = TimeSpan.FromMilliseconds(15) };
                minuterie.Tick += (_, _) => Tic();
                minuterie.Start();
            }
            catch (Exception x) { erreur = x; }
            pret.Set();
            if (erreur == null) Dispatcher.Run();
        }) { IsBackground = true, Name = "MyStream" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!pret.Wait(10000)) throw new TimeoutException("le fil de MyStream ne démarre pas");
        if (erreur != null) throw erreur;
    }

    /// <summary>Le dessin à la taille de MyStream (fil MyStream).</summary>
    void Construire(int l, int h)
    {
        Largeur = l; Hauteur = h;
        canvas = new Canvas { Width = l, Height = h, Background = Brushes.Black };
        rendu = new Rendu { Transitions = false, SansEffets = true, Width = l, Height = h, Background = Brushes.Black };
        canvas.Children.Add(rendu);
        canvas.Measure(new Size(l, h)); canvas.Arrange(new Rect(0, 0, l, h));
        bitmap = new RenderTargetBitmap(l, h, 96, 96, PixelFormats.Pbgra32);
        pixels = new byte[l * h * 4];
        foreach (var m in new[] { media, produit, sortie, flouPetit, flouGrand }) m.Dispose();
        media = new Mat(h, l, MatType.CV_8UC4); produit = new Mat(h, l, MatType.CV_8UC4); sortie = new Mat(h, l, MatType.CV_8UC4, Scalar.All(0));
        flouPetit = new Mat(Math.Max(1, h / 4), Math.Max(1, l / 4), MatType.CV_8UC4); flouGrand = new Mat(h, l, MatType.CV_8UC4);
        calque?.Dispose(); calque = null;
        lock (verrouImage) derniere = new byte[l * h * 4];
        sale = true;
        rendu.Afficher(etat);
    }

    /// <summary>Envoie un travail au fil MyStream (dans l'ordre d'arrivée).</summary>
    void Fil(Action a)
    {
        if (dispose || fil == null) return;
        try { fil.BeginInvoke(a, DispatcherPriority.Normal); } catch (Exception) { }
    }

    /// <summary>Une image vivante passe d'un fil à l'autre : elle doit être gelée (les captures le sont déjà).</summary>
    static BitmapSource? Gelee(BitmapSource? b)
    {
        if (b == null || b.IsFrozen) return b;
        try { if (b.CanFreeze) { var c = (BitmapSource)b.Clone(); c.Freeze(); return c; } } catch (Exception) { }
        return null;
    }

    // ------------------------------------------------------------ ce que l'interface envoie
    public void Afficher(EtatEcran e) => Fil(() =>
    {
        etat = e;
        // les calques partis ne gardent ni image ni pixels
        var ids = (e.Calques ?? Array.Empty<CalqueEcran>()).Select(c => c.Id).ToHashSet();
        foreach (var id in imagesCalques.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            imagesCalques.Remove(id); vuesCalques.Remove(id); taillesCalques.Remove(id);
            if (matsCalques.Remove(id, out var mat)) mat.Dispose();
        }
        rendu.Afficher(e);
        sale = true;
    });
    /// <summary>L'image vivante du contenu principal (caméra, MyCam, fenêtre, vidéo, image ; la source principale sous le bandeau).</summary>
    public void Media(BitmapSource? b)
    {
        b = Gelee(b);
        Fil(() => { image = b; if (Taille(b) != tailleImage) sale = true; aComposer = true; });
    }
    /// <summary>2.5.0 : l'image vivante d'un calque.</summary>
    public void ImageCalque(string id, BitmapSource? b)
    {
        b = Gelee(b);
        Fil(() =>
        {
            imagesCalques[id] = b;
            // une image fixe d'un calque (un logo PNG) est dans le dessin WPF lui-même : il est refait
            if (Fixe(id) || Taille(b) != (taillesCalques.TryGetValue(id, out var t) ? t : null)) sale = true;
            aComposer = true;
        });
    }

    /// <summary>
    /// Un calque d'image fixe (un logo, une photo) : dessiné par WPF avec sa transparence, sans clé ; les autres (caméra, fenêtre,
    /// vidéo, MyCam) sont posés à chaque image.
    /// </summary>
    bool Fixe(string id) => etat.Calques?.FirstOrDefault(c => c.Id == id)?.Media?.Type == "image";

    /// <summary>Le direct démarre ou s'arrête (MyStream compose aussi sans MyVirtualCam).</summary>
    public void Direct(bool actif) { direct = actif; Fil(() => sale = true); }

    /// <summary>Change la taille de MyStream (720p ou 1080p), hors direct.</summary>
    public void ChangerTaille(int l, int h)
    {
        if (l == Largeur && h == Hauteur) return;
        Fil(() => { if (l != Largeur || h != Hauteur) Construire(l, h); });
    }

    /// <summary>Attend que le fil MyStream ait tout traité (essais, changement de taille avant un direct).</summary>
    public void Attendre()
    {
        if (fil == null || dispose) return;
        try { fil.Invoke(() => { Tic(); }, DispatcherPriority.Background); } catch (Exception) { }
    }

    static (int L, int H)? Taille(BitmapSource? b) => b is { PixelWidth: > 0, PixelHeight: > 0 } ? (b.PixelWidth, b.PixelHeight) : null;

    bool Utile => camera != null || direct;

    // ------------------------------------------------------------ le travail, sur le fil MyStream
    void Tic()
    {
        try
        {
            if (!Utile) return;
            long maintenant = Environment.TickCount64;
            // pas plus de 15 dessins WPF par seconde (le reste de l'image suit à chaque image vivante)
            if (sale && maintenant - dernierDessin >= 66)
            {
                dernierDessin = maintenant;
                long t0 = Stopwatch.GetTimestamp();
                Reconstruire();
                Perf.Ajouter("MyStream : dessin (sur son fil)", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                aComposer = true;
            }
            // 1.6.5 : une caméra à 60 images par seconde : MyStream en compose au plus une toutes les 30 ms (Zoom et le direct sont à 30)
            if (aComposer && maintenant - derniereComposition >= 30)
            {
                aComposer = false;
                derniereComposition = maintenant;
                Composer();
                dernierBattement = maintenant;
                return;
            }
            // rien de neuf : l'heure seulement, deux fois par seconde (le filtre de Zoom montre du noir après 2 s sans nouvelles)
            if (maintenant - dernierBattement >= 500) { dernierBattement = maintenant; camera?.Battre(); }
        }
        catch (Exception e) { Journal.Erreur("MyStream", e); sale = false; }
    }

    /// <summary>Clé noire ou blanche de la taille de l'image vivante (même mise en page que l'image elle-même).</summary>
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
        bitmap.CopyPixels(pixels, Largeur * 4, 0);
        Marshal.Copy(pixels, 0, dans.Data, pixels.Length);
    }

    /// <summary>Le dessin WPF du moment : clé noire, clé blanche, et la place de chaque image vivante.</summary>
    void Reconstruire()
    {
        sale = false;
        tailleImage = Taille(image);
        rendu.ImageDisplay(Cle(tailleImage, false));
        foreach (var c in etat.Calques ?? Array.Empty<CalqueEcran>())
        {
            imagesCalques.TryGetValue(c.Id, out var ic);
            taillesCalques[c.Id] = Taille(ic);
            rendu.ImageCalque(c.Id, Fixe(c.Id) ? ic : Cle(Taille(ic), false));
        }
        canvas.UpdateLayout();
        var trous = new List<Trou>();
        var vivantes = new List<Rendu.ImageVivante>();
        rendu.ImagesVivantes(vivantes);
        var tout = new Rect(0, 0, Largeur, Hauteur);
        foreach (var iv in vivantes)
        {
            if (iv.Calque != null && Fixe(iv.Calque)) continue;
            if ((iv.Calque == null ? tailleImage : taillesCalques.TryGetValue(iv.Calque, out var tc) ? tc : null) == null) continue;
            var el = iv.Element;
            double w = el.RenderSize.Width, h = el.RenderSize.Height;
            if (w <= 0 || h <= 0) continue;
            var t = el.TransformToAncestor(canvas);
            var rog = iv.Cadre.TransformToAncestor(canvas).TransformBounds(new Rect(iv.Cadre.RenderSize));
            rog.Intersect(tout);
            if (rog.IsEmpty) continue;
            int x0 = (int)Math.Floor(rog.X), y0 = (int)Math.Floor(rog.Y);
            int x1 = Math.Min(Largeur, (int)Math.Ceiling(rog.Right)), y1 = Math.Min(Hauteur, (int)Math.Ceiling(rog.Bottom));
            if (x1 <= x0 || y1 <= y0) continue;
            // la place réelle de l'image (Stretch Uniform) dans son cadre : rognée à l'image elle-même
            var place = t.TransformBounds(new Rect(0, 0, w, h));
            place.Intersect(new Rect(x0, y0, x1 - x0, y1 - y0));
            if (place.IsEmpty) continue;
            int px0 = Math.Max(x0, (int)Math.Floor(place.X)), py0 = Math.Max(y0, (int)Math.Floor(place.Y));
            int px1 = Math.Min(x1, (int)Math.Ceiling(place.Right)), py1 = Math.Min(y1, (int)Math.Ceiling(place.Bottom));
            if (px1 <= px0 || py1 <= py0) continue;
            trous.Add(new Trou(iv.Calque, iv.Flou, t.Transform(new Point(0, 0)), t.Transform(new Point(w, 0)), t.Transform(new Point(0, h)),
                iv.Flou ? new OpenCvSharp.Rect(x0, y0, x1 - x0, y1 - y0) : new OpenCvSharp.Rect(px0, py0, px1 - px0, py1 - py0)));
        }
        var c1 = new Mat(Hauteur, Largeur, MatType.CV_8UC4);
        var d = new Mat(Hauteur, Largeur, MatType.CV_8UC4);
        Rendre(c1);
        if (trous.Count > 0)
        {
            rendu.ImageDisplay(Cle(tailleImage, true));
            foreach (var c in etat.Calques ?? Array.Empty<CalqueEcran>())
                if (!Fixe(c.Id)) rendu.ImageCalque(c.Id, Cle(taillesCalques.TryGetValue(c.Id, out var tc) ? tc : null, true));
            using var c2 = new Mat(Hauteur, Largeur, MatType.CV_8UC4);
            Rendre(c2);
            Cv2.Subtract(c2, c1, d);
        }
        else d.SetTo(Scalar.All(0));
        bool seule = trous.Count == 1 && !trous[0].Flou && Plein(c1, d, trous[0].Rognage);
        var ancien = calque;
        calque = new Calque { C1 = c1, D = d, Trous = trous, Direct = seule };
        ancien?.Dispose();
    }

    /// <summary>L'image vivante se voit entièrement dans sa place (rien par-dessus) : on peut la poser telle quelle.</summary>
    static bool Plein(Mat c1, Mat d, OpenCvSharp.Rect r)
    {
        using var zd = new Mat(d, r);
        using var zc = new Mat(c1, r);
        using var md = new Mat();
        using var mc = new Mat();
        Cv2.InRange(zd, new Scalar(255, 255, 255, 0), new Scalar(255, 255, 255, 255), md);
        Cv2.InRange(zc, new Scalar(0, 0, 0, 0), new Scalar(0, 0, 0, 255), mc);
        int n = r.Width * r.Height;
        return Cv2.CountNonZero(md) == n && Cv2.CountNonZero(mc) == n;
    }

    /// <summary>Une image WPF (gelée) → pixels BGRA.</summary>
    static void Copier(BitmapSource b, Mat m)
    {
        BitmapSource s = b.Format == PixelFormats.Bgra32 || b.Format == PixelFormats.Bgr32 || b.Format == PixelFormats.Pbgra32 ? b : new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
        m.Create(s.PixelHeight, s.PixelWidth, MatType.CV_8UC4);
        s.CopyPixels(Int32Rect.Empty, m.Data, s.PixelWidth * s.PixelHeight * 4, s.PixelWidth * 4);
    }

    /// <summary>L'image vivante en pixels (convertie une fois par image reçue) : le contenu principal (null) ou un calque.</summary>
    Mat? Vivante(string? calque)
    {
        if (calque == null)
        {
            if (image == null) return null;
            if (!ReferenceEquals(image, imageVue)) { Copier(image, vivante); imageVue = image; }
            return vivante.Empty() ? null : vivante;
        }
        if (!imagesCalques.TryGetValue(calque, out var b) || b == null) return null;
        if (!matsCalques.TryGetValue(calque, out var m)) matsCalques[calque] = m = new Mat();
        if (!ReferenceEquals(b, vuesCalques.TryGetValue(calque, out var vu) ? vu : null)) { Copier(b, m); vuesCalques[calque] = b; }
        return m.Empty() ? null : m;
    }

    /// <summary>L'image de MyStream : le dessin, avec les images vivantes du moment posées à leur place.</summary>
    void Composer()
    {
        var c = calque;
        if (c == null) return;
        long t0 = Stopwatch.GetTimestamp();
        if (c.Trous.Count == 0) { Publier(c.C1); return; }
        if (c.Direct)
        {
            c.C1.CopyTo(sortie);
            var t = c.Trous[0];
            if (Vivante(t.Calque) is { } img)
            {
                using var m = OpenCv(Affine(t, img.Cols, img.Rows), 1, t.Rognage.X, t.Rognage.Y);
                using var vers = new Mat(sortie, t.Rognage);
                Cv2.WarpAffine(img, vers, m, t.Rognage.Size, InterpolationFlags.Linear, BorderTypes.Replicate);
            }
            Publier(sortie);
            Perf.Ajouter("MyStream : image (caméra seule)", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            return;
        }
        media.SetTo(Scalar.All(0));
        foreach (var t in c.Trous)
        {
            if (Vivante(t.Calque) is not { } img) continue;
            var a = Affine(t, img.Cols, img.Rows);
            if (t.Flou)
            {
                // le fond flou d'une caméra en hauteur : flouté au quart de la taille (rapide), puis agrandi
                using var petit = OpenCv(a, 0.25, 0, 0);
                Cv2.WarpAffine(img, flouPetit, petit, flouPetit.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
                Cv2.GaussianBlur(flouPetit, flouPetit, new OpenCvSharp.Size(0, 0), Rendu.RayonFlou / 3 / 4);
                Cv2.Resize(flouPetit, flouGrand, new OpenCvSharp.Size(Largeur, Hauteur), 0, 0, InterpolationFlags.Linear);
                using var de = new Mat(flouGrand, t.Rognage);
                using var vers = new Mat(media, t.Rognage);
                de.CopyTo(vers);
            }
            else
            {
                using var m = OpenCv(a, 1, t.Rognage.X, t.Rognage.Y);
                using var vers = new Mat(media, t.Rognage);
                Cv2.WarpAffine(img, vers, m, t.Rognage.Size, InterpolationFlags.Linear, BorderTypes.Transparent);
            }
        }
        // image = clé noire + différence × images vivantes (la différence vaut 0 là où il n'y a pas d'image vivante)
        Cv2.Multiply(media, c.D, produit, 1.0 / 255);
        Cv2.Add(c.C1, produit, sortie);
        Publier(sortie);
        Perf.Ajouter("MyStream : image (avec texte)", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    /// <summary>L'image composée part vers MyVirtualCam (Zoom) et attend le direct.</summary>
    void Publier(Mat image)
    {
        lock (verrouImage)
        {
            if (derniere.Length == Largeur * Hauteur * 4) Marshal.Copy(image.Data, derniere, 0, derniere.Length);
        }
        Interlocked.Increment(ref images);
        camera?.Ecrire(image);
    }

    /// <summary>
    /// Pixels de l'image (coordonnées continues) → dessin : la place de l'élément Image (son origine et ses axes, pour toute sa taille
    /// affichée). Les images arrivent déjà tournées et en miroir (lecteurs de l'interface).
    /// </summary>
    static double[] Affine(Trou t, int largeur, int hauteur)
    {
        double cxu = (t.X.X - t.O.X) / largeur, cxv = (t.Y.X - t.O.X) / hauteur, cyu = (t.X.Y - t.O.Y) / largeur, cyv = (t.Y.Y - t.O.Y) / hauteur;
        return new[] { cxu, cxv, t.O.X, cyu, cyv, t.O.Y };
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

    // ------------------------------------------------------------ le direct (fil vidéo de la session)
    public long Retard => 0;

    /// <summary>La dernière image de MyStream (BGRA, à la taille de MyStream).</summary>
    public void CopyFrame(byte[] destination, long monde)
    {
        lock (verrouImage)
        {
            if (destination.Length == derniere.Length) Buffer.BlockCopy(derniere, 0, destination, 0, derniere.Length);
            else Array.Clear(destination);
        }
    }

    /// <summary>Le pixel (B, V, R) au point (x, y) de la dernière image (essais).</summary>
    public (byte B, byte G, byte R) Pixel(int x, int y)
    {
        lock (verrouImage)
        {
            int i = (y * Largeur + x) * 4;
            return i >= 0 && i + 2 < derniere.Length ? (derniere[i], derniere[i + 1], derniere[i + 2]) : ((byte)0, (byte)0, (byte)0);
        }
    }

    public void Dispose()
    {
        if (dispose) return;
        dispose = true;
        try { fil?.Invoke(() => { minuterie.Stop(); calque?.Dispose(); calque = null; foreach (var mat in matsCalques.Values) mat.Dispose(); matsCalques.Clear(); }); fil?.InvokeShutdown(); } catch (Exception) { }
    }
}
