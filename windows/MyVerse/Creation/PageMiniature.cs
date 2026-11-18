using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using MyVerse.Audio;
using MyVerse.Miniatures;
using MyVerse.Ui.Audio;
using MyVerse.Ui.Edition;

namespace MyVerse.Ui.Creation;

/// <summary>Les réglages de la page Miniature : le logo de l'église et la dernière miniature ouverte.</summary>
public sealed class ReglagesMiniature
{
    public string Logo { get; set; } = "";
    public string Derniere { get; set; } = "";
    static string Fichier => System.IO.Path.Combine(App.Donnees.Dossier, "miniature.json");
    public static ReglagesMiniature Lire()
    {
        try { if (File.Exists(Fichier)) return JsonSerializer.Deserialize<ReglagesMiniature>(File.ReadAllText(Fichier)) ?? new(); }
        catch (Exception e) { Journal.Erreur("réglages de la miniature", e); }
        return new();
    }
    public void Sauver()
    {
        try { File.WriteAllText(Fichier, JsonSerializer.Serialize(this)); } catch (Exception e) { Journal.Erreur("réglages de la miniature", e); }
    }
}

/// <summary>
/// Siloé Stream 2.3.0 : la page Miniature. Une image envoyée depuis Montage (n'importe quel instant d'une vidéo, en pleine qualité)
/// ou importée devient une miniature YouTube 1280 × 720 : modèles prêts (remplis avec l'image, le titre et le logo), calques
/// (image, texte à gros contour, mots en couleur, formes, dégradés), glisser / redimensionner avec repères magnétiques, aperçu
/// tel que YouTube l'affiche (accueil, suggestions), conseils de lisibilité, export JPG de moins de 2 Mo, et « Utiliser pour la
/// publication ».
/// </summary>
public sealed class PageMiniature : Grid
{
    static Brush P(string c) => MainWindow.Pinceau(c);
    static Style S(string c) => MainWindow.Style_(c);
    const double W = ProjetMiniature.Largeur, H = ProjetMiniature.Hauteur;
    public static string Dossier => System.IO.Path.Combine(AudioConfiguration.DossierEnregistrements, "Miniatures");
    /// <summary>Une miniature est prête pour la publication (la fenêtre « Publier sur YouTube » la prend).</summary>
    public static event Action<string>? MiniaturePrete;

    static readonly string[] Nuancier =
        { "#FFFFFF", "#000000", "#FFD400", "#FF9F1C", "#E1251B", "#FF3D7F", "#8E44FF", "#1E90FF", "#00D1FF", "#2ECC71", "#121212", "#F4EDE4" };

    ProjetMiniature projet = new();
    string? chemin;
    readonly ReglagesMiniature reglages = ReglagesMiniature.Lire();
    readonly List<string> annulations = new(), retablissements = new();
    bool memorise, modifie;
    Calque? sel;
    readonly Dictionary<string, FrameworkElement> elements = new();

    readonly Viewbox vue = new() { Stretch = Stretch.Uniform };
    readonly Grid plan = new() { Width = W, Height = H, ClipToBounds = false };
    Canvas scene = new();
    readonly Canvas surcouche = new() { Width = W, Height = H, IsHitTestVisible = false };
    readonly TextBox nom, titre, sousTitre;
    readonly TextBlock etat = new() { FontSize = 12, Margin = new Thickness(2, 2, 0, 0) };
    readonly WrapPanel modeles = new();
    readonly StackPanel listeCalques = new();
    readonly StackPanel inspecteur = new();
    readonly StackPanel conseils = new();
    readonly TextBlock nomLogo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly VisualBrush miroirGrand = new() { Stretch = Stretch.UniformToFill }, miroirPetit = new() { Stretch = Stretch.UniformToFill };
    readonly TextBlock ytTitre = new(), ytTitrePetit = new();
    readonly CheckBox zoneSure;
    readonly DispatcherTimer minuterieModeles = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly DispatcherTimer minuterieSauvegarde = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly List<Action> valeurs = new();

