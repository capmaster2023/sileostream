using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MyVerse.Miniatures;

namespace MyVerse.Ui.Creation;

/// <summary>Ce que le sélecteur de couleurs connaît de la miniature : ses couleurs, ses photos, l'identité visuelle, la pipette.</summary>
public sealed class ContexteCouleurs
{
    public required Func<IReadOnlyList<string>> Document { get; init; }
    public required Func<IReadOnlyList<(ImageSource Vignette, string[] Couleurs)>> Photos { get; init; }
    public required List<string> Marque { get; init; }
    public required Action SauverMarque { get; init; }
    public required Func<string?> Logo { get; init; }
    /// <summary>Démarre la pipette : le rappel reçoit la couleur cliquée dans la miniature.</summary>
    public required Action<Action<string>?> Pipette { get; init; }
}

/// <summary>
/// Siloé Stream 2.4.0 : le choix d'une couleur, comme dans Canva. Une recherche (« bleu », « #00C4CC »), la pipette, les couleurs
/// du document avec le « + » arc-en-ciel (roue : saturation, luminosité, teinte, code), les couleurs de la photo, l'identité
/// visuelle de l'église (gardée pour toutes les miniatures) et les couleurs par défaut.
/// </summary>
public sealed class SelecteurCouleur : Border
{
    static Brush P(string c) => MainWindow.Pinceau(c);
    static Style S(string c) => MainWindow.Style_(c);
    public const string Aucune = "#00000000";

    /// <summary>Les couleurs par défaut : des neutres, puis des couleurs vives qui se voient sur YouTube.</summary>
    public static readonly string[] ParDefaut =
    {
        "#000000", "#121212", "#545454", "#A6A6A6", "#E6E6E6", "#FFFFFF",
        "#E1251B", "#FF4D4D", "#FF3D7F", "#E040FB", "#8E44FF", "#5B2BD9",
        "#0B3D91", "#1E63FF", "#1E90FF", "#00B4FF", "#00D1FF", "#00E5C8",
        "#00A86B", "#2ECC71", "#9BE564", "#D4FF3A", "#FFE600", "#FFD400",
        "#FFB000", "#FF9F1C", "#FF6B00", "#8B4513", "#C8A27A", "#F4EDE4",
    };

    /// <summary>Les noms de couleurs reconnus par la recherche (sans accents).</summary>
    static readonly (string Nom, string Hex)[] Noms =
    {
        ("noir", "#000000"), ("blanc", "#FFFFFF"), ("gris", "#808080"), ("gris clair", "#D3D3D3"), ("gris fonce", "#404040"), ("argent", "#C0C0C0"), ("anthracite", "#2F3437"),
        ("rouge", "#E1251B"), ("rouge fonce", "#8B0000"), ("bordeaux", "#800020"), ("grenat", "#6E0B14"), ("framboise", "#C72C48"), ("cerise", "#DE3163"),
        ("rose", "#FF69B4"), ("rose pale", "#FFC0CB"), ("fuchsia", "#FF00FF"), ("magenta", "#FF00FF"),
        ("violet", "#8E44FF"), ("mauve", "#B784A7"), ("lavande", "#B57EDC"), ("lilas", "#C8A2C8"), ("prune", "#8E4585"), ("indigo", "#4B0082"),
        ("bleu", "#1E63FF"), ("bleu marine", "#0B1F4D"), ("marine", "#0B1F4D"), ("bleu ciel", "#87CEEB"), ("ciel", "#87CEEB"), ("azur", "#007FFF"), ("bleu roi", "#4169E1"), ("bleu nuit", "#191970"),
        ("cyan", "#00D1FF"), ("turquoise", "#40E0D0"), ("sarcelle", "#008080"), ("canard", "#048B9A"),
        ("vert", "#2ECC71"), ("vert fonce", "#0B6623"), ("vert clair", "#90EE90"), ("emeraude", "#50C878"), ("menthe", "#98FF98"), ("sapin", "#095228"), ("olive", "#808000"), ("kaki", "#8A8B5C"), ("lime", "#C1FF72"),
        ("jaune", "#FFD400"), ("citron", "#FFF44F"), ("or", "#D4AF37"), ("dore", "#D4AF37"), ("moutarde", "#E1AD01"), ("ambre", "#FFBF00"),
        ("orange", "#FF9F1C"), ("abricot", "#FBCEB1"), ("corail", "#FF7F50"), ("saumon", "#FA8072"), ("peche", "#FFCBA4"), ("brique", "#B22222"), ("rouille", "#B7410E"), ("terracotta", "#E2725B"),
        ("marron", "#7B3F00"), ("brun", "#5C4033"), ("chocolat", "#7B3F00"), ("cafe", "#6F4E37"), ("caramel", "#C68E17"), ("taupe", "#483C32"),
        ("beige", "#F5F5DC"), ("creme", "#FFFDD0"), ("ivoire", "#FFFFF0"), ("sable", "#C2B280"),
    };

