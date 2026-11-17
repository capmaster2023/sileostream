using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MyVerse.Audio;
using MyVerse.Montages;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui.Edition;

/// <summary>Le suivi d'une publication ou d'un export : état, rendu, envoi, débit, coupures, avertissements, et ce qu'on peut faire.</summary>
public sealed class CartePublication : Border
{
    static Brush P(string c) => MainWindow.Pinceau(c);
    static Style S(string c) => MainWindow.Style_(c);
    readonly Publication p;
    readonly TextBlock titre = new() { FontSize = 14.5, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock statut = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    readonly TextBlock description = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    readonly ProgressBar barreRendu = new() { Height = 6, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock texteRendu = new() { FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
    readonly ProgressBar barreEnvoi = new() { Height = 6, Maximum = 1, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock texteEnvoi = new() { FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
    readonly TextBlock reseau = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel avertissements = new();
    readonly WrapPanel boutons = new() { Margin = new Thickness(0, 8, 0, 0) };
    string signature = "";
    bool prevu;
    readonly DateTime ouverture = DateTime.Now;

    public CartePublication(Publication p)
    {
        this.p = p;
        Background = P("Encre");
        BorderBrush = P("Nuit3");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(14, 12, 14, 12);
        Margin = new Thickness(0, 0, 0, 10);
        titre.Foreground = P("Blanc");
        description.Foreground = texteRendu.Foreground = texteEnvoi.Foreground = P("Brume");
        reseau.Foreground = P("Or");
        var pile = new StackPanel();
        pile.Children.Add(titre);
        pile.Children.Add(statut);
        pile.Children.Add(description);
        pile.Children.Add(barreRendu);
        pile.Children.Add(texteRendu);
        pile.Children.Add(barreEnvoi);
        pile.Children.Add(texteEnvoi);
        pile.Children.Add(reseau);
        pile.Children.Add(avertissements);
        pile.Children.Add(boutons);
        Child = pile;
        Loaded += (_, _) => { p.Change -= Planifier; p.Change += Planifier; Maj(); };
        Unloaded += (_, _) => p.Change -= Planifier;
        Maj();
    }

    void Planifier()
    {
        if (prevu) return;
        prevu = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { prevu = false; Maj(); }));
    }

    static string Duree(double s) => ProjetMontage.Temps(s, false);

    void Maj()
    {
        titre.Text = (p.Type == Publication.YouTube ? "YouTube · " : "Fichier · ") + (p.Titre.Length > 0 ? p.Titre : "Montage");
        statut.Text = (p.Statut switch
        {
            "preparation" => "Préparation",
            "rendu" => "Rendu en cours",
            "envoi" => p.Type == Publication.YouTube ? "Envoi sur YouTube" : "En cours",
            "termine" => "Terminé",
            "erreur" => "Erreur",
            "annule" => "Annulé",
            _ => "Interrompu",
        }) + (p.Message.Length > 0 ? " · " + p.Message : "");
        statut.Foreground = P(p.Statut switch { "termine" => "Vert", "erreur" => "Danger", "annule" or "interrompu" => "Or", _ => "Blanc" });
        description.Text = p.Description;
        description.Visibility = p.Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool enCours = p.EnCours;
        barreRendu.Value = p.ProgressionRendu;
        barreRendu.Visibility = texteRendu.Visibility = enCours || p.ProgressionRendu > 0 ? Visibility.Visible : Visibility.Collapsed;
        double ecoule = ((p.Fin ?? DateTime.Now) - p.Debut).TotalSeconds;
        string reste = "";
        if (enCours && p.ProgressionRendu > 0.01 && p.ProgressionRendu < 1 && ecoule > 5)
            reste = " · reste environ " + Duree(ecoule / p.ProgressionRendu - ecoule);
        double vitesse = ecoule > 1 && p.DureeMontage > 0 ? p.ProgressionRendu * p.DureeMontage / ecoule : 0;
        texteRendu.Text = $"{(p.Description.StartsWith("Copie") ? "Copie" : "Rendu")} {p.ProgressionRendu * 100:0} %"
            + (p.DureeMontage > 0 ? $" · {Duree(p.ProgressionRendu * p.DureeMontage)} / {Duree(p.DureeMontage)}" : "")
            + (enCours && vitesse > 0 ? $" · ×{vitesse:0.0}" : "") + reste;

        bool youtube = p.Type == Publication.YouTube;
        barreEnvoi.Visibility = texteEnvoi.Visibility = youtube && (enCours || p.OctetsEnvoyes > 0) ? Visibility.Visible : Visibility.Collapsed;
        if (youtube)
        {
            long total = p.Copie.Length > 0 && p.CopieComplete && File.Exists(p.Copie) ? new FileInfo(p.Copie).Length : 0;
            barreEnvoi.IsIndeterminate = enCours && p.OctetsEnvoyes == 0 && p.Statut == "envoi";
            barreEnvoi.Value = total > 0 ? Math.Min(1, (double)p.OctetsEnvoyes / total) : p.Statut == "termine" ? 1 : p.ProgressionRendu;
            texteEnvoi.Text = $"Envoyé : {Publications.Go(p.OctetsEnvoyes)}" + (total > 0 ? $" sur {Publications.Go(total)}" : "")
                + (enCours && p.Debit > 0 ? $" · {p.Debit / (1 << 20):0.0} Mo/s" : "");
        }
        else if (p.OctetsRendus > 0) texteRendu.Text += " · " + Publications.Go(p.OctetsRendus);
        reseau.Text = p.Reseau;
        reseau.Visibility = p.Reseau.Length > 0 && enCours ? Visibility.Visible : Visibility.Collapsed;

        List<string> av;
        lock (p.Avertissements) av = p.Avertissements.ToList();
        // les boutons et les avertissements ne sont refaits que quand l'état change
        string sig = $"{p.Statut}|{p.VideoId}|{p.Copie}|{p.CopieComplete}|{av.Count}|{p.PeutReprendre}";
        if (sig == signature) return;
        signature = sig;
        avertissements.Children.Clear();
        foreach (var a in av)
            avertissements.Children.Add(new TextBlock { Text = "⚠ " + a, FontSize = 12, Foreground = P("Or"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        boutons.Children.Clear();
        Button B(string texte, Action clic, string style = "BtnPetit")
        {
            var b = new Button { Style = S(style), Content = texte, Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 6), FontSize = 12.5 };
            b.Click += (_, _) => clic();
            boutons.Children.Add(b);
            return b;
        }
        if (enCours)
            B("Arrêter", () =>
            {
                if (Dialogue.Confirmer(Window.GetWindow(this), youtube ? "Arrêter l'envoi ? Rien ne sera publié sur YouTube." : "Arrêter l'export ?", "Arrêter", "Continuer")) p.Annuler();
            });
        if (p.VideoId.Length > 0)
        {
            B("Voir sur YouTube", () => SonUi.Ouvrir(p.Lien), "BtnPetitOr");
            B("YouTube Studio", () => SonUi.Ouvrir(p.LienStudio));
            B("Copier le lien", () => { try { Clipboard.SetText(p.Lien); SonUi.Toast("Lien copié : " + p.Lien); } catch (Exception) { } });
        }
        if (p.PeutReprendre)
            B("Reprendre l'envoi", () =>
            {
                if (!App.Direct.Compte.Connecte) { SonUi.Toast("Connectez d'abord le compte YouTube (bouton « Publier sur YouTube »)."); return; }
                Publications.Reprendre(p, App.Direct.Compte);
            }, "BtnPetitOr");
        if (!enCours && p.Type == Publication.Export && p.Statut == "termine" && File.Exists(p.Fichier))
        {
            B("Montrer le fichier", () => SonUi.MontrerFichier(p.Fichier), "BtnPetitOr");
            B("Lire", () => SonUi.Ouvrir(p.Fichier));
        }
        if (!enCours && p.Type == Publication.Export && p.Statut != "termine" && File.Exists(p.Fichier))
            B("Supprimer le fichier incomplet", () =>
            {
                if (Dialogue.Confirmer(Window.GetWindow(this), $"Supprimer le fichier incomplet « {Path.GetFileName(p.Fichier)} » ?", "Supprimer"))
                    SonUi.Toast(Publications.SupprimerFichierIncomplet(p) ? "Fichier incomplet supprimé." : "Impossible de supprimer le fichier.");
                signature = ""; Maj();
            });
        if (!enCours && p.Copie.Length > 0 && File.Exists(p.Copie))
        {
            long taille = new FileInfo(p.Copie).Length;
            B("Montrer la copie", () => SonUi.MontrerFichier(p.Copie));
            B($"Supprimer la copie ({Publications.Go(taille)})", () =>
            {
                string q = p.VideoId.Length > 0
                    ? $"Supprimer la copie sur le PC « {Path.GetFileName(p.Copie)} » ({Publications.Go(taille)}) ? La vidéo reste sur YouTube ; les enregistrements d'origine ne sont pas touchés."
                    : $"Supprimer la copie « {Path.GetFileName(p.Copie)} » ({Publications.Go(taille)}) ? L'envoi ne pourra plus reprendre sans refaire le rendu. Les enregistrements d'origine ne sont pas touchés.";
                if (Dialogue.Confirmer(Window.GetWindow(this), q, "Supprimer"))
                    SonUi.Toast(Publications.SupprimerCopie(p) ? "Copie supprimée." : "Impossible de supprimer la copie.");
            });
        }
        if (!enCours) B("Retirer de la liste", () => Publications.Oublier(p));
    }
}

/// <summary>Les derniers réglages de publication (description, tags, visibilité…), retenus pour la fois suivante.</summary>
public sealed class ReglagesPublication
{
    public string Description { get; set; } = "";
    public string Tags { get; set; } = "culte, église, prédication";
    public string Categorie { get; set; } = "29";
    public string Confidentialite { get; set; } = "public";
    public bool Enfants { get; set; }
    public string Playlist { get; set; } = "";
    public string Miniature { get; set; } = "";
    public bool Copie { get; set; }
    public string DossierCopie { get; set; } = "";

    static string Fichier => Path.Combine(App.Donnees.Dossier, "montage", "publication.json");
    public static ReglagesPublication Lire()
    {
        try { if (File.Exists(Fichier)) return JsonSerializer.Deserialize<ReglagesPublication>(File.ReadAllText(Fichier)) ?? new(); }
        catch (Exception e) { Journal.Erreur("réglages de publication", e); }
        return new();
    }
    public void Sauver()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(Fichier)!); File.WriteAllText(Fichier, JsonSerializer.Serialize(this)); }
        catch (Exception e) { Journal.Erreur("réglages de publication", e); }
    }
}

/// <summary>
/// Siloé Stream 2.2.0 : « Publier sur YouTube ». Une vidéo classique sur la chaîne (pas un direct) : le rendu et l'envoi se font en
/// même temps, sans attendre un fichier exporté. La fenêtre peut se fermer : la publication continue (suivi dans la page Montage).
/// </summary>
public sealed class PublicationWindow : FenetreDialogue
{
    static Brush P(string c) => MainWindow.Pinceau(c);
    static Style S(string c) => MainWindow.Style_(c);
    static YouTubeCompte Compte => App.Direct.Compte;

    readonly ProjetMontage projet;
    readonly Action projetChange;
    readonly ReglagesPublication r = ReglagesPublication.Lire();
    readonly StackPanel corps = new();
    readonly TextBlock compteTexte = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Button connecter, changer, lienGoogle;
    readonly Border blocGoogle = new() { Padding = new Thickness(14, 12, 14, 14), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 10, 0, 0) };
    readonly TextBox googleId;
    readonly TextBlock messageGoogle = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly PasswordBox googleSecret;
    bool montrerGoogle;
    readonly TextBox titre, description, tags, heure;
    readonly ComboBox categorie = new() { MinHeight = 38 }, visibilite = new() { MinHeight = 38 }, playlist = new() { MinHeight = 38 }, resolution = new() { MinHeight = 38 }, encodeur = new() { MinHeight = 38 };
    readonly CheckBox enfants, programmer, copie;
    readonly DatePicker jour = new() { MinHeight = 36, Width = 170 };
    readonly Image miniatureApercu = new() { Width = 160, Height = 90, Stretch = Stretch.UniformToFill };
    readonly TextBlock miniatureNom = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock dossierCopieTexte = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock message = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    readonly Button publier, fermer;
    readonly Border suivi = new() { Visibility = Visibility.Collapsed };
    readonly ScrollViewer defil;
    string miniature;
    string dossierCopie;
    CancellationTokenSource? connexion;

    public PublicationWindow(ProjetMontage projet, Action projetChange)
    {
        this.projet = projet;
        this.projetChange = projetChange;
        Title = "Publier sur YouTube";
        Width = 760;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(560, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;
        Background = P("Page");
        miniature = File.Exists(r.Miniature) ? r.Miniature : "";
        dossierCopie = r.DossierCopie.Length > 0 ? r.DossierCopie : Path.Combine(PageMontage.DossierProjets, "Copies publiées");

        var racine = new DockPanel { Margin = new Thickness(28, 22, 28, 20) };
        // ------------------------------------------------------------ en-tête
        var tete = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var ligneTitre = new StackPanel { Orientation = Orientation.Horizontal };
        var icone = SonUi.Icone((Geometry)FindResource("IconeDirect"), P("Direct"), 26, 2);
        icone.Margin = new Thickness(0, 0, 10, 0);
        ligneTitre.Children.Add(icone);
        ligneTitre.Children.Add(new TextBlock { Text = "Publier sur YouTube", Style = S("Titre2"), VerticalAlignment = VerticalAlignment.Center });
        tete.Children.Add(ligneTitre);
        tete.Children.Add(new TextBlock
        {
            Text = $"« {projet.Nom} » · {ProjetMontage.Temps(projet.Duree, false)}. Une vidéo classique sur la chaîne (pas un direct) : elle part pendant qu'elle se fabrique, sans attendre un fichier exporté. Vous pouvez fermer cette fenêtre, la publication continue.",
            Style = S("Muet"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 20,
        });
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);

        // ------------------------------------------------------------ bas
        var bas = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        publier = new Button { Style = S("BtnRouge"), Content = "Publier", IsDefault = true, MinWidth = 150, Height = 44, Margin = new Thickness(10, 0, 0, 0) };
        fermer = new Button { Style = S("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 120, Height = 44 };
        var droite = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        droite.Children.Add(fermer);
        droite.Children.Add(publier);
        DockPanel.SetDock(droite, Dock.Right);
        bas.Children.Add(droite);
        DockPanel.SetDock(bas, Dock.Bottom);
        racine.Children.Add(bas);
        fermer.Click += (_, _) => Close();
        publier.Click += (_, _) => Publier();

        // ------------------------------------------------------------ la chaîne
        corps.Children.Add(Section("Chaîne"));
        connecter = new Button { Style = S("BtnOr"), Content = "Connecter le compte YouTube", Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(12, 0, 0, 0) };
        changer = new Button { Style = S("Btn"), Content = "Changer de chaîne…", Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "Google demande le compte et la chaîne (chaîne de marque comprise). C'est le même compte que pour le direct." };
        var ligneCompte = new DockPanel();
        var boutonsCompte = new StackPanel { Orientation = Orientation.Horizontal };
        boutonsCompte.Children.Add(connecter);
        boutonsCompte.Children.Add(changer);
        DockPanel.SetDock(boutonsCompte, Dock.Right);
        ligneCompte.Children.Add(boutonsCompte);
        ligneCompte.Children.Add(compteTexte);
        corps.Children.Add(ligneCompte);
        connecter.Click += async (_, _) => await Connecter(false);
        changer.Click += async (_, _) => await Connecter(true);

        // 2.2.1 : l'identifiant Google de l'église (programme installé), s'il n'est pas dans cette version : saisi ici une fois
        // (le même que dans la fenêtre du direct ; gardé chiffré pour cet utilisateur de Windows)
        lienGoogle = new Button { Style = S("BtnLien"), Content = "Identifiant Google…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "L'identifiant client Google de l'église (le même que pour le direct)" };
        lienGoogle.Click += (_, _) => { montrerGoogle = !montrerGoogle; MajCompte(); };
        corps.Children.Add(lienGoogle);
        var pg = new StackPanel();
        pg.Children.Add(new TextBlock { Text = "L'identifiant Google de l'église (une seule fois, gratuit)", FontSize = 14.5, FontWeight = FontWeights.Bold, Foreground = P("Blanc"), TextWrapping = TextWrapping.Wrap });
        pg.Children.Add(new TextBlock
        {
            Text = "1. Ouvrez Google Cloud avec le compte Google de la chaîne et créez un projet (par exemple « Siloé Stream »).\n"
                + "2. API et services › Bibliothèque : activez « YouTube Data API v3 ».\n"
                + "3. API et services › Écran de consentement OAuth : type « Externe », nom « Siloé Stream », votre adresse e-mail ; ajoutez le compte de la chaîne comme utilisateur test, puis « Publier l'application » (sinon Google redemande la connexion chaque semaine).\n"
                + "4. API et services › Identifiants › Créer des identifiants › ID client OAuth › type « Application de bureau ». Copiez ci-dessous l'ID client et le code secret.",
            FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 6, 0, 0),
        });
        var ouvrirCloud = new Button { Style = S("BtnPetit"), Content = "Ouvrir Google Cloud", Height = 32, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        ouvrirCloud.Click += (_, _) => SonUi.Ouvrir("https://console.cloud.google.com/apis/credentials");
        pg.Children.Add(ouvrirCloud);
        pg.Children.Add(Libelle("ID client (se termine par .apps.googleusercontent.com)"));
        googleId = Champ(App.Direct.Coffre.Lire("google.id"));
        googleId.FontSize = 13;
        pg.Children.Add(googleId);
        pg.Children.Add(Libelle("Code secret du client"));
        googleSecret = new PasswordBox { FontSize = 13, Height = 38, Padding = new Thickness(10, 8, 10, 8), Password = App.Direct.Coffre.Lire("google.secret") };
        pg.Children.Add(googleSecret);
        var garder = new Button { Style = S("BtnOr"), Content = "Enregistrer l'identifiant", Height = 36, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        garder.Click += (_, _) => GarderGoogle();
        pg.Children.Add(garder);
        messageGoogle.Foreground = P("Danger");
        pg.Children.Add(messageGoogle);
        pg.Children.Add(new TextBlock { Text = "Il sert aussi au direct (fenêtre YouTube, « Connecter le compte »). Il est gardé chiffré sur ce PC, pour cet utilisateur de Windows.", FontSize = 12, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        blocGoogle.Child = pg;
        blocGoogle.Background = P("Encre");
        blocGoogle.BorderBrush = P("OrBord");
        corps.Children.Add(blocGoogle);

        // ------------------------------------------------------------ la vidéo
        corps.Children.Add(Section("La vidéo"));
        titre = Champ(projet.Nom);
        titre.MaxLength = 100;
        corps.Children.Add(Libelle("Titre (100 caractères au plus)"));
        corps.Children.Add(titre);
        description = Champ(r.Description);
        description.AcceptsReturn = true;
        description.TextWrapping = TextWrapping.Wrap;
        description.MinHeight = 90;
        description.MaxHeight = 200;
        description.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        description.VerticalContentAlignment = VerticalAlignment.Top;
        description.MaxLength = 5000;
        corps.Children.Add(Libelle("Description"));
        corps.Children.Add(description);
        tags = Champ(r.Tags);
        corps.Children.Add(Libelle("Tags (séparés par des virgules)"));
        corps.Children.Add(tags);

        var deux = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        deux.ColumnDefinitions.Add(new ColumnDefinition());
        deux.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        deux.ColumnDefinitions.Add(new ColumnDefinition());
        var colA = new StackPanel();
        colA.Children.Add(Libelle("Visibilité"));
        foreach (var (v, t) in new[] { ("public", "Publique"), ("unlisted", "Non répertoriée (seulement avec le lien)"), ("private", "Privée") })
            visibilite.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        visibilite.SelectedIndex = Math.Max(0, visibilite.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == r.Confidentialite));
        colA.Children.Add(visibilite);
        var colB = new StackPanel();
        colB.Children.Add(Libelle("Catégorie"));
        foreach (var (id, nom) in ModeleDirect.Categories) categorie.Items.Add(new ComboBoxItem { Content = nom, Tag = id });
        categorie.SelectedIndex = Math.Max(0, categorie.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == r.Categorie));
        colB.Children.Add(categorie);
        Grid.SetColumn(colB, 2);
        deux.Children.Add(colA);
        deux.Children.Add(colB);
        corps.Children.Add(deux);

        enfants = new CheckBox { Content = "Vidéo conçue pour les enfants (YouTube le demande)", IsChecked = r.Enfants, Margin = new Thickness(0, 12, 0, 0), FontSize = 13.5 };
        corps.Children.Add(enfants);

        programmer = new CheckBox { Content = "Programmer la publication (privée jusque-là, puis publique toute seule)", Margin = new Thickness(0, 10, 0, 0), FontSize = 13.5 };
        var ligneDate = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 8, 0, 0), IsEnabled = false };
        var demain = DateTime.Today.AddDays(1);
        jour.SelectedDate = demain;
        heure = Champ("10:00");
        heure.Width = 90;
        heure.Margin = new Thickness(10, 0, 0, 0);
        ligneDate.Children.Add(jour);
        ligneDate.Children.Add(heure);
        programmer.Checked += (_, _) => { ligneDate.IsEnabled = true; visibilite.IsEnabled = false; };
        programmer.Unchecked += (_, _) => { ligneDate.IsEnabled = false; visibilite.IsEnabled = true; };
        corps.Children.Add(programmer);
        corps.Children.Add(ligneDate);

        // ------------------------------------------------------------ miniature et playlist
        corps.Children.Add(Section("Miniature et playlist"));
        var ligneMini = new DockPanel();
        var cadre = new Border { Width = 160, Height = 90, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = P("Nuit2"), Child = miniatureApercu, Margin = new Thickness(0, 0, 14, 0) };
        DockPanel.SetDock(cadre, Dock.Left);
        ligneMini.Children.Add(cadre);
        var miniBoutons = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        miniBoutons.Children.Add(miniatureNom);
        var rangMini = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var choisirMini = new Button { Style = S("Btn"), Content = "Choisir une image…", Height = 34, Padding = new Thickness(12, 0, 12, 0) };
        var retirerMini = new Button { Style = S("BtnPetit"), Content = "Sans miniature", Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
        rangMini.Children.Add(choisirMini);
        rangMini.Children.Add(retirerMini);
        miniBoutons.Children.Add(rangMini);
        ligneMini.Children.Add(miniBoutons);
        corps.Children.Add(ligneMini);
        choisirMini.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Title = "Miniature de la vidéo", Filter = "Images (JPEG, PNG)|*.jpg;*.jpeg;*.png" };
            if (d.ShowDialog(this) != true) return;
            miniature = d.FileName;
            MajMiniature();
        };
        retirerMini.Click += (_, _) => { miniature = ""; MajMiniature(); };
        MajMiniature();
        corps.Children.Add(Libelle("Playlist"));
        playlist.Items.Add(new ComboBoxItem { Content = "Aucune", Tag = "" });
        playlist.SelectedIndex = 0;
        corps.Children.Add(playlist);

        // ------------------------------------------------------------ le rendu
        corps.Children.Add(Section("Image et envoi"));
        var trois = new Grid();
        trois.ColumnDefinitions.Add(new ColumnDefinition());
        trois.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        trois.ColumnDefinitions.Add(new ColumnDefinition());
        var colR = new StackPanel();
        colR.Children.Add(Libelle("Résolution"));
        foreach (var (v, t) in new[] { ("source", "Celle des enregistrements"), ("1080", "1080p (Full HD)"), ("1440", "1440p"), ("2160", "4K (2160p)") })
            resolution.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        resolution.SelectedIndex = Math.Max(0, resolution.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == projet.Sortie.Resolution));
        colR.Children.Add(resolution);
        var colE = new StackPanel();
        colE.Children.Add(Libelle("Encodeur"));
        encodeur.Items.Add(new ComboBoxItem { Content = "Automatique (le meilleur de ce PC)", Tag = "auto" });
        foreach (var e in EncodeursVideo.Tous) encodeur.Items.Add(new ComboBoxItem { Content = e.Libelle, Tag = e.Nom });
        encodeur.SelectedIndex = Math.Max(0, encodeur.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == projet.Sortie.Encodeur));
        colE.Children.Add(encodeur);
        Grid.SetColumn(colE, 2);
        trois.Children.Add(colR);
        trois.Children.Add(colE);
        corps.Children.Add(trois);
        resolution.SelectionChanged += (_, _) => { projet.Sortie.Resolution = (string)((ComboBoxItem)resolution.SelectedItem).Tag; projetChange(); };
        encodeur.SelectionChanged += (_, _) => { projet.Sortie.Encodeur = (string)((ComboBoxItem)encodeur.SelectedItem).Tag; projetChange(); };
        corps.Children.Add(new TextBlock
        {
            Text = "Sans titre, logo, générique, fondu ni son changé, et si les enregistrements ont le même format, les images sont copiées telles quelles (qualité d'origine, 4K comprise, très rapide). Sinon, la vidéo est réencodée avec la carte graphique quand elle le peut.",
            Style = S("Muet"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 18,
        });
        copie = new CheckBox { Content = "Garder aussi une copie sur le PC", IsChecked = r.Copie, Margin = new Thickness(0, 12, 0, 0), FontSize = 13.5 };
        corps.Children.Add(copie);
        var ligneCopie = new DockPanel { Margin = new Thickness(26, 4, 0, 0) };
        var changerDossier = new Button { Style = S("BtnPetit"), Content = "Dossier…", Height = 30, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(changerDossier, Dock.Right);
        ligneCopie.Children.Add(changerDossier);
        ligneCopie.Children.Add(dossierCopieTexte);
        corps.Children.Add(ligneCopie);
        corps.Children.Add(new TextBlock
        {
            Text = "Sans copie (conseillé) : rien n'est écrit sur le disque, la vidéo avance au rythme d'Internet. Avec copie : le rendu va à pleine vitesse dans le fichier, l'envoi le suit, et il peut reprendre plus tard (même après avoir fermé Siloé Stream). Elle n'est jamais supprimée sans votre accord.",
            Style = S("Muet"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 6, 0, 0), LineHeight = 18,
        });
        changerDossier.Click += (_, _) =>
        {
            var d = new OpenFolderDialog { Title = "Dossier de la copie", InitialDirectory = Directory.Exists(dossierCopie) ? dossierCopie : AudioConfiguration.DossierEnregistrements };
            if (d.ShowDialog(this) == true) { dossierCopie = d.FolderName; MajDossier(); }
        };
        MajDossier();
        corps.Children.Add(message);

        defil = new ScrollViewer { Content = corps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) };
        var centre = new Grid();
        centre.Children.Add(defil);
        centre.Children.Add(suivi);
        racine.Children.Add(centre);
        Content = racine;

        Loaded += async (_, _) => { MajCompte(); await ChargerPlaylists(); };
        Closed += (_, _) => connexion?.Cancel();
    }

    static TextBlock Section(string titre) => new Capitales { Libelle = titre, Margin = new Thickness(0, 18, 0, 8) };
    static TextBlock Libelle(string t) => new() { Text = t, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 10, 0, 4) };
    static TextBox Champ(string texte) => new() { Style = S("Champ"), Text = texte, MinHeight = 38, FontSize = 14 };

    void MajDossier() => dossierCopieTexte.Text = dossierCopie;

    void MajMiniature()
    {
        if (miniature.Length == 0 || !File.Exists(miniature)) { miniatureApercu.Source = null; miniatureNom.Text = "Sans miniature : YouTube en choisit une. JPEG ou PNG, 1280 × 720 conseillé, 2 Mo au plus."; miniatureNom.Foreground = P("Brume"); return; }
        long taille = new FileInfo(miniature).Length;
        try
        {
            var b = new BitmapImage();
            b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.DecodePixelWidth = 320; b.UriSource = new Uri(miniature); b.EndInit(); b.Freeze();
            miniatureApercu.Source = b;
        }
        catch (Exception) { miniatureApercu.Source = null; }
        miniatureNom.Text = Path.GetFileName(miniature) + " · " + SonUi.Taille(taille) + (taille > 2 * 1024 * 1024 ? " — trop lourde : 2 Mo au plus pour YouTube." : "");
        miniatureNom.Foreground = P(taille > 2 * 1024 * 1024 ? "Danger" : "Blanc");
    }

    void MajCompte()
    {
        bool dispo = Compte.Disponible;
        blocGoogle.Visibility = !dispo || montrerGoogle ? Visibility.Visible : Visibility.Collapsed;
        lienGoogle.Visibility = dispo ? Visibility.Visible : Visibility.Collapsed;
        publier.IsEnabled = dispo;
        if (!dispo)
        {
            compteTexte.Text = "Pour publier directement, Siloé Stream a besoin une seule fois de l'identifiant Google de l'église : suivez les 4 étapes ci-dessous (en attendant, « Exporter un fichier » marche).";
            compteTexte.Foreground = P("Or");
            connecter.Visibility = changer.Visibility = Visibility.Collapsed;
            return;
        }
        bool c = Compte.Connecte;
        var chaine = App.Donnees.Reglages.Direct.Chaine;
        compteTexte.Text = c ? "Chaîne : " + (chaine.Length > 0 ? chaine : "compte connecté") : "Aucun compte YouTube connecté.";
        compteTexte.Foreground = P(c ? "Blanc" : "Brume");
        connecter.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
        changer.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Garde l'identifiant Google saisi (chiffré, dans le coffre du direct), puis propose de connecter le compte.</summary>
    void GarderGoogle()
    {
        var id = googleId.Text.Trim();
        var secret = googleSecret.Password.Trim();
        if (id.Length == 0 || secret.Length == 0) { messageGoogle.Text = "Copiez l'ID client et le code secret de Google Cloud (étape 4)."; return; }
        if (!id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            messageGoogle.Text = "Cet ID client ne ressemble pas à celui de Google (il se termine par .apps.googleusercontent.com) : vérifiez la copie.";
            return;
        }
        messageGoogle.Text = "";
        App.Direct.Coffre.Ecrire("google.id", id);
        App.Direct.Coffre.Ecrire("google.secret", secret);
        montrerGoogle = false;
        message.Text = "Identifiant Google gardé (chiffré sur ce PC). Cliquez maintenant sur « Connecter le compte YouTube ».";
        message.Foreground = P("Vert");
        MajCompte();
    }

    async Task Connecter(bool autre)
    {
        connexion?.Cancel();
        connexion = new CancellationTokenSource();
        connecter.IsEnabled = changer.IsEnabled = false;
        message.Text = "La page de Google s'ouvre dans le navigateur : choisissez le compte et la chaîne de l'église…";
        message.Foreground = P("Brume");
        try
        {
            if (autre) { await Compte.DeconnecterAsync(); App.Donnees.Reglages.Direct.Chaine = ""; App.Donnees.SauverReglages(); }
            var chaine = await Compte.ConnecterAsync(url => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Journal.Erreur("ouvrir " + url, e); } }, connexion.Token);
            App.Donnees.Reglages.Direct.Chaine = chaine.Titre;
            App.Donnees.SauverReglages();
            message.Text = "Connecté à « " + chaine.Titre + " ».";
            message.Foreground = P("Vert");
            Activate();
            await ChargerPlaylists();
        }
        catch (OperationCanceledException) { message.Text = ""; }
        catch (Exception e) { message.Text = e.Message; message.Foreground = P("Danger"); Journal.Erreur("connexion YouTube (montage)", e); }
        finally { connecter.IsEnabled = changer.IsEnabled = true; MajCompte(); }
    }

