using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 3.3 :
/// - la carte « Écran par défaut » de la colonne de contrôle : noir (l'écran se vide, comme avant), fond seul, ou un écran principal
///   du déroulé ; Retirer de l'écran (Échap) revient direct sur celui qui est choisi ;
/// - l'écran en direct en grand sur la page Culte (1.5.0 : Précédent, Suivant, Noir, Effacer sont dans la barre du bas) ;
/// - la barre de gauche, étroite (1.5.0).
/// </summary>
public partial class MainWindow
{
    bool barreLarge;
    string signatureDefaut = "";
    readonly List<(Button Bouton, string Id, Border Coche)> boutonsDefaut = new();

    void InitEcranDefaut()
    {
        CultePrec.Click += (_, _) => R.Bouger(-1);
        CulteSuiv.Click += (_, _) => R.Bouger(1);
        CulteNoir.Click += (_, _) => BasculerNoir();
        CulteRetirer.Click += (_, _) => R.Effacer();
        CentreAgenda.SizeChanged += (_, _) => TailleCulteLive();
        SizeChanged += (_, _) => AdapterBarre();
        // nouveau thème : les miniatures sont redessinées
        Palette.Change += () => { signatureDefaut = ""; MajEcranDefaut(); };
        AdapterBarre();
        MajEcranDefaut();
    }

    // ------------------------------------------------------------ barre de gauche
    /// <summary>1.5.0 : la barre de gauche est toujours étroite (icône au-dessus du nom) : trois pages, Culte, Bibliothèque, Son.</summary>
    void AdapterBarre()
    {
        barreLarge = false;
        if (ColonneRail.Width.Value != 84) ColonneRail.Width = new GridLength(84);
    }

    // ------------------------------------------------------------ écran en direct de la page Culte
    /// <summary>L'écran en direct prend un peu plus de la moitié de la hauteur, sans dépasser la largeur : les diapositives du culte restent dessous.</summary>
    void TailleCulteLive()
    {
        double h = CentreAgenda.ActualHeight, w = CentreAgenda.ActualWidth;
        if (h <= 0 || w <= 0) return;
        double parHauteur = Math.Clamp(h * 0.56, 160, 560) * 16 / 9;
        CulteApercu.MaxWidth = Math.Round(Math.Max(240, Math.Min(parHauteur, w - 52)));
    }

    /// <summary>Le grand écran live : ce qui est à l'écran, ce qui vient avec Suivant, le bouton Noir.</summary>
    void MajCulteLive(EtatEcran e, Suivante? s)
    {
        CulteRendu.Afficher(e);
        CulteApercu.BorderBrush = Pinceau(e.Vide && e.Duo == null ? "Nuit3" : "Ecran");
        var a = AEcran(e);
        CulteAEcran.Text = a.Length > 0 ? char.ToUpperInvariant(a[0]) + a[1..] : "";
        CulteEnsuite.Text = "Ensuite : " + (s?.Label ?? "rien");
        CulteEnsuite.ToolTip = s != null && s.Texte.Length > 0 ? s.Texte.Replace("\r", "").Replace("\n", " ") : null;
        if (noir) { CulteNoir.Background = Pinceau("Blanc"); CulteNoir.Foreground = Pinceau("Page"); CulteNoir.BorderBrush = Pinceau("Blanc"); }
        else { CulteNoir.ClearValue(BackgroundProperty); CulteNoir.ClearValue(ForegroundProperty); CulteNoir.ClearValue(BorderBrushProperty); }
    }

    /// <summary>« Jean 3:16 (LSG) », « Yera, Yera · 2/4 », « écran par défaut · Logo »…</summary>
    string AEcran(EtatEcran e)
    {
        if (noir) return "écran noir";
        if (R.Cible is CibleDefaut)
            return R.ElementCourant is { } p ? "écran principal · " + p.Titre : "écran de repos · " + R.LibelleEcranDefaut;
        if (e.Vide) return e.Duo != null ? "dans le coin · " + e.Duo.Libelle : "rien";
        if (R.Cible is CibleChant cc && R.Trouver(cc.Id) is { } c)
        {
            // 1.5.0 : « À toi la gloire, refrain »
            if (c.Sections.Count <= 1) return c.Title;
            var lab = cc.I >= 0 && cc.I < c.Sections.Count ? (c.Sections[cc.I].Label ?? "").Trim() : "";
            return c.Title + ", " + (lab.Length > 0 ? char.ToLowerInvariant(lab[0]) + lab[1..] : $"{cc.I + 1} sur {c.Sections.Count}");
        }
        if (e.Mode is "verset" or "media" && e.Ref.Length > 0) return e.Ref;
        if (R.ElementCourant is { } el) return TitreElement(el);
        return e.Lignes.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
    }