    static readonly ControlTemplate Nu = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter/></ControlTemplate>");
    static readonly Geometry IconePipette = Geometry.Parse("M9.5,3.5 A2.5,2.5 0 0 1 14.5,3.5 L14.5,7 L16,7 L16,9 L13.5,9 L13.5,18 L12,22 L10.5,18 L10.5,9 L8,9 L8,7 L9.5,7 Z");
    static BitmapSource? arc;
    /// <summary>La roue reste ouverte d'un panneau à l'autre si on l'a ouverte.</summary>
    static bool roueOuverte;

    readonly Func<string> lire;
    readonly Action<string, bool> appliquer;
    readonly Action finGeste;
    readonly bool transparent;
    readonly ContexteCouleurs ctx;
    readonly TextBox recherche;
    readonly WrapPanel resultats = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly WrapPanel document = new();
    readonly StackPanel roue = new() { Margin = new Thickness(0, 2, 0, 6) };
    readonly StackPanel sections = new();
    readonly Grid sv, teinte;
    readonly Border fondSV = new() { CornerRadius = new CornerRadius(8) };
    readonly Ellipse pointSV = new() { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
    readonly Ellipse pointTeinte = new() { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 3, IsHitTestVisible = false };
    readonly Canvas repereSV = new() { IsHitTestVisible = false }, repereTeinte = new() { IsHitTestVisible = false };
    readonly TextBox code;
    double h, s = 1, v = 1;
    bool glisse, planifie;
    string? enAttente;
    string derniere = "";

    public SelecteurCouleur(Func<string> lire, Action<string, bool> appliquer, Action finGeste, bool transparent, ContexteCouleurs ctx)
    {
        this.lire = lire;
        this.appliquer = appliquer;
        this.finGeste = finGeste;
        this.transparent = transparent;
        this.ctx = ctx;
        Background = P("Nuit2");
        BorderBrush = P("Trait2");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(12, 12, 6, 6);
        var pile = new StackPanel();
        Child = pile;

        // ---------- recherche et pipette
        var tete = new DockPanel();
        var pipette = new Button { Style = S("BtnPetit"), Width = 36, Height = 34, Padding = new Thickness(0), Margin = new Thickness(6, 0, 6, 0), ToolTip = "Pipette : prendre une couleur dans la miniature" };
        pipette.Content = new System.Windows.Shapes.Path
        {
            Data = IconePipette, Fill = P("Blanc"), Width = 16, Height = 16, Stretch = Stretch.Uniform,
            RenderTransform = new RotateTransform(45), RenderTransformOrigin = new Point(0.5, 0.5),
        };
        pipette.Click += (_, _) => ctx.Pipette(Choisir);
        DockPanel.SetDock(pipette, Dock.Right);
        tete.Children.Add(pipette);
        recherche = new TextBox { Style = S("Champ"), MinHeight = 34, FontSize = 13, ToolTip = "Un nom (« bleu », « bordeaux ») ou un code (« #00C4CC ») ; Entrée : appliquer" };
        var indice = new TextBlock { Text = "Essayez « bleu » ou « #00C4CC »", FontSize = 13, Foreground = P("Brume2"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var zone = new Grid();
        zone.Children.Add(recherche);
        zone.Children.Add(indice);
        recherche.TextChanged += (_, _) => { indice.Visibility = recherche.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; MajResultats(); };
        recherche.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (Chercher(recherche.Text).FirstOrDefault() is { } c) Choisir(c);
        };
        tete.Children.Add(zone);
        pile.Children.Add(tete);
        pile.Children.Add(resultats);

        // ---------- couleurs du document, avec le « + » arc-en-ciel
        pile.Children.Add(Titre("Couleurs du document"));
        pile.Children.Add(document);

        // ---------- la roue : saturation et luminosité, teinte, code
        sv = new Grid { Height = 150, Cursor = Cursors.Cross, Margin = new Thickness(0, 0, 6, 10), Background = Brushes.Transparent };
        sv.Children.Add(fondSV);
        sv.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        sv.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        repereSV.Children.Add(pointSV);
        sv.Children.Add(repereSV);
        Glisser(sv, p => { s = Math.Clamp(p.X / Math.Max(1, sv.ActualWidth), 0, 1); v = 1 - Math.Clamp(p.Y / Math.Max(1, sv.ActualHeight), 0, 1); });
        sv.SizeChanged += (_, _) => Placer();
        roue.Children.Add(sv);
        teinte = new Grid { Height = 18, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 10), Background = Brushes.Transparent };
        var arcEnCiel = new GradientStopCollection();
        for (int i = 0; i <= 6; i++) arcEnCiel.Add(new GradientStop(DeHsv(i * 60 % 360, 1, 1), i / 6.0));
        teinte.Children.Add(new Border { CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 2, 0, 2), Background = new LinearGradientBrush(arcEnCiel, 0) });
        repereTeinte.Children.Add(pointTeinte);
        teinte.Children.Add(repereTeinte);
        Glisser(teinte, p => h = Math.Clamp(p.X / Math.Max(1, teinte.ActualWidth), 0, 0.9999) * 360);
        teinte.SizeChanged += (_, _) => Placer();
        roue.Children.Add(teinte);
        var rangCode = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
        rangCode.Children.Add(new TextBlock { Text = "Code", FontSize = 12, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        code = new TextBox { Style = S("Champ"), MinHeight = 30, FontSize = 13, ToolTip = "#RRGGBB ; Entrée : appliquer" };
        void ValiderCode()
        {
            if (Normaliser(code.Text) is { } c && !Meme(c, lire())) { Choisir(c); }
            else code.Text = derniere;
        }
        code.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ValiderCode(); e.Handled = true; } };
        code.LostKeyboardFocus += (_, _) => ValiderCode();
        rangCode.Children.Add(code);
        roue.Children.Add(rangCode);
        roue.Visibility = roueOuverte ? Visibility.Visible : Visibility.Collapsed;
        pile.Children.Add(roue);

        pile.Children.Add(sections);
        DepuisCouleur(lire());
        Maj();
    }

