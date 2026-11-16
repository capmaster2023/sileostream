using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 2.0, design « Grand Bouton » : par-dessus la régie complète, des écrans simples pour le dimanche.
/// Bonjour (les vérifications), la Régie (le gros Suivant), la barre magique (Ctrl K), Préparer le culte, le son en gros boutons
/// et Aller en direct. « Mode complet » ramène la régie de toujours ; le bouton « Simple » de sa barre de gauche revient ici.
/// Tout passe par la même régie (<see cref="Regie"/>) : le téléphone, la projection, Zoom et YouTube suivent comme avant.
/// </summary>
public partial class MainWindow
{
    Grid? v2;
    readonly Dictionary<string, FrameworkElement> v2Pages = new();
    string v2Page = "";
    readonly DispatcherTimer v2Minuterie = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int v2Tic;
    Button? v2BoutonSimple;
    bool v2CulteAFaire = true, v2PreparerAFaire = true;
    static readonly CultureInfo V2Fr = CultureInfo.GetCultureInfo("fr-FR");
    static BitmapImage? v2Logo;

    bool V2Visible => v2 is { Visibility: Visibility.Visible };

    void InitV2()
    {
        if (Content is not Grid racine) return;
        v2 = new Grid { Background = V2.Page, Visibility = Visibility.Collapsed };
        Grid.SetRowSpan(v2, 3);
        Grid.SetColumnSpan(v2, 4);
        Panel.SetZIndex(v2, 500);
        TextOptions.SetTextFormattingMode(v2, TextFormattingMode.Ideal);
        racine.Children.Add(v2);
        // le message court reste au-dessus des écrans 2.0 ; l'écran de connexion (1000) aussi
        Panel.SetZIndex(ToastBoite, 900);

        // dans la régie complète : « Simple » ramène aux écrans 2.0 (en bas de la barre de gauche)
        if (ThemeBtn.Parent is Panel bas)
        {
            v2BoutonSimple = new Button
            {
                Style = Style_("BtnRail"), Content = "Simple", Tag = Geometry.Parse(V2.IcMaison),
                ToolTip = "Le mode simple de Siloé Stream 2.0 : le gros bouton Suivant, le culte en cartes",
            };
            v2BoutonSimple.Click += (_, _) => V2Aller("regie");
            bas.Children.Insert(0, v2BoutonSimple);
        }
        v2Minuterie.Tick += (_, _) => V2Battre();
        R.AgendaChange += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            v2CulteAFaire = v2PreparerAFaire = true;
            if (V2Visible) V2MajTout();
        }));
        App.Direct.Change += () => { if (V2Visible) V2MajTout(); };
        App.Telephone.Change += () => Dispatcher.BeginInvoke(new Action(() => { if (V2Visible && v2Page == "accueil") V2MajAccueil(); }));
        // au démarrage : le mode simple, sauf si l'opérateur a choisi la régie complète (et jamais pendant le test automatique)
        if (App.AutoTest == null && App.Donnees.Reglages.Interface != "complet") V2Aller("accueil", false);
    }

    /// <summary>
    /// Va à un écran 2.0 : « accueil », « regie », « preparer », « son », « direct » ; « classique » ramène la régie complète.
    /// <paramref name="retenir"/> : le choix simple / complet est gardé pour le prochain démarrage.
    /// </summary>
    void V2Aller(string page, bool retenir = true)
    {
        if (v2 == null) return;
        if (page != "classique" && !PagePermise("agenda")) page = "classique";
        if (page == "son" && !PagePermise("son")) page = "regie";
        var reg = App.Donnees.Reglages;
        if (page == "classique")
        {
            V2FermerBarre();
            v2.Visibility = Visibility.Collapsed;
            v2Minuterie.Stop();
            if (retenir && reg.Interface != "complet") { reg.Interface = "complet"; App.Donnees.SauverReglages(); }
            return;
        }
        if (retenir && reg.Interface != "simple") { reg.Interface = "simple"; App.Donnees.SauverReglages(); }
        if (!v2Pages.TryGetValue(page, out var p))
        {
            p = page switch
            {
                "accueil" => V2PageAccueil(),
                "preparer" => V2PagePreparer(),
                "son" => V2PageSon(),
                "direct" => V2PageDirect(),
                _ => V2PageRegie(),
            };
            v2Pages[page] = p;
            v2.Children.Insert(0, p);
        }
        foreach (var kv in v2Pages) kv.Value.Visibility = kv.Key == page ? Visibility.Visible : Visibility.Collapsed;
        v2Page = page;
        bool montrer = !V2Visible;
        v2.Visibility = Visibility.Visible;
        if (montrer || page != "regie") V2FermerBarre();
        // le clavier ne reste pas dans un champ de la régie complète : Espace = Suivant
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
        V2MajEcran();
        V2MajTout();
        v2Minuterie.Start();
    }

    void V2MajTout()
    {
        if (!V2Visible) return;
        switch (v2Page)
        {
            case "accueil": V2MajAccueil(); break;
            case "regie": V2MajRegie(); break;
            case "preparer": V2MajPreparer(); break;
            case "son": V2MajSon(true); break;
            case "direct": V2MajDirect(); break;
        }
    }

    void V2Battre()
    {
        if (!V2Visible) { v2Minuterie.Stop(); return; }
        v2Tic++;
        if (v2Page == "son") V2MajSon(false);
        else if (v2Page == "accueil") V2BarresSon();
        if (v2Tic % 4 != 0) return;
        if (v2Page == "accueil") V2MajAccueil();
        else if (v2Page == "regie") V2MajEtats();
        else if (v2Page == "direct") V2MajDirect();
    }

    /// <summary>Le rôle du compte : le mode simple est celui des rôles qui ont le culte (Admin, Affichage).</summary>
    void V2Role()
    {
        bool permis = PagePermise("agenda");
        if (v2BoutonSimple != null) v2BoutonSimple.Visibility = permis ? Visibility.Visible : Visibility.Collapsed;
        if (!permis && V2Visible) V2Aller("classique", false);
        else V2MajTout();
    }

    /// <summary>Les aperçus des écrans 2.0 (l'église, Internet) suivent ce qui est projeté.</summary>
    void V2MajEcran()
    {
        if (!V2Visible) return;
        var e = EtatProjete();
        var salle = R.EtatSalle(e);
        var imageSalle = mediaSalle != null ? App.SourcesDefaut.Image : App.Sources.Image;
        if (v2RenduEglise != null) { v2RenduEglise.Afficher(salle); v2RenduEglise.ImageDisplay(imageSalle); }
        v2RenduStream?.Afficher(etatStream);
        v2RenduDirect?.Afficher(etatStream);
        V2ImageStream(lecteurStream?.Image);
        if (v2Page == "regie") V2MajRegie();
    }

    void V2ImageStream(BitmapSource? b)
    {
        v2RenduStream?.ImageDisplay(b);
        v2RenduDirect?.ImageDisplay(b);
    }

    // ------------------------------------------------------------ le clavier des écrans 2.0
    /// <summary>Vrai si les écrans 2.0 s'occupent de la touche (la régie complète, derrière, ne la voit pas).</summary>
    bool V2Clavier(KeyEventArgs e)
    {
        if (!V2Visible || ConnexionAffichee) return false;
        if (v2BarreOuverte)
        {
            switch (e.Key)
            {
                case Key.Escape: V2FermerBarre(); e.Handled = true; break;
                case Key.Down: V2BarreChoisir(1); e.Handled = true; break;
                case Key.Up: V2BarreChoisir(-1); e.Handled = true; break;
                case Key.Enter: V2BarreValider(false); e.Handled = true; break;
                case Key.Tab: V2BarreValider(true); e.Handled = true; break;
            }
            return true;
        }
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { V2OuvrirBarre(); e.Handled = true; return true; }
        // un champ caché de la régie complète (derrière) ne garde pas le clavier : Espace doit rester « Suivant »
        if (Keyboard.FocusedElement is TextBox or PasswordBox)
        {
            if (Keyboard.FocusedElement is DependencyObject f && v2 != null && v2.IsAncestorOf(f)) return true;
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return true;
        switch (e.Key)
        {
            case Key.Tab:
                e.Handled = true;
                break;
            case Key.Right or Key.Space or Key.PageDown:
                V2Suivant();
                e.Handled = true;
                break;
            case Key.Left or Key.PageUp:
                R.Bouger(-1);
                e.Handled = true;
                break;
            case Key.B:
                BasculerNoir();
                e.Handled = true;
                break;
            case Key.Escape:
                if (v2Page is "preparer" or "son" or "direct") V2Aller("regie");
                else if (v2Page == "regie") R.Effacer();
                e.Handled = true;
                break;
            case Key.Divide or Key.Oem2:
                V2OuvrirBarre();
                e.Handled = true;
                break;
        }
        return true;
    }

    /// <summary>La dernière carte du culte montrée (après « Effacer », Suivant reprend à la carte d'après).</summary>
    ElementAgenda? v2DerniereCarte;

    /// <summary>
    /// Rien du culte à l'écran (le début, ou après « Effacer » sans reprise) : la carte que Suivant montrera.
    /// Null : Suivant suit la régie (la diapositive suivante, la reprise après Effacer, le verset suivant).
    /// </summary>
    ElementAgenda? V2CarteDepart()
    {
        if (noir || R.ElementCourant != null || !(R.Etat.Vide || R.Cible is CibleDefaut) || R.Prochaine() != null) return null;
        var l = R.Agenda.Elements;
        int depuis = v2DerniereCarte != null ? Math.Max(0, l.IndexOf(v2DerniereCarte) + 1) : 0;
        for (int k = depuis; k < l.Count; k++)
            if (R.Diapos(l[k]).Count > 0) return l[k];
        return null;
    }

    /// <summary>Suivant : la diapositive suivante ; rien du culte à l'écran : la carte suivante (la première au début).</summary>
    void V2Suivant()
    {
        if (V2CarteDepart() is { } carte) { R.AfficherElement(carte, 0); return; }
        R.Bouger(1);
    }

    /// <summary>« Commencer le culte » : la projection s'allume (un projecteur est branché), la régie s'ouvre sur la première carte.</summary>
    void V2Commencer()
    {
        if (ecran == null && !AttenteProjecteur && ProjectionBtn.Visibility == Visibility.Visible && Natif.Ecrans().Any(x => !x.Principal)) V2AllumerProjection();
        V2Aller("regie");
        v2DerniereCarte = null;
        var l = R.Agenda.Elements;
        bool dejaCommence = R.ElementCourant is { } c && l.Contains(c);
        if (!dejaCommence && !noir && l.FirstOrDefault(x => R.Diapos(x).Count > 0) is { } premier) R.AfficherElement(premier, 0);
    }

    /// <summary>La projection plein écran sur le premier écran qui n'est pas celui de la régie.</summary>
    void V2AllumerProjection()
    {
        if (ecran != null) { Toast("La projection est déjà allumée."); return; }
        if (ProjectionBtn.Visibility != Visibility.Visible) { Toast("La projection se règle avec le compte de l'administrateur."); return; }
        var autre = Natif.Ecrans().FirstOrDefault(x => !x.Principal);
        if (autre != null) OuvrirSur(autre, true);
        else Toast("Aucun projecteur branché : branche le câble HDMI du projecteur, la carte passera au vert.");
    }

    static string V2Duree(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    static string V2Jour() => DateTime.Now.ToString("dddd", V2Fr);

    // ------------------------------------------------------------ briques communes
    static BitmapImage V2ImageLogo()
    {
        if (v2Logo != null) return v2Logo;
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = new Uri("pack://application:,,,/logo-icone.png");
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.EndInit();
        b.Freeze();
        return v2Logo = b;
    }

    /// <summary>Le logo et « Siloé Stream », avec une ligne dessous (« RÉGIE · DIMANCHE »).</summary>
    static FrameworkElement V2Logo(string? sous, double taille = 46)
    {
        var img = new Image { Source = V2ImageLogo(), Width = taille, Height = taille, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var noms = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        noms.Children.Add(V2.T("Siloé Stream", taille * 0.45, V2.Rouge, true));
        if (sous != null)
        {
            var s = V2.T(sous, 11.5, V2.Muet, false, FontWeights.Bold);
            s.Margin = new Thickness(1, 1, 0, 0);
            noms.Children.Add(s);
        }
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(img);
        p.Children.Add(noms);
        return p;
    }

    /// <summary>L'en-tête des pages 2.0 : « Retour à la régie », le titre et sa phrase, et ce qu'on veut à droite.</summary>
    (FrameworkElement Tete, TextBlock Titre) V2EntetePage(string titre, string phrase, FrameworkElement? droite = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var retour = V2.BoutonPilule("Retour à la régie", () => V2Aller("regie"), V2.IcGauche, V2.Carte, V2.Encre, V2.Trait, 14.5, 46);
        retour.VerticalAlignment = VerticalAlignment.Top;
        retour.Margin = new Thickness(0, 6, 22, 0);
        g.Children.Add(retour);
        var textes = new StackPanel();
        var t = V2.T(titre, 36, V2.Encre, true);
        textes.Children.Add(t);
        var ph = V2.T(phrase, 16.5, V2.Encre2, false, null, true);
        ph.Margin = new Thickness(0, 4, 0, 0);
        textes.Children.Add(ph);
        Grid.SetColumn(textes, 1);
        g.Children.Add(textes);
        if (droite != null)
        {
            droite.VerticalAlignment = VerticalAlignment.Top;
            droite.Margin = new Thickness(16, 6, 0, 0);
            Grid.SetColumn(droite, 2);
            g.Children.Add(droite);
        }
        return (g, t);
    }

    /// <summary>Le gros bouton rouge en relief (« Commencer le culte »).</summary>
    static Button V2GrosBouton(string texte, string icone, double taille, Action clic, double hauteur = 104)
    {
        var ic = V2.Icone(icone, V2.Blanc, taille, 2, true);
        ic.Margin = new Thickness(0, 0, 18, 0);
        var t = V2.T(texte, taille, V2.Blanc, true);
        t.VerticalAlignment = VerticalAlignment.Center;
        var contenu = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        contenu.Children.Add(ic);
        contenu.Children.Add(t);
        var b = new Brique(contenu, V2.Rouge, V2.RougeOmbre, 40, 12, new Thickness(44, 0, 44, 0)) { Height = hauteur };
        return V2.Bouton(b, clic);
    }

    /// <summary>Sorte de carte du culte : son nom, son icône et ses deux couleurs (pâle pour le fond, vive pour l'icône).</summary>
    (string Libelle, string Icone, SolidColorBrush Fond, SolidColorBrush Couleur) V2Genre(ElementAgenda e) => e.Type switch
    {
        "chant" => ("Chant", V2.IcMusique, V2.BleuPale, V2.Bleu),
        "bible" => ("Bible", V2.IcLivre, V2.VioletPale, V2.Violet),
        "compte" => ("Accueil", V2.IcMaison, V2.GrisPale, V2.Gris),
        "principal" => ("Écran principal", V2.IcDrapeau, V2.RougePale, V2.Rouge),
        "display" => e.Source?.Type switch
        {
            "camera" or "mycam" => ("Caméra", V2.IcCamera, V2.RosePale, V2.Rose),
            "video" => ("Vidéo", V2.IcVideo, V2.IndigoPale, V2.Indigo),
            "fenetre" => ("Fenêtre", V2.IcEcran, V2.GrisPale, V2.Gris),
            _ => ("Image", V2.IcImage, V2.TurquoisePale, V2.Turquoise),
        },
        _ => ("Texte", V2.IcTexte, V2.OrPale, V2.Ambre),
    };

    // ------------------------------------------------------------ Bonjour ! (les vérifications du dimanche)
    sealed class V2CarteVerif
    {
        public Brique Brique = null!;
        public FrameworkElement Racine = null!;
        public Border Rond = null!, Carre = null!;
        public Viewbox RondIcone = null!, CarreIcone = null!;
        public TextBlock Texte = null!;
        public Border[]? Barres;
        public string Etat = "";
    }

    V2CarteVerif? v2VerifProjecteur, v2VerifCamera, v2VerifSon, v2VerifInternet;
    TextBlock? v2BonjourTexte, v2TelTexte, v2PretTexte;
    Button? v2PretLien, v2QrBouton;

    FrameworkElement V2PageAccueil()
    {
        var pile = new StackPanel { Margin = new Thickness(56, 30, 56, 40), MaxWidth = 1240 };
        var tete = new DockPanel();
        var complet = V2.BoutonPilule("Mode complet", () => V2Aller("classique"), V2.IcGrille, V2.Carte, V2.Encre2, V2.Trait, 14, 42);
        complet.ToolTip = "Toute la régie : Bible, chants, médias, Duo, réglages";
        DockPanel.SetDock(complet, Dock.Right);
        tete.Children.Add(complet);
        tete.Children.Add(V2Logo(null, 44));
        pile.Children.Add(tete);

        var bonjour = V2.T("Bonjour !", 64, V2.Encre, true);
        bonjour.Margin = new Thickness(0, 28, 0, 6);
        pile.Children.Add(bonjour);
        v2BonjourTexte = V2.T("", 22, V2.Encre2, false, null, true);
        v2BonjourTexte.MaxWidth = 800;
        v2BonjourTexte.HorizontalAlignment = HorizontalAlignment.Left;
        v2BonjourTexte.LineHeight = 31;
        pile.Children.Add(v2BonjourTexte);

        var grille = new UniformGrid { Columns = 4, Margin = new Thickness(0, 30, -16, 0) };
        v2VerifProjecteur = V2Verif(V2.IcEcran, "Projecteur", V2AllumerProjection, "Allumer la projection sur le projecteur");
        v2VerifCamera = V2Verif(V2.IcCamera, "Caméra", null, null);
        v2VerifSon = V2Verif(V2.IcSon, "Son", () => V2Aller("son"), "Le son, en gros boutons", true);
        v2VerifInternet = V2Verif(V2.IcGlobe, "Internet", () => V2Aller("direct"), "Aller en direct sur YouTube et Zoom");
        foreach (var c in new[] { v2VerifProjecteur, v2VerifCamera, v2VerifSon, v2VerifInternet }) grille.Children.Add(c.Racine);
        pile.Children.Add(grille);

        // la télécommande sur le téléphone (facultative)
        var tel = new DockPanel();
        v2QrBouton = V2.BoutonPilule("Montrer le code QR", () => new TelephoneWindow { Owner = this }.ShowDialog(), null, V2.Carte, V2.Rouge, V2.Trait, 15, 48);
        DockPanel.SetDock(v2QrBouton, Dock.Right);
        tel.Children.Add(v2QrBouton);
        var icTel = V2.Icone(V2.IcTelephone, V2.Encre2, 24);
        icTel.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(icTel, Dock.Left);
        tel.Children.Add(icTel);
        v2TelTexte = V2.T("", 16, V2.Encre2, false, null, true);
        v2TelTexte.VerticalAlignment = VerticalAlignment.Center;
        tel.Children.Add(v2TelTexte);
        pile.Children.Add(new Border { Background = V2.Douce, CornerRadius = new CornerRadius(24), Padding = new Thickness(22, 12, 12, 12), Margin = new Thickness(0, 28, 0, 0), Child = tel });

        // le gros bouton
        var bas = new Grid { Margin = new Thickness(0, 34, 0, 0) };
        bas.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bas.ColumnDefinitions.Add(new ColumnDefinition());
        var commencer = V2GrosBouton("Commencer le culte", V2.IcLecture, 30, V2Commencer);
        commencer.MinWidth = 520;
        bas.Children.Add(commencer);
        var pret = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, 0, 0, 10) };
        v2PretTexte = V2.T("", 17, V2.Encre2, false, null, true);
        pret.Children.Add(v2PretTexte);
        v2PretLien = V2.Lien("Le regarder ou le changer d'abord", () => V2Aller("preparer"), 16);
        v2PretLien.HorizontalAlignment = HorizontalAlignment.Left;
        pret.Children.Add(v2PretLien);
        Grid.SetColumn(pret, 1);
        bas.Children.Add(pret);
        pile.Children.Add(bas);

        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = pile };
    }

    V2CarteVerif V2Verif(string icone, string titre, Action? clic, string? info, bool barres = false)
    {
        var c = new V2CarteVerif();
        var tete = new Grid();
        c.CarreIcone = V2.Icone(icone, V2.Vert, 26, 2.1);
        c.Carre = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(16), Background = V2.VertPale, Child = c.CarreIcone, HorizontalAlignment = HorizontalAlignment.Left };
        tete.Children.Add(c.Carre);
        c.RondIcone = V2.Icone(V2.IcCoche, V2.Blanc, 22, 3);
        c.Rond = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = V2.Vert, Child = c.RondIcone, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        tete.Children.Add(c.Rond);
        var corps = new StackPanel();
        corps.Children.Add(tete);
        var t = V2.T(titre, 23, V2.Encre, true);
        t.Margin = new Thickness(0, 18, 0, 6);
        corps.Children.Add(t);
        c.Texte = V2.T("", 16.5, V2.Encre2, false, null, true);
        c.Texte.LineHeight = 23;
        corps.Children.Add(c.Texte);
        if (barres)
        {
            var b = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            c.Barres = new Border[6];
            for (int i = 0; i < 6; i++)
            {
                c.Barres[i] = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 4, 0), Background = V2.Douce2 };
                b.Children.Add(c.Barres[i]);
            }
            corps.Children.Add(b);
        }
        c.Brique = new Brique(corps, V2.Carte, V2.VertRelief, 30, 6, new Thickness(24), V2.VertBord, 3) { MinHeight = 236 };
        c.Racine = clic != null ? V2.Bouton(c.Brique, clic, info) : c.Brique;
        c.Racine.Margin = new Thickness(0, 0, 16, 0);
        return c;
    }

    static void V2EtatVerif(V2CarteVerif c, string etat, string texte)
    {
        c.Texte.Text = texte;
        if (c.Etat == etat) return;
        c.Etat = etat;
        var (bord, relief, rond, iconeRond, dessin, pale, couleur) = etat switch
        {
            "ok" => (V2.VertBord, V2.VertRelief, V2.Vert, V2.Blanc, V2.IcCoche, V2.VertPale, V2.Vert),
            "attention" => (V2.OrBord, V2.OrPale, V2.Or, V2.Encre, V2.IcAttention, V2.OrPale, V2.OrTexte),
            "erreur" => (V2.RougeBord, V2.RougePale, V2.RougeVif, V2.Blanc, V2.IcAttention, V2.RougePale, V2.RougeVif),
            _ => (V2.Trait, V2.Douce2, V2.Douce, V2.Muet, V2.IcTiret, V2.Douce, V2.Muet),
        };
        c.Brique.Couleurs(V2.Carte, relief, bord);
        c.Rond.Background = rond;
        V2.Recolorer(c.RondIcone, iconeRond, dessin);
        c.Carre.Background = pale;
        V2.Recolorer(c.CarreIcone, couleur);
    }

    (string Etat, string Texte) V2EtatProjecteur()
    {
        if (ecran != null) return ("ok", $"Branché et allumé. L'écran de l'église est prêt ({NomEcranCourt()}).");
        if (AttenteProjecteur) return ("attention", "Débranché. Rebranche le câble : l'image revient toute seule.");
        return Natif.Ecrans().Any(x => !x.Principal)
            ? ("attention", "Branché, pas encore allumé. Touche cette carte pour allumer la projection.")
            : ("attention", "Aucun projecteur branché. Branche le câble HDMI du projecteur.");
    }

    (string Etat, string Texte) V2EtatCamera()
    {
        var cams = R.Annonces.Where(a => a.Source?.Type is "camera" or "mycam").ToList();
        if (cams.Count == 0) return ("neutre", "Pas de caméra pour l'instant : c'est facultatif.");
        var noms = string.Join(", ", cams.Take(2).Select(a => a.Titre));
        return ("ok", cams.Count == 1 ? $"{noms} : prête à montrer, dans les médias." : $"{cams.Count} caméras prêtes : {noms}.");
    }

    /// <summary>Le son : « ok » quand un micro parle bien, « attention » (trop fort, coupé), « neutre » (silence), « erreur » (moteur).</summary>
    (string Etat, string Texte, double Niveau) V2EtatSon()
    {
        var audio = App.Audio;
        if (!audio.Running) return ("erreur", audio.ObsAbsent ? "Le moteur du son manque : Réglages › Son › Réparer." : "Le moteur du son démarre…", -100);
        var sons = audio.Config.Sons.Where(s => s.Genre != MyVerse.Audio.GenreSon.Video).ToList();
        if (sons.Count == 0) return ("neutre", "Aucun micro ajouté. Le son des vidéos marche quand même.", -100);
        double max = -100;
        bool bon = false, fort = false, coupe = false;
        foreach (var s in sons)
        {
            var e = audio.Etat(s);
            if (s.Coupe || !e.Actif) { coupe |= s.Coupe; continue; }
            bon |= e.Verdict == MyVerse.Audio.VerdictNiveau.Bon;
            fort |= e.Verdict == MyVerse.Audio.VerdictNiveau.TropFort;
            var (l, r) = audio.Level(s.Id);
            double n = Math.Max(l, r);
            if (!double.IsNaN(n)) max = Math.Max(max, n);
        }
        if (fort) return ("attention", "Un son est trop fort : baisse-le dans Le son.", max);
        if (bon) return ("ok", "La table de mixage parle. Bon niveau.", max);
        if (coupe) return ("attention", "Un micro est coupé. Touche cette carte pour le remettre.", max);
        return ("neutre", "On n'entend rien pour l'instant. Parle dans le micro.", max);
    }

    (string Etat, string Texte) V2EtatInternet()
    {
        var d = App.Direct;
        if (d.EnDirect) return ("ok", "En direct sur YouTube depuis " + V2Duree(d.Duree) + ".");
        bool net;
        try { net = NetworkInterface.GetIsNetworkAvailable(); } catch (Exception) { net = true; }
        if (!net) return ("attention", "Pas d'Internet. Le culte marche quand même dans l'église.");
        var reg = App.Donnees.Reglages.Direct;
        if (reg.Mode == ReglagesDirect.Compte && !d.Compte.Connecte) return ("ok", "Connecté. YouTube n'est pas encore relié : touche cette carte.");
        return ("ok", "Connecté. YouTube et Zoom sont prêts.");
    }

    void V2MajAccueil()
    {
        if (v2VerifProjecteur == null || v2VerifCamera == null || v2VerifSon == null || v2VerifInternet == null) return;
        if (v2BonjourTexte != null)
            v2BonjourTexte.Text = $"C'est {V2Jour()}. Siloé Stream a tout vérifié pour toi : quand les cartes sont vertes, appuie sur le gros bouton.";
        if (v2Tic % 8 == 0 || v2VerifProjecteur.Etat.Length == 0)
        {
            var (ep, tp) = V2EtatProjecteur();
            V2EtatVerif(v2VerifProjecteur, ep, tp);
        }
        var (ec, tc) = V2EtatCamera();
        V2EtatVerif(v2VerifCamera, ec, tc);
        var (es, ts, _) = V2EtatSon();
        V2EtatVerif(v2VerifSon, es, ts);
        var (ei, ti) = V2EtatInternet();
        V2EtatVerif(v2VerifInternet, ei, ti);
        V2BarresSon();

        if (v2TelTexte != null)
        {
            int n = App.Telephone.Connectes().Count;
            v2TelTexte.Inlines.Clear();
            v2TelTexte.Inlines.Add(new Run("Télécommande sur le téléphone : "));
            if (n > 0) v2TelTexte.Inlines.Add(new Bold(new Run($"{n} téléphone{(n > 1 ? "s" : "")} relié{(n > 1 ? "s" : "")}")));
            else
            {
                v2TelTexte.Inlines.Add(new Bold(new Run("facultatif")));
                v2TelTexte.Inlines.Add(new Run(". Aucun téléphone pour l'instant."));
            }
        }
        if (v2QrBouton != null) v2QrBouton.Visibility = TelBtn.Visibility;
        if (v2PretTexte != null && v2PretLien != null)
        {
            int cartes = R.Agenda.Elements.Count;
            v2PretTexte.Inlines.Clear();
            if (cartes == 0)
            {
                v2PretTexte.Inlines.Add(new Run("Le culte n'a pas encore de cartes."));
                ((TextBlock)((Border)v2PretLien.Content).Child).Text = "Le préparer maintenant";
            }
            else
            {
                v2PretTexte.Inlines.Add(new Run("Le culte de ce matin est prêt : "));
                v2PretTexte.Inlines.Add(new Bold(new Run($"{cartes} carte{(cartes > 1 ? "s" : "")}")));
                v2PretTexte.Inlines.Add(new Run("."));
                ((TextBlock)((Border)v2PretLien.Content).Child).Text = "Le regarder ou le changer d'abord";
            }
        }
    }

    /// <summary>Les petites barres de la carte Son : le niveau du micro le plus fort.</summary>
    void V2BarresSon()
    {
        if (v2VerifSon?.Barres is not { } barres) return;
        var (etat, _, niveau) = V2EtatSon();
        int n = etat is "erreur" ? 0 : (int)Math.Round(Math.Clamp((niveau + 60) / 60, 0, 1) * barres.Length);
        for (int i = 0; i < barres.Length; i++) barres[i].Background = i < n ? (etat == "attention" ? V2.Or : V2.Vert) : V2.Douce2;
    }

    // ------------------------------------------------------------ la Régie (le Grand Bouton)
    sealed class V2Puce
    {
        public Border Fond = null!;
        public TextBlock Texte = null!;
        public Viewbox Icone = null!;
        public string Etat = "";
    }

    sealed class V2CarteCulte
    {
        public ElementAgenda Element = null!;
        public Button Bouton = null!;
        public Border Carte = null!, Badge = null!;
        public TextBlock BadgeTexte = null!;
    }

    Rendu? v2RenduEglise, v2RenduStream;
    Border? v2Apercu, v2Encart, v2BadgeEcran;
    TextBlock? v2Position, v2Puis, v2EncartTexte, v2NoirTexte;
    Brique? v2NoirBrique;
    Viewbox? v2NoirIcone;
    StackPanel? v2Cartes;
    ScrollViewer? v2CartesDefil;
    V2Puce? v2PuceEglise, v2PuceInternet, v2PuceSon;
    Button? v2MajBouton;
    readonly List<V2CarteCulte> v2CartesCulte = new();
    ElementAgenda? v2CarteMontree;

    FrameworkElement V2PageRegie()
    {
        var racine = new Grid { Margin = new Thickness(24, 16, 24, 16) };
        racine.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        racine.RowDefinitions.Add(new RowDefinition());
        racine.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---------- en haut : le logo, la barre magique, l'état
        var tete = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        tete.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tete.ColumnDefinitions.Add(new ColumnDefinition());
        tete.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tete.Children.Add(V2Logo("RÉGIE · " + V2Jour().ToUpper(V2Fr), 46));

        var loupe = V2.Icone(V2.IcLoupe, V2.Rouge, 22, 2.6);
        loupe.Margin = new Thickness(0, 0, 14, 0);
        var mots = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mots.Children.Add(V2.T("Que veux-tu montrer ?", 18, V2.Encre, false, FontWeights.Bold));
        mots.Children.Add(V2.T("Écris « jean 3 16 », « wokovu 45 », « caméra »…", 13, V2.Muet));
        var cle = new Border { BorderBrush = V2.Trait, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, Child = V2.T("Ctrl K", 12, V2.Muet, false, FontWeights.Bold) };
        var rech = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(cle, Dock.Right);
        rech.Children.Add(cle);
        DockPanel.SetDock(loupe, Dock.Left);
        rech.Children.Add(loupe);
        rech.Children.Add(mots);
        var rechBrique = new Brique(rech, V2.Carte, V2.Douce2, 999, 4, new Thickness(22, 0, 14, 0), V2.Trait, 1.5) { Height = 64 };
        var rechBouton = V2.Bouton(rechBrique, () => V2OuvrirBarre(), "Chercher un verset, un chant, une caméra (Ctrl K)");
        rechBouton.MaxWidth = 560;
        rechBouton.Margin = new Thickness(22, 0, 14, 0);
        rechBouton.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(rechBouton, 1);
        tete.Children.Add(rechBouton);

        var puces = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        v2MajBouton = V2.BoutonPilule("Mise à jour", () => { V2Aller("classique"); MontrerOnglet("reglages"); Reglages.Aller("maj", false); }, null, V2.OrPale, V2.OrTexte, V2.OrBord, 14, 50);
        v2MajBouton.Visibility = Visibility.Collapsed;
        puces.Children.Add(v2MajBouton);
        Button pe, pi, ps;
        (v2PuceEglise, pe) = V2FairePuce(V2.IcEcran, V2AllumerProjection, "L'écran de l'église (le projecteur)");
        (v2PuceInternet, pi) = V2FairePuce(V2.IcGlobe, () => V2Aller("direct"), "Le direct sur YouTube et Zoom");
        (v2PuceSon, ps) = V2FairePuce(V2.IcSon, () => V2Aller("son"), "Le son, en gros boutons");
        puces.Children.Add(pe);
        puces.Children.Add(pi);
        puces.Children.Add(ps);
        var complet = V2.Bouton(new Border
        {
            Width = 50, Height = 50, CornerRadius = new CornerRadius(25), Background = V2.Carte, BorderBrush = V2.Trait, BorderThickness = new Thickness(2),
            Child = V2.Icone(V2.IcGrille, V2.Encre2, 20, 2.2), Margin = new Thickness(10, 0, 0, 0),
        }, () => V2Aller("classique"), "Mode complet : toute la régie (Bible, chants, médias, Duo, réglages)");
        puces.Children.Add(complet);
        Grid.SetColumn(puces, 2);
        tete.Children.Add(puces);
        racine.Children.Add(tete);

        // ---------- au centre : l'écran de l'église en grand, et le gros Suivant
        var corps = new Grid();
        corps.ColumnDefinitions.Add(new ColumnDefinition());
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
        Grid.SetRow(corps, 1);

        var gauche = new DockPanel();
        var haut = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        v2Position = V2.T("", 15, V2.Encre2, false, FontWeights.Bold);
        v2Position.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(v2Position, Dock.Right);
        haut.Children.Add(v2Position);
        v2BadgeEcran = V2.Badge("● À l'écran", V2.RougeVif, V2.Blanc, 12.5);
        var ou = V2.T("Dans l'église", 16, V2.Encre, false, FontWeights.Bold);
        ou.VerticalAlignment = VerticalAlignment.Center;
        ou.Margin = new Thickness(12, 0, 0, 0);
        var ouSous = V2.T(" · ce que tout le monde voit en ce moment", 15, V2.Muet);
        ouSous.VerticalAlignment = VerticalAlignment.Center;
        var ligneHaut = new StackPanel { Orientation = Orientation.Horizontal };
        ligneHaut.Children.Add(v2BadgeEcran);
        ligneHaut.Children.Add(ou);
        ligneHaut.Children.Add(ouSous);
        haut.Children.Add(ligneHaut);
        DockPanel.SetDock(haut, Dock.Top);
        gauche.Children.Add(haut);

        v2RenduEglise = new Rendu { Width = 1920, Height = 1080, Transitions = false, Leger = true, IsHitTestVisible = false };
        var cadre = new Grid { ClipToBounds = true };
        cadre.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = v2RenduEglise });

        // l'encart « Sur Internet » : ce que voient YouTube et Zoom
        v2RenduStream = new Rendu { Width = 1920, Height = 1080, Transitions = false, Leger = true, IsHitTestVisible = false };
        var encart = new Grid();
        encart.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = v2RenduStream });
        var badgeNet = V2.Badge("Sur Internet", V2.Indigo, V2.Blanc, 10, new Thickness(8, 3, 8, 3));
        badgeNet.HorizontalAlignment = HorizontalAlignment.Left;
        badgeNet.VerticalAlignment = VerticalAlignment.Top;
        badgeNet.Margin = new Thickness(8);
        encart.Children.Add(badgeNet);
        v2EncartTexte = V2.T("", 11.5, V2.Blanc, false, FontWeights.Bold);
        encart.Children.Add(new Border
        {
            Background = V2.Rouge, CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 4, 9, 5), Margin = new Thickness(7),
            VerticalAlignment = VerticalAlignment.Bottom, Child = v2EncartTexte,
        });
        v2Encart = new Border
        {
            Background = V2.Scene2, BorderBrush = V2.Indigo, BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(18), Child = encart,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 16, 16),
            Width = 300, Height = 177, Cursor = Cursors.Hand, ToolTip = "Ce que voient YouTube et Zoom · un clic : Aller en direct",
        };
        v2Encart.MouseLeftButtonUp += (_, _) => V2Aller("direct");
        encart.SizeChanged += (_, e) => encart.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 14, 14);
        cadre.Children.Add(v2Encart);
        cadre.SizeChanged += (_, e) =>
        {
            cadre.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 23, 23);
            double w = Math.Clamp(e.NewSize.Width * 0.3, 170, 380);
            if (v2Encart != null) { v2Encart.Width = w; v2Encart.Height = w * 9 / 16 + 8; }
        };
        v2Apercu = new Border { CornerRadius = new CornerRadius(30), BorderThickness = new Thickness(7), BorderBrush = V2.RougeVif, Background = V2.Scene, Child = cadre };
        gauche.Children.Add(v2Apercu);
        corps.Children.Add(gauche);

        var droite = new Grid();
        droite.RowDefinitions.Add(new RowDefinition());
        droite.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        droite.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var suivPile = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var icSuiv = V2.Icone(V2.IcSuivant, V2.Blanc, 64, 2.4, true);
        icSuiv.HorizontalAlignment = HorizontalAlignment.Center;
        suivPile.Children.Add(icSuiv);
        var suivTitre = V2.T("SUIVANT", 42, V2.Blanc, true);
        suivTitre.HorizontalAlignment = HorizontalAlignment.Center;
        suivTitre.Margin = new Thickness(0, 14, 0, 0);
        suivPile.Children.Add(suivTitre);
        v2Puis = V2.T("", 18, V2.Blanc);
        v2Puis.HorizontalAlignment = HorizontalAlignment.Center;
        v2Puis.MaxWidth = 300;
        v2Puis.Margin = new Thickness(0, 8, 0, 0);
        suivPile.Children.Add(v2Puis);
        var touche = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        touche.Children.Add(new Border { BorderBrush = V2.Blanc, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 3), Child = V2.T("Espace", 13, V2.Blanc, false, FontWeights.Bold) });
        var ouTel = V2.T("ou le téléphone", 14, V2.Blanc80);
        ouTel.VerticalAlignment = VerticalAlignment.Center;
        ouTel.Margin = new Thickness(8, 0, 0, 0);
        touche.Children.Add(ouTel);
        suivPile.Children.Add(touche);
        var suiv = V2.Bouton(new Brique(suivPile, V2.Rouge, V2.RougeOmbre, 40, 12, new Thickness(24)), V2Suivant, "Suivant (Espace, flèche droite ou le téléphone)");
        droite.Children.Add(suiv);

        var boutons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 16, 0, 0) };
        var icRev = V2.Icone(V2.IcRevenir, V2.Encre, 18, 2.2, true);
        icRev.Margin = new Thickness(0, 0, 10, 0);
        var tRev = V2.T("Revenir", 17, V2.Encre, true);
        tRev.VerticalAlignment = VerticalAlignment.Center;
        var rev = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        rev.Children.Add(icRev);
        rev.Children.Add(tRev);
        var revenir = V2.Bouton(new Brique(rev, V2.Carte, V2.Trait, 26, 6, new Thickness(8, 0, 8, 0), V2.Trait, 2) { Height = 86 }, () => R.Bouger(-1), "Revenir à la diapositive d'avant (flèche gauche)");
        revenir.Margin = new Thickness(0, 0, 8, 0);
        boutons.Children.Add(revenir);
        v2NoirIcone = V2.Icone(V2.IcLune, V2.Blanc, 18, 2.2, true);
        v2NoirIcone.Margin = new Thickness(0, 0, 10, 0);
        v2NoirTexte = V2.T("Écran noir", 17, V2.Blanc, true);
        v2NoirTexte.VerticalAlignment = VerticalAlignment.Center;
        var nr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        nr.Children.Add(v2NoirIcone);
        nr.Children.Add(v2NoirTexte);
        v2NoirBrique = new Brique(nr, V2.Encre, V2.Noir, 26, 6, new Thickness(8, 0, 8, 0)) { Height = 86 };
        var noirBouton = V2.Bouton(v2NoirBrique, BasculerNoir, "Écran noir (touche B) : le texte est caché sans être perdu");
        noirBouton.Margin = new Thickness(8, 0, 0, 0);
        boutons.Children.Add(noirBouton);
        Grid.SetRow(boutons, 1);
        droite.Children.Add(boutons);

        var aide = new TextBlock { FontFamily = V2.Sans, FontSize = 14, Foreground = V2.Muet, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 12, 8, 0) };
        aide.Inlines.Add(new Run("Un doute ? Appuie sur "));
        aide.Inlines.Add(new Bold(new Run("Écran noir")) { Foreground = V2.Encre });
        aide.Inlines.Add(new Run(" : rien n'est perdu, tu rallumes quand tu veux."));
        Grid.SetRow(aide, 2);
        droite.Children.Add(aide);
        Grid.SetColumn(droite, 2);
        corps.Children.Add(droite);
        racine.Children.Add(corps);

        // ---------- en bas : le culte en cartes
        var culte = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var teteCulte = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var lienPreparer = V2.Lien("Préparer le culte", () => V2Aller("preparer"), 15);
        DockPanel.SetDock(lienPreparer, Dock.Right);
        teteCulte.Children.Add(lienPreparer);
        var titreCulte = V2.T("Le culte", 20, V2.Encre, true);
        titreCulte.VerticalAlignment = VerticalAlignment.Center;
        var sousCulte = V2.T("Touche une carte pour la montrer tout de suite", 14.5, V2.Muet);
        sousCulte.VerticalAlignment = VerticalAlignment.Center;
        sousCulte.Margin = new Thickness(12, 2, 0, 0);
        var lt = new StackPanel { Orientation = Orientation.Horizontal };
        lt.Children.Add(titreCulte);
        lt.Children.Add(sousCulte);
        teteCulte.Children.Add(lt);
        DockPanel.SetDock(teteCulte, Dock.Top);
        culte.Children.Add(teteCulte);
        v2Cartes = new StackPanel { Orientation = Orientation.Horizontal };
        v2CartesDefil = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = v2Cartes,
            PanningMode = PanningMode.HorizontalOnly, Padding = new Thickness(0, 0, 0, 6),
        };
        v2CartesDefil.PreviewMouseWheel += (_, e) =>
        {
            if (v2CartesDefil.ScrollableWidth <= 0) return;
            v2CartesDefil.ScrollToHorizontalOffset(v2CartesDefil.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        culte.Children.Add(v2CartesDefil);
        Grid.SetRow(culte, 2);
        racine.Children.Add(culte);
        v2CulteAFaire = true;
        return racine;
    }

    (V2Puce Puce, Button Bouton) V2FairePuce(string icone, Action clic, string info)
    {
        var p = new V2Puce { Icone = V2.Icone(icone, V2.VertTexte, 19, 2.2) };
        p.Icone.Margin = new Thickness(0, 0, 9, 0);
        p.Texte = V2.T("", 15, V2.VertTexte, false, FontWeights.Bold);
        p.Texte.VerticalAlignment = VerticalAlignment.Center;
        var l = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        l.Children.Add(p.Icone);
        l.Children.Add(p.Texte);
        p.Fond = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(16, 0, 18, 0), Height = 50, Background = V2.VertPale, Child = l, Margin = new Thickness(10, 0, 0, 0) };
        return (p, V2.Bouton(p.Fond, clic, info));
    }

    static void V2EtatPuce(V2Puce? p, string etat, string icone, string texte, bool plein = false)
    {
        if (p == null) return;
        p.Texte.Text = texte;
        var (fond, couleur) = etat switch
        {
            "ok" => (V2.VertPale, V2.VertTexte),
            "direct" => (V2.RougeVif, V2.Blanc),
            "attention" => (V2.OrPale, V2.OrTexte),
            _ => (V2.Douce, V2.Encre2),
        };
        p.Fond.Background = fond;
        p.Texte.Foreground = couleur;
        V2.Recolorer(p.Icone, couleur, icone, plein);
        p.Etat = etat;
    }

    /// <summary>L'état en haut de la régie : l'église, Internet (le direct), le son ; et la mise à jour pour l'admin.</summary>
    void V2MajEtats()
    {
        if (ecran != null) V2EtatPuce(v2PuceEglise, "ok", V2.IcEcran, "Église : écran OK");
        else if (AttenteProjecteur) V2EtatPuce(v2PuceEglise, "attention", V2.IcEcran, "Église : débranché");
        else V2EtatPuce(v2PuceEglise, "neutre", V2.IcEcran, "Église : aperçu");
        var d = App.Direct;
        if (d.EnDirect) V2EtatPuce(v2PuceInternet, "direct", V2.IcEnregistrer, "Internet : en direct · " + V2Duree(d.Duree), true);
        else if (d.Etat == Audio.EtatDirect.Preparation) V2EtatPuce(v2PuceInternet, "attention", V2.IcGlobe, "Internet : démarrage…");
        else if (d.Etat == Audio.EtatDirect.Erreur) V2EtatPuce(v2PuceInternet, "attention", V2.IcGlobe, "Internet : à vérifier");
        else V2EtatPuce(v2PuceInternet, "neutre", V2.IcGlobe, "Internet : pas en direct");
        var (es, _, _) = V2EtatSon();
        V2EtatPuce(v2PuceSon, es == "ok" ? "ok" : es == "neutre" ? "neutre" : "attention", V2.IcSon,
            es switch { "ok" => "Son : bon niveau", "erreur" => "Son : moteur arrêté", "attention" => "Son : à vérifier", _ => "Son : silence" });
        if (v2MajBouton != null)
        {
            v2MajBouton.Visibility = MajBtn.Visibility;
            if (MajBtn.Content is string m && v2MajBouton.Content is Border { Child: StackPanel sp } && sp.Children.OfType<TextBlock>().FirstOrDefault() is { } tm) tm.Text = m;
        }
    }

    /// <summary>« 3 sur 7 » : la carte à l'écran dans le culte.</summary>
    string V2Position()
    {
        var l = R.Agenda.Elements;
        if (l.Count == 0) return "";
        int i = R.ElementCourant is { } c ? l.IndexOf(c) : -1;
        return i >= 0 ? $"{i + 1} sur {l.Count}" : $"{l.Count} carte{(l.Count > 1 ? "s" : "")}";
    }

    /// <summary>« Puis : Prédication » : ce que Suivant va montrer.</summary>
    string V2Puis()
    {
        if (V2CarteDepart() is { } p) return "Puis : " + TitreElement(p);
        var s = R.Prochaine();
        if (s != null) return "Puis : " + s.Label;
        return R.Agenda.Elements.Count == 0 ? "Puis : rien de prévu" : "Puis : c'est la fin du culte";
    }

    /// <summary>La carte qui viendra avec Suivant (quand la carte à l'écran n'a plus de diapositive).</summary>
    ElementAgenda? V2CarteSuivante()
    {
        var l = R.Agenda.Elements;
        var cur = R.ElementCourant;
        if (V2CarteDepart() is { } depart) return depart;
        if (cur == null || !l.Contains(cur)) return null;
        if (R.PositionAgenda(cur) is int p && p + 1 < R.Diapos(cur).Count) return null;
        for (int k = l.IndexOf(cur) + 1; k < l.Count; k++)
            if (R.Diapos(l[k]).Count > 0) return l[k];
        return null;
    }

    /// <summary>Un court résumé de ce que voient YouTube et Zoom (l'encart « Sur Internet »).</summary>
    static string V2Resume(EtatEcran e)
    {
        if (e.Vide) return e.Duo != null ? e.Duo.Libelle : "Rien pour l'instant";
        if (e.Mode == "media") return e.Ref.Length > 0 ? e.Ref : "Image en direct";
        var ligne = e.Lignes.FirstOrDefault(x => x.Trim().Length > 0)?.Trim() ?? "";
        return e.Ref.Length > 0 ? (ligne.Length > 0 ? e.Ref + " — " + ligne : e.Ref) : ligne;
    }

    void V2MajRegie()
    {
        if (v2Apercu == null || v2BadgeEcran == null) return;
        if (v2CulteAFaire) V2FaireCartes();
        if (R.ElementCourant is { } courant && R.Agenda.Elements.Contains(courant)) v2DerniereCarte = courant;
        var e = R.Etat;
        bool rien = e.Vide && e.Duo == null;
        v2Apercu.BorderBrush = noir ? V2.Encre : rien ? V2.Trait : V2.RougeVif;
        v2BadgeEcran.Background = noir ? V2.Encre : rien ? V2.Muet : V2.RougeVif;
        if (v2BadgeEcran.Child is TextBlock tb) tb.Text = noir ? "ÉCRAN NOIR" : rien ? "RIEN À L'ÉCRAN" : "● À L'ÉCRAN";
        if (v2Position != null) v2Position.Text = V2Position();
        if (v2Puis != null) v2Puis.Text = V2Puis();
        if (v2NoirBrique != null && v2NoirTexte != null && v2NoirIcone != null)
        {
            if (noir)
            {
                v2NoirBrique.Couleurs(V2.Carte, V2.Trait, V2.Encre);
                v2NoirBrique.Face.BorderThickness = new Thickness(2);
                v2NoirTexte.Text = "Rallumer";
                v2NoirTexte.Foreground = V2.Encre;
                V2.Recolorer(v2NoirIcone, V2.Encre, V2.IcSoleil, false);
            }
            else
            {
                v2NoirBrique.Couleurs(V2.Encre, V2.Noir, V2.Encre);
                v2NoirBrique.Face.BorderThickness = new Thickness(0);
                v2NoirTexte.Text = "Écran noir";
                v2NoirTexte.Foreground = V2.Blanc;
                V2.Recolorer(v2NoirIcone, V2.Blanc, V2.IcLune, true);
            }
        }
        if (v2EncartTexte != null) v2EncartTexte.Text = V2Resume(etatStream);
        V2MajEtats();
        V2MajCartes();
    }

    void V2FaireCartes()
    {
        if (v2Cartes == null) return;
        v2CulteAFaire = false;
        v2Cartes.Children.Clear();
        v2CartesCulte.Clear();
        v2CarteMontree = null;
        var l = R.Agenda.Elements;
        if (l.Count == 0)
        {
            var vide = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            vide.Children.Add(V2.T("Le culte n'a pas encore de cartes.", 17, V2.Encre, true));
            var lien = V2.Lien("Préparer le culte maintenant", () => V2Aller("preparer"), 15);
            lien.HorizontalAlignment = HorizontalAlignment.Left;
            vide.Children.Add(lien);
            v2Cartes.Children.Add(new Border { Background = V2.Douce, CornerRadius = new CornerRadius(26), Padding = new Thickness(22, 16, 22, 16), Height = 124, Child = vide });
            return;
        }
        foreach (var el in l)
        {
            var (lib, ic, fond, couleur) = V2Genre(el);
            var c = new V2CarteCulte { Element = el };
            c.BadgeTexte = V2.T("", 10.5, V2.Blanc, true);
            c.Badge = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 3, 8, 3), Child = c.BadgeTexte, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var tete = new Grid();
            var carre = V2.CarreIcone(ic, couleur, V2.Blanc, 40, 12, 20);
            carre.HorizontalAlignment = HorizontalAlignment.Left;
            tete.Children.Add(carre);
            tete.Children.Add(c.Badge);
            var pile = new StackPanel();
            pile.Children.Add(tete);
            var titre = V2.T(lib, 16.5, V2.Encre, true);
            titre.Margin = new Thickness(0, 10, 0, 1);
            pile.Children.Add(titre);
            pile.Children.Add(V2.T(TitreElement(el), 14, V2.Encre2));
            c.Carte = new Border
            {
                Width = 210, Height = 124, CornerRadius = new CornerRadius(26), Padding = new Thickness(14, 11, 14, 11), Background = fond,
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(4), Child = pile,
            };
            var e2 = el;
            c.Bouton = V2.Bouton(c.Carte, () => R.AfficherElement(e2, 0), "Montrer « " + TitreElement(el) + " » tout de suite");
            c.Bouton.Margin = new Thickness(0, 0, 12, 0);
            v2Cartes.Children.Add(c.Bouton);
            v2CartesCulte.Add(c);
        }
    }

    void V2MajCartes()
    {
        var cur = R.ElementCourant;
        bool rien = R.Etat.Vide && R.Etat.Duo == null;
        var suivante = V2CarteSuivante();
        foreach (var c in v2CartesCulte)
        {
            bool ici = c.Element == cur && !rien;
            if (ici)
            {
                c.Carte.BorderBrush = V2.RougeVif;
                c.Badge.Background = V2.RougeVif;
                c.BadgeTexte.Text = "À L'ÉCRAN";
                c.BadgeTexte.Foreground = V2.Blanc;
                c.Badge.Visibility = Visibility.Visible;
            }
            else if (c.Element == suivante)
            {
                c.Carte.BorderBrush = V2.Or;
                c.Badge.Background = V2.Or;
                c.BadgeTexte.Text = "SUIVANT";
                c.BadgeTexte.Foreground = V2.OrTexte;
                c.Badge.Visibility = Visibility.Visible;
            }
            else
            {
                c.Carte.BorderBrush = Brushes.Transparent;
                c.Badge.Visibility = Visibility.Collapsed;
            }
            c.Carte.Opacity = !ici && R.EstFait(c.Element) ? 0.62 : 1;
            if (ici && v2CarteMontree != c.Element)
            {
                v2CarteMontree = c.Element;
                var b = c.Bouton;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => b.BringIntoView()));
            }
        }
    }

    // ------------------------------------------------------------ la barre magique (Ctrl K)
    sealed record V2Res(string Genre, string Titre, string Badge, string Texte, Ref? Ref = null, string? ChantId = null, Annonce? Annonce = null, string? Action = null);

    Grid? v2Barre;
    Border? v2BarreCarte;
    TextBox? v2BarreChamp;
    TextBlock? v2BarreIndice, v2BarreTitre, v2BarreAide;
    StackPanel? v2BarreResultats;
    readonly List<V2Res> v2Res = new();
    int v2ResIndex, v2BarreGeneration;
    bool v2BarreAjout;
    readonly DispatcherTimer v2BarreMinuterie = new() { Interval = TimeSpan.FromMilliseconds(140) };

    bool v2BarreOuverte => v2Barre is { Visibility: Visibility.Visible };

    void V2FaireBarre()
    {
        if (v2 == null || v2Barre != null) return;
        v2Barre = new Grid { Background = V2.Voile, Visibility = Visibility.Collapsed };
        v2Barre.MouseLeftButtonDown += (_, e) => { if (ReferenceEquals(e.OriginalSource, v2Barre)) V2FermerBarre(); };
        v2BarreCarte = new Border
        {
            Background = V2.Carte, CornerRadius = new CornerRadius(30), Padding = new Thickness(28, 22, 28, 20), Width = 880,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24, 36, 24, 36),
            Effect = new DropShadowEffect { BlurRadius = 46, ShadowDepth = 12, Opacity = 0.2, Color = Color.FromRgb(60, 20, 20) },
        };
        var pile = new DockPanel();

        var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var fermer = V2.BoutonPilule("Fermer", V2FermerBarre, V2.IcCroix, V2.Douce, V2.Encre, V2.Douce, 14, 40);
        DockPanel.SetDock(fermer, Dock.Right);
        tete.Children.Add(fermer);
        var logo = new Image { Source = V2ImageLogo(), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        v2BarreTitre = V2.T("Que veux-tu montrer ?", 21, V2.Encre, true);
        v2BarreTitre.VerticalAlignment = VerticalAlignment.Center;
        v2BarreTitre.Margin = new Thickness(12, 0, 0, 0);
        var lt = new StackPanel { Orientation = Orientation.Horizontal };
        lt.Children.Add(logo);
        lt.Children.Add(v2BarreTitre);
        tete.Children.Add(lt);
        DockPanel.SetDock(tete, Dock.Top);
        pile.Children.Add(tete);

        v2BarreChamp = new TextBox
        {
            FontFamily = V2.Titre, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = V2.Encre, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), CaretBrush = V2.Rouge, SelectionBrush = V2.Or, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0), FocusVisualStyle = null,
        };
        v2BarreIndice = V2.T("jean 3 16, wokovu 45, caméra…", 22, V2.Muet);
        v2BarreIndice.IsHitTestVisible = false;
        v2BarreIndice.VerticalAlignment = VerticalAlignment.Center;
        v2BarreIndice.Margin = new Thickness(2, 0, 0, 0);
        var champ = new Grid();
        champ.Children.Add(v2BarreIndice);
        champ.Children.Add(v2BarreChamp);
        var loupe = V2.Icone(V2.IcLoupe, V2.Rouge, 26, 2.6);
        loupe.Margin = new Thickness(0, 0, 14, 0);
        var ligneChamp = new DockPanel();
        DockPanel.SetDock(loupe, Dock.Left);
        ligneChamp.Children.Add(loupe);
        ligneChamp.Children.Add(champ);
        var boite = new Border { CornerRadius = new CornerRadius(999), BorderBrush = V2.Rouge, BorderThickness = new Thickness(3), Background = V2.Carte, Height = 68, Padding = new Thickness(24, 0, 24, 0), Child = ligneChamp };
        boite.MouseLeftButtonDown += (_, _) => v2BarreChamp.Focus();
        DockPanel.SetDock(boite, Dock.Top);
        pile.Children.Add(boite);

        var essais = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) };
        var essaie = V2.T("Essaie :", 14.5, V2.Encre2, false, FontWeights.Bold);
        essaie.VerticalAlignment = VerticalAlignment.Center;
        essaie.Margin = new Thickness(2, 0, 10, 6);
        essais.Children.Add(essaie);
        foreach (var x in new[] { "jean 3 16", "psaume 23", "wokovu 45", "caméra", "noir" })
        {
            var mot = x;
            var b = V2.BoutonPilule(mot, () =>
            {
                if (v2BarreChamp == null) return;
                v2BarreChamp.Text = mot;
                v2BarreChamp.CaretIndex = mot.Length;
                v2BarreChamp.Focus();
            }, null, V2.Carte, V2.Encre, V2.Trait, 13.5, 34);
            b.Margin = new Thickness(0, 0, 8, 6);
            essais.Children.Add(b);
        }
        DockPanel.SetDock(essais, Dock.Top);
        pile.Children.Add(essais);

        v2BarreAide = new TextBlock { FontFamily = V2.Sans, FontSize = 13.5, Foreground = V2.Muet, Margin = new Thickness(2, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(v2BarreAide, Dock.Bottom);
        pile.Children.Add(v2BarreAide);

        v2BarreResultats = new StackPanel();
        pile.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = v2BarreResultats, Margin = new Thickness(0, 6, 0, 0) });
        v2BarreCarte.Child = pile;
        v2Barre.Children.Add(v2BarreCarte);
        v2Barre.SizeChanged += (_, e) => { if (v2BarreCarte != null) v2BarreCarte.MaxHeight = Math.Max(320, e.NewSize.Height - 72); };
        v2.Children.Add(v2Barre);

        v2BarreChamp.TextChanged += (_, _) =>
        {
            if (v2BarreIndice != null) v2BarreIndice.Visibility = v2BarreChamp.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            v2BarreMinuterie.Stop();
            v2BarreMinuterie.Start();
        };
        v2BarreMinuterie.Tick += (_, _) => { v2BarreMinuterie.Stop(); V2BarreChercher(); };
    }

    /// <summary>Ouvre la barre magique. <paramref name="ajout"/> : depuis « Préparer le culte », la carte trouvée va à la fin du culte.</summary>
    void V2OuvrirBarre(bool ajout = false, string texte = "", string? indice = null)
    {
        V2FaireBarre();
        if (v2Barre == null || v2BarreChamp == null || v2BarreTitre == null || v2BarreIndice == null || v2BarreAide == null) return;
        v2BarreAjout = ajout;
        v2BarreTitre.Text = ajout ? "Ajouter une carte au culte" : "Que veux-tu montrer ?";
        v2BarreIndice.Text = indice ?? "jean 3 16, wokovu 45, caméra…";
        v2BarreAide.Inlines.Clear();
        void Touche(string k, string quoi)
        {
            v2BarreAide.Inlines.Add(new Bold(new Run(k)) { Foreground = V2.Encre });
            v2BarreAide.Inlines.Add(new Run(" = " + quoi + "      "));
        }
        if (ajout) { Touche("Entrée", "ajouter à la fin du culte"); Touche("Échap", "fermer"); }
        else { Touche("Entrée", "montrer tout de suite"); Touche("Tab", "mettre après"); Touche("Échap", "fermer"); }
        v2Barre.Visibility = Visibility.Visible;
        v2BarreChamp.Text = texte;
        v2BarreChamp.CaretIndex = texte.Length;
        v2BarreIndice.Visibility = texte.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        v2BarreMinuterie.Stop();
        V2BarreChercher();
        var champ = v2BarreChamp;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { champ.Focus(); Keyboard.Focus(champ); }));
    }

    void V2FermerBarre()
    {
        if (v2Barre == null || v2Barre.Visibility != Visibility.Visible) return;
        v2Barre.Visibility = Visibility.Collapsed;
        v2BarreMinuterie.Stop();
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    async void V2BarreChercher()
    {
        if (v2BarreChamp == null) return;
        var q = v2BarreChamp.Text;
        int g = ++v2BarreGeneration;
        var rec = App.Donnees.Reglages.Recueil;
        string? recueil = rec.Length > 0 ? rec : null;
        List<ResultatCommande> rapide;
        try { rapide = FiltrerRole(R.Commande(q, recueil, false)); }
        catch (Exception x) { Journal.Erreur("barre magique", x); rapide = new(); }
        V2BarreRemplir(q, rapide);
        if (Recherche.Mots(q).Count < 2) return;
        List<ResultatCommande> complet;
        try { complet = FiltrerRole(await Task.Run(() => R.Commande(q, recueil, true))); }
        catch (Exception x) { Journal.Erreur("barre magique (paroles)", x); return; }
        if (g != v2BarreGeneration || v2BarreChamp.Text != q || complet.Count <= rapide.Count) return;
        V2BarreRemplir(q, complet);
    }

    void V2BarreRemplir(string q, List<ResultatCommande> res)
    {
        var choisi = v2ResIndex < v2Res.Count ? v2Res[v2ResIndex] : null;
        v2Res.Clear();
        var medias = V2Medias(q).ToList();
        bool cameraDabord = medias.Count > 0 && medias[0].Genre == "CAMÉRA";
        if (cameraDabord) v2Res.AddRange(medias);
        v2Res.AddRange(res.Select(V2DeCommande));
        if (!cameraDabord) v2Res.AddRange(medias);
        v2ResIndex = choisi != null ? Math.Max(0, v2Res.FindIndex(x => x.Titre == choisi.Titre && x.Genre == choisi.Genre)) : 0;
        V2BarreAfficher();
    }

    /// <summary>Les médias du culte qui répondent : « caméra » donne les caméras ; sinon le nom du média.</summary>
    IEnumerable<V2Res> V2Medias(string? q)
    {
        var n = MyVerse.Texte.Norm(q ?? "").Trim();
        if (n.Length < 2 || !PagePermise("annonces") && !PagePermise("agenda")) yield break;
        bool cam = n is "camera" or "cameras" or "cam" or "camescope" or "predication" || n.StartsWith("camera ");
        int k = 0;
        foreach (var a in R.Annonces)
        {
            if (a.Source == null) continue;
            bool estCam = a.Source.Type is "camera" or "mycam";
            if (!(cam && estCam) && !MyVerse.Texte.Norm(a.Titre).Contains(n)) continue;
            yield return new V2Res(estCam ? "CAMÉRA" : "MÉDIA", a.Titre, estCam ? "Caméra" : "Média", estCam ? "L'image de la caméra, en direct dans l'église et sur Internet." : a.Description, Annonce: a);
            if (++k >= 4) yield break;
        }
    }

    V2Res V2DeCommande(ResultatCommande r)
    {
        if (r.Action != null) return new V2Res("ACTION", r.Titre, "Action", r.Sous, Action: r.Action);
        if (r.ChantId != null)
        {
            var c = R.Trouver(r.ChantId);
            var paroles = c?.Sections.FirstOrDefault()?.Text.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(2) ?? Enumerable.Empty<string>();
            return new V2Res("CHANT", r.Titre, "Chant · " + r.Sous, string.Join(" / ", paroles), ChantId: r.ChantId);
        }
        if (r.Ref is Ref rf)
        {
            var nom = B.Nom.Length > 0 ? B.Nom : VB.Etiquette();
            if (rf.V1 == 0) return new V2Res("BIBLE", r.Titre, "Bible · chapitre entier", "Montre le chapitre verset après verset, avec Suivant.", Ref: rf);
            if (rf.V2 > rf.V1) return new V2Res("BIBLE", r.Titre, $"Bible · {rf.V2 - rf.V1 + 1} versets", $"Montre les versets {rf.V1} à {rf.V2} l'un après l'autre, avec Suivant.", Ref: rf);
            return new V2Res("BIBLE", r.Titre, "Bible · " + nom, r.Sous, Ref: rf);
        }
        return new V2Res(r.Type, r.Titre, r.Type, r.Sous);
    }

    void V2BarreAfficher()
    {
        if (v2BarreResultats == null || v2BarreChamp == null) return;
        v2BarreResultats.Children.Clear();
        if (v2Res.Count == 0)
        {
            var vide = V2.T(v2BarreChamp.Text.Trim().Length == 0
                ? "Écris ce que tu veux montrer : un verset, un chant, une caméra…"
                : "Rien trouvé. Essaie « jean 3 16 », « wokovu 45 » ou des paroles du chant.", 15, V2.Muet, false, null, true);
            vide.Margin = new Thickness(4, 10, 4, 6);
            v2BarreResultats.Children.Add(vide);
            return;
        }
        for (int i = 0; i < v2Res.Count; i++) v2BarreResultats.Children.Add(V2LigneRes(v2Res[i], i == v2ResIndex));
    }

    FrameworkElement V2LigneRes(V2Res r, bool choisi)
    {
        var (fond, couleur, icone) = r.Genre switch
        {
            "BIBLE" => (V2.VioletLeger, V2.Violet, V2.IcLivre),
            "CHANT" => (V2.BleuPale, V2.Bleu, V2.IcMusique),
            "CAMÉRA" => (V2.RosePale, V2.Rose, V2.IcCamera),
            "MÉDIA" => (V2.TurquoisePale, V2.Turquoise, V2.IcImage),
            _ => (V2.Douce, V2.Encre2, r.Action == "noir" ? V2.IcLune : V2.IcCroix),
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var carre = V2.CarreIcone(icone, couleur, V2.Blanc, 44, 13, 22);
        carre.VerticalAlignment = VerticalAlignment.Top;
        carre.Margin = new Thickness(0, 2, 16, 0);
        g.Children.Add(carre);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var ligneTitre = new StackPanel { Orientation = Orientation.Horizontal };
        ligneTitre.Children.Add(V2.T(r.Titre, 19, V2.Encre, true));
        ligneTitre.Children.Add(new Border
        {
            Background = V2.Carte, CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = V2.T(r.Badge, 11.5, V2.Encre2, false, FontWeights.Bold),
        });
        textes.Children.Add(ligneTitre);
        if (r.Texte.Length > 0)
            textes.Children.Add(new TextBlock
            {
                Text = r.Texte, FontFamily = r.Genre == "BIBLE" ? V2.Serif : V2.Sans, FontSize = 15, Foreground = V2.Encre2, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 44, LineHeight = 21, Margin = new Thickness(0, 4, 0, 0),
            });
        Grid.SetColumn(textes, 1);
        g.Children.Add(textes);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        if (v2BarreAjout)
        {
            if (r.Action == null) actions.Children.Add(V2BoutonMontrer("Ajouter", V2.IcPlus, () => { V2MettreApres(r, true); V2FermerBarre(); }));
        }
        else
        {
            actions.Children.Add(V2BoutonMontrer(r.Action != null ? "Faire" : "Montrer", V2.IcLecture, () => V2Montrer(r)));
            if (r.Action == null && PagePermise("agenda"))
            {
                var apres = V2.Bouton(new Border
                {
                    Background = V2.Carte, BorderBrush = V2.Or, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(16), Height = 48,
                    Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(10, 0, 0, 4),
                    Child = new TextBlock { Text = "Mettre après", FontFamily = V2.Sans, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = V2.Encre, VerticalAlignment = VerticalAlignment.Center },
                }, () => { V2MettreApres(r, false); v2BarreChamp?.SelectAll(); v2BarreChamp?.Focus(); }, "Mettre cette carte juste après celle à l'écran (Tab)");
                actions.Children.Add(apres);
            }
        }
        Grid.SetColumn(actions, 2);
        g.Children.Add(actions);
        return new Border
        {
            Background = fond, CornerRadius = new CornerRadius(24), Padding = new Thickness(18, 14, 16, 14), Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(3), BorderBrush = choisi ? V2.Rouge : Brushes.Transparent, Child = g,
        };
    }

    static Button V2BoutonMontrer(string texte, string icone, Action clic)
    {
        var ic = V2.Icone(icone, V2.Blanc, 13, 2.4, icone == V2.IcLecture);
        ic.Margin = new Thickness(0, 0, 8, 0);
        var t = new TextBlock { Text = texte, FontFamily = V2.Sans, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = V2.Blanc, VerticalAlignment = VerticalAlignment.Center };
        var l = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        l.Children.Add(ic);
        l.Children.Add(t);
        return V2.Bouton(new Brique(l, V2.Rouge, V2.RougeOmbre, 16, 4, new Thickness(16, 0, 16, 0)) { Height = 52 }, clic);
    }

    void V2BarreChoisir(int d)
    {
        if (v2Res.Count == 0 || v2BarreResultats == null) return;
        v2ResIndex = (v2ResIndex + d + v2Res.Count) % v2Res.Count;
        V2BarreAfficher();
        if (v2ResIndex < v2BarreResultats.Children.Count && v2BarreResultats.Children[v2ResIndex] is FrameworkElement f) f.BringIntoView();
    }

    void V2BarreValider(bool apres)
    {
        if (v2BarreMinuterie.IsEnabled) { v2BarreMinuterie.Stop(); V2BarreChercher(); }
        if (v2Res.Count == 0) return;
        var r = v2Res[Math.Clamp(v2ResIndex, 0, v2Res.Count - 1)];
        if (v2BarreAjout)
        {
            if (r.Action == null) { V2MettreApres(r, true); V2FermerBarre(); }
            return;
        }
        if (apres)
        {
            if (r.Action == null && PagePermise("agenda")) { V2MettreApres(r, false); v2BarreChamp?.SelectAll(); }
            return;
        }
        V2Montrer(r);
    }

    /// <summary>« Montrer » : tout de suite à l'écran (un passage : son premier verset, puis Suivant verset après verset).</summary>
    void V2Montrer(V2Res r)
    {
        if (r.Action == "noir") BasculerNoir();
        else if (r.Action == "retirer") R.Effacer();
        else if (r.ChantId != null && R.Trouver(r.ChantId) is { Sections.Count: > 0 } c) R.AfficherSection(c.Id, 0);
        else if (r.Ref is Ref rf)
        {
            var s = rf.V1 > 0 ? new Ref(rf.B, rf.C, rf.V1, rf.V1) : new Ref(rf.B, rf.C, 1, 1);
            R.Selection = s;
            R.AfficherVerset(s);
        }
        else if (r.Annonce is { } a) R.AfficherAnnonce(a, 0);
        V2FermerBarre();
    }

    /// <summary>« Mettre après » : la carte va juste après celle à l'écran (<paramref name="fin"/> : à la fin du culte).</summary>
    void V2MettreApres(V2Res r, bool fin)
    {
        var l = R.Agenda.Elements;
        int pos = -1;
        if (!fin && R.ElementCourant is { } cur)
        {
            int i = l.IndexOf(cur);
            if (i >= 0) pos = i + 1;
        }
        ElementAgenda? el = r.ChantId != null ? R.AjouterChantAgenda(r.ChantId, pos)
            : r.Ref is Ref rf ? R.AjouterBibleAgenda(rf, pos)
            : r.Annonce is { } a ? R.AjouterAnnonceAgenda(a.Id, pos)
            : null;
        if (el == null) return;
        Toast(pos >= 0 ? $"« {el.Titre} » vient juste après." : $"« {el.Titre} » est ajouté à la fin du culte.");
    }

    // ------------------------------------------------------------ pour le test automatique
    public bool TestV2Visible => V2Visible;
    public string TestV2Page => v2Page;
    public void TestV2Aller(string page) => V2Aller(page, false);
    public void TestV2Suivant() => V2Suivant();
    public void TestV2Commencer() => V2Commencer();
    public void TestV2Barre(string q) => V2OuvrirBarre(false, q);
    public int TestV2Resultats => v2Res.Count;
    public bool TestV2BarreOuverte => v2BarreOuverte;
    public void TestV2BarreValider(bool apres) => V2BarreValider(apres);
    public void TestV2FermerBarre() => V2FermerBarre();
    public string TestV2Puis => v2Puis?.Text ?? "";
    public int TestV2Cartes => v2CartesCulte.Count;
}
