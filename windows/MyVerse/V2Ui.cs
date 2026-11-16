using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 2.0, design « Grand Bouton » : les couleurs, les polices et les briques des écrans simples
/// (fond crème, rouge de l'église, cartes et boutons en relief comme des touches, gros textes lisibles de loin).
/// </summary>
static class V2
{
    static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // ------------------------------------------------------------ couleurs de la maquette 2.0
    public static readonly SolidColorBrush Page = B("#FAF7F7"), Carte = B("#FFFFFF"), Douce = B("#F3EEEE"), Douce2 = B("#EFE8E8"), Trait = B("#E3D6D6"),
        Encre = B("#1E1818"), Encre2 = B("#463D3D"), Muet = B("#6E6262"),
        Rouge = B("#930000"), RougeOmbre = B("#5B0000"), RougeVif = B("#B3261E"), RougePale = B("#FBEDED"), RougeBord = B("#F6C4C0"),
        Vert = B("#15803D"), VertOmbre = B("#0C4F25"), VertPale = B("#E8F6EE"), VertBord = B("#BFE5CC"), VertRelief = B("#DDF1E4"), VertTexte = B("#0F5F2E"),
        Or = B("#F2B33D"), OrPale = B("#FBF1DC"), OrBord = B("#F5D9A3"), OrTexte = B("#6B4A0C"),
        Scene = B("#120E0C"), Scene2 = B("#2A241C"), Indigo = B("#4F46E5"), Voile = B("#D9E9E1E1"), Blanc = B("#FFFFFF"), Noir = B("#000000"),
        Blanc80 = B("#CCFFFFFF"), Blanc30 = B("#4DFFFFFF");

    // une teinte par sorte de carte : le fond pâle et la couleur de l'icône
    public static readonly SolidColorBrush GrisPale = B("#EEF1F5"), Gris = B("#64748B"), BleuPale = B("#E8F0FE"), Bleu = B("#2563EB"),
        VioletPale = B("#F1EAFE"), Violet = B("#7C3AED"), VioletLeger = B("#F7F3FF"), RosePale = B("#FCE8F2"), Rose = B("#DB2777"),
        TurquoisePale = B("#E3F4F2"), Turquoise = B("#0F766E"), IndigoPale = B("#E8EAFD"), Ambre = B("#B45309");

    // ------------------------------------------------------------ polices (livrées dans Polices)
    public static FontFamily Titre => (FontFamily)Application.Current.FindResource("Titre");
    public static FontFamily Sans => (FontFamily)Application.Current.FindResource("Sans");
    public static FontFamily Serif => (FontFamily)Application.Current.FindResource("Serif");