    async Task ChargerPlaylists()
    {
        if (!Compte.Connecte) return;
        try
        {
            var l = await Compte.PlaylistsAsync();
            string garde = playlist.SelectedItem is ComboBoxItem { Tag: string t } && t.Length > 0 ? t : r.Playlist;
            playlist.Items.Clear();
            playlist.Items.Add(new ComboBoxItem { Content = "Aucune", Tag = "" });
            foreach (var (id, nom) in l) playlist.Items.Add(new ComboBoxItem { Content = nom, Tag = id });
            playlist.SelectedIndex = Math.Max(0, playlist.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == garde));
        }
        catch (Exception e) { Journal.Erreur("playlists YouTube", e); }
    }

    void Publier()
    {
        message.Foreground = P("Danger");
        if (!Compte.Connecte) { message.Text = "Connectez d'abord le compte YouTube de l'église."; return; }
        if (projet.Sequences.Count == 0) { message.Text = "Le montage est vide : ajoutez au moins une séquence."; return; }
        if (titre.Text.Trim().Length == 0) { message.Text = "Donnez un titre à la vidéo."; titre.Focus(); return; }
        if (miniature.Length > 0 && File.Exists(miniature) && new FileInfo(miniature).Length > 2 * 1024 * 1024) { message.Text = "La miniature dépasse 2 Mo : choisissez une image plus légère."; return; }
        DateTimeOffset? quand = null;
        if (programmer.IsChecked == true)
        {
            if (jour.SelectedDate is not { } j || !TimeSpan.TryParse(heure.Text.Trim(), CultureInfo.InvariantCulture, out var h)) { message.Text = "Date ou heure de publication illisible (heure : 10:00)."; return; }
            quand = new DateTimeOffset(j.Date + h, TimeZoneInfo.Local.GetUtcOffset(j.Date + h));
            if (quand < DateTimeOffset.Now.AddMinutes(10)) { message.Text = "La date de publication doit être dans le futur (au moins 10 minutes)."; return; }
        }
        var infos = new InfosVideo
        {
            Titre = titre.Text.Trim(),
            Description = description.Text,
            Tags = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Categorie = (string)((ComboBoxItem)categorie.SelectedItem).Tag,
            Confidentialite = (string)((ComboBoxItem)visibilite.SelectedItem).Tag,
            PublierLe = quand,
            Enfants = enfants.IsChecked == true,
        };
        string pl = playlist.SelectedItem is ComboBoxItem { Tag: string t } ? t : "";
        // retenus pour la prochaine fois
        r.Description = description.Text; r.Tags = tags.Text; r.Categorie = infos.Categorie; r.Confidentialite = infos.Confidentialite; r.Enfants = infos.Enfants;
        r.Playlist = pl; r.Miniature = miniature; r.Copie = copie.IsChecked == true; r.DossierCopie = dossierCopie;
        r.Sauver();
        if (!File.Exists(App.Audio.Ffmpeg)) { message.Text = "FFmpeg est introuvable : réinstallez Siloé Stream."; return; }
        var pub = Publications.PublierYouTube(projet, infos, miniature.Length > 0 ? miniature : null, pl.Length > 0 ? pl : null, copie.IsChecked == true ? dossierCopie : null, Compte, App.Audio.Ffmpeg);
        // la fenêtre devient le suivi de la publication
        var pile = new StackPanel();
        pile.Children.Add(new CartePublication(pub));
        pile.Children.Add(new TextBlock
        {
            Text = "Vous pouvez fermer cette fenêtre : la publication continue en arrière-plan (suivi dans la page Montage). Ne fermez pas Siloé Stream avant la fin de l'envoi.",
            Style = S("Muet"), FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19,
        });
        suivi.Child = pile;
        suivi.Visibility = Visibility.Visible;
        defil.Visibility = Visibility.Collapsed;
        publier.Visibility = Visibility.Collapsed;
        fermer.Content = "Fermer";
        fermer.IsCancel = true;
        message.Text = "";
    }
}
