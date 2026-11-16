using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Réglages › Son : ce qui ne change presque jamais (le moteur du son, les haut-parleurs de l'église, le câble pour Zoom,
/// le dossier des enregistrements). Tout le reste se fait dans la page Son.
/// Réglages › Licences : le code source et les licences (exigé par la GNU GPL de libobs et de FFmpeg).
/// </summary>
public sealed class ReglagesSon : StackPanel
{
    public const string DepotSiloe = "https://github.com/capmaster2023/sileostream";
    public const string DepotObs = "https://github.com/obsproject/obs-studio/tree/33.0.1";
    public const string SiteVbCable = "https://www.vb-cable.com";
    public const string SourcesFfmpeg = "https://ffmpeg.org/releases/";
    const string LicenceGplEnLigne = "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html";

    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    readonly TextBlock moteur = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly System.Windows.Shapes.Ellipse point = SonUi.Point();
    readonly Button reparer;
    readonly ContentControl eglise = new() { Focusable = false };
    readonly TextBlock cable = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock progression = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    readonly Button installer;
    bool aRemplir = true;

    /// <summary>Les sections, pour le sommaire de la page Réglages.</summary>
    public Border Son { get; }
    public Border Licences { get; }

    public ReglagesSon()
    {
        reparer = new Button { Style = S("BtnOr"), Content = "Réparer", Height = 38, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        reparer.ToolTip = "Télécharge Siloé Stream sur siloestream.tech et le réinstalle (chants, cultes et réglages gardés)";
        reparer.Click += (_, _) => ActionsSon.Reparer(reparer, Window.GetWindow(this));
        installer = new Button { Style = S("BtnOr"), Content = "Installer le câble (gratuit)", Height = 40, Padding = new Thickness(16, 0, 16, 0), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        installer.ToolTip = "Télécharge VB-CABLE sur vb-audio.com puis lance son installateur (Windows demande votre accord)";
        installer.Click += async (_, _) =>
        {
            installer.IsEnabled = false;
            progression.Visibility = Visibility.Visible;
            await ActionsSon.InstallerCable(new Progress<string>(t => progression.Text = t));
            installer.IsEnabled = true;
            MajCable();
        };
        Children.Add(Son = SectionSon());
        Children.Add(Licences = SectionLicences());
        A.Changed += () => Dispatcher.BeginInvoke(new Action(MajMoteur));
        A.PeripheriquesChange += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) { RemplirEglise(); MajCable(); } else aRemplir = true; }));
        IsVisibleChanged += (_, _) => { if (IsVisible) { if (aRemplir) RemplirEglise(); MajMoteur(); MajCable(); } };
        RemplirEglise();
        MajMoteur();
        MajCable();
    }

    // ------------------------------------------------------------ petits outils de mise en page
    static (Border Cadre, StackPanel Corps) Section(string titre, string sousTitre)
    {
        var corps = new StackPanel();
        corps.Children.Add(new TextBlock { Text = titre, Style = S("SousTitre"), Margin = new Thickness(0) });
        corps.Children.Add(new TextBlock { Text = sousTitre, Style = S("Muet"), FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 6, 0, 0) });
        return (new Border { Style = S("Section"), Child = corps }, corps);
    }

    static FrameworkElement Champ(string libelle, UIElement controle, double haut = 18)
    {
        var p = new StackPanel { Margin = new Thickness(0, haut, 0, 0) };
        p.Children.Add(new TextBlock { Text = libelle, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = P("Encre2"), Margin = new Thickness(0, 0, 0, 6) });
        p.Children.Add(controle);
        return p;
    }

    static TextBlock Note(string texte, double haut = 8) => new() { Text = texte, FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, haut, 0, 0), LineHeight = 18 };

    /// <summary>Un lien cliquable (ouvre le navigateur).</summary>
    static TextBlock Lien(string texte, string adresse, double taille = 13)
    {
        var h = new Hyperlink(new Run(texte)) { Foreground = P("Or"), Cursor = System.Windows.Input.Cursors.Hand };
        h.Click += (_, _) => SonUi.Ouvrir(adresse);
        var t = new TextBlock { FontSize = taille, TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(h);
        return t;
    }

    // ------------------------------------------------------------ Son
    Border SectionSon()
    {
        var (cadre, corps) = Section("Son", "Les micros, la musique, l'enregistrement et Zoom se règlent dans la page Son (barre de gauche). Ici, ce qui ne change presque jamais.");
        var ouvrir = SonUi.Bouton("Ouvrir la page Son", SonUi.HautParleur, "BtnOr", 42);
        ouvrir.HorizontalAlignment = HorizontalAlignment.Left;
        ouvrir.Margin = new Thickness(0, 14, 0, 0);
        ouvrir.Click += (_, _) => SonUi.Principale?.MontrerSon();
        corps.Children.Add(ouvrir);

        // le moteur du son (OBS)
        var ligneMoteur = new DockPanel();
        DockPanel.SetDock(reparer, Dock.Right);
        ligneMoteur.Children.Add(reparer);
        DockPanel.SetDock(point, Dock.Left);
        ligneMoteur.Children.Add(point);
        ligneMoteur.Children.Add(moteur);
        corps.Children.Add(Champ("Moteur du son", ligneMoteur));

        // les haut-parleurs de l'église
        corps.Children.Add(Champ("Haut-parleurs de l'église", eglise));
        corps.Children.Add(Note("Le son des vidéos et des musiques sort par ici. Les micros n'y passent pas (ils passent déjà par la table de mixage)."));

        // le câble pour Zoom
        var boite = new Border { Background = P("Nuit2"), BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12) };
        var pile = new StackPanel();
        pile.Children.Add(cable);
        pile.Children.Add(installer);
        pile.Children.Add(progression);
        boite.Child = pile;
        corps.Children.Add(Champ("Câble audio virtuel (pour Zoom, OBS, Teams)", boite));
        corps.Children.Add(Note(VbCable.Mention, 8));
        var site = Lien("www.vb-cable.com", SiteVbCable, 12.5);
        site.Margin = new Thickness(0, 2, 0, 0);
        corps.Children.Add(site);

        // le dossier des enregistrements
        var dossier = new TextBox { Style = S("Champ"), Text = AudioConfiguration.DossierEnregistrements, IsReadOnly = true, FontSize = 14 };
        var ouvrirDossier = new Button { Style = S("Btn"), Content = "Ouvrir", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        ouvrirDossier.Click += (_, _) => ActionsSon.OuvrirDossier(AudioConfiguration.DossierEnregistrements);
        // 2.1.0 : le dossier se choisit (celui de « Commencer le culte »)
        var choisirDossier = new Button { Style = S("Btn"), Content = "Choisir…", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        choisirDossier.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier des enregistrements (Commencer le culte enregistre ici)" };
            if (Directory.Exists(AudioConfiguration.DossierEnregistrements)) d.InitialDirectory = AudioConfiguration.DossierEnregistrements;
            bool? ok = Window.GetWindow(choisirDossier) is { } fenetre ? d.ShowDialog(fenetre) : d.ShowDialog();
            if (ok != true || string.IsNullOrWhiteSpace(d.FolderName)) return;
            if (ActionsSon.ChoisirDossierEnregistrements(d.FolderName)) dossier.Text = AudioConfiguration.DossierEnregistrements;
        };
        var ligneDossier = new DockPanel();
        DockPanel.SetDock(ouvrirDossier, Dock.Right);
        ligneDossier.Children.Add(ouvrirDossier);
        DockPanel.SetDock(choisirDossier, Dock.Right);
        ligneDossier.Children.Add(choisirDossier);
        ligneDossier.Children.Add(dossier);
        corps.Children.Add(Champ("Dossier des enregistrements (un sous-dossier par enregistrement)", ligneDossier));
        bool ffmpeg = File.Exists(ChurchAudioService.FfmpegInclus);
        var t = Note(ffmpeg
            ? "MP3 et vidéo : FFmpeg est inclus avec Siloé Stream, rien à installer. La vidéo utilise la carte graphique (NVIDIA, Intel, AMD) quand elle le peut ; le son et l'image se calent tout seuls."
            : "FFmpeg manque dans le dossier de Siloé Stream : réinstallez Siloé Stream pour enregistrer en MP3 ou en vidéo (le WAV marche quand même).", 10);
        if (!ffmpeg) t.Foreground = P("Danger");
        corps.Children.Add(t);
        return cadre;
    }

    void MajMoteur()
    {
        bool panne = A.ObsAbsent || (!A.Running && A.Diagnostic.StartsWith("Le moteur du son ne démarre pas", StringComparison.Ordinal));
        moteur.Text = A.Running ? $"Prêt · moteur d'OBS {A.Version} · 48 kHz stéréo" + (A.TracksActive ? "" : " · mélange pour l'enregistrement et Zoom indisponible (pilote de la carte graphique)") : A.Diagnostic;
        moteur.Foreground = P(panne ? "RougeTexte" : "Blanc");
        point.Fill = P(A.Running && A.TracksActive ? "Vert" : panne ? "Direct" : "Or");
        reparer.Visibility = panne ? Visibility.Visible : Visibility.Collapsed;
        if (aRemplir && A.Running && IsVisible) RemplirEglise();
    }

    void RemplirEglise()
    {
        aRemplir = false;
        eglise.Content = SonUi.ListeSorties(C.Eglise.Appareil, C.Eglise.NomAppareil, (id, nom) =>
        {
            C.Eglise.Appareil = id;
            C.Eglise.NomAppareil = nom;
            if (C.Casque.Actif && ActionsSon.MemeSortie(C.Casque.Appareil, id))
            {
                C.Casque.Actif = false;
                SonUi.Toast("Casque éteint : il avait la même sortie que l'église.");
            }
            A.Apply();
        }, "Haut-parleurs de Windows (conseillé)");
    }

    void MajCable()
    {
        var trouve = VbCable.Entree();
        cable.Text = trouve is { } c
            ? $"Prêt : {c.Nom}. Dans Zoom, OBS ou Teams, choisissez le micro « CABLE Output »."
            : "Pas encore installé. Il fait de Siloé Stream un micro pour Zoom, OBS ou Teams.";
        cable.Foreground = P(trouve != null ? "Blanc" : "Encre2");
        installer.Visibility = VbCable.Installe ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------ code source et licences (GPL)
    static Border SectionLicences()
    {
        var (cadre, corps) = Section("Code source et licences", "Siloé Stream est un logiciel libre : son code source et celui des programmes qu'il inclut sont à votre disposition.");
        var texte = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 10, 0, 0) };
        texte.Inlines.Add(new Run("Son moteur du son utilise "));
        texte.Inlines.Add(new Bold(new Run("libobs")));
        texte.Inlines.Add(new Run(", la bibliothèque d'OBS Studio 33.0.1, distribuée sous licence GNU GPL version 2 ou ultérieure."));
        corps.Children.Add(texte);
        corps.Children.Add(LigneDepot("Code source de Siloé Stream", DepotSiloe));
        corps.Children.Add(LigneDepot("Code source d'OBS Studio 33.0.1 (libobs)", DepotObs));
        corps.Children.Add(LigneDepot("Code source de FFmpeg (ffmpeg.exe 8.0.1, build gyan.dev)", SourcesFfmpeg));

        var licence = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var lire = new Button { Style = S("BtnContour"), Content = "Lire la licence GNU GPL", Height = 36, Padding = new Thickness(14, 0, 14, 0), FontSize = 13.5 };
        lire.Click += (_, _) =>
        {
            string copie = Path.Combine(AppContext.BaseDirectory, "COPYING.txt");
            SonUi.Ouvrir(File.Exists(copie) ? copie : LicenceGplEnLigne);
        };
        licence.Children.Add(lire);
        licence.Children.Add(new TextBlock { Text = "Le texte complet de la licence est installé avec Siloé Stream (COPYING.txt).", FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        corps.Children.Add(licence);
        corps.Children.Add(Note("FFmpeg (www.ffmpeg.org), livré dans le dossier « ffmpeg » de Siloé Stream pour le MP3 et la vidéo, est un programme libre sous licence GNU GPL version 3 : son texte est dans ffmpeg\\LICENSE.txt. Build « essentials » de gyan.dev (www.gyan.dev/ffmpeg/builds).", 14));
        corps.Children.Add(Note(VbCable.Mention + " Il n'est pas inclus dans Siloé Stream : il s'installe à la demande depuis le site officiel.", 8));
        corps.Children.Add(Note("Polices Manrope, IBM Plex Mono, Literata, Atkinson Hyperlegible Next et Unbounded : licence SIL Open Font License 1.1.", 6));
        return cadre;
    }

    static Border LigneDepot(string titre, string adresse)
    {
        var g = new DockPanel();
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ouvrir = new Button { Style = S("Btn"), Content = "Ouvrir", Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13 };
        ouvrir.Click += (_, _) => SonUi.Ouvrir(adresse);
        var copier = new Button { Style = S("Btn"), Content = "Copier le lien", Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13, Margin = new Thickness(8, 0, 0, 0) };
        copier.Click += async (_, _) =>
        {
            try { Clipboard.SetText(adresse); } catch (Exception) { return; }
            copier.Content = "Lien copié";
            await Task.Delay(1600);
            copier.Content = "Copier le lien";
        };
        boutons.Children.Add(ouvrir);
        boutons.Children.Add(copier);
        DockPanel.SetDock(boutons, Dock.Right);
        g.Children.Add(boutons);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14, FontWeight = FontWeights.SemiBold });
        var lien = Lien(adresse, adresse);
        lien.Margin = new Thickness(0, 2, 0, 0);
        textes.Children.Add(lien);
        g.Children.Add(textes);
        return new Border { Child = g, BorderBrush = P("Trait"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
    }
}
