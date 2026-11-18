using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using MyVerse.Audio;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.6.0 : « Le direct sur YouTube », comme la fenêtre d'OBS, en plus simple.
/// Comment : le compte YouTube (conseillé : Siloé Stream crée le direct), la clé de stream de YouTube Studio, ou un autre service.
/// Le direct : titre, description, confidentialité, catégorie, « pour les enfants ? », miniature, latence, DVR, 360°, programmer ;
/// ou un direct déjà prévu sur la chaîne. En direct : la durée, l'état chez YouTube, « Voir sur YouTube », « Arrêter le direct ».
/// 1.6.4 : les directs automatiques (chaque dimanche à 10:00…), dans DirectsAutomatiquesUi.cs.
/// La fenêtre peut rester ouverte pendant le culte (elle ne bloque pas Siloé Stream).
/// </summary>
public sealed partial class DirectWindow : FenetreDialogue
{
    static DirectService D => App.Direct;
    static ReglagesDirect R => App.Donnees.Reglages.Direct;

    readonly StackPanel corps = new();
    // l'état du direct
    readonly Border carteDirect = new();
    readonly TextBlock directTitre = new(), directDetail = new();
    readonly Button voir, studio, arreter;
    // comment
    readonly RadioButton modeCompte, modeCle, modeAutre;
    readonly StackPanel blocCompte = new(), blocCle = new(), blocAutre = new();
    readonly TextBlock compteTexte = new();
    readonly Button connecter, changer;
    readonly StackPanel avance = new() { Visibility = Visibility.Collapsed };
    readonly TextBox googleId, googleSecret;
    readonly PasswordBox cle, cleAutre;
    readonly TextBox serveur;
    // le direct YouTube
    readonly StackPanel blocDirect = new();
    readonly RadioButton nouveau, prevu;
    readonly StackPanel blocNouveau = new(), blocPrevu = new();
    readonly TextBox titre, description;
    readonly ComboBox confidentialite = new(), categorie = new(), latence = new();
    readonly RadioButton enfantsNon, enfantsOui;
    readonly TextBlock miniatureNom = new();
    string miniature = "";
    readonly CheckBox dvr, v360, programmer, retenir;
    readonly DatePicker jour = new();
    readonly TextBox heure;
    readonly StackPanel listePrevus = new();
    List<DirectPrevu> prevus = new();
    DirectPrevu? choisi;
    // image et son
    readonly RadioButton q720, q1080, q2160;
    readonly ChoixSons sons;
    // bas
    readonly TextBlock message = new();
    readonly Button creer, demarrer;
    CancellationTokenSource? connexion;