    // ------------------------------------------------------------ carte « Écran par défaut »
    /// <summary>
    /// Un bouton par choix (noir, fond seul, puis les écrans principaux du déroulé), avec la miniature de ce qui reviendrait à l'écran ;
    /// le choix en cours est en or. Redessinée seulement quand la liste, l'habillage ou le thème change.
    /// </summary>
    void MajEcranDefaut()
    {
        var choix = new List<(string Id, string Nom, string Sous)>
        {
            ("noir", "Noir", "L'écran se vide (comme avant)"),
            ("fond", "Fond seul", "Le fond seul, sans texte"),
        };
        foreach (var p in R.EcransPrincipaux)
            choix.Add((p.Id, p.Titre, (p.Heure is { Length: > 0 } h ? h + " · " : "") + "Programme · " + R.Description(p).Sous.Split(" · ")[0]));
        var signature = string.Join("|", choix.Select(c => c.Id + "=" + c.Nom + "=" + c.Sous)) + "|" + Habillage(R.Etat);
        if (signature != signatureDefaut)
        {
            signatureDefaut = signature;
            DefautChoix.Children.Clear();
            boutonsDefaut.Clear();
            foreach (var (id, nom, sous) in choix) DefautChoix.Children.Add(BoutonDefaut(id, nom, sous));
        }
        var actuel = R.EcranDefaut;
        foreach (var (b, id, coche) in boutonsDefaut)
        {
            bool ok = id == actuel;
            b.BorderBrush = Pinceau(ok ? "OrVif" : "Nuit3");
            b.BorderThickness = new Thickness(ok ? 2 : 1);
            b.Background = Pinceau(ok ? "OrLeger" : "Encre");
            coche.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        }
        var lib = R.LibelleEcranDefaut;
        CulteRetourTexte.Text = "Retour direct : " + (actuel == "noir" ? "noir" : lib);
        TiroirReposValeur.Text = actuel == "noir" ? "Noir" : lib;
        TiroirReposValeur.ToolTip = TiroirReposValeur.Text;
        Effacer.ToolTip = CulteRetirer.ToolTip = actuel == "noir"
            ? "Échap · l'écran se vide (écran de repos : noir)"
            : $"Échap · retour direct sur l'écran de repos : {lib}";
    }

    Button BoutonDefaut(string id, string nom, string sous)
    {
        // miniature : l'écran tel qu'il reviendrait (noir : rien)
        var mini = new Grid { Background = Brushes.Black, ClipToBounds = true };
        EtatEcran? etat = null;
        if (id != "noir")
        {
            try { etat = R.EtatCible(new CibleDefaut(id)); }
            catch (Exception x) { Journal.Erreur("miniature de l'écran de repos", x); }
        }
        if (etat != null)
        {
            var rendu = new Rendu { Transitions = false, Leger = true, Width = 1920, Height = 1080, IsHitTestVisible = false };
            rendu.Afficher(etat);
            if (etat is { Mode: "media", Media: { Type: "image" } m }) rendu.ImageDisplay(ImageFichier(m.Chemin, 160));
            mini.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = rendu });
            if (etat is { Mode: "media", Media.Type: "camera" or "mycam" or "fenetre" or "video" } && etat.Media is { } mv)
                mini.Children.Add(new TextBlock
                {
                    Text = mv.Type switch { "camera" => "Caméra", "mycam" => "MyCam", "fenetre" => "Fenêtre", _ => "Vidéo" }, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = Pinceau("TuileTexte"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
        }
        var cadre = new Border
        {
            Width = 64, Height = 36, CornerRadius = new CornerRadius(6), Child = mini, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Pinceau("Nuit3"), BorderThickness = new Thickness(1), ClipToBounds = true,
        };
        var texte = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        texte.Children.Add(new TextBlock { Text = nom, FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Pinceau("Blanc"), TextTrimming = TextTrimming.CharacterEllipsis });
        texte.Children.Add(new TextBlock { Text = sous, FontSize = 11.5, Foreground = Pinceau("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        var coche = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Pinceau("OrVif"), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            Child = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconeCoche"), Stroke = Pinceau("SurOr"), StrokeThickness = 2.4, Width = 12, Height = 12, Stretch = Stretch.Uniform,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grille.Children.Add(cadre);
        Grid.SetColumn(texte, 1);
        grille.Children.Add(texte);
        Grid.SetColumn(coche, 2);
        grille.Children.Add(coche);
        var b = new Button
        {
            Style = Style_("Btn"), Content = grille, Padding = new Thickness(6, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8), MinHeight = 50,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = id,
            ToolTip = id == "noir" ? "Effacer vide l'écran, comme avant" : $"Effacer revient tout de suite sur « {nom} »",
        };
        b.Click += (_, _) =>
        {
            R.ChoisirEcranDefaut(id);
            MajEcranDefaut();
            Toast(id == "noir"
                ? "Écran de repos : noir. Effacer vide l'écran, comme avant."
                : $"Écran de repos : {nom}. Effacer (Échap) revient tout de suite dessus.");
        };
        boutonsDefaut.Add((b, id, coche));
        return b;
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>« Noir | Fond seul | Logo* » : les choix de la carte « Écran par défaut », * sur celui qui est choisi.</summary>
    public string TestEcranDefautChoix => string.Join(" | ", boutonsDefaut.Select(x =>
        (((x.Bouton.Content as Grid)?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault()?.Text) ?? x.Id)
        + (x.Coche.Visibility == Visibility.Visible ? "*" : "")));
    /// <summary>Clic sur le choix <paramref name="id"/> de la carte « Écran par défaut ».</summary>
    public bool TestChoisirEcranDefaut(string id)
    {
        if (boutonsDefaut.FirstOrDefault(x => x.Id == id).Bouton is not { } b) return false;
        b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return true;
    }
    public string TestCulteAEcran => CulteAEcran.Text;
    public string TestCulteRetour => CulteRetourTexte.Text;
    public bool TestBarreLarge => barreLarge;
}
