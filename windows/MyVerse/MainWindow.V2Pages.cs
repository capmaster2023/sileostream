using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GenreSon = MyVerse.Audio.GenreSon;
using VerdictNiveau = MyVerse.Audio.VerdictNiveau;
using VolumeCurseur = MyVerse.Audio.VolumeCurseur;

namespace MyVerse.Ui;

/// <summary>Siloé Stream 2.0 : Préparer le culte, le son en gros boutons, Aller en direct.</summary>
public partial class MainWindow
{
    // ------------------------------------------------------------ Préparer le culte
    StackPanel? v2Liste;
    TextBlock? v2PreparerTitre;

    FrameworkElement V2PagePreparer()
    {
        var racine = new Grid { Margin = new Thickness(28, 20, 28, 20) };
        racine.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        racine.RowDefinitions.Add(new RowDefinition());
        var reprendre = V2.BoutonPilule("Reprendre dimanche dernier", V2Reprendre, V2.IcCopier, V2.Carte, V2.Encre, V2.Trait, 14.5, 48);
        reprendre.ToolTip = "Les cartes du culte d'avant s'ajoutent à la fin de celui-ci";
        var (tete, titre) = V2EntetePage("Le culte de dimanche", "Mets les cartes dans l'ordre. Dimanche, il n'y aura plus qu'à appuyer sur Suivant.", reprendre);
        v2PreparerTitre = titre;
        racine.Children.Add(tete);

        var corps = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        corps.ColumnDefinitions.Add(new ColumnDefinition());
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        Grid.SetRow(corps, 1);
        v2Liste = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        corps.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = v2Liste });

        // ajouter une carte
        var ajout = new StackPanel();
        ajout.Children.Add(V2.T("Ajouter une carte", 19, V2.Encre, true));
        var aide = V2.T("Touche ce que tu veux : la carte va à la fin du culte.", 14.5, V2.Encre2, false, null, true);
        aide.Margin = new Thickness(0, 6, 0, 14);
        ajout.Children.Add(aide);
        var tuiles = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 0) };
        tuiles.Children.Add(V2Tuile("Chant", V2.IcMusique, V2.BleuPale, V2.Bleu, _ => V2OuvrirBarre(true, "", "Le numéro ou le titre : 45, wokovu 45, à toi la gloire…")));
        tuiles.Children.Add(V2Tuile("Bible", V2.IcLivre, V2.VioletPale, V2.Violet, _ => V2OuvrirBarre(true, "", "Un passage : jean 3 16, psaume 23…")));
        tuiles.Children.Add(V2Tuile("Caméra", V2.IcCamera, V2.RosePale, V2.Rose, V2AjouterCamera));
        tuiles.Children.Add(V2Tuile("Image", V2.IcImage, V2.TurquoisePale, V2.Turquoise, _ => V2AjouterFichier("image")));
        tuiles.Children.Add(V2Tuile("Vidéo", V2.IcVideo, V2.GrisPale, V2.Gris, _ => V2AjouterFichier("video")));
        tuiles.Children.Add(V2Tuile("Texte", V2.IcTexte, V2.OrPale, V2.Ambre, _ => V2AjouterTexte()));
        ajout.Children.Add(tuiles);
        var pret = V2.Icone(V2.IcCoche, V2.Blanc, 20, 3);
        pret.Margin = new Thickness(0, 0, 12, 0);
        var tPret = V2.T("C'est prêt !", 20, V2.Blanc, true);
        tPret.VerticalAlignment = VerticalAlignment.Center;
        var lPret = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lPret.Children.Add(pret);
        lPret.Children.Add(tPret);
        var boutonPret = V2.Bouton(new Brique(lPret, V2.Vert, V2.VertOmbre, 28, 8, new Thickness(12, 0, 12, 0)) { Height = 78 }, () => V2Aller("regie"), "Retour à la régie");
        boutonPret.Margin = new Thickness(0, 10, 0, 0);
        ajout.Children.Add(boutonPret);
        var note = V2.T("Tout est enregistré tout seul, à chaque changement.", 13.5, V2.Muet, false, null, true);
        note.TextAlignment = TextAlignment.Center;
        note.Margin = new Thickness(0, 10, 0, 0);
        ajout.Children.Add(note);
        var cote = new Border
        {
            Background = V2.Carte, BorderBrush = V2.Trait, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(28), Padding = new Thickness(22),
            VerticalAlignment = VerticalAlignment.Top, Child = ajout,
        };
        var defilCote = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cote };
        Grid.SetColumn(defilCote, 2);
        corps.Children.Add(defilCote);
        racine.Children.Add(corps);
        v2PreparerAFaire = true;
        return racine;
    }

    Button V2Tuile(string titre, string icone, Brush fond, Brush couleur, Action<FrameworkElement> clic)
    {
        var pile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ic = V2.Icone(icone, couleur, 26, 2.2);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        pile.Children.Add(ic);
        var t = V2.T(titre, 14.5, V2.Encre, true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 0);
        pile.Children.Add(t);
        var b = new Border { Background = fond, CornerRadius = new CornerRadius(18), Height = 92, Margin = new Thickness(0, 0, 10, 10), Child = pile };
        Button? bouton = null;
        bouton = V2.Bouton(b, () => clic(bouton!), titre + " : ajouter à la fin du culte");
        return bouton;
    }

    void V2MajPreparer()
    {
        if (v2Liste == null) return;
        if (v2PreparerTitre != null) v2PreparerTitre.Text = R.Agenda.Nom is { Length: > 0 } n ? n : "Le culte";
        if (!v2PreparerAFaire) return;
        v2PreparerAFaire = false;
        v2Liste.Children.Clear();
        var l = R.Agenda.Elements;
        if (l.Count == 0)
        {
            var vide = new StackPanel();
            vide.Children.Add(V2.T("Le culte est vide.", 20, V2.Encre, true));
            var t = V2.T("Ajoute les cartes à droite (un chant, la Bible, la caméra…), ou reprends le culte de dimanche dernier.", 16, V2.Encre2, false, null, true);
            t.Margin = new Thickness(0, 6, 0, 0);
            vide.Children.Add(t);
            v2Liste.Children.Add(new Border { Background = V2.Douce, CornerRadius = new CornerRadius(24), Padding = new Thickness(26, 22, 26, 24), Child = vide });
            return;
        }
        for (int i = 0; i < l.Count; i++) v2Liste.Children.Add(V2LigneCulte(l[i], i, l.Count));
    }

    FrameworkElement V2LigneCulte(ElementAgenda e, int i, int n)
    {
        var (lib, ic, fond, couleur) = V2Genre(e);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var num = V2.T((i + 1).ToString(), 14, V2.Encre, true);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        num.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = V2.Carte, Child = num, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
        var carre = V2.CarreIcone(ic, couleur, V2.Blanc, 44, 13, 22);
        carre.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetColumn(carre, 1);
        g.Children.Add(carre);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(V2.T(lib, 17, V2.Encre, true));
        string sous;
        try { sous = TitreElement(e) + " · " + R.Description(e).Sous; }
        catch (Exception x) { Journal.Erreur("préparer le culte", x); sous = TitreElement(e); }
        var s = V2.T(sous, 14, V2.Encre2);
        s.Margin = new Thickness(0, 2, 0, 0);
        textes.Children.Add(s);
        Grid.SetColumn(textes, 2);
        g.Children.Add(textes);
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var id = e.Id;
        boutons.Children.Add(V2PetitBouton(V2.IcHaut, () => R.DeplacerAgenda(id, -1), i > 0, "Monter"));
        boutons.Children.Add(V2PetitBouton(V2.IcBas, () => R.DeplacerAgenda(id, 1), i < n - 1, "Descendre"));
        var titre = TitreElement(e);
        boutons.Children.Add(V2PetitBouton(V2.IcCroix, () =>
        {
            if (Dialogue.Confirmer(this, $"Retirer « {titre} » du culte ?", "Retirer")) R.RetirerAgenda(id);
        }, true, "Retirer cette carte du culte", V2.RougeVif));
        Grid.SetColumn(boutons, 3);
        g.Children.Add(boutons);
        return new Border { Background = fond, CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = g };
    }

    static Button V2PetitBouton(string icone, Action clic, bool actif, string info, Brush? couleur = null)
    {
        var b = V2.Bouton(new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Background = V2.Carte, BorderBrush = V2.Trait, BorderThickness = new Thickness(2),
            Margin = new Thickness(8, 0, 0, 0), Child = V2.Icone(icone, couleur ?? V2.Encre, 18, 2.4),
        }, clic, info);
        b.IsEnabled = actif;
        return b;
    }

    void V2Reprendre()
    {
        var actuel = R.Agenda;
        var source = R.Agendas.LastOrDefault(a => !ReferenceEquals(a, actuel) && a.Elements.Count > 0);
        if (source == null) { Toast("Pas encore d'autre culte à reprendre."); return; }
        if (actuel.Elements.Count > 0 && !Dialogue.Confirmer(this, $"Ajouter les {source.Elements.Count} cartes de « {source.Nom} » à la fin de ce culte ?", "Ajouter")) return;
        int n = R.ReprendreAgenda(source.Id);
        Toast($"{n} carte{(n > 1 ? "s" : "")} reprise{(n > 1 ? "s" : "")} de « {source.Nom} ».");
    }

    void V2AjouterCamera(FrameworkElement depuis)
    {
        var cams = R.Annonces.Where(a => a.Source?.Type is "camera" or "mycam").ToList();
        if (cams.Count == 0)
        {
            Toast("Aucune caméra dans les médias. Ajoute-la une fois dans le mode complet : Médias › Ajouter une source.");
            return;
        }
        if (cams.Count == 1) { V2AjouterAnnonce(cams[0]); return; }
        var m = new ContextMenu();
        foreach (var a in cams)
        {
            var c = a;
            m.Items.Add(Article(c.Titre, () => V2AjouterAnnonce(c)));
        }
        Ouvrir(m, depuis, PlacementMode.Bottom);
    }

    void V2AjouterAnnonce(Annonce a)
    {
        if (R.AjouterAnnonceAgenda(a.Id) is { } el) Toast($"« {el.Titre} » est ajouté à la fin du culte.");
    }

    /// <summary>Une image (copiée dans les données de Siloé Stream) ou une vidéo : un média, puis une carte à la fin du culte.</summary>
    void V2AjouterFichier(string type)
    {
        bool image = type == "image";
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Title = image ? "Ajouter une image au culte" : "Ajouter une vidéo au culte",
            Filter = image ? "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" : "Vidéos|*.mp4;*.mkv;*.wmv;*.avi;*.mov",
            Multiselect = image,
        };
        if (d.ShowDialog(this) != true) return;
        int n = 0;
        foreach (var f in d.FileNames)
        {
            try
            {
                var chemin = f;
                if (image)
                {
                    var dir = Path.Combine(App.Donnees.Dossier, "display-images");
                    Directory.CreateDirectory(dir);
                    chemin = Path.Combine(dir, Guid.NewGuid().ToString("N") + Path.GetExtension(f));
                    File.Copy(f, chemin);
                }
                var a = R.NouveauDisplay(Path.GetFileNameWithoutExtension(f), new SourceDisplay { Type = type, Chemin = chemin });
                if (R.AjouterAnnonceAgenda(a.Id) != null) n++;
            }
            catch (Exception x)
            {
                Journal.Erreur("carte du culte : " + type, x);
                Toast((image ? "Image" : "Vidéo") + " impossible à ajouter : " + x.Message);
            }
        }
        if (n > 0) Toast(n > 1 ? $"{n} images sont ajoutées à la fin du culte." : (image ? "L'image" : "La vidéo") + " est ajoutée à la fin du culte.");
    }

    void V2AjouterTexte()
    {
        if (Dialogue.DemanderTexte(this, "Ajouter un texte au culte", "Annonce", "") is not { } t) return;
        if (t.Titre.Trim().Length == 0 && t.Texte.Trim().Length == 0) return;
        var el = R.AjouterTexteAgenda(t.Titre, t.Texte);
        Toast($"« {el.Titre} » est ajouté à la fin du culte.");
    }

    // ------------------------------------------------------------ le son, en gros boutons
    sealed class V2CarteSon
    {
        public MyVerse.Audio.Son Son = null!;
        public Brique Brique = null!;
        public Border Badge = null!, CouperFond = null!;
        public TextBlock BadgeTexte = null!, Pourcent = null!, CouperTexte = null!;
        public Border[] Barres = Array.Empty<Border>();
        public Slider Curseur = null!;
        public string Verdict = "";
    }

    sealed class V2Dest
    {
        public Border Fond = null!, Rail = null!, Bouton = null!;
        public TextBlock Sous = null!;
    }

    UniformGrid? v2GrilleSons;
    readonly List<V2CarteSon> v2Sons = new();
    string v2SignatureSons = "";
    bool v2SonCode;
    V2Dest? v2DestEglise, v2DestInternet, v2DestEnregistrer;

    FrameworkElement V2PageSon()
    {
        var racine = new Grid { Margin = new Thickness(28, 20, 28, 20) };
        racine.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        racine.RowDefinitions.Add(new RowDefinition());
        var tous = V2.BoutonPilule("Tous les réglages du son", () => { V2Aller("classique"); MontrerOnglet("son"); }, V2.IcGrille, V2.Carte, V2.Encre, V2.Trait, 14.5, 48);
        var (tete, _) = V2EntetePage("Le son", "Vert, c'est bien. Orange, monte un peu. Rouge, baisse. C'est tout.", tous);
        racine.Children.Add(tete);
        var pile = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        v2GrilleSons = new UniformGrid { Columns = 4, Margin = new Thickness(0, 18, -16, 0) };
        pile.Children.Add(v2GrilleSons);
        var ajouter = V2.Lien("+ Ajouter un micro, un instrument ou une musique", () => Audio.PageSon.Ajouter(this), 15);
        ajouter.HorizontalAlignment = HorizontalAlignment.Left;
        ajouter.Margin = new Thickness(0, 4, 0, 0);
        pile.Children.Add(ajouter);
        var ou = V2.T("Où va le son ?", 22, V2.Encre, true);
        ou.Margin = new Thickness(0, 26, 0, 14);
        pile.Children.Add(ou);
        var dest = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -16, 0) };
        v2DestEglise = V2FaireDest("Dans l'église", false, () => { V2Aller("classique"); MontrerOnglet("son"); Toast("Dans l'église : chaque son se règle avec « Régler » (risque de larsen pour les micros)."); });
        v2DestInternet = V2FaireDest("Sur Internet", true, V2BasculerZoom);
        v2DestEnregistrer = V2FaireDest("Enregistrer", true, () => { Audio.PageSon.EnregistrerLeCulte(); V2MajDestinations(); });
        dest.Children.Add(v2DestEglise.Bouton);
        dest.Children.Add(v2DestInternet.Bouton);
        dest.Children.Add(v2DestEnregistrer.Bouton);
        pile.Children.Add(dest);
        var defil = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = pile };
        Grid.SetRow(defil, 1);
        racine.Children.Add(defil);
        v2SignatureSons = "";
        return racine;
    }

    void V2MajSon(bool tout)
    {
        if (v2GrilleSons == null) return;
        var sons = App.Audio.Config.Sons;
        var sig = string.Join("|", sons.Select(s => s.Id + "/" + s.Nom + "/" + s.Genre));
        if (sig != v2SignatureSons) { v2SignatureSons = sig; V2FaireSons(); }
        foreach (var c in v2Sons) V2MajCarteSon(c);
        if (tout || v2Tic % 4 == 0) V2MajDestinations();
    }

    void V2FaireSons()
    {
        if (v2GrilleSons == null) return;
        v2GrilleSons.Children.Clear();
        v2Sons.Clear();
        var sons = App.Audio.Config.Sons;
        v2GrilleSons.Columns = Math.Clamp(sons.Count, 1, 4);
        foreach (var s in sons)
        {
            var c = V2FaireCarteSon(s);
            v2Sons.Add(c);
            v2GrilleSons.Children.Add(c.Brique);
        }
    }

    V2CarteSon V2FaireCarteSon(MyVerse.Audio.Son s)
    {
        var c = new V2CarteSon { Son = s };
        var (icone, fond, couleur) = s.Genre switch
        {
            GenreSon.Voix => (V2.IcMicro, V2.RougePale, V2.Rouge),
            GenreSon.Table => (V2.IcTable, V2.BleuPale, V2.Bleu),
            GenreSon.Instrument => (V2.IcMusique, V2.OrPale, V2.Ambre),
            GenreSon.Ordinateur => (V2.IcOrdinateur, V2.GrisPale, V2.Gris),
            GenreSon.Video => (V2.IcVideo, V2.TurquoisePale, V2.Turquoise),
            _ => (V2.IcMusique, V2.VioletPale, V2.Violet),
        };
        var corps = new StackPanel();
        var tete = new Grid();
        tete.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tete.ColumnDefinitions.Add(new ColumnDefinition());
        var carre = V2.CarreIcone(icone, fond, couleur, 46, 14, 22);
        carre.VerticalAlignment = VerticalAlignment.Top;
        carre.Margin = new Thickness(0, 0, 12, 0);
        tete.Children.Add(carre);
        var noms = new StackPanel();
        var nom = V2.T(s.Nom, 18.5, V2.Encre, true, null, true);
        nom.MaxHeight = 50;
        nom.TextTrimming = TextTrimming.CharacterEllipsis;
        nom.ToolTip = s.Nom;
        noms.Children.Add(nom);
        c.BadgeTexte = V2.T("", 12, V2.Blanc, false, FontWeights.Bold);
        c.Badge = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 4), Child = c.BadgeTexte, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
        noms.Children.Add(c.Badge);
        Grid.SetColumn(noms, 1);
        tete.Children.Add(noms);
        corps.Children.Add(tete);

        var barres = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        c.Barres = new Border[12];
        for (int i = 0; i < c.Barres.Length; i++)
        {
            c.Barres[i] = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 0), Background = V2.Douce2 };
            barres.Children.Add(c.Barres[i]);
        }
        corps.Children.Add(barres);

        var vol = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        c.Pourcent = V2.T("", 14, V2.Encre, false, FontWeights.Bold);
        DockPanel.SetDock(c.Pourcent, Dock.Right);
        vol.Children.Add(c.Pourcent);
        vol.Children.Add(V2.T("Volume", 14, V2.Encre2, false, FontWeights.Bold));
        corps.Children.Add(vol);
        c.Curseur = new Slider { Minimum = 0, Maximum = 1, SmallChange = 0.01, LargeChange = 0.05, Value = VolumeCurseur.Position(s.VolumeDb), Margin = new Thickness(0, 6, 0, 0), Focusable = false };
        c.Curseur.ValueChanged += (_, e) =>
        {
            if (v2SonCode) return;
            double v = e.NewValue;
            if (Math.Abs(v - VolumeCurseur.Normal) < 0.015) v = VolumeCurseur.Normal;
            s.VolumeDb = Math.Round(VolumeCurseur.Db(v), 1);
            c.Pourcent.Text = VolumeCurseur.Texte(s.VolumeDb);
            App.Audio.Apply();
        };
        c.Curseur.MouseDoubleClick += (_, _) => c.Curseur.Value = VolumeCurseur.Normal;
        corps.Children.Add(c.Curseur);

        var boutons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        boutons.ColumnDefinitions.Add(new ColumnDefinition());
        boutons.ColumnDefinitions.Add(new ColumnDefinition());
        boutons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        var moins = V2BoutonCarre("−", () => V2Volume(s, -10), "Moins fort");
        var plus = V2BoutonCarre("+", () => V2Volume(s, 10), "Plus fort");
        plus.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(plus, 1);
        c.CouperTexte = V2.T("Couper", 14.5, V2.Rouge, false, FontWeights.Bold);
        c.CouperTexte.HorizontalAlignment = HorizontalAlignment.Center;
        c.CouperTexte.VerticalAlignment = VerticalAlignment.Center;
        c.CouperFond = new Border { Height = 48, CornerRadius = new CornerRadius(14), Background = V2.Carte, BorderBrush = V2.RougeBord, BorderThickness = new Thickness(2), Child = c.CouperTexte };
        var couper = V2.Bouton(c.CouperFond, () => { Audio.PageSon.Couper(s, !s.Coupe); V2MajCarteSon(c); }, "Couper ce son partout (ou le remettre)");
        Grid.SetColumn(couper, 2);
        boutons.Children.Add(moins);
        boutons.Children.Add(plus);
        boutons.Children.Add(couper);
        corps.Children.Add(boutons);

        c.Brique = new Brique(corps, V2.Carte, V2.Douce2, 32, 6, new Thickness(22), V2.Trait, 3) { Margin = new Thickness(0, 0, 16, 16) };
        V2MajCarteSon(c);
        return c;
    }

    static Button V2BoutonCarre(string texte, Action clic, string info)
    {
        var t = V2.T(texte, 22, V2.Encre, true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        return V2.Bouton(new Border { Height = 48, CornerRadius = new CornerRadius(14), Background = V2.Douce, BorderBrush = V2.Trait, BorderThickness = new Thickness(2), Child = t }, clic, info);
    }

    /// <summary>− et + : le volume de 10 en 10 (100 % = le volume normal).</summary>
    static void V2Volume(MyVerse.Audio.Son s, int pas)
    {
        int p = VolumeCurseur.Pourcent(s.VolumeDb) + pas;
        p = Math.Clamp((int)Math.Round(p / 10.0) * 10, 0, 120);
        double pos = Math.Clamp(p / 100.0 * VolumeCurseur.Normal, 0, 1);
        s.VolumeDb = Math.Round(VolumeCurseur.Db(pos), 1);
        App.Audio.Apply();
    }

    void V2MajCarteSon(V2CarteSon c)
    {
        var audio = App.Audio;
        var e = audio.Etat(c.Son);
        string v = c.Son.Coupe ? "coupe" : !e.Actif ? "gris" : e.Verdict switch
        {
            VerdictNiveau.Bon => "bon",
            VerdictNiveau.Faible => "faible",
            VerdictNiveau.TropFort => "fort",
            _ => "gris",
        };
        if (v != c.Verdict)
        {
            c.Verdict = v;
            var (bord, relief, badge, texte) = v switch
            {
                "bon" => (V2.VertBord, V2.VertRelief, V2.Vert, V2.Blanc),
                "faible" => (V2.OrBord, V2.OrPale, V2.Or, V2.OrTexte),
                "fort" => (V2.RougeBord, V2.RougePale, V2.RougeVif, V2.Blanc),
                "coupe" => (V2.Trait, V2.Douce2, V2.Douce2, V2.Encre2),
                _ => (V2.Trait, V2.Douce2, V2.Douce, V2.Muet),
            };
            c.Brique.Couleurs(V2.Carte, relief, bord);
            c.Badge.Background = badge;
            c.BadgeTexte.Foreground = texte;
        }
        c.BadgeTexte.Text = e.Texte;
        var (l, r) = audio.Level(c.Son.Id);
        double db = Math.Max(l, r);
        if (double.IsNaN(db)) db = -100;
        int n = c.Son.Coupe || !e.Actif ? 0 : (int)Math.Round(Math.Clamp((db + 60) / 60, 0, 1) * c.Barres.Length);
        var plein = v == "fort" ? V2.RougeVif : v == "faible" ? V2.Or : V2.Vert;
        for (int i = 0; i < c.Barres.Length; i++) c.Barres[i].Background = i < n ? plein : V2.Douce2;
        c.Pourcent.Text = VolumeCurseur.Texte(c.Son.VolumeDb);
        if (!c.Curseur.IsMouseCaptureWithin)
        {
            v2SonCode = true;
            c.Curseur.Value = VolumeCurseur.Position(c.Son.VolumeDb);
            v2SonCode = false;
        }
        c.CouperTexte.Text = c.Son.Coupe ? "Remettre" : "Couper";
        c.CouperTexte.Foreground = c.Son.Coupe ? V2.Blanc : V2.Rouge;
        c.CouperFond.Background = c.Son.Coupe ? V2.RougeVif : V2.Carte;
        c.CouperFond.BorderBrush = c.Son.Coupe ? V2.RougeVif : V2.RougeBord;
    }

    V2Dest V2FaireDest(string titre, bool interrupteur, Action clic)
    {
        var d = new V2Dest();
        var boule = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = V2.Blanc, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left };
        d.Rail = new Border { Width = 56, Height = 32, CornerRadius = new CornerRadius(16), Background = V2.Trait, Child = boule, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        if (!interrupteur)
        {
            d.Rail.Width = 32;
            d.Rail.Child = V2.Icone(V2.IcSon, V2.Blanc, 18, 2.2);
        }
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(V2.T(titre, 16.5, V2.Encre, true));
        d.Sous = V2.T("", 13.5, V2.Encre2, false, null, true);
        d.Sous.Margin = new Thickness(0, 2, 0, 0);
        textes.Children.Add(d.Sous);
        var l = new DockPanel();
        DockPanel.SetDock(d.Rail, Dock.Left);
        l.Children.Add(d.Rail);
        l.Children.Add(textes);
        d.Fond = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(3), Padding = new Thickness(16, 14, 16, 14), Child = l, MinHeight = 86 };
        var b = V2.Bouton(d.Fond, clic);
        b.Margin = new Thickness(0, 0, 16, 0);
        d.Bouton = new Border { Child = b };
        return d;
    }

    static void V2EtatDest(V2Dest? d, bool on, string sous)
    {
        if (d == null) return;
        d.Sous.Text = sous;
        d.Fond.Background = on ? V2.VertPale : V2.Carte;
        d.Fond.BorderBrush = on ? V2.Vert : V2.Trait;
        d.Rail.Background = on ? V2.Vert : V2.Trait;
        if (d.Rail.Child is Border boule) boule.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    void V2MajDestinations()
    {
        var audio = App.Audio;
        var c = audio.Config;
        int eglise = c.Sons.Count(s => s.Eglise && !s.Coupe);
        V2EtatDest(v2DestEglise, eglise > 0, eglise > 0 ? $"{eglise} son{(eglise > 1 ? "s" : "")} dans les haut-parleurs de la salle · Régler" : "Les haut-parleurs de la salle · Régler");
        bool zoom = c.VirtualSounds.Any(v => v.Enabled);
        bool direct = App.Direct.EnDirect;
        V2EtatDest(v2DestInternet, zoom || direct, zoom && direct ? "YouTube et Zoom entendent tout"
            : direct ? "YouTube entend tout · Zoom : touche pour l'allumer"
            : zoom ? "Zoom entend tout (micro « CABLE Output »)"
            : "Éteint · YouTube entend tout pendant le direct");
        var p = c.Profiles.FirstOrDefault();
        bool enr = p != null && audio.Recorder.Active(p.Id);
        V2EtatDest(v2DestEnregistrer, enr, enr ? "Enregistre le culte… touche pour arrêter" : "Le culte en MP3, prêt à partager");
    }

    /// <summary>« Sur Internet » : le son part vers Zoom par le câble audio virtuel (créé au premier clic).</summary>
    void V2BasculerZoom()
    {
        var c = App.Audio.Config;
        var v = c.VirtualSounds.FirstOrDefault();
        if (v == null)
        {
            Audio.PageSon.NouvelEnvoiZoom();
            Toast("Le son part vers Zoom : dans Zoom, choisis le micro « CABLE Output ». Le câble audio se règle dans Tous les réglages du son.");
        }
        else
        {
            Audio.PageSon.Allumer(v, !v.Enabled);
            Toast(v.Enabled ? "Le son part vers Zoom (micro « CABLE Output »)." : "Le son ne part plus vers Zoom.");
        }
        V2MajDestinations();
    }

    // ------------------------------------------------------------ Aller en direct
    Rendu? v2RenduDirect;
    Brique? v2DirectBrique;
    TextBlock? v2DirectTitre, v2DirectSous, v2DirectMessage, v2DirectInfoTitre, v2DirectInfoQualite, v2DirectQui;
    Viewbox? v2DirectIcone;
    Border? v2OptionPublic, v2OptionLien;
    bool v2DirectPublic = true, v2DirectLance;

    FrameworkElement V2PageDirect()
    {
        v2DirectPublic = App.Donnees.Reglages.Direct.Modele.Confidentialite != "unlisted";
        var racine = new Grid { Margin = new Thickness(28, 20, 28, 20) };
        racine.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        racine.RowDefinitions.Add(new RowDefinition());
        var avance = V2.BoutonPilule("Réglages avancés", OuvrirDirect, V2.IcGrille, V2.Carte, V2.Encre, V2.Trait, 14.5, 48);
        avance.ToolTip = "Le compte YouTube, la clé de stream, la qualité, un direct programmé…";
        var (tete, _) = V2EntetePage("Aller en direct", "Un seul bouton. YouTube et Zoom reçoivent la même image.", avance);
        racine.Children.Add(tete);

        var corps = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        corps.ColumnDefinitions.Add(new ColumnDefinition());
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        corps.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(corps, 1);

        // le gros bouton rond
        var gauche = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var contenu = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        v2DirectIcone = V2.Icone(V2.IcDirect, V2.Blanc, 54, 2.2);
        v2DirectIcone.HorizontalAlignment = HorizontalAlignment.Center;
        contenu.Children.Add(v2DirectIcone);
        v2DirectTitre = V2.T("Aller\nen direct", 25, V2.Blanc, true, null, true);
        v2DirectTitre.TextAlignment = TextAlignment.Center;
        v2DirectTitre.HorizontalAlignment = HorizontalAlignment.Center;
        v2DirectTitre.Margin = new Thickness(0, 10, 0, 0);
        contenu.Children.Add(v2DirectTitre);
        v2DirectSous = V2.T("", 14, V2.Blanc80);
        v2DirectSous.HorizontalAlignment = HorizontalAlignment.Center;
        v2DirectSous.Margin = new Thickness(0, 6, 0, 0);
        contenu.Children.Add(v2DirectSous);
        v2DirectBrique = new Brique(contenu, V2.RougeVif, V2.RougeOmbre, 125, 12, new Thickness(10)) { Width = 250, Height = 262 };
        var rond = V2.Bouton(v2DirectBrique, V2DemarrerDirect);
        rond.HorizontalAlignment = HorizontalAlignment.Center;
        gauche.Children.Add(rond);
        v2DirectQui = V2.T("Qui peut regarder ?", 15, V2.Encre2, false, FontWeights.Bold);
        v2DirectQui.Margin = new Thickness(0, 26, 0, 10);
        gauche.Children.Add(v2DirectQui);
        var options = new UniformGrid { Columns = 2, Width = 460 };
        Button bp, bl;
        (v2OptionPublic, bp) = V2Option("Tout le monde", () => { v2DirectPublic = true; V2MajDirect(); });
        (v2OptionLien, bl) = V2Option("Seulement avec le lien", () => { v2DirectPublic = false; V2MajDirect(); });
        bp.Margin = new Thickness(0, 0, 6, 0);
        bl.Margin = new Thickness(6, 0, 0, 0);
        options.Children.Add(bp);
        options.Children.Add(bl);
        gauche.Children.Add(options);
        v2DirectMessage = V2.T("", 14.5, V2.Muet, false, null, true);
        v2DirectMessage.TextAlignment = TextAlignment.Center;
        v2DirectMessage.MaxWidth = 460;
        v2DirectMessage.Margin = new Thickness(0, 16, 0, 0);
        gauche.Children.Add(v2DirectMessage);
        corps.Children.Add(new Border
        {
            Background = V2.Carte, BorderBrush = V2.Trait, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(30), Padding = new Thickness(30, 30, 30, 28),
            VerticalAlignment = VerticalAlignment.Top, Child = gauche,
        });

        // ce que voient YouTube et Zoom
        var droite = new StackPanel();
        var ligne = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        ligne.Children.Add(V2.Badge("Sur Internet", V2.Indigo, V2.Blanc, 11));
        var ce = V2.T("Ce que voient YouTube et Zoom", 15, V2.Encre, false, FontWeights.Bold);
        ce.VerticalAlignment = VerticalAlignment.Center;
        ce.Margin = new Thickness(10, 0, 0, 0);
        ligne.Children.Add(ce);
        droite.Children.Add(ligne);
        v2RenduDirect = new Rendu { Width = 1920, Height = 1080, Transitions = false, Leger = true, IsHitTestVisible = false };
        var vue = new Grid { ClipToBounds = true };
        vue.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = v2RenduDirect });
        vue.SizeChanged += (_, e) => vue.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 18, 18);
        droite.Children.Add(new Border { BorderBrush = V2.Indigo, BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(24), Background = V2.Scene2, Child = vue });
        var infos = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        infos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infos.ColumnDefinitions.Add(new ColumnDefinition());
        v2DirectInfoTitre = V2.T("", 15, V2.Encre, false, FontWeights.Bold);
        v2DirectInfoQualite = V2.T("", 15, V2.Encre, false, FontWeights.Bold);
        var lignes = new (string Nom, TextBlock Valeur)[]
        {
            ("Titre", v2DirectInfoTitre),
            ("Qualité", v2DirectInfoQualite),
            ("Zoom", V2.T("Même image, par la caméra virtuelle", 15, V2.Encre, false, FontWeights.Bold)),
        };
        for (int i = 0; i < lignes.Length; i++)
        {
            infos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var n = V2.T(lignes[i].Nom, 14.5, V2.Muet);
            n.Margin = new Thickness(0, 0, 18, 8);
            Grid.SetRow(n, i);
            infos.Children.Add(n);
            var v = lignes[i].Valeur;
            v.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            infos.Children.Add(v);
        }
        droite.Children.Add(new Border { Background = V2.Carte, BorderBrush = V2.Trait, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(22), Padding = new Thickness(20, 16, 20, 10), Margin = new Thickness(0, 16, 0, 0), Child = infos });
        Grid.SetColumn(droite, 2);
        corps.Children.Add(droite);
        var defil = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = corps };
        Grid.SetRow(defil, 1);
        racine.Children.Add(defil);
        return racine;
    }

    (Border Fond, Button Bouton) V2Option(string texte, Action clic)
    {
        var t = V2.T(texte, 14.5, V2.Encre, false, FontWeights.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        var f = new Border { Height = 54, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(3), BorderBrush = V2.Trait, Background = V2.Carte, Child = t };
        return (f, V2.Bouton(f, clic));
    }

    static string V2TitreDirect(string nom)
    {
        var t = (nom.Length > 0 ? nom : Regie.NomAgendaParDefaut()).Replace("<", "").Replace(">", "").Trim();
        return t.Length > 100 ? t[..100] : t;
    }

    void V2MajDirect()
    {
        if (v2DirectBrique == null || v2DirectTitre == null || v2DirectSous == null || v2DirectMessage == null || v2DirectIcone == null) return;
        var d = App.Direct;
        var reg = App.Donnees.Reglages.Direct;
        bool compte = reg.Mode == ReglagesDirect.Compte;
        bool permis = DirectBtn.Visibility == Visibility.Visible;
        if (d.EnDirect)
        {
            v2DirectBrique.Couleurs(V2.Encre, V2.Noir);
            v2DirectTitre.Text = "Arrêter\nle direct";
            v2DirectSous.Text = (d.Etat == Audio.EtatDirect.Reconnexion ? "Reconnexion… · " : "En direct · ") + V2Duree(d.Duree);
            V2.Recolorer(v2DirectIcone, V2.Blanc, V2.IcDirect);
        }
        else if (d.Actif)
        {
            v2DirectBrique.Couleurs(V2.Or, V2.Ambre);
            v2DirectTitre.Text = d.Etat == Audio.EtatDirect.Arret ? "Arrêt…" : "Démarrage…";
            v2DirectSous.Text = d.Etat == Audio.EtatDirect.Arret ? "YouTube termine le direct" : "YouTube se prépare";
            V2.Recolorer(v2DirectIcone, V2.Blanc, V2.IcDirect);
        }
        else
        {
            v2DirectBrique.Couleurs(V2.RougeVif, V2.RougeOmbre);
            v2DirectTitre.Text = "Aller\nen direct";
            v2DirectSous.Text = compte && !d.Compte.Connecte ? "relier YouTube d'abord" : "";
            V2.Recolorer(v2DirectIcone, V2.Blanc, V2.IcDirect);
        }
        v2DirectSous.Visibility = v2DirectSous.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (v2OptionPublic != null && v2OptionLien != null && v2DirectQui != null)
        {
            bool choix = compte && !d.Actif;
            v2OptionPublic.BorderBrush = v2DirectPublic ? V2.Rouge : V2.Trait;
            v2OptionPublic.Background = v2DirectPublic ? V2.RougePale : V2.Carte;
            v2OptionLien.BorderBrush = !v2DirectPublic ? V2.Rouge : V2.Trait;
            v2OptionLien.Background = !v2DirectPublic ? V2.RougePale : V2.Carte;
            v2OptionPublic.Opacity = v2OptionLien.Opacity = choix ? 1 : 0.55;
            v2OptionPublic.IsHitTestVisible = v2OptionLien.IsHitTestVisible = choix;
            v2DirectQui.Text = compte ? "Qui peut regarder ?" : "Qui peut regarder ? (avec une clé de stream : dans YouTube Studio)";
        }
        string message = !permis ? "Le direct se lance avec le compte de l'administrateur."
            : d.Message.Length > 0 && (d.Etat == Audio.EtatDirect.Erreur || v2DirectLance || d.Actif) ? d.Message
            : compte && !d.Compte.Connecte ? "La première fois : relie le compte YouTube de l'église (Réglages avancés), ensuite c'est un seul bouton."
            : "YouTube reçoit l'image de « Sur Internet » et tout le son. Zoom reçoit la même image par la caméra virtuelle.";
        v2DirectMessage.Text = message;
        v2DirectMessage.Foreground = d.Etat == Audio.EtatDirect.Erreur ? V2.RougeVif : V2.Muet;
        if (v2DirectInfoTitre != null) v2DirectInfoTitre.Text = compte ? V2TitreDirect(R.Agenda.Nom) : "Celui de YouTube Studio (clé de stream)";
        if (v2DirectInfoQualite != null) v2DirectInfoQualite.Text = reg.Qualite switch { "2160p" => "4K", "1080p" => "1080p · haute définition", _ => "720p · sûre même si Internet est lent" };
    }

    async void V2DemarrerDirect()
    {
        var d = App.Direct;
        if (DirectBtn.Visibility != Visibility.Visible) { Toast("Le direct se lance avec le compte de l'administrateur."); return; }
        if (d.Actif)
        {
            if (d.Etat is Audio.EtatDirect.Preparation or Audio.EtatDirect.Arret) return;
            if (!Dialogue.Confirmer(this, "Arrêter le direct sur YouTube ? Les personnes qui regardent verront la fin du direct.", "Arrêter le direct")) return;
            await d.ArreterAsync();
            V2MajDirect();
            return;
        }
        var reg = App.Donnees.Reglages.Direct;
        v2DirectLance = true;
        if (reg.Mode == ReglagesDirect.Compte)
        {
            if (!d.Compte.Connecte)
            {
                OuvrirDirect();
                Toast("Relie d'abord le compte YouTube de l'église (une seule fois), puis reviens ici.");
                return;
            }
            var m = MyVerse.Audio.AudioConfiguration.Copy(reg.Modele);
            m.Titre = V2TitreDirect(R.Agenda.Nom);
            m.Confidentialite = v2DirectPublic ? "public" : "unlisted";
            m.Enfants ??= false;
            await d.CreerAsync(m, null);
        }
        else await d.DemarrerAvecCleAsync();
        if (d.Etat == Audio.EtatDirect.Erreur) Toast(d.Message);
        V2MajDirect();
    }
}
