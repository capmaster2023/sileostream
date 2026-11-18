using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MyVerse.Miniatures;

namespace MyVerse.Ui.Creation;

/// <summary>
/// Le dessin d'une miniature (1280 × 720) : le même pour l'éditeur, les aperçus des modèles et l'export. Ce qu'on voit à l'écran
/// est exactement ce qui part sur YouTube.
/// </summary>
public static class RenduMiniature
{
    const double W = ProjetMiniature.Largeur, H = ProjetMiniature.Hauteur;
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Les polices proposées : celles livrées avec Siloé Stream, puis celles de Windows faites pour les titres.</summary>
    public static readonly string[] Polices = { "Impact", "Unbounded", "Bahnschrift", "Arial Black", "Segoe UI Black", "Manrope", "Georgia", "Literata", "Trebuchet MS", "Verdana" };
    static readonly Dictionary<string, FontFamily> polices = new();

    public static FontFamily Police(string nom)
    {
        lock (polices)
        {
            if (polices.TryGetValue(nom, out var f)) return f;
            f = nom switch
            {
                "Unbounded" or "Manrope" or "Literata" => new FontFamily(new Uri("pack://application:,,,/"), "./Polices/#" + nom),
                _ => new FontFamily(nom),
            };
            polices[nom] = f;
            return f;
        }
    }

    public static Color Couleur(string hex)
    {
        var (a, r, g, b) = Couleurs.Lire(hex);
        return Color.FromArgb(a, r, g, b);
    }

    static SolidColorBrush Pinceau(string hex)
    {
        var b = new SolidColorBrush(Couleur(hex));
        b.Freeze();
        return b;
    }

    // ================================================================================================ les images (filtrées, gardées)
    sealed record CleImage(string Chemin, DateTime Date, double Luminosite, double Contraste, double Saturation);
    static readonly Dictionary<CleImage, BitmapSource> images = new();
    static readonly Dictionary<(string, DateTime), BitmapSource> originaux = new();

