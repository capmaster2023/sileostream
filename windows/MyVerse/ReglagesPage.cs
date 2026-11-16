using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyVerse.Ui;

/// <summary>
/// La page Réglages (Siloé Stream 3.1) : un sommaire à gauche (Apparence, Son, Projection, Téléphone, Mise à jour, Transfert,
/// Journal, Licences) et les sections en cartes à droite. Le sommaire suit le défilement.
/// </summary>
public sealed class ReglagesPage : Grid
{
    readonly ScrollViewer defil;
    readonly StackPanel pile = new() { MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left };
    readonly List<(string Id, RadioButton Bouton, FrameworkElement Section)> sections = new();
    readonly Audio.ReglagesSon son = new();
    readonly StackPanel ecrans = new() { Margin = new Thickness(0, 8, 0, 0) };
    readonly StackPanel distance = new() { Margin = new Thickness(0, 8, 0, 0) };
    int versionDistance;
    readonly StackPanel eglise = new() { Margin = new Thickness(0, 8, 0, 0) };
    int versionEglise;
    readonly Button clair, sombre;
    bool defilement;

    public OutilsPanel Outils { get; } = new();

    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);
    MainWindow? Principale => Window.GetWindow(this) as MainWindow ?? Application.Current.MainWindow as MainWindow;

    public ReglagesPage()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        ColumnDefinitions.Add(new ColumnDefinition());

        // ---------------- sections
        (clair, sombre) = (Apercu(false), Apercu(true));
        var themes = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        themes.Children.Add(clair);
        themes.Children.Add(sombre);
        var apparence = Section("Apparence", "Le thème clair est celui par défaut. Le thème sombre repose les yeux dans une salle obscure ; il s'applique tout de suite, à toutes les fenêtres. La projection garde son propre habillage.", themes);

        var projection = Section("Projection", "L'écran choisi reçoit la Bible, les chants et Display, comme le projecteur plein écran d'OBS. MyVirtualCam reçoit toujours la même image en 720p.", ecrans);

        var jumeler = new Button { Style = S("Btn"), Content = "Jumeler un téléphone (code QR)…", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        jumeler.Click += (_, _) => { if (Principale is { } m) new TelephoneWindow { Owner = m }.ShowDialog(); };
        var telephone = Section("Téléphone", "La télécommande Android passe les diapositives et choisit les chants. Le PC et le téléphone doivent être sur le même réseau.", jumeler);
        var aDistance = Section("À distance", "Contrôler Siloé Stream avec le téléphone de n'importe où, par Internet (passe par le relais Siloé Stream du VPS). "
            + "Réglé une fois ici : ensuite, dès que ce PC est allumé, il est joignable, et le téléphone peut même démarrer ou fermer Siloé Stream.", distance);
        var comptes = Section("Église et comptes", "Chacun se connecte avec son compte Google (par siloestream.tech) et ne voit que les pages de son rôle : "
            + "Versets (la Bible), Chants, Affichage (ce qui est montré dans l'église et sur Zoom : Culte, Médias, Duo). L'admin ajoute les membres par leur Gmail.", eglise);

        // l'ordre de la page : Licences (dernière section du son) passe tout en bas
        son.Children.Remove(son.Licences);
        Outils.Children_Retirer(out var maj, out var transfert, out var journal);
        Ajouter("apparence", "Apparence", apparence);
        Ajouter("son", "Son", son.Son, false);
        pile.Children.Add(son);
        Ajouter("projection", "Projection", projection);
        Ajouter("telephone", "Téléphone", telephone);
        Ajouter("distance", "À distance", aDistance);
        Ajouter("eglise", "Église et comptes", comptes);
        Ajouter("maj", "Mise à jour", maj);
        Ajouter("transfert", "Transfert", transfert);
        Ajouter("journal", "Journal", journal);
        Ajouter("licences", "Licences", son.Licences);

        // ---------------- sommaire et défilement
        var sommaire = new StackPanel { Margin = new Thickness(20, 22, 14, 18) };
        foreach (var (_, b, _) in sections) sommaire.Children.Add(b);
        Children.Add(new Border { BorderBrush = P("Nuit3"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sommaire });
        defil = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(28, 22, 28, 40) };
        SetColumn(defil, 1);
        Children.Add(defil);
        defil.ScrollChanged += (_, _) => Suivre();
        sections[0].Bouton.IsChecked = true;

        Palette.Change += MajApparence;
        IsVisibleChanged += (_, _) => { if (IsVisible) { MajEcrans(); MajDistance(); MajEglise(); MajApparence(); Outils.MajJournal(); } };
        MajApparence();
    }

    void Ajouter(string id, string titre, FrameworkElement section, bool dansLaPile = true)
    {
        var b = new RadioButton { Style = S("Sommaire"), Content = titre, GroupName = "Reglages" + GetHashCode() };
        b.Click += (_, _) => Aller(id, false);
        sections.Add((id, b, section));
        if (dansLaPile) pile.Children.Add(section);
    }

    /// <summary>Fait défiler jusqu'à une section (« apparence », « son », « maj »…) ; « maj » avec chercher : recherche une mise à jour.</summary>
    public async void Aller(string id, bool chercher)
    {
        var s = sections.FirstOrDefault(x => x.Id == id);
        if (s.Section == null) return;
        s.Bouton.IsChecked = true;
        UpdateLayout();
        try
        {
            var haut = s.Section.TransformToAncestor(pile).Transform(new Point(0, 0)).Y;
            defilement = true;
            defil.ScrollToVerticalOffset(Math.Max(0, haut - 4));
        }
        catch (InvalidOperationException) { }
        await Task.Delay(120);
        defilement = false;
        if (id == "maj" && chercher) await Outils.ChercherMaintenant();
    }

    /// <summary>Le sommaire montre la section en haut de l'écran.</summary>
    void Suivre()
    {
        if (defilement || !IsVisible) return;
        var actuelle = sections[0];
        foreach (var s in sections)
        {
            try
            {
                if (!s.Section.IsVisible) continue;
                if (s.Section.TransformToAncestor(defil).Transform(new Point(0, 0)).Y <= 80) actuelle = s;
            }
            catch (InvalidOperationException) { }
        }
        // tout en bas : la dernière section
        if (defil.VerticalOffset >= defil.ScrollableHeight - 2 && defil.ScrollableHeight > 0) actuelle = sections[^1];
        actuelle.Bouton.IsChecked = true;
    }

    // ------------------------------------------------------------ sections
    static Border Section(string titre, string texte, UIElement contenu)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = titre, Style = S("SousTitre"), Margin = new Thickness(0) });
        p.Children.Add(new TextBlock { Text = texte, Style = S("Muet"), FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 6, 0, 0) });
        p.Children.Add(contenu);
        return new Border { Style = S("Section"), Child = p };
    }

    /// <summary>Un aperçu de thème : une mini-fenêtre dessinée avec les couleurs de ce thème, son nom dessous.</summary>
    Button Apercu(bool fonce)
    {
        Brush C(string cle) => new SolidColorBrush(Palette.De(cle, fonce));
        var mini = new Grid { Height = 128, ClipToBounds = true };
        mini.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        mini.ColumnDefinitions.Add(new ColumnDefinition());
        var rail = new StackPanel { Background = C("Rail") };
        rail.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(4), Background = C("OrVif"), Margin = new Thickness(0, 8, 0, 6) });
        for (int i = 0; i < 3; i++) rail.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(4), Background = C("Nuit2"), Margin = new Thickness(0, 0, 0, 6) });
        mini.Children.Add(rail);
        var corps = new Grid { Background = C("Page"), Margin = new Thickness(0) };
        corps.ColumnDefinitions.Add(new ColumnDefinition());
        corps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        corps.RowDefinitions.Add(new RowDefinition());
        corps.RowDefinitions.Add(new RowDefinition());
        void Bloc(int r, int c, Brush fond, bool bord)
        {
            var b = new Border { Background = fond, CornerRadius = new CornerRadius(5), Margin = new Thickness(4), BorderBrush = bord ? C("Nuit3") : null, BorderThickness = new Thickness(bord ? 1 : 0) };
            SetRow(b, r); SetColumn(b, c);
            corps.Children.Add(b);
        }
        Bloc(0, 0, C("Encre"), true);
        Bloc(0, 1, new RadialGradientBrush(Color.FromRgb(0x26, 0x35, 0x7A), Color.FromRgb(0x0C, 0x12, 0x30)), false);
        Bloc(1, 0, C("Encre"), true);
        Bloc(1, 1, C("OrVif"), false);
        SetColumn(corps, 1);
        mini.Children.Add(corps);
        var nom = new DockPanel { Margin = new Thickness(4, 10, 4, 0) };
        var note = new TextBlock { Text = fonce ? "salle obscure" : "par défaut", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(note, Dock.Right);
        nom.Children.Add(note);
        nom.Children.Add(new TextBlock { Text = fonce ? "Sombre" : "Clair", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var pileB = new StackPanel { Width = 230 };
        pileB.Children.Add(new Border { CornerRadius = new CornerRadius(9), Child = mini, ClipToBounds = true, Background = C("Page") });
        pileB.Children.Add(nom);
        var b = new Button
        {
            Style = S("Btn"), Content = pileB, Padding = new Thickness(10, 10, 10, 12), Margin = new Thickness(0, 0, 16, 0), BorderThickness = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = fonce ? "Thème sombre" : "Thème clair (par défaut)",
        };
        b.Click += (_, _) =>
        {
            App.AppliquerApparence(fonce ? "sombre" : "clair");
            Principale?.Toast(fonce ? "Thème sombre : appliqué tout de suite, à toutes les fenêtres." : "Thème clair (par défaut).");
        };
        return b;
    }

    void MajApparence()
    {
        bool fonce = Palette.Actuelle.Sombre;
        clair.BorderBrush = P(fonce ? "Nuit3" : "OrVif");
        sombre.BorderBrush = P(fonce ? "OrVif" : "Nuit3");
    }

    /// <summary>Les écrans détectés, avec « Projeter ici » ou « Arrêter », puis la fenêtre et MyVirtualCam.</summary>
    public void MajEcrans()
    {
        ecrans.Children.Clear();
        var m = Principale;
        var actuel = App.Donnees.Reglages.Ecran;
        bool ouvert = m?.Ecran != null;
        int k = 0;
        try
        {
            foreach (var e in Natif.Ecrans())
            {
                var ec = e;
                bool ici = ouvert && actuel is { Mode: "plein" } && actuel.X == e.X && actuel.Y == e.Y;
                var bouton = new Button { Style = S(ici ? "BtnRouge" : "BtnPetit"), Content = ici ? "Arrêter" : "Projeter ici", Height = 34 };
                bouton.Click += (_, _) => { if (ici) m?.ArreterProjection(); else m?.OuvrirSur(ec, true); MajEcrans(); };
                ecrans.Children.Add(Ligne(ici, $"Écran {++k}{(e.Nom.Length > 0 ? " · " + e.Nom : "")} · {e.W} × {e.H}" + (e.Principal ? " (principal)" : ""),
                    ici ? "en cours de projection" : e.Principal ? "l'écran de la régie" : "disponible", bouton));
            }
        }
        catch (Exception x) { Journal.Erreur("liste des écrans", x); }
        if (m is { AttenteProjecteur: true } mw && actuel != null)
        {
            // projecteur débranché : Siloé Stream l'attend (la projection se rouvrira toute seule)
            var attendre = new Button { Style = S("BtnPetit"), Content = "Ne plus attendre", Height = 34 };
            attendre.Click += (_, _) => { mw.ArreterProjection(); MajEcrans(); };
            ecrans.Children.Add(Ligne(false, $"{(actuel.Nom.Length > 0 ? actuel.Nom : "Projecteur")} · débranché", "la projection se rouvrira toute seule dès qu'il sera branché", attendre));
        }
        bool fenetre = ouvert && actuel is { Mode: "fenetre" };
        var f = new Button { Style = S("BtnPetit"), Content = fenetre ? "Fermer" : "Ouvrir", Height = 34 };
        f.Click += (_, _) =>
        {
            if (fenetre) m?.ArreterProjection();
            else if (Natif.Ecrans() is { Count: > 0 } l) m?.OuvrirSur(l.FirstOrDefault(x => x.Principal) ?? l[0], false);
            MajEcrans();
        };
        ecrans.Children.Add(Ligne(fenetre, "Fenêtre", "une fenêtre redimensionnable", f));
        ecrans.Children.Add(Ligne(App.CameraVirtuelle != null, "MyVirtualCam", App.CameraVirtuelle != null ? "caméra virtuelle 720p30, toujours disponible" : "indisponible sur cet ordinateur", null));
    }

    /// <summary>
    /// 1.7.0 : la section « À distance » : activer (l'agent démarre avec Windows), l'adresse du relais, et l'état vu par le
    /// relais lui-même (ce PC est-il joignable de l'extérieur ?).
    /// </summary>
    public async void MajDistance()
    {
        int v = ++versionDistance;
        var dossier = App.Donnees.Dossier;
        var r = ReglagesDistance.Lire(dossier);
        distance.Children.Clear();
        // un bouton neuf pour chaque ligne (un élément WPF n'a qu'un parent)
        Button Bascule()
        {
            var b = new Button { Style = S(r.Actif ? "BtnPetit" : "Btn"), Content = r.Actif ? "Couper" : "Activer", Height = 34, Padding = new Thickness(14, 0, 14, 0) };
            b.Click += (_, _) =>
            {
                var n = ReglagesDistance.Lire(dossier);
                n.Actif = !n.Actif;
                try { n.Ecrire(dossier); }
                catch (Exception x) { Principale?.Toast("Réglage impossible à enregistrer : " + x.Message); return; }
                Journal.Info(n.Actif ? $"à distance : activé (relais « {n.Relais} »)" : "à distance : coupé");
                App.MajAgent();
                Principale?.Toast(n.Actif ? "Contrôle à distance activé : ce PC sera joignable par Internet dès que Windows démarre." : "Contrôle à distance coupé.");
                MajDistance();
            };
            return b;
        }
        var etat = Ligne(r.Actif, "Contrôle à distance", r.Actif ? "vérification auprès du relais…" : "coupé : le téléphone ne joint ce PC que sur le Wi-Fi de l'église", Bascule());
        distance.Children.Add(etat);

        // l'adresse du relais (celle du VPS par défaut)
        var adresse = new TextBox { Style = S("Champ"), Text = r.Relais, FontSize = 13.5, MinWidth = 300, Margin = new Thickness(0, 0, 10, 0) };
        var garder = new Button { Style = S("BtnPetit"), Content = "Enregistrer", Height = 34, Padding = new Thickness(12, 0, 12, 0) };
        garder.Click += (_, _) =>
        {
            var n = ReglagesDistance.Lire(dossier);
            n.Relais = ReglagesDistance.Nettoyer(adresse.Text);
            try { n.Ecrire(dossier); }
            catch (Exception x) { Principale?.Toast("Réglage impossible à enregistrer : " + x.Message); return; }
            Journal.Info($"à distance : relais « {n.Relais} »");
            Principale?.Toast($"Relais : {n.Relais}");
            MajDistance();
        };
        var ligneAdresse = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(garder, Dock.Right);
        ligneAdresse.Children.Add(garder);
        ligneAdresse.Children.Add(adresse);
        var blocAdresse = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        blocAdresse.Children.Add(new TextBlock { Text = "Adresse du relais (le VPS Siloé Stream)", FontSize = 12.5, Foreground = P("Brume") });
        blocAdresse.Children.Add(ligneAdresse);
        distance.Children.Add(blocAdresse);

        if (Natif.SessionAutomatique() != true)
            distance.Children.Add(new TextBlock
            {
                Text = "Pour que tout reparte seul après un redémarrage, Windows doit ouvrir la session sans qu'on tape de mot de passe "
                    + "(touche Windows + R › netplwiz › décocher « Les utilisateurs doivent entrer un nom d'utilisateur et un mot de passe »). "
                    + "Sans mot de passe sur ce compte, rien à faire.",
                Style = S("Muet"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
            });
        if (!r.Actif) return;

        // ce que voit le relais : ce PC joignable ?
        var joignable = await ReglagesDistance.PcJoignable(r.Relais, App.Telephone.PcId);
        if (v != versionDistance) return;
        var agent = App.AgentEnMarche();
        string sous = joignable switch
        {
            true => $"actif · joignable de l'extérieur par « {ReglagesDistance.Nettoyer(r.Relais)} »",
            false => agent ? "actif · l'agent se relie au relais… (quelques secondes)" : "actif · l'agent ne tourne pas : fermez puis rouvrez Siloé Stream",
            null => $"actif · relais « {ReglagesDistance.Nettoyer(r.Relais)} » injoignable (VPS éteint, adresse fausse, ou pas d'Internet)",
        };
        int i = distance.Children.IndexOf(etat);
        if (i < 0) return;
        distance.Children.RemoveAt(i);
        distance.Children.Insert(i, Ligne(joignable == true, "Contrôle à distance", sous, Bascule()));
        if (joignable != true && IsVisible)
        {
            await Task.Delay(4000);
            if (v == versionDistance && IsVisible) MajDistance();
        }
    }

    /// <summary>
    /// 1.9.3 : la section « Église et comptes » : le compte Google connecté, l'église du PC, la connexion obligatoire
    /// et, pour l'admin, les membres (ajouter par Gmail, changer le rôle, retirer) ou la liaison du PC à une église.
    /// </summary>
    public async void MajEglise()
    {
        int v = ++versionEglise;
        var dossier = App.Donnees.Dossier;
        eglise.Children.Clear();
        var s = ComptesPc.Session;
        var lien = ComptesPc.Eglise;
        static Button Petit(string texte) => new() { Style = S("BtnPetit"), Content = texte, Height = 34, Padding = new Thickness(12, 0, 12, 0) };

        // le compte connecté sur ce PC
        if (s == null)
        {
            var entrer = Petit("Se connecter avec Google…");
            entrer.Click += async (_, _) =>
            {
                Principale?.Toast("Terminez la connexion dans votre navigateur (siloestream.tech).");
                var err = await ComptesPc.Connecter();
                Principale?.Toast(err ?? $"Connecté : {ComptesPc.Session?.Nom} ({Roles.Nom(ComptesPc.Session?.Role)}).");
                MajEglise();
            };
            eglise.Children.Add(Ligne(false, "Compte Google", "personne n'est connecté sur ce PC", entrer));
        }
        else
        {
            var sortir = Petit("Se déconnecter");
            sortir.Click += async (_, _) => { await ComptesPc.Deconnecter(); MajEglise(); };
            eglise.Children.Add(Ligne(true, s.Nom.Length > 0 ? s.Nom : s.Email, $"{s.Email} · {Roles.Nom(s.Role)} : {Roles.Description(s.Role)}", sortir));
        }

        // l'église de ce PC, et la connexion obligatoire
        if (lien != null)
        {
            eglise.Children.Add(Ligne(true, $"Église « {lien.Nom} »", "ce PC est relié : ses membres se connectent avec leur Gmail", null));
            if (s != null && ComptesPc.EstAdmin)
            {
                var bascule = Petit(lien.ConnexionObligatoire ? "Rendre facultative" : "Rendre obligatoire");
                bascule.Click += (_, _) =>
                {
                    var c = CompteEglise.Lire(dossier);
                    if (c == null) return;
                    c.ConnexionObligatoire = !c.ConnexionObligatoire;
                    try { c.Ecrire(dossier); }
                    catch (Exception x) { Principale?.Toast("Réglage impossible à enregistrer : " + x.Message); return; }
                    Journal.Info(c.ConnexionObligatoire ? "comptes : connexion obligatoire" : "comptes : connexion facultative");
                    Principale?.AppliquerRole();
                    Principale?.Toast(c.ConnexionObligatoire
                        ? "Connexion obligatoire : chacun se connecte au démarrage et ne voit que les pages de son rôle."
                        : "Connexion facultative : sans compte connecté, le logiciel reste entier.");
                    MajEglise();
                };
                eglise.Children.Add(Ligne(lien.ConnexionObligatoire, "Connexion Google obligatoire", lien.ConnexionObligatoire
                    ? "chacun se connecte au démarrage et ne voit que les pages de son rôle"
                    : "facultative : sans compte connecté, le logiciel reste entier", bascule));
            }
        }
        if (s == null || !ComptesPc.EstAdmin) return;

        if (lien == null)
        {
            // relier ce PC : à une église dont ce compte est l'admin, ou à une nouvelle
            ComptesApi.Moi moi;
            try { moi = await ComptesApi.MoiActuel(s.Relais, s.Jeton); }
            catch (Exception x) when (x is ComptesApi.Refus or HttpRequestException or TaskCanceledException)
            {
                if (v == versionEglise) eglise.Children.Add(Note("Églises : " + x.Message));
                return;
            }
            if (v != versionEglise) return;
            foreach (var eg in moi.Eglises.Where(x => x.Role == Roles.Admin))
            {
                var (id, nomEglise) = (eg.Id, eg.Nom);
                var relier = Petit("Relier ce PC");
                relier.Click += async (_, _) =>
                {
                    relier.IsEnabled = false;
                    var err = await ComptesPc.RelierPc(id);
                    Principale?.Toast(err ?? $"Ce PC est relié à l'église « {nomEglise} ».");
                    MajEglise();
                };
                eglise.Children.Add(Ligne(false, $"Église « {nomEglise} »", "vous en êtes l'admin", relier));
            }
            var nom = new TextBox { Style = S("Champ"), FontSize = 13.5, MinWidth = 260, Margin = new Thickness(0, 0, 10, 0) };
            var creer = Petit("Créer mon église");
            creer.Click += async (_, _) =>
            {
                var n = nom.Text.Trim();
                if (n.Length < 2) { Principale?.Toast("Donnez un nom à votre église."); return; }
                creer.IsEnabled = false;
                var err = await ComptesPc.CreerEglise(n);
                Principale?.Toast(err ?? $"Église « {n} » créée : ce PC y est relié, et vous en êtes l'admin.");
                MajEglise();
            };
            eglise.Children.Add(Rangee("Nouvelle église (vous en serez l'admin)", nom, creer));
            return;
        }

        // les membres de l'église (admin)
        List<ComptesApi.Membre> membres;
        try { membres = await ComptesApi.Membres(s.Relais, s.Jeton, lien.Eglise); }
        catch (Exception x) when (x is ComptesApi.Refus or HttpRequestException or TaskCanceledException)
        {
            if (v == versionEglise) eglise.Children.Add(Note("Membres : " + x.Message));
            return;
        }
        if (v != versionEglise) return;
        eglise.Children.Add(new TextBlock { Text = $"Membres ({membres.Count})", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 4) });
        foreach (var m in membres.OrderBy(x => x.Role != Roles.Admin).ThenBy(x => x.Email))
        {
            var (courrielMembre, roleMembre) = (m.Email, m.Role);
            var choix = ChoixRole(roleMembre);
            choix.SelectionChanged += async (_, _) =>
            {
                if (choix.SelectedItem is ComboBoxItem { Tag: string r } && r != roleMembre)
                    await Agir(() => ComptesApi.AjouterMembre(s.Relais, s.Jeton, lien.Eglise, courrielMembre, r), $"{courrielMembre} : {Roles.Nom(r)}.");
            };
            var retirer = Petit("Retirer");
            retirer.Click += async (_, _) =>
            {
                if (!Dialogue.Confirmer(Principale, $"Retirer {courrielMembre} de l'église « {lien.Nom} » ? Ce compte ne pourra plus se connecter.", "Retirer")) return;
                await Agir(() => ComptesApi.RetirerMembre(s.Relais, s.Jeton, lien.Eglise, courrielMembre), $"{courrielMembre} est retiré de l'église.");
            };
            eglise.Children.Add(LigneMembre(m.Nom.Length > 0 ? m.Nom : courrielMembre, m.Nom.Length > 0 ? courrielMembre : "pas encore connecté", choix, retirer));
        }

        // ajouter quelqu'un : son Gmail et son rôle
        var courriel = new TextBox { Style = S("Champ"), FontSize = 13.5, MinWidth = 220, Margin = new Thickness(0, 0, 10, 0) };
        var role = ChoixRole(Roles.Bible);
        role.Margin = new Thickness(0, 0, 10, 0);
        var ajouter = Petit("Ajouter");
        ajouter.Click += async (_, _) =>
        {
            var em = courriel.Text.Trim();
            if (em.Length == 0) { Principale?.Toast("Écrivez son adresse Gmail."); return; }
            if (role.SelectedItem is ComboBoxItem { Tag: string r })
                await Agir(() => ComptesApi.AjouterMembre(s.Relais, s.Jeton, lien.Eglise, em, r), $"{em} est ajouté ({Roles.Nom(r)}) : il se connecte avec ce Gmail.");
        };
        var ligneAjout = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(ajouter, Dock.Right);
        ligneAjout.Children.Add(ajouter);
        DockPanel.SetDock(role, Dock.Right);
        ligneAjout.Children.Add(role);
        ligneAjout.Children.Add(courriel);
        var bloc = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        bloc.Children.Add(new TextBlock { Text = "Ajouter quelqu'un : son adresse Gmail et son rôle", FontSize = 12.5, Foreground = P("Brume") });
        bloc.Children.Add(ligneAjout);
        eglise.Children.Add(bloc);
    }

    async Task Agir(Func<Task> action, string reussi)
    {
        try { await action(); Principale?.Toast(reussi); }
        catch (Exception x) when (x is ComptesApi.Refus or HttpRequestException or TaskCanceledException) { Principale?.Toast(x.Message); }
        await ComptesPc.Rafraichir();
        MajEglise();
    }

    static ComboBox ChoixRole(string actuel)
    {
        var c = new ComboBox { Width = 270, Height = 34, FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var r in Roles.Tous) c.Items.Add(new ComboBoxItem { Content = $"{Roles.Nom(r)} · {Roles.Description(r)}", Tag = r });
        c.SelectedItem = c.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == actuel);
        return c;
    }

    static Border LigneMembre(string titre, string sous, ComboBox role, Button retirer)
    {
        var d = new DockPanel();
        DockPanel.SetDock(retirer, Dock.Right);
        d.Children.Add(retirer);
        role.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(role, Dock.Right);
        d.Children.Add(role);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        t.Children.Add(new TextBlock { Text = sous, FontSize = 12.5, Foreground = P("Brume"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        d.Children.Add(t);
        return new Border { Child = d, BorderBrush = P("Trait"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 9, 0, 9) };
    }

    static StackPanel Rangee(string titre, TextBox champ, Button bouton)
    {
        var ligne = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(bouton, Dock.Right);
        ligne.Children.Add(bouton);
        ligne.Children.Add(champ);
        var bloc = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        bloc.Children.Add(new TextBlock { Text = titre, FontSize = 12.5, Foreground = P("Brume") });
        bloc.Children.Add(ligne);
        return bloc;
    }

    static TextBlock Note(string texte) => new() { Text = texte, Style = S("Muet"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };

    /// <summary>Pour le test automatique : le texte de l'état « À distance ».</summary>
    public string TestDistance => string.Join(" / ", distance.Children.OfType<Border>().Select(b => b.Child).OfType<DockPanel>()
        .SelectMany(d => d.Children.OfType<StackPanel>()).SelectMany(sp => sp.Children.OfType<TextBlock>()).Select(t => t.Text));

    static Border Ligne(bool actif, string titre, string sous, Button? bouton)
    {
        var d = new DockPanel();
        d.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = P(actif ? "Vert" : "Brume2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        if (bouton != null) { DockPanel.SetDock(bouton, Dock.Right); d.Children.Add(bouton); }
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.SemiBold });
        t.Children.Add(new TextBlock { Text = sous, FontSize = 12.5, Foreground = P("Brume"), Margin = new Thickness(0, 2, 0, 0) });
        d.Children.Add(t);
        return new Border { Child = d, BorderBrush = P("Trait"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 11, 0, 11) };
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestSection(string id) => Aller(id, false);
}