    static TextBlock Titre(string t) => new() { Text = t, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = P("Blanc"), Margin = new Thickness(0, 12, 0, 8) };

    // ================================================================================================ les pastilles
    public static bool EstAucune(string c) => Couleurs.Valide(c, "") is { Length: 9 } v && v.StartsWith("#00", StringComparison.Ordinal);

    /// <summary>« #RRGGBB » (ou « #AARRGGBB » si elle est transparente), en majuscules ; null si ce n'est pas une couleur.</summary>
    public static string? Normaliser(string? brut)
    {
        var t = (brut ?? "").Trim().TrimStart('#');
        if (t.Length == 3 && t.All(Uri.IsHexDigit)) t = string.Concat(t.Select(c => $"{c}{c}"));
        if (!(t.Length is 6 or 8) || !t.All(Uri.IsHexDigit)) return null;
        t = t.ToUpperInvariant();
        if (t.Length == 8 && t.StartsWith("FF", StringComparison.Ordinal)) t = t[2..];
        return "#" + t;
    }

    static bool Meme(string a, string b)
    {
        if (EstAucune(a) && EstAucune(b)) return true;
        return Normaliser(a) is { } x && x == Normaliser(b);
    }

    Button Pastille(string hex, double taille = 30, string? tip = null)
    {
        bool aucune = EstAucune(hex), choisi = Meme(hex, lire());
        var g = new Grid { Width = taille, Height = taille };
        if (choisi) g.Children.Add(new Ellipse { Stroke = P("Or"), StrokeThickness = 2.5 });
        g.Children.Add(new Ellipse
        {
            Margin = new Thickness(choisi ? 4.5 : 0), StrokeThickness = 1, Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            Fill = aucune ? Brushes.White : new SolidColorBrush(RenduMiniature.Couleur(hex)),
        });
        if (aucune)
            g.Children.Add(new Line { X1 = taille * 0.27, Y1 = taille * 0.73, X2 = taille * 0.73, Y2 = taille * 0.27, Stroke = new SolidColorBrush(Color.FromRgb(225, 37, 27)), StrokeThickness = 2.2 });
        var b = new Button { Template = Nu, Content = g, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 7, 7), ToolTip = tip ?? (aucune ? "Aucune" : hex) };
        string c = hex;
        b.Click += (_, _) => Choisir(c);
        return b;
    }

    static BitmapSource Arc()
    {
        if (arc != null) return arc;
        const int n = 96;
        var px = new byte[n * n * 4];
        double c = (n - 1) / 2.0, rExt = n / 2.0 - 0.5, rInt = rExt * 0.66;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double dx = x - c, dy = y - c, d = Math.Sqrt(dx * dx + dy * dy);
                double a = Math.Clamp(rExt - d + 0.5, 0, 1) * Math.Clamp(d - rInt + 0.5, 0, 1);
                if (a <= 0) continue;
                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
                var col = DeHsv(hue, 0.85, 1);
                int i = (y * n + x) * 4;
                px[i] = col.B; px[i + 1] = col.G; px[i + 2] = col.R; px[i + 3] = (byte)(a * 255);
            }
        var b = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
        b.Freeze();
        return arc = b;
    }

    Button BoutonRoue()
    {
        var g = new Grid { Width = 30, Height = 30 };
        g.Children.Add(new Image { Source = Arc(), Width = 30, Height = 30 });
        g.Children.Add(new TextBlock { Text = "+", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = P("Blanc"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) });
        var b = new Button { Template = Nu, Content = g, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 7, 7), ToolTip = "Une autre couleur : la roue (teinte, luminosité, code)" };
        b.Click += (_, _) =>
        {
            roueOuverte = roue.Visibility != Visibility.Visible;
            roue.Visibility = roueOuverte ? Visibility.Visible : Visibility.Collapsed;
            if (roueOuverte) { DepuisCouleur(lire()); Dispatcher.BeginInvoke(new Action(() => roue.BringIntoView()), DispatcherPriority.Loaded); }
        };
        return b;
    }

    static FrameworkElement Plus(double l, double hauteur, string texte)
    {
        var g = new Grid { Height = hauteur, MinWidth = l };
        g.Children.Add(new Rectangle { RadiusX = hauteur / 2, RadiusY = hauteur / 2, Stroke = P("Brume"), StrokeThickness = 1.3, StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = Brushes.Transparent });
        g.Children.Add(new TextBlock { Text = texte, FontSize = texte.Length > 1 ? 12.5 : 17, FontWeight = FontWeights.SemiBold, Foreground = P("Blanc"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(texte.Length > 1 ? 14 : 0, -1, texte.Length > 1 ? 14 : 0, 0) });
        return g;
    }

    // ================================================================================================ mise à jour
    /// <summary>Redessine les pastilles (la couleur a changé, ou le document) ; rien pendant que l'on glisse dans la roue.</summary>
    public void Maj()
    {
        if (glisse) return;
        var actuelle = lire();
        if (!Meme(actuelle, derniere)) DepuisCouleur(actuelle);

        document.Children.Clear();
        document.Children.Add(BoutonRoue());
        foreach (var c in ctx.Document().Where(c => !EstAucune(c)).Take(17)) document.Children.Add(Pastille(c));

        sections.Children.Clear();
        var photos = ctx.Photos();
        if (photos.Count > 0)
        {
            sections.Children.Add(Titre("Couleurs de la photo"));
            foreach (var (vignette, couleurs) in photos)
            {
                var rang = new WrapPanel();
                rang.Children.Add(new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 9, 7),
                    Background = new ImageBrush(vignette) { Stretch = Stretch.UniformToFill },
                });
                foreach (var c in couleurs) rang.Children.Add(Pastille(c));
                sections.Children.Add(rang);
            }
        }

        // l'identité visuelle de l'église (gardée pour toutes les miniatures)
        sections.Children.Add(Titre("Identité visuelle"));
        var marque = new WrapPanel();
        foreach (var c in ctx.Marque.ToList())
        {
            var b = Pastille(c, 30, c + " · clic droit : retirer de l'identité visuelle");
            var menu = new ContextMenu();
            var retirer = new MenuItem { Header = "Retirer de l'identité visuelle" };
            string cc = c;
            retirer.Click += (_, _) => { ctx.Marque.Remove(cc); ctx.SauverMarque(); Maj(); };
            menu.Items.Add(retirer);
            b.ContextMenu = menu;
            marque.Children.Add(b);
        }
        bool peutAjouter = !EstAucune(actuelle) && !ctx.Marque.Any(m => Meme(m, actuelle));
        if (ctx.Marque.Count == 0)
        {
            var b = new Button { Template = Nu, Content = Plus(0, 32, "+ Ajouter vos couleurs de marque"), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 7, 7), ToolTip = "Ajoute la couleur actuelle aux couleurs de l'église" };
            b.Click += (_, _) => AjouterMarque(lire());
            marque.Children.Add(b);
        }
        else if (peutAjouter)
        {
            var b = new Button { Template = Nu, Content = Plus(30, 30, "+"), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 7, 7), ToolTip = "Ajouter la couleur actuelle (" + actuelle + ") à l'identité visuelle" };
            b.Click += (_, _) => AjouterMarque(lire());
            marque.Children.Add(b);
        }
        sections.Children.Add(marque);
        if (ctx.Logo() is { } logo)
        {
            var depuisLogo = CouleursPhoto.DuFichier(logo).Where(c => !ctx.Marque.Any(m => Meme(m, c))).Take(4).ToList();
            if (depuisLogo.Count > 0)
            {
                var b = new Button { Style = S("BtnPetit"), Content = "Prendre les couleurs du logo", Height = 28, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Ajoute à l'identité visuelle les couleurs principales du logo de l'église" };
                b.Click += (_, _) => { ctx.Marque.AddRange(depuisLogo); ctx.SauverMarque(); Maj(); };
                sections.Children.Add(b);
            }
        }

        sections.Children.Add(Titre("Couleurs par défaut"));
        var defaut = new WrapPanel();
        if (transparent) defaut.Children.Add(Pastille(Aucune));
        foreach (var c in ParDefaut) defaut.Children.Add(Pastille(c));
        sections.Children.Add(defaut);
        MajResultats();
    }

    void AjouterMarque(string c)
    {
        if (EstAucune(c) || Normaliser(c) is not { } n) return;
        if (!ctx.Marque.Any(m => Meme(m, n))) { ctx.Marque.Add(n); ctx.SauverMarque(); }
        Maj();
    }

    void MajResultats()
    {
        resultats.Children.Clear();
        var l = recherche.Text.Trim().Length == 0 ? new List<string>() : Chercher(recherche.Text);
        if (recherche.Text.Trim().Length > 0 && l.Count == 0)
            resultats.Children.Add(new TextBlock { Text = "Aucune couleur de ce nom.", FontSize = 12, Foreground = P("Brume") });
        foreach (var c in l) resultats.Children.Add(Pastille(c));
        resultats.Visibility = resultats.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static string SansAccents(string t)
    {
        var sb = new StringBuilder();
        foreach (var ch in t.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>Les couleurs pour une recherche : un code, ou un nom (avec des nuances plus claires et plus foncées).</summary>
    public static List<string> Chercher(string texte)
    {
        var q = SansAccents(texte.Trim());
        var l = new List<string>();
        if (q.Length == 0) return l;
        if (Normaliser(q) is { } hex) { l.Add(hex); return l; }
        var trouves = Noms.Where(n => n.Nom == q).Concat(Noms.Where(n => n.Nom.StartsWith(q, StringComparison.Ordinal)))
            .Concat(q.Length >= 3 ? Noms.Where(n => n.Nom.Contains(q, StringComparison.Ordinal)) : Enumerable.Empty<(string Nom, string Hex)>())
            .Select(n => n.Hex).Distinct().Take(3).ToList();
        if (trouves.Count == 0)
        {
            try { if (ColorConverter.ConvertFromString(texte.Trim()) is Color c) trouves.Add($"#{c.R:X2}{c.G:X2}{c.B:X2}"); } catch (FormatException) { }
        }
        foreach (var t in trouves)
            foreach (var n in Nuances(t))
                if (!l.Contains(n)) l.Add(n);
        return l.Take(15).ToList();
    }

    static IEnumerable<string> Nuances(string hex)
    {
        var c = RenduMiniature.Couleur(hex);
        string Melange(Color avec, double k) => Hex(Color.FromRgb((byte)(c.R + (avec.R - c.R) * k), (byte)(c.G + (avec.G - c.G) * k), (byte)(c.B + (avec.B - c.B) * k)));
        yield return Hex(c);
        yield return Melange(Colors.White, 0.55);
        yield return Melange(Colors.White, 0.28);
        yield return Melange(Colors.Black, 0.3);
        yield return Melange(Colors.Black, 0.55);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ================================================================================================ choisir
    internal void TestChoisir(string hex) => Choisir(hex);

    void Choisir(string hex)
    {
        Envoyer();
        if (!Meme(hex, lire())) { appliquer(EstAucune(hex) ? Aucune : Normaliser(hex) ?? hex, true); finGeste(); }
        DepuisCouleur(lire());
        Maj();
    }

    void DepuisCouleur(string hex)
    {
        derniere = hex;
        code.Text = EstAucune(hex) ? "" : Normaliser(hex) ?? hex;
        if (!EstAucune(hex))
        {
            var (hh, ss, vv) = VersHsv(RenduMiniature.Couleur(hex));
            if (ss > 0.001 && vv > 0.001) h = hh;
            s = ss; v = vv;
        }
        Placer();
    }

    void Placer()
    {
        fondSV.Background = new SolidColorBrush(DeHsv(h, 1, 1));
        pointSV.Fill = new SolidColorBrush(DeHsv(h, s, v));
        pointTeinte.Fill = new SolidColorBrush(DeHsv(h, 1, 1));
        Canvas.SetLeft(pointSV, s * sv.ActualWidth - 8);
        Canvas.SetTop(pointSV, (1 - v) * sv.ActualHeight - 8);
        Canvas.SetLeft(pointTeinte, h / 360 * teinte.ActualWidth - 9);
        Canvas.SetTop(pointTeinte, 0);
    }

    /// <summary>Glisser dans la roue : la couleur change en direct, et un seul Ctrl Z revient au début du geste.</summary>
    void Glisser(FrameworkElement zone, Action<Point> lirePosition)
    {
        zone.MouseLeftButtonDown += (_, e) =>
        {
            zone.CaptureMouse();
            glisse = true;
            lirePosition(e.GetPosition(zone));
            Placer();
            var hex = Hex(DeHsv(h, s, v));
            derniere = hex; code.Text = hex;
            appliquer(hex, true);
            e.Handled = true;
        };
        zone.MouseMove += (_, e) =>
        {
            if (!glisse || !zone.IsMouseCaptured) return;
            lirePosition(e.GetPosition(zone));
            Placer();
            var hex = Hex(DeHsv(h, s, v));
            derniere = hex; code.Text = hex;
            enAttente = hex;
            if (planifie) return;
            planifie = true;
            Dispatcher.BeginInvoke(new Action(Envoyer), DispatcherPriority.Background);
        };
        zone.MouseLeftButtonUp += (_, _) => { if (zone.IsMouseCaptured) zone.ReleaseMouseCapture(); };
        zone.LostMouseCapture += (_, _) =>
        {
            if (!glisse) return;
            Envoyer();
            glisse = false;
            finGeste();
            Maj();
        };
    }

    void Envoyer()
    {
        planifie = false;
        if (enAttente is not { } c) return;
        enAttente = null;
        appliquer(c, false);
    }

    // ================================================================================================ teinte, saturation, luminosité
    public static Color DeHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) VersHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }
}

/// <summary>Les couleurs principales d'une image (photo, logo) : quelques groupes de couleurs proches, du plus présent au moins présent.</summary>
public static class CouleursPhoto
{
    static readonly Dictionary<string, string[]> cache = new();

    public static string[] De(string cle, Func<BitmapSource?> charger, int n = 6)
    {
        lock (cache) if (cache.TryGetValue(cle, out var c)) return c;
        string[] r;
        try { r = charger() is { } src ? Calculer(src, n) : Array.Empty<string>(); }
        catch (Exception e) { Journal.Erreur("couleurs de l'image", e); r = Array.Empty<string>(); }
        lock (cache) cache[cle] = r;
        return r;
    }

    public static string[] DuFichier(string chemin, int n = 6)
    {
        if (!File.Exists(chemin)) return Array.Empty<string>();
        return De(chemin + "|" + File.GetLastWriteTimeUtc(chemin).Ticks, () => RenduMiniature.Image(chemin), n);
    }

    public static string[] Calculer(BitmapSource src, int n = 6)
    {
        double k = Math.Min(1, 96.0 / Math.Max(1, Math.Max(src.PixelWidth, src.PixelHeight)));
        BitmapSource b = k < 1 ? new TransformedBitmap(src, new ScaleTransform(k, k)) : src;
        var conv = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, hh = conv.PixelHeight;
        var px = new byte[w * hh * 4];
        conv.CopyPixels(px, w * 4, 0);
        var pts = new List<(double R, double G, double B)>();
        for (int i = 0; i < px.Length; i += 4) if (px[i + 3] >= 128) pts.Add((px[i + 2], px[i + 1], px[i]));
        if (pts.Count == 0) return Array.Empty<string>();
        static double D2((double R, double G, double B) a, (double R, double G, double B) c) => (a.R - c.R) * (a.R - c.R) + (a.G - c.G) * (a.G - c.G) + (a.B - c.B) * (a.B - c.B);
        // k-moyennes (départ « k-means++ », toujours le même tirage : le même résultat à chaque fois)
        var rnd = new Random(7);
        var centres = new List<(double R, double G, double B)> { pts[rnd.Next(pts.Count)] };
        var dist = new double[pts.Count];
        while (centres.Count < Math.Min(8, pts.Count))
        {
            double total = 0;
            for (int i = 0; i < pts.Count; i++) { dist[i] = centres.Min(c => D2(pts[i], c)); total += dist[i]; }
            if (total <= 0) break;
            double t = rnd.NextDouble() * total;
            int j = 0;
            for (; j < pts.Count - 1; j++) { t -= dist[j]; if (t <= 0) break; }
            centres.Add(pts[j]);
        }
        var groupe = new int[pts.Count];
        var compte = new int[centres.Count];
        for (int tour = 0; tour < 10; tour++)
        {
            var somme = new (double R, double G, double B)[centres.Count];
            Array.Clear(compte);
            for (int i = 0; i < pts.Count; i++)
            {
                int meilleur = 0; double dm = double.MaxValue;
                for (int c = 0; c < centres.Count; c++) { double d = D2(pts[i], centres[c]); if (d < dm) { dm = d; meilleur = c; } }
                groupe[i] = meilleur; compte[meilleur]++;
                somme[meilleur] = (somme[meilleur].R + pts[i].R, somme[meilleur].G + pts[i].G, somme[meilleur].B + pts[i].B);
            }
            for (int c = 0; c < centres.Count; c++)
                if (compte[c] > 0) centres[c] = (somme[c].R / compte[c], somme[c].G / compte[c], somme[c].B / compte[c]);
        }
        var garde = new List<(double R, double G, double B)>();
        foreach (var c in Enumerable.Range(0, centres.Count).Where(c => compte[c] > 0).OrderByDescending(c => compte[c]).Select(c => centres[c]))
        {
            if (garde.Any(g => D2(g, c) < 30 * 30)) continue;
            garde.Add(c);
            if (garde.Count >= n) break;
        }
        return garde.Select(c => SelecteurCouleur.Hex(Color.FromRgb((byte)Math.Round(c.R), (byte)Math.Round(c.G), (byte)Math.Round(c.B)))).ToArray();
    }
}