    /// <summary>L'image du fichier, avec luminosité, contraste et saturation appliqués (null si elle ne se lit pas).</summary>
    public static BitmapSource? Image(string chemin, double luminosite = 0, double contraste = 1, double saturation = 1)
    {
        if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin)) return null;
        var date = File.GetLastWriteTimeUtc(chemin);
        var cle = new CleImage(chemin, date, Math.Round(luminosite, 3), Math.Round(contraste, 3), Math.Round(saturation, 3));
        lock (images) if (images.TryGetValue(cle, out var deja)) return deja;
        BitmapSource? source;
        lock (originaux) originaux.TryGetValue((chemin, date), out source);
        if (source == null)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.UriSource = new Uri(chemin);
                bi.EndInit();
                // assez de détails pour 1280 × 720 avec un zoom, sans garder une 4K entière en mémoire
                source = bi.PixelWidth > 2560 ? new TransformedBitmap(bi, new ScaleTransform(2560.0 / bi.PixelWidth, 2560.0 / bi.PixelWidth)) : bi;
                source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                source = new WriteableBitmap(source);
                source.Freeze();
            }
            catch (Exception e) { Journal.Erreur("miniature : image " + chemin, e); return null; }
            lock (originaux) { if (originaux.Count > 12) originaux.Clear(); originaux[(chemin, date)] = source; }
        }
        BitmapSource resultat = source;
        if (Math.Abs(cle.Luminosite) > 0.001 || Math.Abs(cle.Contraste - 1) > 0.001 || Math.Abs(cle.Saturation - 1) > 0.001)
            resultat = Filtrer(source, cle.Luminosite, cle.Contraste, cle.Saturation);
        lock (images) { if (images.Count > 40) images.Clear(); images[cle] = resultat; }
        return resultat;
    }

    static BitmapSource Filtrer(BitmapSource s, double lum, double con, double sat)
    {
        int w = s.PixelWidth, h = s.PixelHeight, pas = w * 4;
        var px = new byte[pas * h];
        s.CopyPixels(px, pas, 0);
        // une table par composante : contraste autour du gris moyen, puis luminosité
        var table = new byte[256];
        for (int i = 0; i < 256; i++) table[i] = (byte)Math.Clamp(((i - 128) * con + 128) + lum * 255, 0, 255);
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            if (Math.Abs(sat - 1) > 0.001)
            {
                double gris = 0.299 * r + 0.587 * g + 0.114 * b;
                r = gris + (r - gris) * sat; g = gris + (g - gris) * sat; b = gris + (b - gris) * sat;
            }
            px[i] = table[(int)Math.Clamp(b, 0, 255)];
            px[i + 1] = table[(int)Math.Clamp(g, 0, 255)];
            px[i + 2] = table[(int)Math.Clamp(r, 0, 255)];
        }
        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), px, pas, 0);
        wb.Freeze();
        return wb;
    }

    // ================================================================================================ la scène
    /// <summary>La miniature dessinée : un Canvas de 1280 × 720 ; <paramref name="elements"/> reçoit l'élément de chaque calque.</summary>
    public static Canvas Construire(ProjetMiniature p, Dictionary<string, FrameworkElement>? elements = null)
    {
        var scene = new Canvas { Width = W, Height = H, ClipToBounds = true, Background = Pinceau(p.Fond), SnapsToDevicePixels = true };
        foreach (var c in p.Calques)
        {
            if (!c.Visible) continue;
            var e = Element(c);
            Canvas.SetLeft(e, c.X);
            Canvas.SetTop(e, c.Y);
            e.Opacity = c.Opacite;
            if (Math.Abs(c.Rotation) > 0.01) e.RenderTransform = new RotateTransform(c.Rotation, c.L / 2, c.H / 2);
            scene.Children.Add(e);
            if (elements != null) elements[c.Id] = e;
        }
        return scene;
    }

    public static FrameworkElement Element(Calque c) => c.Type switch
    {
        Calque.TypeImage => ElementImage(c),
        Calque.TypeTexte => new TexteMiniature(c),
        Calque.TypeDegrade => ElementDegrade(c),
        _ => ElementForme(c),
    };

    static FrameworkElement ElementImage(Calque c)
    {
        var cadre = new Canvas { Width = c.L, Height = c.H, ClipToBounds = true };
        var b = Image(c.Chemin, c.Luminosite, c.Contraste, c.Saturation);
        if (b == null)
        {
            cadre.Children.Add(new Rectangle { Width = c.L, Height = c.H, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), Stroke = Brushes.White, StrokeDashArray = new DoubleCollection { 4, 4 } });
            var t = new TextBlock { Text = c.Chemin.Length > 0 ? "Image introuvable" : "Image", Foreground = Brushes.White, FontSize = Math.Max(14, Math.Min(c.L, c.H) / 8) };
            Canvas.SetLeft(t, 12); Canvas.SetTop(t, 8);
            cadre.Children.Add(t);
            return cadre;
        }
        double sx = c.L / b.PixelWidth, sy = c.H / b.PixelHeight;
        double k = (c.Ajustement == "ajuster" ? Math.Min(sx, sy) : Math.Max(sx, sy)) * c.Zoom;
        double w = b.PixelWidth * k, h = b.PixelHeight * k;
        var img = new System.Windows.Controls.Image { Source = b, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(img, (c.L - w) * c.CadrageX);
        Canvas.SetTop(img, (c.H - h) * c.CadrageY);
        if (c.Miroir) img.RenderTransform = new ScaleTransform(-1, 1, w / 2, h / 2);
        if (c.Flou > 0.1) img.Effect = new BlurEffect { Radius = c.Flou, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        cadre.Children.Add(img);
        if (c.Arrondi > 0) cadre.Clip = new RectangleGeometry(new Rect(0, 0, c.L, c.H), c.Arrondi, c.Arrondi);
        if (c.EpaisseurBordure > 0)
            cadre.Children.Add(new Rectangle { Width = c.L, Height = c.H, RadiusX = c.Arrondi, RadiusY = c.Arrondi, Stroke = Pinceau(c.Bordure), StrokeThickness = c.EpaisseurBordure * 2 });
        return cadre;
    }

    static FrameworkElement ElementForme(Calque c)
    {
        Shape s = c.FormeType switch
        {
            "cercle" => new Ellipse(),
            "fleche" => new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0.32 H0.6 V0.04 L1,0.5 L0.6,0.96 V0.68 H0 Z"), Stretch = Stretch.Fill },
            _ => new Rectangle { RadiusX = c.Arrondi, RadiusY = c.Arrondi },
        };
        s.Width = c.L;
        s.Height = c.H;
        s.Fill = Pinceau(c.Remplissage);
        if (c.EpaisseurBordure > 0) { s.Stroke = Pinceau(c.Bordure); s.StrokeThickness = c.EpaisseurBordure; s.StrokeLineJoin = PenLineJoin.Round; }
        return s;
    }

    static FrameworkElement ElementDegrade(Calque c)
    {
        var col = Couleur(c.Remplissage);
        Color A(double a) => Color.FromArgb((byte)Math.Clamp(a * 255 * (col.A / 255.0), 0, 255), col.R, col.G, col.B);
        Brush b;
        if (c.Direction == "vignette")
        {
            b = new RadialGradientBrush
            {
                RadiusX = 0.75, RadiusY = 0.75,
                GradientStops = { new GradientStop(A(0), Math.Max(0, 1 - c.Etendue)), new GradientStop(A(c.Force * 0.55), 1 - c.Etendue / 2), new GradientStop(A(c.Force), 1) },
            };
        }
        else
        {
            var (debut, fin) = c.Direction switch
            {
                "droite" => (new Point(1, 0.5), new Point(0, 0.5)),
                "haut" => (new Point(0.5, 0), new Point(0.5, 1)),
                "bas" => (new Point(0.5, 1), new Point(0.5, 0)),
                _ => (new Point(0, 0.5), new Point(1, 0.5)),
            };
            b = new LinearGradientBrush
            {
                StartPoint = debut, EndPoint = fin,
                GradientStops = { new GradientStop(A(c.Force), 0), new GradientStop(A(c.Force * 0.75), c.Etendue * 0.45), new GradientStop(A(0), c.Etendue) },
            };
        }
        b.Freeze();
        return new Rectangle { Width = c.L, Height = c.H, Fill = b };
    }

    // ================================================================================================ export
    /// <summary>La miniature en image (1280 × 720 par défaut), sans rien de l'éditeur.</summary>
    public static BitmapSource Rendre(ProjetMiniature p, int largeur = (int)W)
    {
        var scene = Construire(p);
        scene.Measure(new Size(W, H));
        scene.Arrange(new Rect(0, 0, W, H));
        scene.UpdateLayout();
        double k = largeur / W;
        var rtb = new RenderTargetBitmap(largeur, (int)Math.Round(H * k), 96, 96, PixelFormats.Pbgra32);
        if (Math.Abs(k - 1) < 0.001) rtb.Render(scene);
        else
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen()) { dc.PushTransform(new ScaleTransform(k, k)); dc.DrawRectangle(new VisualBrush(scene), null, new Rect(0, 0, W, H)); }
            rtb.Render(v);
        }
        rtb.Freeze();
        return rtb;
    }

    /// <summary>La miniature en JPEG d'au plus 2 Mo (la qualité baisse un peu si besoin) ; rend aussi la qualité retenue.</summary>
    public static (byte[] Octets, int Qualite) Jpeg(ProjetMiniature p)
    {
        var image = Rendre(p);
        byte[] octets = Array.Empty<byte>();
        foreach (int q in new[] { 95, 90, 85, 80, 72, 64, 55 })
        {
            var enc = new JpegBitmapEncoder { QualityLevel = q };
            enc.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            enc.Save(ms);
            octets = ms.ToArray();
            if (octets.Length <= ProjetMiniature.TailleMax) return (octets, q);
        }
        return (octets, 55);
    }

    public static byte[] Png(ProjetMiniature p)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Rendre(p)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Un texte de miniature : contour épais (le style des miniatures YouTube), mots en couleur d'accent, fond plein facultatif
