using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 2.5.0 : Médias en calques, comme une scène d'OBS (et comme l'éditeur de Miniature). Au centre, l'écran en grand :
/// chaque source superposée se déplace à la souris et s'agrandit par ses coins (Maj : garder ses proportions ; elle se colle aux bords
/// et au milieu, Alt : sans aimant). Dessous, la liste des sources à l'écran : la case les montre ou les cache, ▲ ▼ les montent ou
/// les descendent (celle du haut passe devant), ⤢ leur donne tout l'écran, ✕ les retire. Le contenu principal (versets, chants,
/// média montré en grand) est une ligne de la liste : au-dessus d'une caméra (les paroles sur l'image) ou dessous.
/// « Superposer » (⧉), sur un média à gauche ou sur sa tuile, le pose par-dessus ce qui est à l'écran. Remplace le Duo.
/// </summary>
public partial class MainWindow
{
    const double LargeurEcran = 1920, HauteurEcran = 1080;
    readonly Rendu calquesRendu = new() { Transitions = false, Leger = true, Width = LargeurEcran, Height = HauteurEcran, IsHitTestVisible = false };
    readonly Canvas calquesSurcouche = new() { Width = LargeurEcran, Height = HauteurEcran, Background = Brushes.Transparent, ClipToBounds = true };
    readonly Viewbox calquesVue = new() { Stretch = Stretch.Uniform };
    readonly StackPanel listeCalquesUi = new();
    readonly TextBlock calquesAide = new()
    {
        Text = "Rien de superposé. Le bouton ⧉ d'un média (à gauche, ou sur sa tuile) le pose par-dessus ce qui est à l'écran : déplacez-le ici, agrandissez-le par ses coins. La source la plus haute de la liste passe devant les autres.",
        TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, Margin = new Thickness(2, 10, 2, 0),
    };
    readonly Button toutRetirer = new() { Content = "Tout retirer", ToolTip = "Retire toutes les sources superposées (le contenu principal reste)" };
    /// <summary>Le calque choisi : ses poignées dans l'écran, sa ligne en or dans la liste.</summary>
    string? calqueChoisi;
    string signatureCalques = "";

    sealed class GlisseCalque { public required string Id; public Point Depart; public int Poignee = -1; public double X, Y, L, H; public bool Commence; }
    GlisseCalque? glisseCalque;
    /// <summary>La place du calque qu'on glisse (parts de l'écran), montrée tout de suite ; la régie la reçoit au lâcher.</summary>
    (string Id, double X, double Y, double L, double H)? placeLocale;

    void InitCalques()
    {
        var titre = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        toutRetirer.Style = Style_("BtnPetit");
        toutRetirer.Click += (_, _) => { R.RetirerCalques(); calqueChoisi = null; };
        DockPanel.SetDock(toutRetirer, Dock.Right);
        titre.Children.Add(toutRetirer);
        titre.Children.Add(new Capitales { Libelle = "À l'écran · sources superposées", VerticalAlignment = VerticalAlignment.Center });
        ZoneCalques.Children.Add(titre);

        var plan = new Grid { Width = LargeurEcran, Height = HauteurEcran };
        plan.Children.Add(calquesRendu);
        plan.Children.Add(calquesSurcouche);
        calquesVue.Child = plan;
        ZoneCalques.Children.Add(new Border
        {
            Background = Brushes.Black, CornerRadius = new CornerRadius(10), ClipToBounds = true, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), Child = calquesVue,
        });
        ZoneCalques.Children.Add(new TextBlock
        {
            Text = "Glisser : déplacer · coins : agrandir (Maj : garder les proportions) · Alt : sans aimant",
            FontSize = 12, Foreground = Pinceau("Brume"), Margin = new Thickness(2, 6, 0, 12), TextWrapping = TextWrapping.Wrap,
        });
        ZoneCalques.Children.Add(new Capitales { Libelle = "Sources à l'écran · la plus haute passe devant", Margin = new Thickness(0, 0, 0, 8) });
        ZoneCalques.Children.Add(listeCalquesUi);
        calquesAide.Foreground = Pinceau("Brume");
        ZoneCalques.Children.Add(calquesAide);