    public PageMiniature()
    {
        Background = P("Page");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ============================================================ barre du haut
        var haut = new DockPanel { Margin = new Thickness(22, 12, 22, 10) };
        var utiliser = SonUi.Bouton("Utiliser pour la publication", (Geometry)Application.Current.FindResource("IconeDirect"), "BtnRouge", 40);
        utiliser.ToolTip = "Exporte la miniature (JPG, moins de 2 Mo) et la met dans « Publier sur YouTube »";
        var exporter = SonUi.Bouton("Exporter…", null, "Btn", 40);
        exporter.Margin = new Thickness(0, 0, 10, 0);
        var droite = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        droite.Children.Add(exporter);
        droite.Children.Add(utiliser);
        DockPanel.SetDock(droite, Dock.Right);
        haut.Children.Add(droite);
        var gauche = new StackPanel { Orientation = Orientation.Horizontal };
        var blocNom = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        nom = new TextBox { FontSize = 20, FontWeight = FontWeights.ExtraBold, Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1), MinWidth = 220, MaxWidth = 420, Padding = new Thickness(0, 2, 0, 2) };
        nom.Foreground = P("Blanc"); nom.CaretBrush = P("Blanc"); nom.BorderBrush = P("Nuit3");
        etat.Foreground = P("Brume");
        blocNom.Children.Add(nom);
        blocNom.Children.Add(etat);
        gauche.Children.Add(blocNom);
        Button Petit(string texte, string tip, Action clic)
        {
            var b = new Button { Style = S("BtnPetit"), Content = texte, Height = 32, Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => clic();
            gauche.Children.Add(b);
            return b;
        }
        Petit("Nouvelle", "Une nouvelle miniature (celle-ci reste enregistrée)", Nouvelle);
        Petit("Ouvrir…", "Ouvrir une miniature", Ouvrir);
        Petit("↶", "Annuler (Ctrl Z)", Annuler).FontSize = 16;
        Petit("↷", "Rétablir (Ctrl Y)", Retablir).FontSize = 16;
        haut.Children.Add(gauche);
        Children.Add(haut);
        nom.TextChanged += (_, _) => { if (nom.Text.Trim() == projet.Nom || nom.Text.Trim().Length == 0) return; projet.Nom = nom.Text.Trim(); ytTitre.Text = ytTitrePetit.Text = projet.Nom; Sauver(); };
        exporter.Click += (_, _) => Exporter();
        utiliser.Click += (_, _) => UtiliserPourPublication();

        // ============================================================ milieu
        var milieu = new Grid { Margin = new Thickness(22, 0, 22, 14) };
        milieu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        milieu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        milieu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        SetRow(milieu, 1);
        Children.Add(milieu);

        // ---------- à gauche : textes rapides, image, modèles, ajouter, calques
        var colG = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        colG.Children.Add(new Capitales { Libelle = "Textes", Margin = new Thickness(0, 0, 0, 6) });
        titre = new TextBox { Style = S("Champ"), MinHeight = 34, FontSize = 14, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ToolTip = "Le gros titre. Entourez un mot d'astérisques pour le mettre en couleur : la *grâce* de Dieu" };
        sousTitre = new TextBox { Style = S("Champ"), MinHeight = 34, FontSize = 14, ToolTip = "Le prédicateur, la date, la référence…" };
        colG.Children.Add(Libelle("Titre (*mot* = en couleur)"));
        colG.Children.Add(titre);
        colG.Children.Add(Libelle("Sous-titre"));
        colG.Children.Add(sousTitre);
        titre.TextChanged += (_, _) => TexteRapide("titre", titre.Text);
        sousTitre.TextChanged += (_, _) => TexteRapide("sous", sousTitre.Text);
        titre.GotKeyboardFocus += (_, _) => memorise = false;
        sousTitre.GotKeyboardFocus += (_, _) => memorise = false;

        colG.Children.Add(new Capitales { Libelle = "Image et logo", Margin = new Thickness(0, 16, 0, 6) });
        var rangImage = new WrapPanel();
        rangImage.Children.Add(Bouton("Image de fond…", "Une photo ou une capture (dans Montage : bouton 🖼 pour envoyer l'image de la tête de lecture)", ChoisirFond));
        rangImage.Children.Add(Bouton("Logo de l'église…", "Le logo, utilisé par les modèles (PNG à fond transparent conseillé)", ChoisirLogo));
        colG.Children.Add(rangImage);
        nomLogo.Foreground = P("Brume");
        colG.Children.Add(nomLogo);

        colG.Children.Add(new Capitales { Libelle = "Modèles (avec votre image et votre titre)", Margin = new Thickness(0, 16, 0, 6) });
        colG.Children.Add(modeles);

        colG.Children.Add(new Capitales { Libelle = "Ajouter", Margin = new Thickness(0, 14, 0, 6) });
        var ajouter = new WrapPanel();
        ajouter.Children.Add(Bouton("Texte", "Un texte à gros contour", () => Ajouter(new Calque { Type = Calque.TypeTexte, Nom = "Texte", Texte = "Votre *texte*", X = 360, Y = 260, L = 560, H = 200, Taille = 96, Alignement = "centre" })));
        ajouter.Children.Add(Bouton("Badge", "Un badge plein (« EN DIRECT », « NOUVEAU »…)", () => Ajouter(new Calque
        {
            Type = Calque.TypeTexte, Nom = "Badge", Role = "badge", Texte = "● EN DIRECT", X = 48, Y = 44, L = 330, H = 82, Taille = 46, Police = "Bahnschrift", Poids = 700,
            Alignement = "centre", Epaisseur = 0, Ombre = false, FondTexte = "#E1251B", Marge = 12, Arrondi = 12,
        })));
        ajouter.Children.Add(Bouton("Bandeau", "Un bandeau de couleur derrière un texte", () => Ajouter(new Calque { Type = Calque.TypeForme, Nom = "Bandeau", X = -40, Y = 470, L = 1360, H = 150, Remplissage = "#E1251B", Rotation = -3 })));
        ajouter.Children.Add(Bouton("Cercle", "Un cercle (entourer un détail)", () => Ajouter(new Calque { Type = Calque.TypeForme, Nom = "Cercle", FormeType = "cercle", X = 520, Y = 220, L = 260, H = 260, Remplissage = "#00FFFFFF", Bordure = "#FFD400", EpaisseurBordure = 12 })));
        ajouter.Children.Add(Bouton("Flèche", "Une flèche", () => Ajouter(new Calque { Type = Calque.TypeForme, Nom = "Flèche", FormeType = "fleche", X = 760, Y = 300, L = 260, H = 130, Remplissage = "#FFD400", Bordure = "#000000", EpaisseurBordure = 6, Rotation = 160 })));
        ajouter.Children.Add(Bouton("Dégradé", "Une ombre qui rend le texte lisible", () => Ajouter(new Calque { Type = Calque.TypeDegrade, Nom = "Dégradé", X = 0, Y = 0, L = W, H = H, Direction = "bas", Force = 0.85, Etendue = 0.6, Remplissage = "#000000" })));
        ajouter.Children.Add(Bouton("Image", "Une image en plus (photo du prédicateur, objet…)", AjouterImage));
        colG.Children.Add(ajouter);

        colG.Children.Add(new Capitales { Libelle = "Calques (le plus haut d'abord)", Margin = new Thickness(0, 14, 0, 6) });
        colG.Children.Add(listeCalques);
        milieu.Children.Add(new ScrollViewer { Content = colG, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) });

        // ---------- au centre : la miniature
        var centre = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
        var basCentre = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        zoneSure = new CheckBox { Content = "Montrer le minutage de YouTube", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "YouTube affiche la durée en bas à droite : rien d'important dessous" };
        zoneSure.Checked += (_, _) => MajSurcouche();
        zoneSure.Unchecked += (_, _) => MajSurcouche();
        DockPanel.SetDock(zoneSure, Dock.Right);
        basCentre.Children.Add(zoneSure);
        basCentre.Children.Add(new TextBlock
        {
            Text = "Glisser : déplacer · coins : agrandir (Maj : garder les proportions) · flèches : 1 px (Maj : 10) · Suppr · Ctrl D : dupliquer · Alt : sans aimant",
            FontSize = 12, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(basCentre, Dock.Bottom);
        centre.Children.Add(basCentre);
        plan.Children.Add(scene);
        plan.Children.Add(surcouche);
        vue.Child = plan;
        var cadre = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = vue };
        centre.Children.Add(cadre);
        SetColumn(centre, 1);
        milieu.Children.Add(centre);
        plan.MouseLeftButtonDown += Bas;
        plan.MouseMove += Bouge;
        plan.MouseLeftButtonUp += (_, _) => Haut();
        plan.LostMouseCapture += (_, _) => { if (glisse != null) Haut(); };
        plan.MouseMove += (_, e) => { if (glisse == null) plan.Cursor = CurseurSous(e.GetPosition(plan)); };
        plan.Background = Brushes.Transparent;

        // ---------- à droite : réglages, conseils, aperçu YouTube
        var colD = new StackPanel();
        colD.Children.Add(inspecteur);
        colD.Children.Add(new Capitales { Libelle = "Sur YouTube", Margin = new Thickness(0, 18, 0, 8) });
        colD.Children.Add(ApercuYouTube());
        colD.Children.Add(conseils);
        var defilD = new ScrollViewer { Content = colD, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
        SetColumn(defilD, 2);
        milieu.Children.Add(defilD);

        minuterieModeles.Tick += (_, _) => { minuterieModeles.Stop(); MajModeles(); };
        minuterieSauvegarde.Tick += (_, _) => { minuterieSauvegarde.Stop(); Sauver(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible && modifie) Sauver(); };
        MajLogo();
        ChargerDerniere();
    }

    static TextBlock Libelle(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 8, 0, 4) };