/// (badge, bandeau), ombre ou lueur. Centré verticalement dans son cadre ; les lignes reviennent toutes seules.
/// </summary>
public sealed class TexteMiniature : FrameworkElement
{
    readonly Calque c;
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public TexteMiniature(Calque c)
    {
        this.c = c;
        Width = c.L;
        Height = c.H;
        if (c.Ombre)
            Effect = new DropShadowEffect
            {
                Color = RenduMiniature.Couleur(c.OmbreCouleur), BlurRadius = c.OmbreFlou, ShadowDepth = c.OmbreDistance, Direction = 300,
                Opacity = 0.9, RenderingBias = RenderingBias.Quality,
            };
    }

    /// <summary>Le texte mis en forme pour une largeur et une taille données (sert aussi à « Ajuster la taille »).</summary>
    public static FormattedText Mettre(Calque c, double taille, double pixelsParDip = 1)
    {
        var (texte, accents) = Calque.Accents(c.Texte);
        if (c.Majuscules) texte = texte.ToUpper(Fr);
        if (texte.Length == 0) texte = " ";
        var tf = new Typeface(RenduMiniature.Police(c.Police), c.Italique ? FontStyles.Italic : FontStyles.Normal, FontWeight.FromOpenTypeWeight(c.Poids), FontStretches.Normal);
        double marge = c.FondTexte.Length > 0 ? c.Marge : 0;
        var ft = new FormattedText(texte, Fr, FlowDirection.LeftToRight, tf, taille, new SolidColorBrush(RenduMiniature.Couleur(c.Couleur)), pixelsParDip)
        {
            MaxTextWidth = Math.Max(10, c.L - 2 * marge - c.Epaisseur * 2),
            TextAlignment = c.Alignement switch { "centre" => TextAlignment.Center, "droite" => TextAlignment.Right, _ => TextAlignment.Left },
            LineHeight = taille * c.Interligne,
            Trimming = TextTrimming.None,
        };
        var accent = new SolidColorBrush(RenduMiniature.Couleur(c.Accent));
        foreach (var (d, l) in accents)
            if (d < texte.Length) ft.SetForegroundBrush(accent, d, Math.Min(l, texte.Length - d));
        return ft;
    }