    // ------------------------------------------------------------ icônes (dessinées sur une grille de 24 × 24)
    public const string IcEcran = "M3,5 H21 V16 H3 Z M8,20 H16 M12,16 V20";
    public const string IcCamera = "M3,7 H15 V17 H3 Z M15,10.5 L21,7 V17 L15,13.5 Z";
    public const string IcSon = "M4,9.5 H7.5 L12,5.5 V18.5 L7.5,14.5 H4 Z M15.5,9 A4,4 0 0 1 15.5,15 M18,6.5 A7.5,7.5 0 0 1 18,17.5";
    public const string IcGlobe = "M12,3 A9,9 0 1 1 11.99,3 Z M3,12 H21 M12,3 A13,13 0 0 1 12,21 M12,3 A13,13 0 0 0 12,21";
    public const string IcTelephone = "M8,2.5 H16 A1.5,1.5 0 0 1 17.5,4 V20 A1.5,1.5 0 0 1 16,21.5 H8 A1.5,1.5 0 0 1 6.5,20 V4 A1.5,1.5 0 0 1 8,2.5 Z M11,18 H13";
    public const string IcLecture = "M7,4.5 L19.5,12 L7,19.5 Z";
    public const string IcSuivant = "M5,4.5 L15.5,12 L5,19.5 Z M19,4.5 V19.5";
    public const string IcRevenir = "M19,4.5 L8.5,12 L19,19.5 Z M5,4.5 V19.5";
    public const string IcLune = "M20,14.5 A8,8 0 0 1 9.5,4 A8,8 0 1 0 20,14.5 Z";
    public const string IcSoleil = "M12,8 A4,4 0 1 1 11.99,8 Z M12,2.5 V4.5 M12,19.5 V21.5 M2.5,12 H4.5 M19.5,12 H21.5 M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M5.3,18.7 L6.7,17.3 M17.3,6.7 L18.7,5.3";
    public const string IcLoupe = "M10.5,3.5 A7,7 0 1 1 10.49,3.5 Z M15.6,15.6 L20.5,20.5";
    public const string IcMaison = "M3.5,11 L12,4 L20.5,11 M5.5,9.5 V20 H18.5 V9.5 M10,20 V14.5 H14 V20";
    public const string IcMusique = "M9,18 V5 L20,3 V16 M3,18 A3,3 0 1 0 9,18 A3,3 0 1 0 3,18 M14,16 A3,3 0 1 0 20,16 A3,3 0 1 0 14,16";
    public const string IcLivre = "M12,6.5 C10,5 7,4.5 3,5 V19 C7,18.5 10,19 12,20.5 C14,19 17,18.5 21,19 V5 C17,4.5 14,5 12,6.5 Z M12,6.5 V20.5";
    public const string IcImage = "M4,5 H20 V19 H4 Z M4,15.5 L9,11 L13,15 L16,12.5 L20,16 M15.5,7.5 A1.5,1.5 0 1 1 15.49,7.5 Z";
    public const string IcVideo = "M4,5 H20 V19 H4 Z M10,9 L15,12 L10,15 Z";
    public const string IcTexte = "M5,5 H19 M12,5 V19 M9,19 H15";
    public const string IcCoeur = "M12,20 C4,14.5 3,10.5 4.5,7.5 C6,4.5 10,4.5 12,7.5 C14,4.5 18,4.5 19.5,7.5 C21,10.5 20,14.5 12,20 Z";
    public const string IcDrapeau = "M5,21 V4 M5,4 H18 L15.5,8 L18,12 H5";
    public const string IcGauche = "M14.5,5.5 L8,12 L14.5,18.5";
    public const string IcHaut = "M6,14.5 L12,8.5 L18,14.5";
    public const string IcBas = "M6,9.5 L12,15.5 L18,9.5";
    public const string IcCroix = "M6,6 L18,18 M18,6 L6,18";
    public const string IcCoche = "M5,12.5 L10,17.5 L19,7";
    public const string IcPlus = "M12,5 V19 M5,12 H19";
    public const string IcMoins = "M5,12 H19";
    public const string IcCopier = "M8,8 H19 V19 H8 Z M5,15.5 V5 H15.5";
    public const string IcDirect = "M10.5,12 A1.5,1.5 0 1 0 13.5,12 A1.5,1.5 0 1 0 10.5,12 Z M8.5,8.5 A5,5 0 0 0 8.5,15.5 M15.5,8.5 A5,5 0 0 1 15.5,15.5 M5.5,5.5 A9.2,9.2 0 0 0 5.5,18.5 M18.5,5.5 A9.2,9.2 0 0 1 18.5,18.5";
    public const string IcMicro = "M12,3 A3,3 0 0 1 15,6 V11 A3,3 0 0 1 9,11 V6 A3,3 0 0 1 12,3 Z M5.5,10.5 A6.5,6.5 0 0 0 18.5,10.5 M12,17 V21 M8.5,21 H15.5";
    public const string IcAttention = "M12,6.5 V13.5 M12,17.5 V17.6";
    public const string IcTiret = "M7,12 H17";
    public const string IcGrille = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";
    public const string IcTable = "M6,4 V20 M12,4 V20 M18,4 V20 M4,15 H8 M10,8.5 H14 M16,13 H20";
    public const string IcOrdinateur = "M4.5,4 H19.5 A2,2 0 0 1 21.5,6 V14.5 A2,2 0 0 1 19.5,16.5 H4.5 A2,2 0 0 1 2.5,14.5 V6 A2,2 0 0 1 4.5,4 Z M8,20.5 H16 M12,16.5 V20.5";
    public const string IcEnregistrer = "M12,5 A7,7 0 1 1 11.99,5 Z";