    public DirectWindow()
    {
        Title = "Le direct sur YouTube";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;
        Background = Pinceau("Page");
        var racine = new DockPanel { Margin = new Thickness(28, 22, 28, 22) };

        // ------------------------------------------------------------ en-tête
        var tete = new StackPanel();
        var ligneTitre = new StackPanel { Orientation = Orientation.Horizontal };
        var icone = SonUi.Icone((Geometry)FindResource("IconeDirect"), Pinceau("Direct"), 26, 2);
        icone.Margin = new Thickness(0, 0, 10, 0);
        ligneTitre.Children.Add(icone);
        ligneTitre.Children.Add(new TextBlock { Text = "Le direct sur YouTube", Style = Style_("Titre2"), VerticalAlignment = VerticalAlignment.Center });
        tete.Children.Add(ligneTitre);
        tete.Children.Add(new TextBlock
        {
            Text = "MyStream (l'écran de droite, la même image que Zoom) et le son partent en direct. Si Internet coupe, Siloé Stream se reconnecte tout seul.",
            Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10), LineHeight = 21,
        });
        // la carte du direct en cours
        var pileDirect = new StackPanel();
        var ligneDirect = new DockPanel();
        arreter = new Button { Style = Style_("BtnRouge"), Content = "Arrêter le direct", Height = 42, Padding = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        arreter.Click += async (_, _) =>
        {
            if (!Dialogue.Confirmer(this, "Arrêter le direct ? Les gens en ligne ne verront plus le culte.", "Arrêter", "Continuer")) return;
            await D.ArreterAsync();
        };
        DockPanel.SetDock(arreter, Dock.Right);
        ligneDirect.Children.Add(arreter);
        var textesDirect = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        directTitre.FontSize = 17; directTitre.FontWeight = FontWeights.Bold; directTitre.Foreground = Pinceau("Blanc");
        directDetail.FontSize = 13; directDetail.Foreground = Pinceau("Brume"); directDetail.TextWrapping = TextWrapping.Wrap; directDetail.Margin = new Thickness(0, 3, 0, 0);
        textesDirect.Children.Add(directTitre);
        textesDirect.Children.Add(directDetail);
        ligneDirect.Children.Add(textesDirect);
        pileDirect.Children.Add(ligneDirect);
        var liens = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        voir = new Button { Style = Style_("BtnPetit"), Content = "Voir sur YouTube", Padding = new Thickness(12, 4, 12, 4) };
        voir.Click += (_, _) => { if (D.Video is { } v) Ouvrir("https://www.youtube.com/watch?v=" + v); };
        studio = new Button { Style = Style_("BtnPetit"), Content = "YouTube Studio", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        studio.Click += (_, _) => Ouvrir(D.Video is { } v ? $"https://studio.youtube.com/video/{v}/livestreaming" : "https://studio.youtube.com/");
        liens.Children.Add(voir);
        liens.Children.Add(studio);
        pileDirect.Children.Add(liens);
        carteDirect.Child = pileDirect;
        carteDirect.CornerRadius = new CornerRadius(14);
        carteDirect.Padding = new Thickness(18, 14, 18, 14);
        carteDirect.BorderThickness = new Thickness(2);
        carteDirect.Margin = new Thickness(0, 4, 0, 6);
        tete.Children.Add(carteDirect);
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        // ------------------------------------------------------------ bas : message, boutons
        var pied = new StackPanel();
        message.FontSize = 13.5; message.TextWrapping = TextWrapping.Wrap; message.Margin = new Thickness(0, 12, 0, 0);
        pied.Children.Add(message);
        var bas = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var fermer = new Button { Style = Style_("Btn"), Content = "Fermer", IsCancel = true, MinWidth = 110, Height = 42 };
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Left);
        bas.Children.Add(fermer);
        demarrer = new Button { Style = Style_("BtnOr"), Content = "Créer et démarrer le direct", IsDefault = true, MinWidth = 200, Height = 42, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
        demarrer.Click += async (_, _) => await Demarrer(true);
        DockPanel.SetDock(demarrer, Dock.Right);
        bas.Children.Add(demarrer);
        creer = new Button { Style = Style_("Btn"), Content = "Créer le direct", MinWidth = 130, Height = 42, Padding = new Thickness(14, 0, 14, 0) };
        creer.Click += async (_, _) => await Demarrer(false);
        DockPanel.SetDock(creer, Dock.Right);
        bas.Children.Add(creer);
        pied.Children.Add(bas);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);

        // ------------------------------------------------------------ comment
        corps.Children.Add(Libelle("Comment", 4));
        modeCompte = Option("Mon compte YouTube (conseillé)", "Siloé Stream crée le direct sur la chaîne : titre, description, miniature… comme OBS.", R.Mode == ReglagesDirect.Compte, "mode");
        modeCle = Option("Clé de stream", "La clé de YouTube Studio › Créer › Passer au direct. Le titre se règle dans YouTube Studio.", R.Mode == ReglagesDirect.Cle, "mode");
        modeAutre = Option("Autre service", "Facebook, Vimeo… : une adresse rtmp:// ou rtmps:// et une clé.", R.Mode == ReglagesDirect.Autre, "mode");
        corps.Children.Add(modeCompte);
        // compte
        compteTexte.FontSize = 14; compteTexte.TextWrapping = TextWrapping.Wrap; compteTexte.VerticalAlignment = VerticalAlignment.Center;
        var ligneCompte = new DockPanel { Margin = new Thickness(30, 0, 0, 12) };
        connecter = new Button { Style = Style_("BtnOr"), Content = "Connecter le compte YouTube", Height = 40, Padding = new Thickness(16, 0, 16, 0) };
        connecter.Click += async (_, _) => await Connecter();
        changer = new Button { Style = Style_("BtnPetit"), Content = "Changer de compte", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        changer.Click += async (_, _) => { await D.Compte.DeconnecterAsync(); R.Chaine = ""; App.Donnees.SauverReglages(); await Connecter(); };
        DockPanel.SetDock(changer, Dock.Right);
        ligneCompte.Children.Add(changer);
        var pileCompte = new StackPanel();
        pileCompte.Children.Add(compteTexte);
        pileCompte.Children.Add(connecter);
        ligneCompte.Children.Add(pileCompte);
        blocCompte.Children.Add(ligneCompte);
        // identifiant Google (avancé) : pour une copie de Siloé Stream compilée sans
        googleId = new TextBox { Style = Style_("Champ"), FontSize = 13, Text = D.Coffre.Lire("google.id") };
        googleSecret = new TextBox { Style = Style_("Champ"), FontSize = 13, Text = D.Coffre.Lire("google.secret") };
        avance.Margin = new Thickness(30, 0, 0, 12);
        avance.Children.Add(Note("Identifiant client Google (programme installé) et son secret : Google Cloud › API YouTube Data v3 › Identifiants. Voir les notes de la 1.6.0.", 0));
        avance.Children.Add(Petit("Identifiant client"));
        avance.Children.Add(googleId);
        avance.Children.Add(Petit("Secret du client"));
        avance.Children.Add(googleSecret);
        var garder = new Button { Style = Style_("BtnPetit"), Content = "Garder", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        garder.Click += (_, _) => { D.Coffre.Ecrire("google.id", googleId.Text.Trim()); D.Coffre.Ecrire("google.secret", googleSecret.Text.Trim()); MajCompte(); Dire("Identifiant Google gardé (chiffré pour cet utilisateur de Windows)."); };
        avance.Children.Add(garder);
        blocCompte.Children.Add(avance);
        corps.Children.Add(blocCompte);
        // clé de stream
        corps.Children.Add(modeCle);
        cle = new PasswordBox { FontSize = 14, Height = 40, Padding = new Thickness(10, 8, 10, 8), Password = D.Coffre.Lire(AdresseDirect.CleStream) };
        blocCle.Margin = new Thickness(30, 0, 0, 12);
        blocCle.Children.Add(Petit("Clé de stream (YouTube Studio › Créer › Passer au direct › Clé du flux)", 0));
        blocCle.Children.Add(cle);
        corps.Children.Add(blocCle);
        // autre service
        corps.Children.Add(modeAutre);
        serveur = new TextBox { Style = Style_("Champ"), FontSize = 14, Text = R.Serveur };
        cleAutre = new PasswordBox { FontSize = 14, Height = 40, Padding = new Thickness(10, 8, 10, 8), Password = D.Coffre.Lire(AdresseDirect.CleAutre) };
        blocAutre.Margin = new Thickness(30, 0, 0, 12);
        blocAutre.Children.Add(Petit("Adresse du serveur (rtmps://…)", 0));
        blocAutre.Children.Add(serveur);
        blocAutre.Children.Add(Petit("Clé"));
        blocAutre.Children.Add(cleAutre);
        corps.Children.Add(blocAutre);
        foreach (var o in new[] { modeCompte, modeCle, modeAutre }) o.Checked += (_, _) => { R.Mode = modeCle.IsChecked == true ? ReglagesDirect.Cle : modeAutre.IsChecked == true ? ReglagesDirect.Autre : ReglagesDirect.Compte; App.Donnees.SauverReglages(); Maj(); };

        // ------------------------------------------------------------ le direct YouTube
        blocDirect.Children.Add(Libelle("Le direct"));
        var choix = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        nouveau = new RadioButton { Content = "Nouveau direct", IsChecked = true, GroupName = "direct", FontSize = 14.5, Margin = new Thickness(0, 0, 22, 0) };
        prevu = new RadioButton { Content = "Direct déjà prévu", GroupName = "direct", FontSize = 14.5 };
        nouveau.Checked += (_, _) => Maj();
        prevu.Checked += async (_, _) => { Maj(); await ChargerPrevus(); };
        choix.Children.Add(nouveau);
        choix.Children.Add(prevu);
        blocDirect.Children.Add(choix);
        var m = R.Modele;
        blocNouveau.Children.Add(Petit("Titre *", 4));
        titre = new TextBox { Style = Style_("Champ"), FontSize = 15, MaxLength = 100, Text = TitreParDefaut(m) };
        blocNouveau.Children.Add(titre);
        blocNouveau.Children.Add(Petit("Description"));
        description = new TextBox { Style = Style_("Zone"), FontSize = 14, Height = 90, Text = m.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 5000 };
        blocNouveau.Children.Add(description);
        // confidentialité et catégorie côte à côte
        var deux = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        deux.ColumnDefinitions.Add(new ColumnDefinition());
        deux.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        deux.ColumnDefinitions.Add(new ColumnDefinition());
        var gauche = new StackPanel();
        gauche.Children.Add(Petit("Confidentialité"));
        foreach (var (v, nom) in new[] { ("public", "Public"), ("unlisted", "Non répertorié (avec le lien)"), ("private", "Privé") })
            confidentialite.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = m.Confidentialite == v });
        confidentialite.MinHeight = 40;
        gauche.Children.Add(confidentialite);
        var droite = new StackPanel();
        droite.Children.Add(Petit("Catégorie"));
        foreach (var (v, nom) in ModeleDirect.Categories) categorie.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = m.Categorie == v });
        if (categorie.SelectedIndex < 0) categorie.SelectedIndex = 0;
        categorie.MinHeight = 40;
        droite.Children.Add(categorie);
        Grid.SetColumn(droite, 2);
        deux.Children.Add(gauche);
        deux.Children.Add(droite);
        blocNouveau.Children.Add(deux);
        blocNouveau.Children.Add(Petit("Cette vidéo est-elle conçue pour les enfants ? *"));
        var enfants = new StackPanel { Orientation = Orientation.Horizontal };
        enfantsNon = new RadioButton { Content = "Non, elle n'est pas conçue pour les enfants", GroupName = "enfants", IsChecked = m.Enfants == false, FontSize = 14, Margin = new Thickness(0, 0, 18, 0) };
        enfantsOui = new RadioButton { Content = "Oui", GroupName = "enfants", IsChecked = m.Enfants == true, FontSize = 14 };
        enfants.Children.Add(enfantsNon);
        enfants.Children.Add(enfantsOui);
        blocNouveau.Children.Add(enfants);
        blocNouveau.Children.Add(Petit("Miniature"));
        var ligneMini = new StackPanel { Orientation = Orientation.Horizontal };
        var choisirMini = new Button { Style = Style_("BtnPetit"), Content = "Choisir une image…", Padding = new Thickness(12, 4, 12, 4) };
        choisirMini.Click += (_, _) => ChoisirMiniature();
        var retirerMini = new Button { Style = Style_("BtnPetit"), Content = "×", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Sans miniature (celle de YouTube)" };
        retirerMini.Click += (_, _) => { miniature = ""; MajMiniature(); };
        miniatureNom.FontSize = 13; miniatureNom.Foreground = Pinceau("Brume"); miniatureNom.VerticalAlignment = VerticalAlignment.Center; miniatureNom.Margin = new Thickness(10, 0, 0, 0);
        ligneMini.Children.Add(choisirMini);
        ligneMini.Children.Add(retirerMini);
        ligneMini.Children.Add(miniatureNom);
        blocNouveau.Children.Add(ligneMini);
        miniature = File.Exists(m.Miniature) ? m.Miniature : "";
        MajMiniature();
        // plus d'options
        var (plus, options) = Tiroir("Plus d'options : latence, DVR, 360°, programmer", "IconeHorloge");
        plus.Margin = new Thickness(0, 14, 0, 0);
        options.Children.Add(Petit("Latence", 0));
        foreach (var (v, nom) in new[] { ("normal", "Normale (meilleure qualité)"), ("low", "Faible"), ("ultraLow", "Très faible (pour parler avec les gens)") })
            latence.Items.Add(new ComboBoxItem { Content = nom, Tag = v, IsSelected = m.Latence == v });
        latence.MinHeight = 40;
        options.Children.Add(latence);
        dvr = new CheckBox { Content = "DVR : revenir en arrière pendant le direct", IsChecked = m.Dvr, Margin = new Thickness(0, 10, 0, 0), FontSize = 14 };
        v360 = new CheckBox { Content = "Vidéo 360°", IsChecked = m.Video360, Margin = new Thickness(0, 8, 0, 0), FontSize = 14 };
        programmer = new CheckBox { Content = "Programmer pour plus tard", Margin = new Thickness(0, 8, 0, 0), FontSize = 14 };
        options.Children.Add(dvr);
        options.Children.Add(v360);
        options.Children.Add(programmer);
        var quand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 6, 0, 0), IsEnabled = false };
        jour.SelectedDate = DateTime.Today.AddDays(DateTime.Today.DayOfWeek == DayOfWeek.Sunday ? 7 : 7 - (int)DateTime.Today.DayOfWeek);
        jour.Width = 160; jour.FontSize = 14;
        heure = new TextBox { Style = Style_("Champ"), Text = "10:30", Width = 80, FontSize = 14, Margin = new Thickness(10, 0, 0, 0) };
        quand.Children.Add(jour);
        quand.Children.Add(heure);
        options.Children.Add(quand);
        programmer.Checked += (_, _) => { quand.IsEnabled = true; Maj(); };
        programmer.Unchecked += (_, _) => { quand.IsEnabled = false; Maj(); };
        blocNouveau.Children.Add(plus);
        blocNouveau.Children.Add(options);
        retenir = new CheckBox { Content = "Retenir ces réglages pour le prochain direct", IsChecked = R.Retenir, Margin = new Thickness(0, 12, 0, 0), FontSize = 14 };
        blocNouveau.Children.Add(retenir);
        blocDirect.Children.Add(blocNouveau);
        // directs déjà prévus
        var lignePrevus = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var actualiser = new Button { Style = Style_("BtnPetit"), Content = "Actualiser", Padding = new Thickness(12, 4, 12, 4) };
        actualiser.Click += async (_, _) => await ChargerPrevus();
        DockPanel.SetDock(actualiser, Dock.Right);
        lignePrevus.Children.Add(actualiser);
        lignePrevus.Children.Add(new TextBlock { Text = "Les directs à venir de la chaîne (créés ici ou dans YouTube Studio).", FontSize = 13, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        blocPrevu.Children.Add(lignePrevus);
        blocPrevu.Children.Add(listePrevus);
        blocDirect.Children.Add(blocPrevu);
        corps.Children.Add(blocDirect);

        // ------------------------------------------------------------ 1.6.4 : les directs automatiques (chaque dimanche à 10:00…)
        corps.Children.Add(ConstruireAutomatiques());

        // ------------------------------------------------------------ image et son
        var (image, pileImage) = Tiroir("Image et son du direct", "IconeEcran");
        image.Margin = new Thickness(0, 18, 0, 0);
        pileImage.Children.Add(Note("L'image : MyStream, à droite dans Siloé Stream (sa source principale, ou ce que montre l'église après « Envoyer dans le stream »). La même que Zoom.", 0));
        pileImage.Children.Add(Petit("Qualité"));
        q720 = new RadioButton { Content = "720p, 4,5 Mb/s (conseillé)", GroupName = "qualite", IsChecked = R.Qualite is not ("1080p" or "2160p"), FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };
        q1080 = new RadioButton { Content = "1080p, 6 Mb/s (Internet rapide : 10 Mb/s d'envoi au moins)", GroupName = "qualite", IsChecked = R.Qualite == "1080p", FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };
        q2160 = new RadioButton { Content = "4K (2160p), 30 Mb/s (Internet très rapide : 40 Mb/s d'envoi ; carte graphique ; latence normale)", GroupName = "qualite", IsChecked = R.Qualite == "2160p", FontSize = 14 };
        pileImage.Children.Add(q720);
        pileImage.Children.Add(q1080);
        pileImage.Children.Add(q2160);
        pileImage.Children.Add(Note("Pour une vraie image 4K, la source principale de MyStream doit être en 4K (caméra par Cam Link 4K réglée en 2160p, ou MyCam 4K).", 6));
        pileImage.Children.Add(Petit("Ce qu'on entend"));
        sons = new ChoixSons(App.Audio.Config.Direct.ChoixSons);
        pileImage.Children.Add(sons);
        corps.Children.Add(image);
        corps.Children.Add(pileImage);

        racine.Children.Add(new ScrollViewer { Content = corps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) });
        Content = racine;

        D.Change += Maj;
        // la durée du direct avance chaque seconde
        var horloge = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        horloge.Tick += (_, _) => { if (D.EnDirect) MajCarte(); };
        horloge.Start();
        Closed += (_, _) => { horloge.Stop(); D.Change -= Maj; connexion?.Cancel(); Garder(); };
        Maj();
        MajCompte();
        // 2.4.2 : l'identifiant Google vient du site (rien à retaper après une réinstallation)
        Loaded += async (_, _) => { await D.ChargerIdentifiantsSiteAsync(); MajCompte(); };
    }

    // ------------------------------------------------------------ petits morceaux
    static TextBlock Libelle(string texte, double haut = 20) => new Capitales { Libelle = texte, Margin = new Thickness(0, haut, 0, 8) };
    static TextBlock Petit(string texte, double haut = 12) => new() { Text = texte, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MainWindow.Pinceau("Encre2"), Margin = new Thickness(0, haut, 0, 5), TextWrapping = TextWrapping.Wrap };
    static TextBlock Note(string texte, double haut = 6) => new() { Text = texte, FontSize = 12.5, Foreground = MainWindow.Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, haut, 0, 0), LineHeight = 18 };
    /// <summary>Une ligne qui s'ouvre et se referme (comme les outils de la colonne de droite).</summary>
    static (ToggleButton Tete, StackPanel Corps) Tiroir(string titre, string icone)
    {
        var tete = new ToggleButton { Style = MainWindow.Style_("TiroirTete"), Tag = Application.Current.FindResource(icone), Content = new TextBlock { Text = titre, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };
        var corps = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(4, 6, 0, 12) };
        tete.Checked += (_, _) => corps.Visibility = Visibility.Visible;
        tete.Unchecked += (_, _) => corps.Visibility = Visibility.Collapsed;
        return (tete, corps);
    }

    static RadioButton Option(string titre, string detail, bool coche, string groupe)
    {
        var p = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        p.Children.Add(new TextBlock { Text = titre, FontSize = 15, FontWeight = FontWeights.SemiBold });
        p.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, Foreground = MainWindow.Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return new RadioButton { Content = p, IsChecked = coche, GroupName = groupe, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Top };
    }

    static void Ouvrir(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Journal.Erreur("ouvrir " + url, e); }
    }

    /// <summary>Le titre proposé : le nom du culte du programme, sinon le dernier titre, sinon « Culte du dimanche 15 novembre ».</summary>
    static string TitreParDefaut(ModeleDirect m)
    {
        var nom = App.Regie.Agenda.Nom?.Trim() ?? "";
        if (nom.Length > 0 && nom.Length <= 100) return nom;
        if (m.Titre.Trim().Length > 0) return m.Titre.Trim();
        return "Culte du " + DateTime.Now.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("fr-CA"));
    }

    void Dire(string texte, bool erreur = false)
    {
        message.Text = texte;
        message.Foreground = Pinceau(erreur ? "Danger" : "Brume");
    }

    void MajMiniature() => miniatureNom.Text = miniature.Length > 0 ? Path.GetFileName(miniature) : "Aucune : YouTube en prend une dans le direct.";

    void ChoisirMiniature()
    {
        var d = new OpenFileDialog { Title = "Miniature du direct (JPEG ou PNG, 2 Mo au plus, 1280 × 720 conseillé)", Filter = "Images|*.jpg;*.jpeg;*.png" };
        if (d.ShowDialog(this) != true) return;
        if (new FileInfo(d.FileName).Length > 2 * 1024 * 1024) { Dire("Cette image fait plus de 2 Mo : YouTube la refuserait. Choisissez-en une plus légère.", true); return; }
        miniature = d.FileName;
        MajMiniature();
    }

    /// <summary>Garde la qualité, les sons, le serveur et les clés.</summary>
    void Garder()
    {
        if (!D.Actif) R.Qualite = q2160.IsChecked == true ? "2160p" : q1080.IsChecked == true ? "1080p" : "720p";
        R.Serveur = serveur.Text.Trim();
        R.Retenir = retenir.IsChecked == true;
        App.Donnees.SauverReglages();
        D.Coffre.Ecrire(AdresseDirect.CleStream, cle.Password.Trim());
        D.Coffre.Ecrire(AdresseDirect.CleAutre, cleAutre.Password.Trim());
        var choix = sons.Choix;
        var avant = App.Audio.Config.Direct.ChoixSons;
        bool change = (avant == null) != (choix == null) || (avant != null && choix != null && !avant.ToHashSet().SetEquals(choix));
        if (change) { App.Audio.Config.Direct.ChoixSons = choix; App.Audio.Apply(); }
    }

    // ------------------------------------------------------------ l'état
    void MajCompte()
    {
        bool dispo = D.Compte.Disponible, connecte = D.Compte.Connecte;
        compteTexte.Text = connecte ? $"Connecté : {(R.Chaine.Length > 0 ? R.Chaine : "la chaîne YouTube")}" : dispo ? "" : "Pour connecter un compte, Siloé Stream a besoin d'un identifiant Google (voir les notes de la 1.6.0). En attendant, la clé de stream marche tout de suite.";
        compteTexte.FontWeight = connecte ? FontWeights.SemiBold : FontWeights.Normal;
        compteTexte.Foreground = Pinceau(connecte ? "Vert" : "Brume");
        compteTexte.Visibility = compteTexte.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        compteTexte.Margin = new Thickness(0, 0, 0, connecte ? 0 : 8);
        connecter.Visibility = connecte ? Visibility.Collapsed : Visibility.Visible;
        connecter.IsEnabled = dispo;
        changer.Visibility = connecte ? Visibility.Visible : Visibility.Collapsed;
        avance.Visibility = !dispo || D.Coffre.Lire("google.id").Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Maj();
    }

    /// <summary>La carte du direct : l'état, la durée, YouTube, la santé de l'image, l'encodeur, les reconnexions.</summary>
    void MajCarte()
    {
        bool actif = D.Actif;
        carteDirect.Visibility = actif || D.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var duree = D.Duree;
        string temps = $"{(int)duree.TotalHours}:{duree.Minutes:00}:{duree.Seconds:00}";
        directTitre.Text = D.Etat switch
        {
            EtatDirect.EnDirect => "● En direct · " + temps,
            EtatDirect.Reconnexion => "Reconnexion… · " + temps,
            EtatDirect.Preparation => "Démarrage du direct…",
            EtatDirect.Arret => "Fin du direct…",
            EtatDirect.Erreur => "Le direct n'a pas démarré",
            _ => "Pas de direct en ce moment",
        };
        directTitre.Foreground = Pinceau(D.Etat switch { EtatDirect.EnDirect => "Direct", EtatDirect.Reconnexion => "OrVif", EtatDirect.Erreur => "Danger", _ => "Blanc" });
        var details = new List<string> { D.Message };
        if (D.EnDirect && D.Video != null)
        {
            if (D.StatutYouTube.Length > 0) details.Add("YouTube : " + D.StatutYouTube switch { "live" => "en direct", "ready" or "created" => "en attente de l'image", "testing" or "testStarting" or "liveStarting" => "démarrage", "complete" => "terminé", _ => D.StatutYouTube });
            if (D.Sante.Length > 0) details.Add("image reçue : " + D.Sante switch { "good" => "bonne", "ok" => "correcte", "bad" => "mauvaise (Internet trop lent ?)", "noData" => "rien reçu", _ => D.Sante });
        }
        if (D.EnDirect && D.Encodeur.Length > 0) details.Add("encodeur : " + D.Encodeur switch { "h264_nvenc" => "carte graphique NVIDIA", "h264_qsv" => "Intel Quick Sync", "h264_amf" => "carte graphique AMD", _ => "processeur (x264)" });
        if (D.Reconnexions > 0) details.Add($"{D.Reconnexions} reconnexion{(D.Reconnexions > 1 ? "s" : "")}");
        directDetail.Text = string.Join(" · ", details.Where(x => x.Length > 0));
        carteDirect.Background = Pinceau(D.EnDirect ? "RougeLeger" : "Nuit2");
        carteDirect.BorderBrush = Pinceau(D.Etat == EtatDirect.EnDirect ? "Direct" : D.Etat == EtatDirect.Reconnexion ? "Ensuite" : "Nuit3");
        arreter.Visibility = actif ? Visibility.Visible : Visibility.Collapsed;
        arreter.IsEnabled = D.Etat is EtatDirect.EnDirect or EtatDirect.Reconnexion;
        voir.Visibility = studio.Visibility = D.Video != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Met la fenêtre à jour : ce qui se voit selon le mode et l'état du direct.</summary>
    void Maj()
    {
        bool compte = modeCompte.IsChecked == true, cleMode = modeCle.IsChecked == true, autre = modeAutre.IsChecked == true;
        bool connecte = D.Compte.Connecte;
        blocCompte.Visibility = compte ? Visibility.Visible : Visibility.Collapsed;
        blocCle.Visibility = cleMode ? Visibility.Visible : Visibility.Collapsed;
        blocAutre.Visibility = autre ? Visibility.Visible : Visibility.Collapsed;
        blocDirect.Visibility = compte && connecte ? Visibility.Visible : Visibility.Collapsed;
        blocNouveau.Visibility = nouveau.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        blocPrevu.Visibility = prevu.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        bool actif = D.Actif;
        MajCarte();
        RemplirAutomatiques();
        // les boutons du bas
        corps.IsEnabled = !actif;
        bool plusTard = programmer.IsChecked == true;
        creer.Visibility = compte && connecte && nouveau.IsChecked == true && !actif && !plusTard ? Visibility.Visible : Visibility.Collapsed;
        demarrer.Visibility = actif ? Visibility.Collapsed : Visibility.Visible;
        demarrer.Content = !compte ? "Démarrer le direct" : !connecte ? "Connecter le compte YouTube" : prevu.IsChecked == true ? "Démarrer ce direct" : plusTard ? "Programmer le direct" : "Créer et démarrer le direct";
        demarrer.IsEnabled = !compte || D.Compte.Disponible;
    }

    // ------------------------------------------------------------ actions
    async Task Connecter()
    {
        if (!D.Compte.Disponible) { MajCompte(); return; }
        connexion?.Cancel();
        connexion = new CancellationTokenSource();
        connecter.IsEnabled = false;
        Dire("Une page de Google s'ouvre dans le navigateur : choisissez le compte de la chaîne de l'église, puis « Autoriser ». (Si Google dit que l'application n'est pas validée : « Paramètres avancés », puis « Accéder à Siloé Stream ».)");
        try
        {
            var chaine = await D.Compte.ConnecterAsync(Ouvrir, connexion.Token);
            R.Chaine = chaine.Titre;
            App.Donnees.SauverReglages();
            Dire($"Connecté à « {chaine.Titre} ». Remplissez le direct, puis « Créer et démarrer le direct ».");
            Activate();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is ErreurYouTube or HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            Dire(e is ErreurYouTube ? e.Message : "Connexion impossible : " + e.Message, true);
            Journal.Erreur("YouTube : connexion du compte", e);
        }
        finally { connecter.IsEnabled = true; MajCompte(); }
    }

    async Task ChargerPrevus()
    {
        listePrevus.Children.Clear();
        choisi = null;
        listePrevus.Children.Add(Note("Lecture des directs de la chaîne…", 0));
        try { prevus = await D.Compte.DirectsAsync(); }
        catch (Exception e) when (e is ErreurYouTube or HttpRequestException or TaskCanceledException)
        {
            listePrevus.Children.Clear();
            Dire(e is ErreurYouTube ? e.Message : "YouTube ne répond pas : vérifiez la connexion Internet.", true);
            return;
        }
        listePrevus.Children.Clear();
        if (prevus.Count == 0) { listePrevus.Children.Add(Note("Aucun direct prévu sur la chaîne. Créez-en un avec « Nouveau direct ».", 0)); return; }
        foreach (var p in prevus)
        {
            var quand = p.Debut is { } d ? d.LocalDateTime.ToString("dddd d MMMM 'à' HH:mm", CultureInfo.GetCultureInfo("fr-CA")) : "sans date";
            var conf = p.Confidentialite switch { "public" => "public", "unlisted" => "non répertorié", "private" => "privé", _ => p.Confidentialite };
            var r = Option(p.Titre.Length > 0 ? p.Titre : "(sans titre)", (p.Statut == "live" ? "En direct maintenant · " : "") + quand + " · " + conf, false, "prevus");
            var cible = p;
            r.Checked += (_, _) => { choisi = cible; Maj(); };
            listePrevus.Children.Add(r);
        }
    }

    /// <summary>Démarrer (ou seulement créer) le direct, selon le mode.</summary>
    async Task Demarrer(bool maintenant)
    {
        Garder();
        Dire("");
        if (modeCompte.IsChecked != true)
        {
            await D.DemarrerAvecCleAsync();
            if (D.Etat == EtatDirect.Erreur) Dire(D.Message, true);
            return;
        }
        if (!D.Compte.Connecte) { await Connecter(); return; }
        if (prevu.IsChecked == true)
        {
            if (choisi == null) { Dire("Choisissez un direct dans la liste.", true); return; }
            await D.DemarrerPrevuAsync(choisi);
            if (D.Etat == EtatDirect.Erreur) Dire(D.Message, true);
            return;
        }
        var t = titre.Text.Trim();
        if (t.Length == 0) { Dire("Donnez un titre au direct.", true); titre.Focus(); return; }
        if (t.Contains('<') || t.Contains('>')) { Dire("Le titre ne peut pas contenir < ni >.", true); titre.Focus(); return; }
        if (enfantsNon.IsChecked != true && enfantsOui.IsChecked != true) { Dire("Répondez à « conçue pour les enfants ? » : YouTube l'exige.", true); return; }
        DateTimeOffset? plusTard = null;
        if (programmer.IsChecked == true)
        {
            if (jour.SelectedDate is not { } j || !TimeSpan.TryParse(heure.Text.Trim().Replace('h', ':'), CultureInfo.InvariantCulture, out var h)) { Dire("Donnez le jour et l'heure du direct (par exemple 10:30).", true); return; }
            var quand = new DateTimeOffset(j.Date + h);
            if (quand <= DateTimeOffset.Now) { Dire("L'heure du direct programmé doit être dans le futur.", true); return; }
            plusTard = quand;
        }
        else if (!maintenant) plusTard = DateTimeOffset.Now.AddMinutes(1);
        var m = new ModeleDirect
        {
            Titre = t, Description = description.Text, Confidentialite = (string)((ComboBoxItem)confidentialite.SelectedItem).Tag,
            Categorie = (string)((ComboBoxItem)categorie.SelectedItem).Tag, Enfants = enfantsOui.IsChecked == true,
            Latence = (string)((ComboBoxItem)latence.SelectedItem).Tag, Dvr = dvr.IsChecked == true, Video360 = v360.IsChecked == true, Miniature = miniature,
        };
        await D.CreerAsync(m, plusTard);
        Dire(D.Etat == EtatDirect.Erreur ? D.Message : D.Etat == EtatDirect.Arrete ? D.Message : "", D.Etat == EtatDirect.Erreur);
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>« Autre service » vers une adresse (serveur RTMP local du test), puis Démarrer.</summary>
    public async Task TestDemarrerAutre(string adresse, string cleTest)
    {
        modeAutre.IsChecked = true;
        serveur.Text = adresse;
        cleAutre.Password = cleTest;
        await Demarrer(true);
    }
    public string TestMessage => message.Text;
    public string TestTitre => titre.Text;
    public string TestBouton => (string)demarrer.Content;
    public void TestModeCompte() => modeCompte.IsChecked = true;
}