    /// <summary>La plus grande taille où le texte tient dans son cadre sans couper un mot.</summary>
    public static double TailleQuiTient(Calque c)
    {
        double bas = 8, haut = 400;
        double hauteurUtile = c.H - 2 * (c.FondTexte.Length > 0 ? c.Marge : 0) - c.Epaisseur * 2;
        for (int i = 0; i < 22; i++)
        {
            double t = (bas + haut) / 2;
            var ft = Mettre(c, t);
            bool tient = ft.Height <= hauteurUtile && ft.MinWidth <= ft.MaxTextWidth + 0.5;
            if (tient) bas = t; else haut = t;
        }
        return Math.Floor(bas);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (c.FondTexte.Length > 0)
            dc.DrawRoundedRectangle(new SolidColorBrush(RenduMiniature.Couleur(c.FondTexte)), null, new Rect(0, 0, c.L, c.H), c.Arrondi, c.Arrondi);
        if (Calque.Accents(c.Texte).Texte.Trim().Length == 0) return;
        double ppd = 1;
        try { ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { }
        var ft = Mettre(c, c.Taille, ppd);
        double marge = c.FondTexte.Length > 0 ? c.Marge : 0;
        var origine = new Point(marge + c.Epaisseur, (c.H - ft.Height) / 2);
        if (c.Epaisseur > 0)
        {
            var g = ft.BuildGeometry(origine);
            var stylo = new Pen(new SolidColorBrush(RenduMiniature.Couleur(c.Contour)), c.Epaisseur * 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, stylo, g);
        }
        dc.DrawText(ft, origine);
    }
}
