using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MyVerse.Ui;

/// <summary>
/// Dessin de l'écran de projection (comme ecran.html de la 3.x) : fond du thème, texte à la plus grande taille
/// qui tient à l'écran (lignes équilibrées), 2e version en italique, référence en or ; plein écran ou bandeau.
/// Utilisé par la fenêtre de projection et par l'aperçu de la régie.
/// </summary>
public sealed class Rendu : Grid
{
    readonly Border fond = new();
    readonly Image media = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    /// <summary>
    /// Derrière une caméra en hauteur (MyCam tenu à la verticale) ou en 4:3 : la même image, agrandie et floutée, remplit les côtés
    /// au lieu de bandes noires, comme à la télé. Effet de la carte graphique.
    /// </summary>
    readonly Image mediaFlou = new()
    {
        // débordée de 90 px de chaque côté et centrée : le bord estompé par le flou tombe hors de l'écran (le dessin est rogné à ses bords)
        Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed, Opacity = 0.5, IsHitTestVisible = false,
        Margin = new Thickness(-90), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    /// <summary>Rayon du flou derrière une caméra en hauteur (repris par l'enregistrement, qui floute lui-même).</summary>
    internal const double RayonFlou = 56;
    readonly BlurEffect flou = new() { Radius = RayonFlou, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    readonly Border scene = new();
    readonly StackPanel pile = new();
    readonly TextBlock texte = new(), texte2 = new(), refT = new();
    EtatEcran etat = new();
    string cle = "";
    int version;

    /// <summary>Fondu entre deux diapositives (fenêtre de projection) ; sans fondu pour l'aperçu.</summary>
    public bool Transitions { get; set; } = true;

    /// <summary>Fond flou derrière une caméra en hauteur ou en 4:3 (coupé pour la caméra virtuelle, dessinée sans carte graphique).</summary>
    public bool FondFlou { get; set; } = true;

    static readonly SolidColorBrush Blanc = Gele(new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEC)));
    static readonly SolidColorBrush Or2 = Gele(new SolidColorBrush(Color.FromRgb(0xF6, 0xD6, 0x8A)));
    static readonly SolidColorBrush FondBandeau = Gele(new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x10, 0x28)));
    // fonds clairs « Lin » et « Ciel » : texte foncé, sans ombre
    static readonly SolidColorBrush FondBandeauClair = Gele(new SolidColorBrush(Color.FromArgb(0xF0, 0xFB, 0xF8, 0xF2)));
    static readonly SolidColorBrush EncreLin = Gele(new SolidColorBrush(Color.FromRgb(0x1F, 0x1B, 0x16)));
    static readonly SolidColorBrush EncreCiel = Gele(new SolidColorBrush(Color.FromRgb(0x14, 0x21, 0x3D)));
    static readonly SolidColorBrush RefLin = Gele(new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x12)));
    static readonly SolidColorBrush RefCiel = Gele(new SolidColorBrush(Color.FromRgb(0x1E, 0x4F, 0xB0)));
    static bool Clair(string? t) => t is "lin" or "ciel";
    readonly DropShadowEffect ombre = new() { Color = Colors.Black, Opacity = 0.55, BlurRadius = 18, ShadowDepth = 3, Direction = 270, RenderingBias = RenderingBias.Performance };
    static T Gele<T>(T f) where T : Freezable { f.Freeze(); return f; }

    static FontFamily Police(string cle, string secours)
    {
        try { if (Application.Current?.TryFindResource(cle) is FontFamily f) return f; } catch (Exception) { }
        return new FontFamily(secours);
    }
    static readonly FontFamily Serif = Police("Serif", "Georgia");
    static readonly FontFamily Sans = Police("Sans", "Segoe UI");

    public Rendu()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        mediaFlou.Effect = flou;
        // 1.6.0 : le texte au-dessus de l'image (MyStream : les paroles en bandeau sur la caméra)
        Children.Add(fond);
        Children.Add(mediaFlou);
        Children.Add(media);
        Children.Add(scene);
        scene.Child = pile;
        pile.VerticalAlignment = VerticalAlignment.Center;
        pile.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var t in new[] { texte, texte2, refT })
        {
            t.TextAlignment = TextAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextWrapping = TextWrapping.WrapWithOverflow;
            t.Foreground = Blanc;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            pile.Children.Add(t);
        }
        texte2.FontStyle = FontStyles.Italic;
        texte2.FontFamily = Serif;
        texte2.Opacity = 0.86;
        refT.FontFamily = Sans;
        refT.FontWeight = FontWeights.Bold;
        refT.Foreground = Or2;
        pile.Effect = ombre;
        scene.Opacity = 0;
        fond.Opacity = 0;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SizeChanged += (_, _) => { Placer(); Ajuster(); PlacerCalques(); };
    }

    public void ImageDisplay(ImageSource? image)
    {
        media.Source = image;
        MajFlou();
    }

    // ------------------------------------------------------------ Siloé Stream 1.2 : les images vivantes, pour l'enregistrement
    /// <summary>
    /// Une image vivante du dessin : l'élément, son contenu (<paramref name="Calque"/> : l'identifiant du calque, null pour le contenu
    /// principal), net ou fond flou ; et le dessin qui la rogne.
    /// </summary>
    internal readonly record struct ImageVivante(Image Element, string? Calque, bool Flou, FrameworkElement Cadre);

    /// <summary>
    /// Les images vivantes visibles (caméra, MyCam, vidéo…), dans l'ordre du dessin (dessous d'abord) : l'enregistrement et MyStream
    /// les dessinent eux-mêmes à chaque image, le reste (fond, texte, calques de texte) est rendu par WPF quelques fois par seconde.
    /// 2.5.0 : les calques à leur étage, le contenu principal au sien.
    /// </summary>
    internal void ImagesVivantes(List<ImageVivante> liste)
    {
        if (Visibility != Visibility.Visible) return;
        for (int p = 0; p <= listeCalques.Count; p++)
        {
            if (p == rang) { ImagesVivantesSeul(liste, null); continue; }
            var c = listeCalques[p < rang ? p : p - 1];
            if (c.Visible && calques.TryGetValue(c.Id, out var enfant)) enfant.ImagesVivantesSeul(liste, c.Id);
        }
    }

    void ImagesVivantesSeul(List<ImageVivante> liste, string? calque)
    {
        if (Visibility != Visibility.Visible) return;
        if (mediaFlou.Visibility == Visibility.Visible && mediaFlou.Source != null) liste.Add(new(mediaFlou, calque, true, this));
        if (media.Visibility == Visibility.Visible && media.Source != null) liste.Add(new(media, calque, false, this));
    }

    bool leger;
    /// <summary>
    /// 1.5.1 : un petit aperçu (vignettes, Ensuite, la télécommande, les miniatures) : sans l'ombre floue sous le texte ni le fond flou
    /// des caméras. À cette taille on ne les voit presque pas, et ce sont eux qui coûtent le plus à dessiner.
    /// </summary>
    public bool Leger
    {
        get => leger;
        set
        {
            leger = value;
            if (value) { pile.Effect = null; mediaFlou.Effect = null; FondFlou = false; }
            foreach (var c in calques.Values) c.Leger = value;
        }
    }

    bool sansEffets;
    /// <summary>
    /// Dessin pour l'enregistrement (Siloé Stream 1.2) : le fond flou est rendu sans son effet (l'enregistrement le floute lui-même à chaque image),
    /// et les dessins des calques aussi.
    /// </summary>
    internal bool SansEffets
    {
        get => sansEffets;
        set
        {
            sansEffets = value;
            mediaFlou.Effect = value || leger ? null : flou;
            foreach (var c in calques.Values) c.SansEffets = value;
        }
    }

    /// <summary>Le fond flou ne sert qu'aux caméras (MyCam, caméra USB) plus étroites que l'écran : une image ou une vidéo garde ses bandes.</summary>
    void MajFlou()
    {
        bool flou = FondFlou && (etat.Mode == "media" || Regie.TexteSurImage(etat)) && (etat.Media?.Type is "mycam" or "camera")
            && media.Source is { Width: > 0, Height: > 0 } b && b.Width / b.Height < 1.6;
        var source = flou ? media.Source : null;
        if (!ReferenceEquals(mediaFlou.Source, source)) mediaFlou.Source = source;
        var v = flou ? Visibility.Visible : Visibility.Collapsed;
        if (mediaFlou.Visibility != v) mediaFlou.Visibility = v;
    }

    // ------------------------------------------------------------ 2.5.0 : les calques (sources superposées, comme dans OBS)
    readonly Dictionary<string, Rendu> calques = new();
    readonly Dictionary<string, ImageSource?> imagesCalques = new();
    IReadOnlyList<CalqueEcran> listeCalques = Array.Empty<CalqueEcran>();
    /// <summary>L'étage du contenu principal dans la pile (le nombre de calques dessous).</summary>
    int rang;
    /// <summary>Le dessin d'un calque : un média n'a pas de fond noir derrière lui (un logo PNG garde sa transparence).</summary>
    bool dansCalque;

    /// <summary>L'image vivante (caméra, fenêtre, image, vidéo) d'un calque.</summary>
    public void ImageCalque(string id, ImageSource? image)
    {
        imagesCalques[id] = image;
        if (calques.TryGetValue(id, out var c)) { c.media.Source = image; c.MajFlou(); }
    }

    /// <summary>Taille connue : celle du dessin, sinon celle demandée (aperçu 1920 × 1080, caméra virtuelle) avant la première mise en page.</summary>
    static double Cote(double reel, double demande) => reel > 0 ? reel : double.IsNaN(demande) ? 0 : demande;

    /// <summary>Les calques de l'état : un dessin chacun, à sa place, dans l'ordre de la pile (le contenu principal à son étage).</summary>
    void MajCalques(EtatEcran e)
    {
        var ids = listeCalques.Select(c => c.Id).ToHashSet();
        foreach (var id in calques.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            Children.Remove(calques[id]);
            calques.Remove(id);
            imagesCalques.Remove(id);
        }
        foreach (var c in listeCalques)
        {
            if (!calques.TryGetValue(c.Id, out var r))
            {
                r = new Rendu { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, SansEffets = sansEffets, Leger = leger, FondFlou = FondFlou, dansCalque = true };
                calques[c.Id] = r;
                Children.Add(r);
            }
            r.Transitions = Transitions;
            r.Visibility = c.Visible ? Visibility.Visible : Visibility.Collapsed;
            r.AfficherSeul(c.Etat with { Theme = e.Theme, Taille = e.Taille, Presentation = "plein", Calques = null, Rang = 0 });
            imagesCalques.TryGetValue(c.Id, out var image);
            if (!ReferenceEquals(r.media.Source, image)) { r.media.Source = image; r.MajFlou(); }
        }
        // l'ordre : le fond tout en bas, puis chaque étage (dessous d'abord) ; le contenu principal : fond flou, image, texte
        SetZIndex(fond, 0);
        for (int p = 0; p <= listeCalques.Count; p++)
        {
            int z = 10 * (p + 1);
            if (p == rang) { SetZIndex(mediaFlou, z); SetZIndex(media, z + 1); SetZIndex(scene, z + 2); continue; }
            if (calques.TryGetValue(listeCalques[p < rang ? p : p - 1].Id, out var r)) SetZIndex(r, z);
        }
        PlacerCalques();
    }

    void PlacerCalques()
    {
        if (calques.Count == 0) return;
        double W = Cote(ActualWidth, Width), H = Cote(ActualHeight, Height);
        if (W <= 0 || H <= 0) return;
        foreach (var c in listeCalques)
        {
            if (!calques.TryGetValue(c.Id, out var r)) continue;
            r.Width = Math.Max(1, W * c.L);
            r.Height = Math.Max(1, H * c.H);
            r.Margin = new Thickness(W * c.X, H * c.Y, 0, 0);
        }
    }

    /// <summary>Pendant qu'on le glisse (page Médias) : le calque se dessine à cette place, sans attendre la régie.</summary>
    internal void PlacerCalqueLocal(string id, double x, double y, double l, double h)
    {
        if (!calques.TryGetValue(id, out var r)) return;
        double W = Cote(ActualWidth, Width), H = Cote(ActualHeight, Height);
        if (W <= 0 || H <= 0) return;
        r.Width = Math.Max(1, W * l);
        r.Height = Math.Max(1, H * h);
        r.Margin = new Thickness(W * x, H * y, 0, 0);
    }

    /// <summary>Affiche un nouvel état : le contenu principal, et (2.5.0) les calques par-dessus ou dessous, à leur étage.</summary>
    public void Afficher(EtatEcran e)
    {
        listeCalques = e.Calques ?? (IReadOnlyList<CalqueEcran>)Array.Empty<CalqueEcran>();
        rang = Math.Clamp(e.Rang, 0, listeCalques.Count);
        AfficherSeul(e with { Calques = null, Rang = 0 });
        MajCalques(e);
    }

    // ------------------------------------------------------------ fonds des thèmes
    static Brush FondTheme(string theme)
    {
        switch (theme)
        {
            case "noir": return Brushes.Black;
            case "vert": return Gele(new SolidColorBrush(Color.FromRgb(0x00, 0xB1, 0x40)));
            case "transparent": return Brushes.Transparent;
            case "lin": return Gele(new SolidColorBrush(Color.FromRgb(0xF1, 0xEE, 0xE8)));
            case "ciel":
            {
                var b = new LinearGradientBrush(Color.FromRgb(0xEE, 0xF5, 0xFD), Color.FromRgb(0xDC, 0xE9, 0xF8), 90);
                return Gele(b);
            }
            case "aube":
            {
                var b = new RadialGradientBrush { Center = new Point(0.5, 1.0), GradientOrigin = new Point(0.5, 1.0), RadiusX = 1.1, RadiusY = 0.9 };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x5A, 0x3A, 0x12), 0));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x1A, 0x0A), 0.45));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x06, 0x04), 1));
                return Gele(b);
            }
            default:
            {
                var b = new RadialGradientBrush { Center = new Point(0.5, 0.1), GradientOrigin = new Point(0.5, 0.1), RadiusX = 1.2, RadiusY = 0.9 };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0x30, 0x6A), 0));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x18, 0x3A), 0.45));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(0x05, 0x08, 0x17), 1));
                return Gele(b);
            }
        }
    }

    static void Animer(UIElement e, double vers, double secondes, Action? fini = null)
    {
        if (secondes <= 0)
        {
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = vers;
            fini?.Invoke();
            return;
        }
        var a = new DoubleAnimation(vers, TimeSpan.FromSeconds(secondes)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        if (fini != null) a.Completed += (_, _) => fini();
        e.BeginAnimation(OpacityProperty, a, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Affiche un nouvel état (fondu si le texte change).</summary>
    void AfficherSeul(EtatEcran e)
    {
        var avantTheme = etat.Theme;
        etat = e;
        if (avantTheme != e.Theme || fond.Background == null)
        {
            fond.Background = FondTheme(e.Theme);
            var encre = e.Theme == "ciel" ? EncreCiel : e.Theme == "lin" ? EncreLin : Blanc;
            texte.Foreground = texte2.Foreground = encre;
            refT.Foreground = e.Theme == "ciel" ? RefCiel : e.Theme == "lin" ? RefLin : Or2;
            pile.Effect = Clair(e.Theme) || leger ? null : ombre;
            Placer();
        }
        // MyStream (1.6.0) : les paroles en bandeau sur la source principale (l'image reste dessous)
        bool surImage = Regie.TexteSurImage(e);
        media.Visibility = e.Mode == "media" || surImage ? Visibility.Visible : Visibility.Collapsed;
        MajFlou();
        if (e.Mode == "media")
        {
            version++; cle = "media";
            Animer(scene, 0, 0);
            if (dansCalque) Animer(fond, 0, 0);
            else { Animer(fond, 1, 0); fond.Background = Brushes.Black; }
            return;
        }
        // A live source may have temporarily replaced the theme background.
        fond.Background = surImage ? Brushes.Black : FondTheme(e.Theme);
        bool vide = e.Vide;
        // « fond » (écran par défaut « Fond seul », 3.3) : l'habillage reste, sans texte
        bool sansTexte = vide || e.Mode == "fond";
        // 3.0.0 : sous un chant swahili, sa traduction française (seulement si on la montre)
        var deux = (e.Mode is "verset" or "chant") && e.Second != null ? e.Second.Texte : "";
        var nouvelle = string.Join("\u0001", new[] { e.Mode, string.Join("\n", e.Lignes), e.Ref, e.Taille.ToString(), e.Presentation, deux });
        // 1.5.0 : des fondus plus courts (le texte suivant arrive en un tiers de seconde au lieu de près d'une seconde)
        double dureeFond = Transitions ? 0.35 : 0;
        Animer(fond, vide ? 0 : 1, dureeFond);
        if (nouvelle == cle) return;
        var avant = cle;
        cle = nouvelle;
        int v = ++version;
        if (sansTexte)
        {
            Animer(scene, 0, Transitions ? 0.25 : 0);
            return;
        }
        if (Transitions && avant.Length > 0 && scene.Opacity > 0.05)
        {
            Animer(scene, 0, 0.14, () =>
            {
                if (v != version) return;
                Remplir(e, deux);
                Animer(scene, 1, 0.2);
            });
        }
        else
        {
            Remplir(e, deux);
            Animer(scene, 1, Transitions ? 0.2 : 0);
        }
    }

    void Remplir(EtatEcran e, string deux)
    {
        bool chant = e.Mode == "chant";
        texte.Text = string.Join("\n", e.Lignes);
        texte.FontFamily = chant ? Sans : Serif;
        texte.FontWeight = chant ? FontWeights.Bold : FontWeights.Normal;
        texte2.Text = deux;
        texte2.Visibility = deux.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        refT.Text = e.Ref ?? "";
        refT.Visibility = refT.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Placer();
        Ajuster();
    }

    double Rem => Math.Max(1, ActualHeight) / 108.0;

    /// <summary>Pour le test automatique (1.6.6) : où est le texte dans le dessin (vide s'il n'y en a pas).</summary>
    public Rect TestZoneTexte => texte.IsVisible && texte.ActualWidth > 0 ? texte.TransformToAncestor(this).TransformBounds(new Rect(texte.RenderSize)) : Rect.Empty;
    /// <summary>Pour le test automatique (1.6.8) : aucun mot ne dépasse de son bloc (le plus long tient dans la largeur du texte).</summary>
    public bool TestTexteEntier => texte.IsVisible && PlusLongMot(texte) <= texte.ActualWidth + 2 && texte.ActualWidth <= ActualWidth;

    /// <summary>Plein écran (1.6.6 : marges 4 % / 3,5 %, tout l'écran pour le texte) ou bandeau en bas (30 % de la hauteur).</summary>
    void Placer()
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0 || H <= 0) return;
        if (etat.Presentation == "bandeau")
        {
            scene.Margin = new Thickness(0.05 * W, 0, 0.05 * W, 0.05 * H);
            scene.VerticalAlignment = VerticalAlignment.Bottom;
            scene.Height = 0.30 * H;
            scene.Background = Clair(etat.Theme) ? FondBandeauClair : FondBandeau;
            scene.CornerRadius = new CornerRadius(2 * Rem);
            scene.Padding = new Thickness(0.04 * W, 0.03 * H, 0.04 * W, 0.03 * H);
            refT.Margin = new Thickness(0, 1.4 * Rem, 0, 0);
            refT.FontSize = 2.6 * Rem;
        }
        else
        {
            scene.Margin = new Thickness(0.035 * W, 0.04 * H, 0.035 * W, 0.04 * H);
            scene.VerticalAlignment = VerticalAlignment.Stretch;
            scene.Height = double.NaN;
            scene.Background = null;
            scene.CornerRadius = new CornerRadius(0);
            scene.Padding = new Thickness(0);
            refT.Margin = new Thickness(0, 3.2 * Rem, 0, 0);
            refT.FontSize = 3.2 * Rem;
        }
        texte2.Margin = new Thickness(0, 2.2 * Rem, 0, 0);
    }

    void Taille(double r, bool avec2)
    {
        bool chant = etat.Mode == "chant";
        texte.FontSize = r * Rem;
        texte.LineHeight = r * Rem * (chant ? 1.25 : 1.32);
        if (avec2)
        {
            texte2.FontSize = r * 0.68 * Rem;
            texte2.LineHeight = r * 0.68 * Rem * 1.3;
        }
    }

    static Size Mesure(TextBlock t, double largeur)
    {
        t.Measure(new Size(largeur, double.PositiveInfinity));
        return t.DesiredSize;
    }

    /// <summary>
    /// La plus grande taille de texte qui tient dans l'écran, puis des lignes de longueurs voisines. 1.6.6 : le texte prend tout l'écran
    /// (jusqu'à 20 % de la hauteur par ligne) ; « Taille du texte » change ce plafond, mais le texte n'est jamais coupé : il tient
    /// toujours dans l'écran, quitte à être plus petit.
    /// </summary>
    void Ajuster()
    {
        double W = ActualWidth, H = ActualHeight;
        if (W <= 0 || H <= 0 || texte.Text.Length == 0) return;
        texte.MaxWidth = double.PositiveInfinity;
        texte2.MaxWidth = double.PositiveInfinity;
        double largeur = W - scene.Margin.Left - scene.Margin.Right - scene.Padding.Left - scene.Padding.Right;
        double hauteur = (double.IsNaN(scene.Height) ? H - scene.Margin.Top - scene.Margin.Bottom : scene.Height) - scene.Padding.Top - scene.Padding.Bottom;
        if (largeur <= 10 || hauteur <= 10) return;
        bool avec2 = texte2.Visibility == Visibility.Visible;
        double maxH = hauteur;
        if (refT.Visibility == Visibility.Visible) maxH -= Mesure(refT, largeur).Height + refT.Margin.Top + 40 * H / 1080;
        double plafond = 20 * (etat.Taille > 0 ? etat.Taille : 1);
        // 1.6.8 : un mot ne se coupe pas (une adresse courriel, un long mot en gras) : le plus long doit tenir sur une ligne.
        // Le TextBlock mesuré dans une largeur ne le dit pas (sa taille est rognée à cette largeur) : le mot est mesuré à part,
        // une fois, et sa largeur suit la taille du texte.
        Taille(10, avec2);
        double mot1 = PlusLongMot(texte) / texte.FontSize, mot2 = avec2 ? PlusLongMot(texte2) / texte2.FontSize : 0;
        double lo = 0.5, hi = plafond, best = 0.5;
        // 12 essais : la taille à un demi-millième près (16 refaisaient la mise en page pour rien, sur chaque vignette du culte)
        for (int i = 0; i < 12; i++)
        {
            double mid = (lo + hi) / 2;
            Taille(mid, avec2);
            var s1 = Mesure(texte, largeur);
            double haut = s1.Height;
            bool ok = mot1 * texte.FontSize <= largeur * 0.98;
            if (avec2)
            {
                var s2 = Mesure(texte2, largeur);
                haut += texte2.Margin.Top + s2.Height;
                ok &= mot2 * texte2.FontSize <= largeur * 0.98;
            }
            if (haut <= maxH + 2 && ok) { best = mid; lo = mid; } else hi = mid;
        }
        Taille(best, avec2);
        Equilibrer(texte, largeur, mot1 * texte.FontSize);
        if (avec2) Equilibrer(texte2, largeur, mot2 * texte2.FontSize);
    }

    /// <summary>
    /// La largeur du plus long mot de <paramref name="t"/> à sa taille actuelle (les mots sont séparés par les espaces et les retours
    /// à la ligne ; une espace insécable lie deux mots, comme à l'écran).
    /// </summary>
    double PlusLongMot(TextBlock t)
    {
        if (string.IsNullOrEmpty(t.Text)) return 0;
        var police = new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch);
        double ppp = VisualTreeHelper.GetDpi(this).PixelsPerDip, max = 0;
        foreach (var mot in t.Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ft = new FormattedText(mot, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, police, t.FontSize, Brushes.White, ppp);
            if (ft.Width > max) max = ft.Width;
        }
        return max;
    }

    /// <summary>
    /// Lignes équilibrées (comme « text-wrap: balance ») : la largeur la plus petite qui garde le même nombre de lignes, jamais plus
    /// étroite que le plus long mot (1.6.8 : sinon un mot qui ne se coupe pas dépassait et était rogné, « DIME ET OFFRAN »).
    /// </summary>
    static void Equilibrer(TextBlock t, double largeur, double motMin)
    {
        double h = Mesure(t, largeur).Height;
        if (h <= t.LineHeight * 1.5) return; // une seule ligne
        double lo = Math.Max(largeur * 0.45, motMin + 4), hi = largeur;
        if (lo >= hi) return;
        for (int i = 0; i < 9; i++)
        {
            double mid = (lo + hi) / 2;
            if (Mesure(t, mid).Height <= h + 0.5) hi = mid; else lo = mid;
        }
        t.MaxWidth = Math.Min(largeur, hi + 2);
    }
}