    /// <summary>Une icône de la grille 24 × 24, à la taille voulue (le trait grandit avec elle).</summary>
    public static Viewbox Icone(string donnees, Brush couleur, double taille = 22, double epaisseur = 2, bool plein = false)
    {
        var p = new Shapes.Path
        {
            Data = Geometry.Parse(donnees), Stroke = couleur, StrokeThickness = epaisseur, Fill = plein ? couleur : null,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        var c = new Canvas { Width = 24, Height = 24 };
        c.Children.Add(p);
        return new Viewbox { Width = taille, Height = taille, Child = c, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }

    /// <summary>Change la couleur (et le dessin) d'une icône faite par <see cref="Icone"/>.</summary>
    public static void Recolorer(Viewbox icone, Brush couleur, string? donnees = null, bool? plein = null)
    {
        if (icone.Child is not Canvas c || c.Children.Count == 0 || c.Children[0] is not Shapes.Path p) return;
        p.Stroke = couleur;
        if (donnees != null) p.Data = Geometry.Parse(donnees);
        if (plein is bool pl) p.Fill = pl ? couleur : null;
        else if (p.Fill != null) p.Fill = couleur;
    }

    // ------------------------------------------------------------ textes
    /// <summary>Un texte : Atkinson Hyperlegible (lisible de loin), ou Unbounded pour les titres.</summary>
    public static TextBlock T(string texte, double taille, Brush couleur, bool titre = false, FontWeight? poids = null, bool retour = false) => new()
    {
        Text = texte, FontSize = taille, Foreground = couleur, FontFamily = titre ? Titre : Sans,
        FontWeight = poids ?? (titre ? FontWeights.Bold : FontWeights.Normal),
        TextWrapping = retour ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = retour ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    /// <summary>Une étiquette en capitales sur une pilule (« À L'ÉCRAN », « SUIVANT »).</summary>
    public static Border Badge(string texte, Brush fond, Brush couleur, double taille = 12, Thickness? padding = null)
    {
        var t = T(texte.ToUpperInvariant(), taille, couleur, true);
        return new Border { Background = fond, CornerRadius = new CornerRadius(999), Padding = padding ?? new Thickness(10, 4, 10, 4), Child = t, VerticalAlignment = VerticalAlignment.Center };
    }

    public static Border Pilule(UIElement contenu, Brush fond, Thickness padding, Brush? bord = null, double epaisseur = 0) => new()
    {
        Background = fond, CornerRadius = new CornerRadius(999), Padding = padding, Child = contenu, BorderBrush = bord, BorderThickness = new Thickness(epaisseur),
    };

    public static StackPanel Ligne(double espace, params UIElement[] elements)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < elements.Length; i++)
        {
            if (i > 0 && elements[i] is FrameworkElement fe && espace > 0) fe.Margin = new Thickness(espace, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(elements[i]);
        }
        return p;
    }

    /// <summary>Un carré arrondi coloré avec son icône (les cartes du culte, les sons).</summary>
    public static Border CarreIcone(string icone, Brush fond, Brush couleur, double taille = 44, double rayon = 14, double icon = 22) => new()
    {
        Width = taille, Height = taille, CornerRadius = new CornerRadius(rayon), Background = fond,
        Child = Icone(icone, couleur, icon), VerticalAlignment = VerticalAlignment.Center,
    };

    // ------------------------------------------------------------ boutons sans habillage Windows
    static ControlTemplate? nu;

    /// <summary>Un bouton qui ne dessine que son contenu (le relief, les couleurs : c'est le contenu qui les porte).</summary>
    public static ControlTemplate Nu
    {
        get
        {
            if (nu != null) return nu;
            var t = new ControlTemplate(typeof(Button));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            t.VisualTree = cp;
            t.Seal();
            return nu = t;
        }
    }

    /// <summary>Un bouton : un clic, la main au survol, jamais le focus du clavier (Espace reste « Suivant »).</summary>
    public static Button Bouton(UIElement contenu, Action clic, string? info = null)
    {
        var b = new Button
        {
            Template = Nu, Content = contenu, Cursor = Cursors.Hand, Focusable = false, ToolTip = info,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        b.Click += (_, _) => clic();
        if (contenu is Brique br)
        {
            b.PreviewMouseLeftButtonDown += (_, _) => br.Enfoncer(true);
            b.PreviewMouseLeftButtonUp += (_, _) => br.Enfoncer(false);
            b.MouseLeave += (_, _) => br.Enfoncer(false);
        }
        b.MouseEnter += (_, _) => { if (b.IsEnabled) b.Opacity = 0.93; };
        b.MouseLeave += (_, _) => b.Opacity = b.IsEnabled ? 1 : 0.5;
        b.IsEnabledChanged += (_, _) => b.Opacity = b.IsEnabled ? 1 : 0.5;
        return b;
    }

    /// <summary>Un lien : du texte rouge souligné, cliquable.</summary>
    public static Button Lien(string texte, Action clic, double taille = 15, Brush? couleur = null)
    {
        var t = T(texte, taille, couleur ?? Rouge, false, FontWeights.Bold);
        t.TextDecorations = TextDecorations.Underline;
        return Bouton(new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 4, 2, 4), Child = t }, clic);
    }

    /// <summary>Un petit bouton pilule (« Montrer le code QR », « Fermer », « Retour à la régie »).</summary>
    public static Button BoutonPilule(string texte, Action clic, string? icone = null, Brush? fond = null, Brush? couleur = null, Brush? bord = null, double taille = 15, double hauteur = 46)
    {
        var c = couleur ?? Encre;
        var contenu = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (icone != null)
        {
            var i = Icone(icone, c, taille + 1, 2.4);
            i.Margin = new Thickness(0, 0, 8, 0);
            contenu.Children.Add(i);
        }
        var t = T(texte, taille, c, false, FontWeights.Bold);
        t.VerticalAlignment = VerticalAlignment.Center;
        contenu.Children.Add(t);
        var p = new Border
        {
            Background = fond ?? Carte, BorderBrush = bord ?? Trait, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(999),
            Padding = new Thickness(18, 0, 18, 0), Height = hauteur, Child = contenu,
        };
        return Bouton(p, clic);
    }
}

/// <summary>
/// Une brique en relief, comme une touche : la face, et dessous son bord plus foncé (le « 0 6px 0 » de la maquette).
/// Appuyée, la face descend sur son bord.
/// </summary>
sealed class Brique : Grid
{
    public readonly Border Face, Dessous;
    readonly double profondeur;

    public Brique(UIElement? contenu, Brush fond, Brush relief, double rayon, double profondeur, Thickness padding, Brush? bord = null, double epaisseur = 0)
    {
        this.profondeur = profondeur;
        Dessous = new Border { Background = relief, CornerRadius = new CornerRadius(rayon), Margin = new Thickness(0, profondeur, 0, 0) };
        Face = new Border
        {
            Background = fond, CornerRadius = new CornerRadius(rayon), Padding = padding, Child = contenu, Margin = new Thickness(0, 0, 0, profondeur),
            BorderBrush = bord, BorderThickness = new Thickness(epaisseur),
        };
        Children.Add(Dessous);
        Children.Add(Face);
    }

    public void Enfoncer(bool bas) => Face.Margin = bas ? new Thickness(0, profondeur * 0.6, 0, profondeur * 0.4) : new Thickness(0, 0, 0, profondeur);

    public void Couleurs(Brush fond, Brush relief, Brush? bord = null)
    {
        Face.Background = fond;
        Dessous.Background = relief;
        if (bord != null) Face.BorderBrush = bord;
    }
}