        calquesSurcouche.MouseLeftButtonDown += CalqueBas;
        calquesSurcouche.MouseMove += CalqueBouge;
        calquesSurcouche.MouseLeftButtonUp += (_, _) => CalqueHaut();
        calquesSurcouche.LostMouseCapture += (_, _) => { if (glisseCalque != null) CalqueHaut(); };
        calquesSurcouche.MouseMove += (_, e) => { if (glisseCalque == null) calquesSurcouche.Cursor = CurseurCalque(e.GetPosition(calquesSurcouche)); };
        calquesVue.SizeChanged += (_, _) => DessinerSurcouche();

        // les images vivantes des calques, vers chaque dessin de l'écran (et vers MyStream, qui les pose lui-même)
        App.Calques.Image += (id, b) =>
        {
            ApercuRendu.ImageCalque(id, b);
            CulteRendu.ImageCalque(id, b);
            ecran?.Rendu.ImageCalque(id, b);
            EgliseRendu.ImageCalque(id, b);
            StreamRendu.ImageCalque(id, b);
            calquesRendu.ImageCalque(id, b);
            App.MyStream?.ImageCalque(id, b as BitmapSource);
        };
        MajCalquesUi(R.Etat);
    }

    /// <summary>Ce qui part à l'écran : l'état de la régie, ou rien du tout (ni texte, ni calque) pendant l'écran noir.</summary>
    EtatEcran EtatProjete() =>
        noir ? R.Etat with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null, Calques = null, Rang = 0 } : R.Etat;

    /// <summary>Pour la page OBS : chaque calque dit le fichier de son image vivante.</summary>
    static EtatEcran AvecImagesObs(EtatEcran e) =>
        e.Calques is { Count: > 0 } l ? e with { Calques = l.Select(c => c with { Image = App.Calques.FichierObs(c.Id) }).ToList() } : e;

    /// <summary>« Reconnecter » : relance les captures en cours (écran principal et calques).</summary>
    void ReconnecterSources()
    {
        // 1.6.5 : les caméras USB cherchent de nouveau leur meilleur réglage (images par seconde, taille)
        MyVerse.Ui.Audio.CameraFrames.Oublier();
        App.Sources.Reconnecter(EtatProjete());
        App.Calques.Reconnecter();
    }

    // ------------------------------------------------------------ superposer
    /// <summary>« Superposer » : le média part à l'écran par-dessus ce qui y est (un nouveau calque, choisi dans Médias).</summary>
    void Superposer(Annonce a)
    {
        if (R.Superposer(a) is not { } c) return;
        calqueChoisi = c.Id;
        MajCalquesUi(R.Etat, true);
        Toast($"« {a.Titre} » est superposé : dans Médias, déplacez-le et agrandissez-le ; ▲ ▼ le mettent devant ou derrière.");
    }

    void SuperposerAnnonceListe_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is Annonce a) Superposer(a);
    }

    // ------------------------------------------------------------ l'écran et la liste
    /// <summary>Après chaque changement de l'écran : le dessin de Médias, ses poignées et la liste des sources (refaite si elle a changé).</summary>
    void MajCalquesUi(EtatEcran e, bool forcer = false)
    {
        if (calqueChoisi != null && R.Calques.All(c => c.Id != calqueChoisi)) calqueChoisi = null;
        calquesRendu.Afficher(e);
        calquesRendu.ImageDisplay(App.Sources.Image);
        var calques = R.Calques;
        int rang = Math.Clamp(R.Etat.Rang, 0, calques.Count);
        var signature = string.Join("|", calques.Select(c => $"{c.Id}:{c.Visible}:{c.Libelle}:{c.L:0.##}")) + $"#{rang}#{LibellePrincipal(e)}#{calqueChoisi}#{noir}";
        DessinerSurcouche();
        if (!forcer && signature == signatureCalques) return;
        signatureCalques = signature;
        listeCalquesUi.Children.Clear();
        // du haut (devant) vers le bas (derrière), comme dans OBS ; le contenu principal à son étage
        for (int p = calques.Count; p >= 0; p--)
        {
            bool haut = p == calques.Count, bas = p == 0;
            listeCalquesUi.Children.Add(p == rang ? LignePrincipal(e, haut, bas) : LigneCalque(calques[p < rang ? p : p - 1], haut, bas));
        }
        calquesAide.Visibility = calques.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        toutRetirer.Visibility = calques.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Le contenu principal en quelques mots (« Jean 3:16 », « Caméra 1 », « rien »).</summary>
    string LibellePrincipal(EtatEcran e)
    {
        if (noir) return "écran noir";
        var t = e.Mode switch
        {
            "vide" => "rien pour l'instant",
            "fond" => "le fond seul",
            "media" => e.Ref is { Length: > 0 } r ? r : "un média",
            "verset" => e.Ref,
            _ => (e.Lignes.FirstOrDefault() ?? "").Trim(),
        };
        return t.Length > 48 ? t[..48].TrimEnd() + "…" : t;
    }

    static string SorteCalque(CalqueEcran c) => c.Media?.Type switch
    {
        "camera" => "Caméra", "mycam" => "MyCam", "fenetre" => "Fenêtre", "video" => "Vidéo", "image" => "Image", _ => "Texte",
    };

    Button BoutonCalque(string texte, string tip, Action clic, bool actif = true)
    {
        var b = new Button { Style = Style_("BtnPlat"), Content = texte, FontSize = 14, Padding = new Thickness(9, 3, 9, 3), ToolTip = tip, IsEnabled = actif, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetShowOnDisabled(b, true);
        b.Click += (_, e) => { e.Handled = true; clic(); };
        return b;
    }

    Border CarteCalque(UIElement contenu, bool choisi) => new()
    {
        Child = contenu, Padding = new Thickness(8, 6, 6, 6), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(10),
        Background = Pinceau(choisi ? "OrLeger" : "Encre"), BorderBrush = Pinceau(choisi ? "OrVif" : "Nuit3"), BorderThickness = new Thickness(1),
    };

    FrameworkElement LigneCalque(CalqueEcran c, bool haut, bool bas)
    {
        var ligne = new DockPanel();
        var boutons = new StackPanel { Orientation = Orientation.Horizontal };
        boutons.Children.Add(BoutonCalque("⤢", "Tout l'écran", () => R.PleinEcranCalque(c.Id)));
        boutons.Children.Add(BoutonCalque("▲", "Monter : passe devant", () => R.MonterCalque(c.Id, 1), !haut));
        boutons.Children.Add(BoutonCalque("▼", "Descendre : passe derrière", () => R.MonterCalque(c.Id, -1), !bas));
        boutons.Children.Add(BoutonCalque("✕", "Retirer de l'écran", () => { if (calqueChoisi == c.Id) calqueChoisi = null; R.RetirerCalque(c.Id); }));
        DockPanel.SetDock(boutons, Dock.Right);
        ligne.Children.Add(boutons);
        var oeil = new CheckBox { IsChecked = c.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0), ToolTip = "Montrer ou cacher (elle reste dans la liste)" };
        oeil.Click += (_, _) => R.MontrerCalque(c.Id, oeil.IsChecked == true);
        DockPanel.SetDock(oeil, Dock.Left);
        ligne.Children.Add(oeil);
        var noms = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        noms.Children.Add(new TextBlock { Text = c.Libelle, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Pinceau(c.Visible ? "Blanc" : "Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
        bool plein = c.L >= 0.999 && c.H >= 0.999;
        noms.Children.Add(new TextBlock
        {
            Text = SorteCalque(c) + (c.Visible ? "" : " · caché") + (plein ? " · tout l'écran" : $" · {Math.Round(c.L * 100)} % × {Math.Round(c.H * 100)} %"),
            FontSize = 12, Foreground = Pinceau("Brume2"), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        ligne.Children.Add(noms);
        var carte = CarteCalque(ligne, c.Id == calqueChoisi);
        carte.Cursor = Cursors.Hand;
        carte.ToolTip = "Clic : la choisir (ses poignées apparaissent dans l'écran)";
        carte.MouseLeftButtonDown += (_, _) => { calqueChoisi = c.Id; MajCalquesUi(R.Etat, true); };
        return carte;
    }

    FrameworkElement LignePrincipal(EtatEcran e, bool haut, bool bas)
    {
        var ligne = new DockPanel();
        var boutons = new StackPanel { Orientation = Orientation.Horizontal };
        boutons.Children.Add(BoutonCalque("▲", "Monter : le contenu principal passe devant", () => R.MonterPrincipal(1), !haut));
        boutons.Children.Add(BoutonCalque("▼", "Descendre : le contenu principal passe derrière", () => R.MonterPrincipal(-1), !bas));
        DockPanel.SetDock(boutons, Dock.Right);
        ligne.Children.Add(boutons);
        var icone = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(e.Mode == "media" ? "IconeEcran" : "IconeTexte"), Stroke = Pinceau("Or"), StrokeThickness = 1.8, Width = 15, Height = 15,
            Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        DockPanel.SetDock(icone, Dock.Left);
        ligne.Children.Add(icone);
        var noms = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        noms.Children.Add(new TextBlock { Text = "Écran principal", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Pinceau("Blanc") });
        noms.Children.Add(new TextBlock { Text = "Versets, chants, média en grand · " + LibellePrincipal(e), FontSize = 12, Foreground = Pinceau("Brume2"), TextTrimming = TextTrimming.CharacterEllipsis });
        ligne.Children.Add(noms);
        var carte = CarteCalque(ligne, false);
        carte.BorderBrush = Pinceau("OrBord");
        return carte;
    }

    // ------------------------------------------------------------ l'écran : choisir, déplacer, agrandir
    double EchelleCalques() => calquesVue.ActualWidth > 0 ? calquesVue.ActualWidth / LargeurEcran : 0.35;

    /// <summary>La place d'un calque (en pixels de l'écran 1920 × 1080), celle qu'on glisse si c'est lui.</summary>
    Rect PlaceCalque(CalqueEcran c)
    {
        var (x, y, l, h) = placeLocale is { } p && p.Id == c.Id ? (p.X, p.Y, p.L, p.H) : (c.X, c.Y, c.L, c.H);
        return new Rect(x * LargeurEcran, y * HauteurEcran, l * LargeurEcran, h * HauteurEcran);
    }

    /// <summary>Les 8 poignées : coins (0 à 3, sens horaire depuis le haut à gauche), puis bords (4 à 7 : haut, droite, bas, gauche).</summary>
    static Point[] PoigneesCalque(Rect r) => new[]
    {
        r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft,
        new Point(r.X + r.Width / 2, r.Y), new Point(r.Right, r.Y + r.Height / 2), new Point(r.X + r.Width / 2, r.Bottom), new Point(r.X, r.Y + r.Height / 2),
    };

    void DessinerSurcouche()
    {
        calquesSurcouche.Children.Clear();
        double k = EchelleCalques();
        foreach (var c in R.Calques.Where(c => c.Visible))
        {
            var r = PlaceCalque(c);
            bool choisi = c.Id == calqueChoisi;
            var cadre = new Rectangle
            {
                Width = r.Width, Height = r.Height, StrokeThickness = (choisi ? 2.5 : 1.2) / k,
                Stroke = choisi ? new SolidColorBrush(Color.FromRgb(255, 196, 0)) : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                StrokeDashArray = choisi ? null : new DoubleCollection { 4, 3 },
            };
            Canvas.SetLeft(cadre, r.X); Canvas.SetTop(cadre, r.Y);
            calquesSurcouche.Children.Add(cadre);
            if (!choisi) continue;
            double t = 11 / k;
            foreach (var p in PoigneesCalque(r))
            {
                var poignee = new Rectangle { Width = t, Height = t, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(255, 160, 0)), StrokeThickness = 1.5 / k };
                Canvas.SetLeft(poignee, p.X - t / 2); Canvas.SetTop(poignee, p.Y - t / 2);
                calquesSurcouche.Children.Add(poignee);
            }
            var nom = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 196, 0)), CornerRadius = new CornerRadius(4 / k), Padding = new Thickness(6 / k, 2 / k, 6 / k, 2 / k),
                Child = new TextBlock { Text = c.Libelle, FontSize = 12 / k, FontWeight = FontWeights.Bold, Foreground = Brushes.Black },
            };
            Canvas.SetLeft(nom, r.X + 4 / k);
            Canvas.SetTop(nom, r.Y > 24 / k ? r.Y - 22 / k : r.Y + 4 / k);
            calquesSurcouche.Children.Add(nom);
        }
    }

    int PoigneeCalqueSous(Point p)
    {
        if (calqueChoisi == null || R.Calques.FirstOrDefault(c => c.Id == calqueChoisi) is not { Visible: true } c) return -1;
        double seuil = 12 / EchelleCalques();
        var poignees = PoigneesCalque(PlaceCalque(c));
        for (int i = 0; i < poignees.Length; i++)
            if (Math.Abs(p.X - poignees[i].X) <= seuil && Math.Abs(p.Y - poignees[i].Y) <= seuil) return i;
        return -1;
    }

    /// <summary>Le calque montré sous la souris, celui de devant d'abord.</summary>
    CalqueEcran? CalqueSous(Point p)
    {
        var l = R.Calques;
        for (int i = l.Count - 1; i >= 0; i--)
            if (l[i].Visible && PlaceCalque(l[i]).Contains(p)) return l[i];
        return null;
    }

    Cursor CurseurCalque(Point p) => PoigneeCalqueSous(p) switch
    {
        0 or 2 => Cursors.SizeNWSE,
        1 or 3 => Cursors.SizeNESW,
        4 or 6 => Cursors.SizeNS,
        5 or 7 => Cursors.SizeWE,
        _ => CalqueSous(p) != null ? Cursors.SizeAll : Cursors.Arrow,
    };

    void CalqueBas(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(calquesSurcouche);
        int poignee = PoigneeCalqueSous(p);
        CalqueEcran? c = poignee >= 0 ? R.Calques.FirstOrDefault(x => x.Id == calqueChoisi) : CalqueSous(p);
        if (c == null)
        {
            if (calqueChoisi != null) { calqueChoisi = null; MajCalquesUi(R.Etat, true); }
            return;
        }
        if (c.Id != calqueChoisi) { calqueChoisi = c.Id; MajCalquesUi(R.Etat, true); }
        if (e.ClickCount == 2 && poignee < 0) { R.PleinEcranCalque(c.Id); e.Handled = true; return; }
        glisseCalque = new GlisseCalque { Id = c.Id, Depart = p, Poignee = poignee, X = c.X, Y = c.Y, L = c.L, H = c.H };
        calquesSurcouche.CaptureMouse();
        e.Handled = true;
    }

    void CalqueBouge(object sender, MouseEventArgs e)
    {
        if (glisseCalque is not { } g) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CalqueHaut(); return; }
        var p = e.GetPosition(calquesSurcouche);
        double dx = (p.X - g.Depart.X) / LargeurEcran, dy = (p.Y - g.Depart.Y) / HauteurEcran;
        if (!g.Commence)
        {
            if (Math.Abs(p.X - g.Depart.X) < 3 && Math.Abs(p.Y - g.Depart.Y) < 3) return;
            g.Commence = true;
        }
        bool aimant = !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        double x = g.X, y = g.Y, l = g.L, h = g.H;
        int q = g.Poignee;
        if (q < 0)
        {
            x = g.X + dx; y = g.Y + dy;
            if (aimant) { x = Aimanter(x, l); y = Aimanter(y, h); }
        }
        else
        {
            if (q is 0 or 3 or 7) { x = g.X + dx; l = g.L - dx; }
            if (q is 1 or 2 or 5) l = g.L + dx;
            if (q is 0 or 1 or 4) { y = g.Y + dy; h = g.H - dy; }
            if (q is 2 or 3 or 6) h = g.H + dy;
            // Maj : les proportions de départ (coins seulement)
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && q <= 3 && g.L > 0 && g.H > 0)
            {
                double ratio = g.L / g.H;
                if (Math.Abs(l - g.L) / g.L > Math.Abs(h - g.H) / g.H) { double h2 = l / ratio; if (q is 0 or 1) y += h - h2; h = h2; }
                else { double l2 = h * ratio; if (q is 0 or 3) x += l - l2; l = l2; }
            }
            double min = Regie.TailleMinCalque;
            if (l < min) { if (q is 0 or 3 or 7) x -= min - l; l = min; }
            if (h < min) { if (q is 0 or 1 or 4) y -= min - h; h = min; }
        }
        // dans l'écran
        l = Math.Min(l, 1); h = Math.Min(h, 1);
        x = Math.Clamp(x, 0, 1 - l); y = Math.Clamp(y, 0, 1 - h);
        placeLocale = (g.Id, x, y, l, h);
        calquesRendu.PlacerCalqueLocal(g.Id, x, y, l, h);
        DessinerSurcouche();
    }

    /// <summary>L'aimant : le bord, le milieu ou l'autre bord du calque se colle au bord ou au milieu de l'écran (à 1 % près).</summary>
    static double Aimanter(double pos, double taille)
    {
        const double seuil = 0.012;
        foreach (var cible in new[] { 0, 0.5, 1 })
            foreach (var decalage in new[] { 0, taille / 2, taille })
                if (Math.Abs(pos + decalage - cible) < seuil) return cible - decalage;
        return pos;
    }

    void CalqueHaut()
    {
        var g = glisseCalque;
        glisseCalque = null;
        if (calquesSurcouche.IsMouseCaptured) calquesSurcouche.ReleaseMouseCapture();
        if (g is { Commence: true } && placeLocale is { } p && p.Id == g.Id) R.PlacerCalque(p.Id, p.X, p.Y, p.L, p.H);
        placeLocale = null;
        DessinerSurcouche();
    }

    // ------------------------------------------------------------ pour le test automatique
    public IReadOnlyList<CalqueEcran> TestCalques => R.Calques;
    public void TestSuperposer(Annonce a) => Superposer(a);
    /// <summary>Les lignes de la liste (du haut vers le bas) : le nom de chaque source, « Écran principal » pour le contenu principal.</summary>
    public string TestListeCalques => string.Join(" | ", listeCalquesUi.Children.OfType<Border>()
        .Select(b => b.Child is DockPanel d ? d.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "" : ""));
    /// <summary>Glisse le calque choisi à la souris (déplacement en pixels de l'écran 1920 × 1080), comme dans Médias.</summary>
    public bool TestGlisserCalque(string id, double dx, double dy, int poignee = -1)
    {
        if (R.Calques.FirstOrDefault(c => c.Id == id) is not { } c) return false;
        calqueChoisi = id;
        var r = PlaceCalque(c);
        var depart = poignee >= 0 ? PoigneesCalque(r)[poignee] : new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        glisseCalque = new GlisseCalque { Id = id, Depart = depart, Poignee = poignee, X = c.X, Y = c.Y, L = c.L, H = c.H, Commence = true };
        var arrivee = new Point(depart.X + dx, depart.Y + dy);
        double ddx = (arrivee.X - depart.X) / LargeurEcran, ddy = (arrivee.Y - depart.Y) / HauteurEcran;
        double x = c.X, y = c.Y, l = c.L, h = c.H;
        if (poignee < 0) { x += ddx; y += ddy; }
        else
        {
            if (poignee is 0 or 3 or 7) { x += ddx; l -= ddx; }
            if (poignee is 1 or 2 or 5) l += ddx;
            if (poignee is 0 or 1 or 4) { y += ddy; h -= ddy; }
            if (poignee is 2 or 3 or 6) h += ddy;
        }
        l = Math.Clamp(l, Regie.TailleMinCalque, 1); h = Math.Clamp(h, Regie.TailleMinCalque, 1);
        placeLocale = (id, Math.Clamp(x, 0, 1 - l), Math.Clamp(y, 0, 1 - h), l, h);
        CalqueHaut();
        return true;
    }
}