    static Button Bouton(string texte, string tip, Action clic)
    {
        var b = new Button { Style = S("BtnPetit"), Content = texte, Height = 30, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
        b.Click += (_, _) => clic();
        return b;
    }

    // ================================================================================================ projet
    void ChargerDerniere()
    {
        try
        {
            if (reglages.Derniere.Length > 0 && File.Exists(reglages.Derniere)) { Charger(ProjetMiniature.Ouvrir(reglages.Derniere), reglages.Derniere); return; }
        }
        catch (Exception e) { Journal.Erreur("miniature : dernière", e); }
        Charger(ModelesMiniature.Creer("predication", null, "Titre de la *prédication*", "Pasteur", Logo(), "Miniature"), null);
    }

    string? Logo() => reglages.Logo.Length > 0 && File.Exists(reglages.Logo) ? reglages.Logo : null;

    void Charger(ProjetMiniature p, string? fichier)
    {
        projet = p;
        chemin = fichier;
        annulations.Clear();
        retablissements.Clear();
        sel = null;
        memorise = false;
        nom.Text = p.Nom;
        titre.Text = p.Role("titre")?.Texte ?? "";
        sousTitre.Text = p.Role("sous")?.Texte ?? "";
        modifie = false;
        Changer(false);
        MajModeles();
    }

    void Memoriser()
    {
        if (memorise) return;
        memorise = true;
        annulations.Add(projet.VersJson());
        if (annulations.Count > 100) annulations.RemoveAt(0);
        retablissements.Clear();
    }

    void Annuler()
    {
        if (annulations.Count == 0) { SonUi.Toast("Rien à annuler."); return; }
        retablissements.Add(projet.VersJson());
        Restaurer(annulations[^1]);
        annulations.RemoveAt(annulations.Count - 1);
    }

    void Retablir()
    {
        if (retablissements.Count == 0) { SonUi.Toast("Rien à rétablir."); return; }
        annulations.Add(projet.VersJson());
        Restaurer(retablissements[^1]);
        retablissements.RemoveAt(retablissements.Count - 1);
    }

    void Restaurer(string json)
    {
        string? id = sel?.Id;
        projet = ProjetMiniature.DepuisJson(json);
        sel = id == null ? null : projet.Calques.FirstOrDefault(c => c.Id == id);
        memorise = false;
        titre.Text = projet.Role("titre")?.Texte ?? titre.Text;
        sousTitre.Text = projet.Role("sous")?.Texte ?? sousTitre.Text;
        Changer();
    }

    /// <summary>Le projet a changé : la scène, les calques, les réglages (si <paramref name="inspecteurAussi"/>), l'aperçu ; sauvegarde un peu plus tard.</summary>
    void Changer(bool marquer = true, bool inspecteurAussi = true)
    {
        if (marquer) { modifie = true; minuterieSauvegarde.Stop(); minuterieSauvegarde.Start(); }
        if (sel != null && !projet.Calques.Contains(sel)) sel = null;
        Dessiner();
        MajCalques();
        if (inspecteurAussi) MajInspecteur(); else foreach (var v in valeurs) v();
        MajConseils();
        etat.Text = (modifie ? "Modifiée · enregistrement automatique…" : chemin != null ? "Enregistrée · " + System.IO.Path.GetFileName(chemin) : "Pas encore enregistrée")
            + (annulations.Count > 0 ? " · Ctrl Z pour annuler" : "");
    }

    void Sauver()
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            chemin ??= NomLibre(projet.Nom, ProjetMiniature.Extension);
            projet.Enregistrer(chemin);
            modifie = false;
            reglages.Derniere = chemin;
            reglages.Sauver();
            etat.Text = "Enregistrée · " + System.IO.Path.GetFileName(chemin);
        }
        catch (Exception e) { Journal.Erreur("miniature : sauvegarde", e); }
    }

    static string NomLibre(string nomProjet, string extension)
    {
        string b = RecordingFiles.SafeName(nomProjet);
        for (int i = 1; i < 1000; i++)
        {
            var c = System.IO.Path.Combine(Dossier, b + (i == 1 ? "" : $" ({i})") + extension);
            if (!File.Exists(c)) return c;
        }
        return System.IO.Path.Combine(Dossier, b + " " + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    void Nouvelle()
    {
        if (modifie) Sauver();
        Charger(ModelesMiniature.Creer("predication", projet.Role("fond")?.Chemin, "Titre de la *prédication*", "Pasteur", Logo(), "Miniature"), null);
        SonUi.Toast("Nouvelle miniature : l'ancienne reste enregistrée (Ouvrir…).");
    }

    void Ouvrir()
    {
        var d = new OpenFileDialog { Title = "Ouvrir une miniature", Filter = "Miniature Siloé Stream|*" + ProjetMiniature.Extension, InitialDirectory = Directory.Exists(Dossier) ? Dossier : AudioConfiguration.DossierEnregistrements };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        if (modifie) Sauver();
        try { Charger(ProjetMiniature.Ouvrir(d.FileName), d.FileName); }
        catch (Exception e) { SonUi.Toast("Cette miniature ne s'ouvre pas : " + e.Message); }
    }

    // ================================================================================================ image, logo, modèles
    /// <summary>Une image envoyée depuis Montage (ou ailleurs) : elle devient le fond de la miniature.</summary>
    public void RecevoirImage(string image, string? titreVideo = null)
    {
        Memoriser();
        if (projet.Role("fond") is { } fond) { fond.Chemin = image; fond.Zoom = 1; fond.CadrageX = fond.CadrageY = 0.5; }
        else projet.Calques.Insert(0, new Calque { Type = Calque.TypeImage, Nom = "Image de fond", Role = "fond", Chemin = image, X = 0, Y = 0, L = W, H = H });
        if (!string.IsNullOrWhiteSpace(titreVideo) && projet.Nom is "Miniature" or "") { projet.Nom = titreVideo!; nom.Text = titreVideo; }
        memorise = false;
        Changer();
        MajModeles();
        SonUi.Toast("Image reçue : choisissez un modèle à gauche, ou modifiez la miniature.");
    }

    void ChoisirFond()
    {
        var d = new OpenFileDialog { Title = "Image de fond", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif;*.tiff" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        RecevoirImage(d.FileName);
    }

    void ChoisirLogo()
    {
        var d = new OpenFileDialog { Title = "Logo de l'église", Filter = "Images (PNG conseillé)|*.png;*.jpg;*.jpeg" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        reglages.Logo = d.FileName;
        reglages.Sauver();
        Memoriser();
        if (projet.Role("logo") is { } l) l.Chemin = d.FileName;
        else projet.Calques.Add(new Calque { Type = Calque.TypeImage, Nom = "Logo", Role = "logo", Chemin = d.FileName, Ajustement = "ajuster", X = 48, Y = 40, L = 110, H = 110 });
        memorise = false;
        MajLogo();
        Changer();
        MajModeles();
    }

    void MajLogo() => nomLogo.Text = Logo() is { } l ? "Logo : " + System.IO.Path.GetFileName(l) : "Pas de logo choisi.";

    void AjouterImage()
    {
        var d = new OpenFileDialog { Title = "Ajouter une image", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        Ajouter(new Calque { Type = Calque.TypeImage, Nom = System.IO.Path.GetFileNameWithoutExtension(d.FileName), Chemin = d.FileName, Ajustement = "ajuster", X = 760, Y = 160, L = 440, H = 440 });
    }

    void Ajouter(Calque c)
    {
        Memoriser();
        c.Normaliser();
        projet.Calques.Add(c);
        sel = c;
        memorise = false;
        Changer();
    }

    /// <summary>Les modèles, dessinés avec l'image, le titre et le logo d'ici : un clic applique.</summary>
    void MajModeles()
    {
        modeles.Children.Clear();
        string? image = projet.Role("fond")?.Chemin is { Length: > 0 } f ? f : null;
        foreach (var m in ModelesMiniature.Tous)
        {
            var p = ModelesMiniature.Creer(m.Id, image, titre.Text, sousTitre.Text, Logo(), projet.Nom);
            var dessin = new Viewbox { Stretch = Stretch.Uniform, Child = RenduMiniature.Construire(p) };
            var pile = new StackPanel();
            pile.Children.Add(new Border { Width = 112, Height = 63, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = dessin, Background = Brushes.Black });
            pile.Children.Add(new TextBlock { Text = m.Nom, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = P("Blanc"), Margin = new Thickness(2, 4, 0, 0) });
            var b = new Button { Style = S("BtnPlat"), Content = pile, Padding = new Thickness(3), Margin = new Thickness(0, 0, 6, 6), ToolTip = m.Description + " (remplace la mise en page actuelle ; Ctrl Z revient)" };
            string id = m.Id;
            b.Click += (_, _) => AppliquerModele(id);
            modeles.Children.Add(b);
        }
    }

    void AppliquerModele(string id)
    {
        Memoriser();
        string? image = projet.Role("fond")?.Chemin is { Length: > 0 } f ? f : null;
        var p = ModelesMiniature.Creer(id, image, titre.Text, sousTitre.Text, Logo(), projet.Nom);
        projet.Calques = p.Calques;
        projet.Fond = p.Fond;
        sel = projet.Role("titre");
        memorise = false;
        if (projet.Role("titre") is { } t) titre.Text = t.Texte;
        Changer();
    }

    /// <summary>Les champs rapides changent le calque qui a ce rôle (le titre, le sous-titre).</summary>
    void TexteRapide(string role, string texte)
    {
        minuterieModeles.Stop();
        minuterieModeles.Start();
        var c = projet.Role(role);
        if (c == null || c.Texte == texte) return;
        Memoriser();
        c.Texte = texte;
        Changer(true, sel != c);
    }

    // ================================================================================================ dessin et sélection
    void Dessiner()
    {
        elements.Clear();
        plan.Children.Remove(scene);
        scene = RenduMiniature.Construire(projet, elements);
        plan.Children.Insert(0, scene);
        miroirGrand.Visual = scene;
        miroirPetit.Visual = scene;
        MajSurcouche();
    }

    double Echelle()
    {
        if (vue.ActualWidth <= 0 || vue.ActualHeight <= 0) return 0.5;
        return Math.Min(vue.ActualWidth / W, vue.ActualHeight / H);
    }

    readonly List<(double X1, double Y1, double X2, double Y2)> guides = new();

    void MajSurcouche()
    {
        surcouche.Children.Clear();
        double k = Echelle();
        if (zoneSure.IsChecked == true)
        {
            var zone = new Border
            {
                Width = 150, Height = 54, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Child = new TextBlock { Text = "1:24:05", Foreground = Brushes.White, FontSize = 34, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Canvas.SetLeft(zone, W - 150 - 16);
            Canvas.SetTop(zone, H - 54 - 16);
            zone.Opacity = 0.75;
            surcouche.Children.Add(zone);
        }
        foreach (var (x1, y1, x2, y2) in guides)
            surcouche.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = new SolidColorBrush(Color.FromRgb(255, 45, 160)), StrokeThickness = 1.5 / k });
        if (sel == null || !sel.Visible) return;
        var boite = new Canvas { Width = sel.L, Height = sel.H };
        boite.Children.Add(new Rectangle { Width = sel.L, Height = sel.H, Stroke = new SolidColorBrush(Color.FromRgb(255, 196, 0)), StrokeThickness = 2 / k, StrokeDashArray = new DoubleCollection { 4, 3 } });
        if (!sel.Verrouille)
        {
            double t = 10 / k;
            foreach (var (px, py) in Poignees(sel))
            {
                var r = new Rectangle { Width = t, Height = t, Fill = Brushes.White, Stroke = new SolidColorBrush(Color.FromRgb(255, 160, 0)), StrokeThickness = 1.5 / k };
                Canvas.SetLeft(r, px - t / 2);
                Canvas.SetTop(r, py - t / 2);
                boite.Children.Add(r);
            }
        }
        Canvas.SetLeft(boite, sel.X);
        Canvas.SetTop(boite, sel.Y);
        if (Math.Abs(sel.Rotation) > 0.01) boite.RenderTransform = new RotateTransform(sel.Rotation, sel.L / 2, sel.H / 2);
        surcouche.Children.Add(boite);
    }

    /// <summary>Les 8 poignées dans le repère du calque : coins (0 à 3, sens horaire depuis le haut à gauche), puis bords (4 à 7 : haut, droite, bas, gauche).</summary>
    static (double X, double Y)[] Poignees(Calque c) => new[]
    {
        (0.0, 0.0), (c.L, 0.0), (c.L, c.H), (0.0, c.H),
        (c.L / 2, 0.0), (c.L, c.H / 2), (c.L / 2, c.H), (0.0, c.H / 2),
    };

    /// <summary>Le point dans le repère du calque (sans sa rotation).</summary>
    static Point Local(Calque c, Point p)
    {
        if (Math.Abs(c.Rotation) < 0.01) return new Point(p.X - c.X, p.Y - c.Y);
        double a = -c.Rotation * Math.PI / 180, cx = c.X + c.L / 2, cy = c.Y + c.H / 2;
        double dx = p.X - cx, dy = p.Y - cy;
        return new Point(dx * Math.Cos(a) - dy * Math.Sin(a) + c.L / 2, dx * Math.Sin(a) + dy * Math.Cos(a) + c.H / 2);
    }

    int PoigneeSous(Point p)
    {
        if (sel == null || sel.Verrouille) return -1;
        var l = Local(sel, p);
        double r = 10 / Echelle();
        var poignees = Poignees(sel);
        for (int i = 0; i < poignees.Length; i++)
            if (Math.Abs(l.X - poignees[i].X) <= r && Math.Abs(l.Y - poignees[i].Y) <= r) return i;
        return -1;
    }

    Calque? CalqueSous(Point p)
    {
        for (int i = projet.Calques.Count - 1; i >= 0; i--)
        {
            var c = projet.Calques[i];
            if (!c.Visible || c.Verrouille) continue;
            var l = Local(c, p);
            if (l.X >= 0 && l.Y >= 0 && l.X <= c.L && l.Y <= c.H) return c;
        }
        return null;
    }

    Cursor CurseurSous(Point p) => PoigneeSous(p) switch
    {
        0 or 2 => Cursors.SizeNWSE,
        1 or 3 => Cursors.SizeNESW,
        4 or 6 => Cursors.SizeNS,
        5 or 7 => Cursors.SizeWE,
        _ => CalqueSous(p) != null ? Cursors.SizeAll : Cursors.Arrow,
    };

    sealed class Glisse { public Point Depart; public int Poignee = -1; public double X, Y, L, H; public bool Commence; }
    Glisse? glisse;

    void Bas(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(plan);
        plan.Focus();
        Keyboard.Focus(plan);
        int poignee = PoigneeSous(p);
        if (poignee < 0)
        {
            var c = CalqueSous(p);
            if (!ReferenceEquals(c, sel)) { sel = c; memorise = false; MajCalques(); MajInspecteur(); MajSurcouche(); }
            if (c == null) return;
            if (e.ClickCount == 2 && c.Type == Calque.TypeTexte) { Dispatcher.BeginInvoke(new Action(() => champTexte?.Focus()), DispatcherPriority.Input); return; }
        }
        if (sel == null || sel.Verrouille) return;
        glisse = new Glisse { Depart = p, Poignee = poignee, X = sel.X, Y = sel.Y, L = sel.L, H = sel.H };
        plan.CaptureMouse();
        e.Handled = true;
    }

    void Bouge(object sender, MouseEventArgs e)
    {
        if (glisse == null || sel == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Haut(); return; }
        var p = e.GetPosition(plan);
        double dx = p.X - glisse.Depart.X, dy = p.Y - glisse.Depart.Y;
        if (!glisse.Commence)
        {
            if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2) return;
            glisse.Commence = true;
            memorise = false;
            Memoriser();
        }
        guides.Clear();
        bool aimant = !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (glisse.Poignee < 0)
        {
            double x = glisse.X + dx, y = glisse.Y + dy;
            if (aimant) (x, y) = Aimanter(sel, x, y);
            sel.X = Math.Round(x);
            sel.Y = Math.Round(y);
            if (elements.TryGetValue(sel.Id, out var el)) { Canvas.SetLeft(el, sel.X); Canvas.SetTop(el, sel.Y); }
            MajSurcouche();
            foreach (var v in valeurs) v();
            return;
        }
        // agrandir dans le repère du calque (sa rotation enlevée)
        double a = -sel.Rotation * Math.PI / 180;
        double lx = dx * Math.Cos(a) - dy * Math.Sin(a), ly = dx * Math.Sin(a) + dy * Math.Cos(a);
        double nx = glisse.X, ny = glisse.Y, nl = glisse.L, nh = glisse.H;
        int q = glisse.Poignee;
        if (q is 0 or 3 or 7) { nx = glisse.X + lx; nl = glisse.L - lx; }
        if (q is 1 or 2 or 5) nl = glisse.L + lx;
        if (q is 0 or 1 or 4) { ny = glisse.Y + ly; nh = glisse.H - ly; }
        if (q is 2 or 3 or 6) nh = glisse.H + ly;
        bool proportions = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && q <= 3;
        if (proportions && glisse.L > 0 && glisse.H > 0)
        {
            double ratio = glisse.L / glisse.H;
            if (Math.Abs(nl - glisse.L) / glisse.L > Math.Abs(nh - glisse.H) / glisse.H) { double h2 = nl / ratio; if (q is 0 or 1) ny += nh - h2; nh = h2; }
            else { double l2 = nh * ratio; if (q is 0 or 3) nx += nl - l2; nl = l2; }
        }
        if (nl < 12) { if (q is 0 or 3 or 7) nx -= 12 - nl; nl = 12; }
        if (nh < 12) { if (q is 0 or 1 or 4) ny -= 12 - nh; nh = 12; }
        sel.X = Math.Round(nx); sel.Y = Math.Round(ny); sel.L = Math.Round(nl); sel.H = Math.Round(nh);
        Dessiner();
        foreach (var v in valeurs) v();
    }

    /// <summary>L'aimant : les bords et le centre du calque se collent aux bords et au centre de l'image et des autres calques.</summary>
    (double X, double Y) Aimanter(Calque c, double x, double y)
    {
        double seuil = 8 / Echelle();
        var cx = new List<double> { 0, W / 2, W };
        var cy = new List<double> { 0, H / 2, H };
        foreach (var o in projet.Calques)
        {
            if (ReferenceEquals(o, c) || !o.Visible || Math.Abs(o.Rotation) > 0.01) continue;
            cx.AddRange(new[] { o.X, o.X + o.L / 2, o.X + o.L });
            cy.AddRange(new[] { o.Y, o.Y + o.H / 2, o.Y + o.H });
        }
        double Coller(double pos, double taille, List<double> cibles, out double? guide)
        {
            guide = null;
            double meilleur = seuil, res = pos;
            foreach (var off in new[] { 0, taille / 2, taille })
                foreach (var t in cibles)
                    if (Math.Abs(pos + off - t) < meilleur) { meilleur = Math.Abs(pos + off - t); res = t - off; guide = t; }
            return res;
        }
        double nx = Coller(x, c.L, cx, out var gx), ny = Coller(y, c.H, cy, out var gy);
        if (gx is { } vx) guides.Add((vx, 0, vx, H));
        if (gy is { } vy) guides.Add((0, vy, W, vy));
        return (nx, ny);
    }

    void Haut()
    {
        var g = glisse;
        glisse = null;
        if (plan.IsMouseCaptured) plan.ReleaseMouseCapture();
        guides.Clear();
        if (g is { Commence: true }) { memorise = false; Changer(true, false); }
        else MajSurcouche();
    }

    // ================================================================================================ calques
    void MajCalques()
    {
        listeCalques.Children.Clear();
        for (int i = projet.Calques.Count - 1; i >= 0; i--)
        {
            var c = projet.Calques[i];
            bool choisi = ReferenceEquals(c, sel);
            var rang = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var oeil = new CheckBox { IsChecked = c.Visible, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Visible", Margin = new Thickness(0, 0, 4, 0) };
            oeil.Checked += (_, _) => { Memoriser(); c.Visible = true; memorise = false; Changer(); };
            oeil.Unchecked += (_, _) => { Memoriser(); c.Visible = false; memorise = false; Changer(); };
            DockPanel.SetDock(oeil, Dock.Left);
            rang.Children.Add(oeil);
            var outils = new StackPanel { Orientation = Orientation.Horizontal };
            Button Mini(string t, string tip, Action a)
            {
                var b = new Button { Style = S("BtnPlat"), Content = t, Padding = new Thickness(6, 1, 6, 1), FontSize = 13, ToolTip = tip };
                b.Click += (_, _) => a();
                outils.Children.Add(b);
                return b;
            }
            int index = i;
            Mini(c.Verrouille ? "🔒" : "🔓", c.Verrouille ? "Déverrouiller" : "Verrouiller (plus de clic dessus dans l'image)", () => { Memoriser(); c.Verrouille = !c.Verrouille; memorise = false; Changer(); });
            Mini("▲", "Monter", () => Deplacer(c, +1));
            Mini("▼", "Descendre", () => Deplacer(c, -1));
            DockPanel.SetDock(outils, Dock.Right);
            rang.Children.Add(outils);
            string nomCalque = c.Nom.Length > 0 ? c.Nom : c.Type;
            if (c.Type == Calque.TypeTexte && Calque.Accents(c.Texte).Texte.Trim() is { Length: > 0 } tx) nomCalque += " · " + (tx.Length > 18 ? tx[..18] + "…" : tx).Replace('\n', ' ');
            var etiquette = new Button
            {
                Style = S("BtnPlat"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4),
                Content = new TextBlock { Text = nomCalque, FontSize = 12.5, FontWeight = choisi ? FontWeights.Bold : FontWeights.Normal, Foreground = P(choisi ? "Or" : "Blanc"), TextTrimming = TextTrimming.CharacterEllipsis },
                Background = choisi ? P("OrLeger") : Brushes.Transparent,
            };
            etiquette.Click += (_, _) => { sel = c; memorise = false; MajCalques(); MajInspecteur(); MajSurcouche(); };
            rang.Children.Add(etiquette);
            listeCalques.Children.Add(rang);
        }
    }

    void Deplacer(Calque c, int sens)
    {
        int i = projet.Calques.IndexOf(c), j = i + sens;
        if (i < 0 || j < 0 || j >= projet.Calques.Count) return;
        Memoriser();
        projet.Calques.RemoveAt(i);
        projet.Calques.Insert(j, c);
        memorise = false;
        Changer();
    }

    void Supprimer()
    {
        if (sel == null) return;
        Memoriser();
        projet.Calques.Remove(sel);
        sel = null;
        memorise = false;
        Changer();
    }

    void Dupliquer()
    {
        if (sel == null) return;
        Memoriser();
        var c = sel.Copie();
        c.Id = ProjetMiniature.NouvelId();
        c.Role = "";
        c.X += 24; c.Y += 24;
        projet.Calques.Insert(projet.Calques.IndexOf(sel) + 1, c);
        sel = c;
        memorise = false;
        Changer();
    }

    /// <summary>Les touches de la page (sans champ de saisie actif) ; vrai si la touche est traitée.</summary>
    public bool Touche(KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        bool ctrl = m.HasFlag(ModifierKeys.Control), maj = m.HasFlag(ModifierKeys.Shift);
        double pas = maj ? 10 : 1;
        void Pousser(double dx, double dy)
        {
            if (sel == null || sel.Verrouille) return;
            Memoriser();
            sel.X += dx; sel.Y += dy;
            Changer(true, false);
        }
        switch (e.Key)
        {
            case Key.Delete or Key.Back: Supprimer(); return true;
            case Key.Z when ctrl && maj: Retablir(); return true;
            case Key.Z when ctrl: Annuler(); return true;
            case Key.Y when ctrl: Retablir(); return true;
            case Key.D when ctrl: Dupliquer(); return true;
            case Key.S when ctrl: Sauver(); SonUi.Toast("Miniature enregistrée."); return true;
            case Key.E when ctrl: Exporter(); return true;
            case Key.Left: Pousser(-pas, 0); return true;
            case Key.Right: Pousser(pas, 0); return true;
            case Key.Up: Pousser(0, -pas); return true;
            case Key.Down: Pousser(0, pas); return true;
        }
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) memorise = false;
        return false;
    }

    // ================================================================================================ réglages (à droite)
    TextBox? champTexte;

    void Titre(string t) => inspecteur.Children.Add(new Capitales { Libelle = t, Margin = new Thickness(0, 0, 0, 4) });

    void Section(string t) => inspecteur.Children.Add(new TextBlock { Text = t, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = P("Blanc"), Margin = new Thickness(0, 14, 0, 2) });

    Slider Curseur(string libelle, double min, double max, Func<double> lire, Action<double> ecrire, Func<double, string> format, double pas = 1)
    {
        var tete = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
        var val = new TextBlock { Text = format(lire()), FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = P("Blanc") };
        DockPanel.SetDock(val, Dock.Right);
        tete.Children.Add(val);
        tete.Children.Add(new TextBlock { Text = libelle, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume") });
        inspecteur.Children.Add(tete);
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(lire(), min, max), SmallChange = pas, LargeChange = pas * 10, IsMoveToPointEnabled = true };
        bool interne = false;
        s.GotMouseCapture += (_, _) => memorise = false;
        s.ValueChanged += (_, e) =>
        {
            val.Text = format(e.NewValue);
            if (interne) return;
            Memoriser();
            ecrire(Math.Round(e.NewValue / pas) * pas);
            Changer(true, false);
        };
        valeurs.Add(() => { interne = true; s.Value = Math.Clamp(lire(), min, max); val.Text = format(lire()); interne = false; });
        inspecteur.Children.Add(s);
        return s;
    }

    void Nombres(params (string Libelle, Func<double> Lire, Action<double> Ecrire)[] champs)
    {
        var g = new UniformGrid { Columns = champs.Length, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (lib, lire, ecrire) in champs)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            p.Children.Add(new TextBlock { Text = lib, FontSize = 11.5, Foreground = P("Brume") });
            var tb = new TextBox { Style = S("Champ"), MinHeight = 30, FontSize = 13, Text = Math.Round(lire()).ToString() };
            void Valider()
            {
                if (double.TryParse(tb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && Math.Abs(v - lire()) > 0.4)
                { memorise = false; Memoriser(); ecrire(v); memorise = false; Changer(true, false); }
                tb.Text = Math.Round(lire()).ToString();
            }
            tb.LostKeyboardFocus += (_, _) => Valider();
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Valider(); e.Handled = true; } };
            valeurs.Add(() => { if (!tb.IsKeyboardFocusWithin) tb.Text = Math.Round(lire()).ToString(); });
            p.Children.Add(tb);
            g.Children.Add(p);
        }
        inspecteur.Children.Add(g);
    }

    CheckBox Case(string libelle, Func<bool> lire, Action<bool> ecrire)
    {
        var c = new CheckBox { Content = libelle, IsChecked = lire(), FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
        c.Checked += (_, _) => { if (lire()) return; memorise = false; Memoriser(); ecrire(true); memorise = false; Changer(); };
        c.Unchecked += (_, _) => { if (!lire()) return; memorise = false; Memoriser(); ecrire(false); memorise = false; Changer(); };
        inspecteur.Children.Add(c);
        return c;
    }

    ComboBox Liste(string libelle, (string Valeur, string Texte)[] options, Func<string> lire, Action<string> ecrire)
    {
        inspecteur.Children.Add(new TextBlock { Text = libelle, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 8, 0, 3) });
        var cb = new ComboBox { MinHeight = 32 };
        foreach (var (v, t) in options) cb.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        cb.SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => o.Valeur == lire()));
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not ComboBoxItem { Tag: string v } || v == lire()) return;
            memorise = false; Memoriser(); ecrire(v); memorise = false; Changer(true, false);
        };
        inspecteur.Children.Add(cb);
        return cb;
    }

    static readonly ControlTemplate ModelePastille = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border CornerRadius='11' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/></ControlTemplate>");

    /// <summary>Une couleur : la palette, et le code (#RRGGBB) pour une couleur précise.</summary>
    void Couleur(string libelle, Func<string> lire, Action<string> ecrire, bool transparent = false)
    {
        inspecteur.Children.Add(new TextBlock { Text = libelle, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 8, 0, 3) });
        var rang = new WrapPanel();
        var code = new TextBox { Style = S("Champ"), MinHeight = 26, FontSize = 12, Width = 92, Text = lire(), Margin = new Thickness(0, 0, 6, 4) };
        void Mettre(string c)
        {
            if (Couleurs.Valide(c, "") is not { Length: > 0 } v || v == lire()) return;
            memorise = false; Memoriser(); ecrire(v); memorise = false; Changer(true, false);
            code.Text = v;
        }
        code.LostKeyboardFocus += (_, _) => Mettre(code.Text);
        code.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Mettre(code.Text); e.Handled = true; } };
        rang.Children.Add(code);
        foreach (var c in transparent ? Nuancier.Prepend("#00000000") : Nuancier)
        {
            var pastille = new Button
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 4), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, ToolTip = c == "#00000000" ? "Aucune" : c,
                Background = new SolidColorBrush(RenduMiniature.Couleur(c)), BorderBrush = P("Trait2"),
                Template = ModelePastille,
            };
            string cc = c;
            pastille.Click += (_, _) => Mettre(cc);
            rang.Children.Add(pastille);
        }
        inspecteur.Children.Add(rang);
    }

    void MajInspecteur()
    {
        inspecteur.Children.Clear();
        valeurs.Clear();
        champTexte = null;
        var c = sel;
        if (c == null)
        {
            Titre("La miniature");
            inspecteur.Children.Add(new TextBlock { Text = "Cliquez un élément de l'image pour le régler. Un modèle à gauche fait une mise en page complète d'un clic.", FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap });
            Couleur("Couleur sous les calques", () => projet.Fond, v => projet.Fond = v);
            return;
        }
        Titre(c.Nom.Length > 0 ? c.Nom : c.Type);
        if (c.Type == Calque.TypeTexte)
        {
            inspecteur.Children.Add(new TextBlock { Text = "Texte (*mot* = couleur d'accent)", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 4, 0, 3) });
            var tb = new TextBox { Style = S("Champ"), MinHeight = 64, FontSize = 14, Text = c.Texte, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top };
            tb.GotKeyboardFocus += (_, _) => memorise = false;
            tb.TextChanged += (_, _) =>
            {
                if (tb.Text == c.Texte) return;
                Memoriser();
                c.Texte = tb.Text;
                if (c.Role == "titre" && titre.Text != tb.Text) titre.Text = tb.Text;
                if (c.Role == "sous" && sousTitre.Text != tb.Text) sousTitre.Text = tb.Text;
                Changer(true, false);
            };
            champTexte = tb;
            inspecteur.Children.Add(tb);
            Liste("Police", RenduMiniature.Polices.Select(p => (p, p)).ToArray(), () => c.Police, v => c.Police = v);
            Curseur("Taille", 12, 360, () => c.Taille, v => c.Taille = v, v => $"{v:0}");
            var ajuster = new Button { Style = S("BtnPetit"), Content = "Ajuster la taille au cadre", Height = 28, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            ajuster.Click += (_, _) => { memorise = false; Memoriser(); c.Taille = TexteMiniature.TailleQuiTient(c); memorise = false; Changer(true, false); };
            inspecteur.Children.Add(ajuster);
            Liste("Graisse", new[] { ("400", "Normale"), ("600", "Demi-gras"), ("700", "Gras"), ("800", "Extra-gras"), ("900", "Noir") }, () => (c.Poids >= 850 ? 900 : c.Poids >= 750 ? 800 : c.Poids >= 650 ? 700 : c.Poids >= 500 ? 600 : 400).ToString(), v => c.Poids = int.Parse(v));
            Liste("Alignement", new[] { ("gauche", "À gauche"), ("centre", "Centré"), ("droite", "À droite") }, () => c.Alignement, v => c.Alignement = v);
            Case("MAJUSCULES", () => c.Majuscules, v => c.Majuscules = v);
            Case("Italique", () => c.Italique, v => c.Italique = v);
            Curseur("Interligne", 0.6, 1.8, () => c.Interligne, v => c.Interligne = v, v => $"{v:0.00}", 0.01);
            Couleur("Couleur du texte", () => c.Couleur, v => c.Couleur = v);
            Couleur("Couleur d'accent (*mots*)", () => c.Accent, v => c.Accent = v);
            Section("Contour");
            Curseur("Épaisseur", 0, 30, () => c.Epaisseur, v => c.Epaisseur = v, v => v < 0.5 ? "aucun" : $"{v:0}");
            Couleur("Couleur du contour", () => c.Contour, v => c.Contour = v);
            Section("Ombre ou lueur");
            Case("Ombre", () => c.Ombre, v => c.Ombre = v);
            Curseur("Flou", 0, 80, () => c.OmbreFlou, v => c.OmbreFlou = v, v => $"{v:0}");
            Curseur("Distance (0 : lueur autour)", 0, 40, () => c.OmbreDistance, v => c.OmbreDistance = v, v => $"{v:0}");
            Couleur("Couleur de l'ombre", () => c.OmbreCouleur, v => c.OmbreCouleur = v);
            Section("Fond (badge, bandeau)");
            Couleur("Couleur du fond", () => c.FondTexte.Length > 0 ? c.FondTexte : "#00000000", v => c.FondTexte = v == "#00000000" ? "" : v, true);
            Curseur("Arrondi", 0, 80, () => c.Arrondi, v => c.Arrondi = v, v => $"{v:0}");
            Curseur("Marge", 0, 80, () => c.Marge, v => c.Marge = v, v => $"{v:0}");
        }
        else if (c.Type == Calque.TypeImage)
        {
            inspecteur.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(c.Chemin), FontSize = 12, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
            var remplacer = new Button { Style = S("BtnPetit"), Content = "Remplacer l'image…", Height = 28, Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            remplacer.Click += (_, _) =>
            {
                var d = new OpenFileDialog { Title = "Remplacer l'image", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
                if (d.ShowDialog(Window.GetWindow(this)) != true) return;
                memorise = false; Memoriser(); c.Chemin = d.FileName; memorise = false; Changer();
            };
            inspecteur.Children.Add(remplacer);
            Liste("Dans le cadre", new[] { ("remplir", "Remplir (coupe les bords)"), ("ajuster", "Toute l'image") }, () => c.Ajustement, v => c.Ajustement = v);
            Curseur("Zoom", 0.5, 4, () => c.Zoom, v => c.Zoom = v, v => $"{v * 100:0} %", 0.01);
            Curseur("Cadrage horizontal", 0, 1, () => c.CadrageX, v => c.CadrageX = v, v => $"{v * 100:0} %", 0.01);
            Curseur("Cadrage vertical", 0, 1, () => c.CadrageY, v => c.CadrageY = v, v => $"{v * 100:0} %", 0.01);
            Section("Retouche");
            Curseur("Luminosité", -0.8, 0.8, () => c.Luminosite, v => c.Luminosite = v, v => $"{v * 100:+0;-0;0}", 0.01);
            Curseur("Contraste", 0.4, 2, () => c.Contraste, v => c.Contraste = v, v => $"{v * 100:0} %", 0.01);
            Curseur("Saturation", 0, 2.5, () => c.Saturation, v => c.Saturation = v, v => $"{v * 100:0} %", 0.01);
            Curseur("Flou", 0, 40, () => c.Flou, v => c.Flou = v, v => $"{v:0}");
            Case("Miroir (retourner)", () => c.Miroir, v => c.Miroir = v);
            Section("Cadre");
            Curseur("Arrondi", 0, 360, () => c.Arrondi, v => c.Arrondi = v, v => $"{v:0}");
            Curseur("Bordure", 0, 30, () => c.EpaisseurBordure, v => c.EpaisseurBordure = v, v => $"{v:0}");
            Couleur("Couleur de la bordure", () => c.Bordure, v => c.Bordure = v);
        }
        else if (c.Type == Calque.TypeDegrade)
        {
            Liste("D'où vient l'ombre", new[] { ("gauche", "De la gauche"), ("droite", "De la droite"), ("haut", "Du haut"), ("bas", "Du bas"), ("vignette", "Des bords (vignette)") }, () => c.Direction, v => c.Direction = v);
            Curseur("Force", 0, 1, () => c.Force, v => c.Force = v, v => $"{v * 100:0} %", 0.01);
            Curseur("Étendue", 0.05, 1, () => c.Etendue, v => c.Etendue = v, v => $"{v * 100:0} %", 0.01);
            Couleur("Couleur", () => c.Remplissage, v => c.Remplissage = v);
        }
        else
        {
            Liste("Forme", new[] { ("rectangle", "Rectangle"), ("cercle", "Cercle"), ("fleche", "Flèche") }, () => c.FormeType, v => c.FormeType = v);
            Couleur("Remplissage", () => c.Remplissage, v => c.Remplissage = v, true);
            Curseur("Arrondi", 0, 200, () => c.Arrondi, v => c.Arrondi = v, v => $"{v:0}");
            Curseur("Bordure", 0, 30, () => c.EpaisseurBordure, v => c.EpaisseurBordure = v, v => $"{v:0}");
            Couleur("Couleur de la bordure", () => c.Bordure, v => c.Bordure = v);
        }
        Section("Place");
        Nombres(("X", () => c.X, v => c.X = v), ("Y", () => c.Y, v => c.Y = v), ("Largeur", () => c.L, v => c.L = Math.Max(4, v)), ("Hauteur", () => c.H, v => c.H = Math.Max(4, v)));
        Curseur("Rotation", -180, 180, () => c.Rotation, v => c.Rotation = v, v => $"{v:0}°");
        Curseur("Opacité", 0, 1, () => c.Opacite, v => c.Opacite = v, v => $"{v * 100:0} %", 0.01);
        var outils = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        outils.Children.Add(Bouton("Centrer", "Au milieu de l'image (horizontalement)", () => { memorise = false; Memoriser(); c.X = Math.Round((W - c.L) / 2); memorise = false; Changer(true, false); }));
        outils.Children.Add(Bouton("Dupliquer (Ctrl D)", "Une copie juste au-dessus", Dupliquer));
        var sup = Bouton("Supprimer", "Retirer ce calque (Ctrl Z revient)", Supprimer);
        sup.Style = S("BtnDanger");
        outils.Children.Add(sup);
        inspecteur.Children.Add(outils);
    }

    void MajConseils()
    {
        conseils.Children.Clear();
        var l = projet.Conseils();
        if (l.Count == 0)
            conseils.Children.Add(new TextBlock { Text = "✓ Lisible sur téléphone, rien sous le minutage.", FontSize = 12.5, Foreground = P("Vert"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        foreach (var c in l)
            conseils.Children.Add(new TextBlock { Text = "⚠ " + c, FontSize = 12.5, Foreground = P("Or"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
    }

    /// <summary>Comment YouTube montrera la miniature : la grande vignette de l'accueil et la petite des suggestions (thème sombre de YouTube).</summary>
    Border ApercuYouTube()
    {
        var fond = new SolidColorBrush(Color.FromRgb(15, 15, 15));
        var pile = new StackPanel();
        Grid Vignette(double l, double h, VisualBrush brosse, double taillePolice)
        {
            var g = new Grid { Width = l, Height = h };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(l > 200 ? 10 : 6), ClipToBounds = true, Background = Brushes.Black, Child = new Rectangle { Fill = brosse } });
            var duree = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = "1:24:05", Foreground = Brushes.White, FontSize = taillePolice, FontWeight = FontWeights.SemiBold },
            };
            g.Children.Add(duree);
            return g;
        }
        pile.Children.Add(Vignette(310, 174, miroirGrand, 12));
        var infos = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var avatar = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = new SolidColorBrush(Color.FromRgb(147, 0, 0)), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(avatar, Dock.Left);
        infos.Children.Add(avatar);
        var textes = new StackPanel();
        ytTitre.Text = projet.Nom; ytTitre.Foreground = Brushes.White; ytTitre.FontSize = 14; ytTitre.FontWeight = FontWeights.SemiBold; ytTitre.TextWrapping = TextWrapping.Wrap; ytTitre.MaxHeight = 40;
        textes.Children.Add(ytTitre);
        textes.Children.Add(new TextBlock { Text = (App.Donnees.Reglages.Direct.Chaine is { Length: > 0 } ch ? ch : "Votre chaîne") + "\n128 vues · il y a 2 heures", Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        infos.Children.Add(textes);
        pile.Children.Add(infos);
        var suggestion = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var petite = Vignette(168, 94, miroirPetit, 10);
        petite.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(petite, Dock.Left);
        suggestion.Children.Add(petite);
        ytTitrePetit.Text = projet.Nom; ytTitrePetit.Foreground = Brushes.White; ytTitrePetit.FontSize = 12.5; ytTitrePetit.FontWeight = FontWeights.SemiBold; ytTitrePetit.TextWrapping = TextWrapping.Wrap;
        var t2 = new StackPanel();
        t2.Children.Add(ytTitrePetit);
        t2.Children.Add(new TextBlock { Text = "Suggestion · téléphone", Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        suggestion.Children.Add(t2);
        pile.Children.Add(suggestion);
        return new Border { Background = fond, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = pile };
    }

    // ================================================================================================ export
    string Exporter(string fichier)
    {
        if (System.IO.Path.GetExtension(fichier).ToLowerInvariant() == ".png") File.WriteAllBytes(fichier, RenduMiniature.Png(projet));
        else
        {
            var (o, q) = RenduMiniature.Jpeg(projet);
            File.WriteAllBytes(fichier, o);
            if (q < 85) Journal.Info($"miniature : qualité JPEG {q} pour rester sous 2 Mo");
        }
        return fichier;
    }

    void Exporter()
    {
        Directory.CreateDirectory(Dossier);
        var d = new SaveFileDialog { Title = "Exporter la miniature", Filter = "JPEG (YouTube, moins de 2 Mo)|*.jpg|PNG|*.png", FileName = RecordingFiles.SafeName(projet.Nom) + ".jpg", InitialDirectory = Dossier };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            Exporter(d.FileName);
            SonUi.Toast($"Miniature exportée : {System.IO.Path.GetFileName(d.FileName)} ({SonUi.Taille(new FileInfo(d.FileName).Length)}).");
        }
        catch (Exception e) { Journal.Erreur("miniature : export", e); SonUi.Toast("Export impossible : " + e.Message); }
    }

    /// <summary>Exporte en JPG (moins de 2 Mo) et la met dans « Publier sur YouTube ». Rend le fichier, ou null.</summary>
    string? UtiliserPourPublication()
    {
        try
        {
            Directory.CreateDirectory(Dossier);
            var f = Exporter(System.IO.Path.Combine(Dossier, RecordingFiles.SafeName(projet.Nom) + ".jpg"));
            var r = ReglagesPublication.Lire();
            r.Miniature = f;
            r.Sauver();
            Sauver();
            MiniaturePrete?.Invoke(f);
            SonUi.Toast($"Miniature prête ({SonUi.Taille(new FileInfo(f).Length)}) : la prochaine publication sur YouTube l'utilise.");
            return f;
        }
        catch (Exception e) { Journal.Erreur("miniature : publication", e); SonUi.Toast("Export impossible : " + e.Message); return null; }
    }

    // ================================================================================================ pour le test automatique
    internal ProjetMiniature TestProjet => projet;
    internal void TestModele(string id) => AppliquerModele(id);
    internal void TestTitre(string t) => titre.Text = t;
    internal string? TestUtiliser() => UtiliserPourPublication();
}
