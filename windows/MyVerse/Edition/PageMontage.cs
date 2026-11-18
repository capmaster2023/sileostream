using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MyVerse.Audio;
using MyVerse.Montages;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui.Edition;

/// <summary>
/// Siloé Stream 2.2.0 : la page Montage. À gauche, les enregistrements (ceux de Siloé Stream, ceux d'OBS dans Vidéos, et les
/// fichiers importés) ; au centre, l'aperçu ; à droite, les réglages de ce qui est choisi (ou du projet) et les publications en
/// cours ; en bas, la ligne de temps. Tout se sauvegarde tout seul ; « Annuler » (Ctrl Z) revient en arrière.
/// Raccourcis : Espace lire, S couper, I / O marquer le début / la fin, Suppr supprimer, ← → une image (Maj : une seconde),
/// Début / Fin, Ctrl Z / Ctrl Y, Ctrl S, + / − zoom.
/// </summary>
public sealed class PageMontage : Grid
{
    static Brush P(string c) => MainWindow.Pinceau(c);
    static Style S(string c) => MainWindow.Style_(c);
    static string Ffmpeg => App.Audio.Ffmpeg;
    public static string DossierProjets => Path.Combine(AudioConfiguration.DossierEnregistrements, "Montages");
    static string FichierDernier => Path.Combine(App.Donnees.Dossier, "montage", "dernier.txt");
    static readonly Dictionary<(int, int), List<EncodeurVideo>> encodeursDetectes = new();

    ProjetMontage projet = new();
    string? chemin;
    bool modifie;
    readonly List<string> annulations = new(), retablissements = new();
    readonly Dictionary<string, InfoMedia> infos = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> erreursAnalyse = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> enAnalyse = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer minuterieSauvegarde = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly DispatcherTimer minuterieMedias = new() { Interval = TimeSpan.FromMilliseconds(300) };
    List<FileInfo> enregistrements = new();

    readonly TextBox nom;
    readonly TextBlock etat = new() { FontSize = 12, Margin = new Thickness(2, 2, 0, 0) };
    readonly TextBlock resume = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, Margin = new Thickness(0, 0, 14, 0) };
    readonly StackPanel listeMedias = new();
    readonly ApercuMontage apercu = new();
    readonly TextBlock temps = new() { FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0), MinWidth = 190, TextAlignment = TextAlignment.Center };
    readonly TextBlock infoElement = new() { FontSize = 12.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
    readonly Button lire;
    readonly StackPanel inspecteur = new();
    readonly StackPanel cartes = new();
    readonly TextBlock titreCartes;
    readonly ScrollBar barre = new() { Orientation = Orientation.Horizontal, Height = 14 };
    readonly TimelineMontage ligne;
    object? selection;
    double tete;
    /// <summary>La modification en cours (un champ, un curseur) est déjà mémorisée pour « Annuler ».</summary>
    bool memorise;

    public PageMontage()
    {
        Background = P("Page");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 240 });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(372), MinHeight = 160 });
        ligne = new TimelineMontage(barre);

        // ============================================================ barre du haut
        var haut = new DockPanel { Margin = new Thickness(22, 12, 22, 10) };
        var publier = SonUi.Bouton("Publier sur YouTube", (Geometry)Application.Current.FindResource("IconeDirect"), "BtnRouge", 40);
        publier.ToolTip = "Rendu et envoi en même temps : la vidéo part pendant qu'elle se fabrique (pas de fichier exporté à attendre)";
        var exporter = SonUi.Bouton("Exporter un fichier…", null, "Btn", 40);
        exporter.Margin = new Thickness(0, 0, 10, 0);
        exporter.ToolTip = "Écrire le montage dans un fichier MP4 (ou MKV)";
        var droite = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        resume.Foreground = P("Brume");
        droite.Children.Add(resume);
        droite.Children.Add(exporter);
        droite.Children.Add(publier);
        DockPanel.SetDock(droite, Dock.Right);
        haut.Children.Add(droite);
        var gauche = new StackPanel { Orientation = Orientation.Horizontal };
        var blocNom = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        nom = new TextBox
        {
            FontSize = 20, FontWeight = FontWeights.ExtraBold, Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 1), MinWidth = 240, MaxWidth = 420,
            Foreground = P("Blanc"), CaretBrush = P("Blanc"), Padding = new Thickness(0, 2, 0, 2), ToolTip = "Le nom du montage (et du fichier du projet)",
        };
        nom.BorderBrush = P("Nuit3");
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
        Petit("Nouveau", "Un nouveau montage (celui-ci reste enregistré)", Nouveau);
        Petit("Ouvrir…", "Ouvrir un projet de montage", Ouvrir);
        Petit("Enregistrer sous…", "Enregistrer le projet ailleurs ou sous un autre nom (il s'enregistre déjà tout seul)", EnregistrerSous);
        Petit("↶", "Annuler (Ctrl Z)", Annuler).FontSize = 16;
        Petit("↷", "Rétablir (Ctrl Y)", Retablir).FontSize = 16;
        haut.Children.Add(gauche);
        Children.Add(haut);
        nom.TextChanged += (_, _) =>
        {
            if (nom.Text.Trim() == projet.Nom) return;
            Memoriser();
            projet.Nom = nom.Text.Trim().Length > 0 ? nom.Text.Trim() : "Montage";
            Changer(false);
        };
        nom.GotKeyboardFocus += (_, _) => memorise = false;
        publier.Click += (_, _) => OuvrirPublication();
        exporter.Click += (_, _) => Exporter();

        // ============================================================ milieu : médias, aperçu, réglages
        var milieu = new Grid { Margin = new Thickness(22, 0, 22, 8) };
        milieu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        milieu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        milieu.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        SetRow(milieu, 1);
        Children.Add(milieu);

        // ---------- médias
        var colMedias = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var teteMedias = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var importer = new Button { Style = S("BtnPetit"), Content = "+ Importer…", Height = 30, Padding = new Thickness(10, 0, 10, 0), ToolTip = "Ajouter des vidéos ou des sons (OBS, caméra, téléphone, musique…)" };
        importer.Click += (_, _) => Importer();
        DockPanel.SetDock(importer, Dock.Right);
        teteMedias.Children.Add(importer);
        teteMedias.Children.Add(new Capitales { Libelle = "Médias", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(teteMedias, Dock.Top);
        colMedias.Children.Add(teteMedias);
        var aideMedias = new TextBlock { Text = "Double-clic : à la suite sur la piste vidéo. Les enregistrements de Siloé Stream et ceux d'OBS (dossier Vidéos) sont listés tout seuls.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = P("Brume"), LineHeight = 17 };
        DockPanel.SetDock(aideMedias, Dock.Top);
        colMedias.Children.Add(aideMedias);
        colMedias.Children.Add(new ScrollViewer { Content = listeMedias, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        milieu.Children.Add(colMedias);

        // ---------- aperçu et transport
        var colApercu = new DockPanel();
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        Button T(string texte, string tip, Action clic, double largeur = 40)
        {
            var b = new Button { Style = S("Btn"), Content = texte, Width = largeur, Height = 36, Padding = new Thickness(0), Margin = new Thickness(3, 0, 3, 0), ToolTip = tip, FontSize = 14 };
            b.Click += (_, _) => { clic(); ligne.Focus(); };
            transport.Children.Add(b);
            return b;
        }
        T("⏮", "Au début (Début)", () => AllerA(0));
        T("−5s", "5 secondes avant (Maj ←)", () => AllerA(tete - 5), 48);
        T("◂", "Une image avant (←)", () => AllerA(tete - 1.0 / 30));
        lire = T("▶", "Lire / pause (Espace)", () => apercu.Basculer(), 56);
        lire.Style = S("BtnOr");
        T("▸", "Une image après (→)", () => AllerA(tete + 1.0 / 30));
        T("+5s", "5 secondes après (Maj →)", () => AllerA(tete + 5), 48);
        T("⏭", "À la fin (Fin)", () => AllerA(projet.Duree));
        transport.Children.Add(temps);
        T("I", "Marquer le début de la séquence ici (I) : ce qui est avant est retiré", MarquerDebut);
        T("O", "Marquer la fin de la séquence ici (O) : ce qui est après est retiré", MarquerFin);
        T("✂", "Couper la séquence ici (S)", Couper);
        T("🖼", "Envoyer cette image dans Miniature (pleine qualité, pour la miniature YouTube)", EnvoyerMiniature);
        temps.Foreground = P("Blanc");
        infoElement.Foreground = P("Brume");
        var basApercu = new StackPanel();
        basApercu.Children.Add(transport);
        basApercu.Children.Add(infoElement);
        DockPanel.SetDock(basApercu, Dock.Bottom);
        colApercu.Children.Add(basApercu);
        colApercu.Children.Add(apercu);
        SetColumn(colApercu, 1);
        milieu.Children.Add(colApercu);
        apercu.Ffmpeg = () => Ffmpeg;
        apercu.Avance += t => { tete = t; ligne.ChoisirTete(t); MajTemps(); };
        apercu.LectureChange += l => lire.Content = l ? "❚❚" : "▶";

        // ---------- réglages et publications
        var colDroite = new StackPanel();
        colDroite.Children.Add(inspecteur);
        titreCartes = new Capitales { Libelle = "Publications et exports", Margin = new Thickness(0, 22, 0, 10), Visibility = Visibility.Collapsed };
        colDroite.Children.Add(titreCartes);
        colDroite.Children.Add(cartes);
        var defilDroite = new ScrollViewer { Content = colDroite, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(16, 0, 0, 0), Padding = new Thickness(0, 0, 6, 0) };
        SetColumn(defilDroite, 2);
        milieu.Children.Add(defilDroite);

        // ============================================================ la ligne de temps
        var separateur = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = P("Nuit3"), ResizeDirection = GridResizeDirection.Rows, Cursor = Cursors.SizeNS };
        SetRow(separateur, 2);
        Children.Add(separateur);
        var bas = new DockPanel { Margin = new Thickness(0) };
        var outils = new WrapPanel { Margin = new Thickness(22, 8, 22, 6) };
        Button O(string texte, string tip, Action clic, string style = "BtnPetit")
        {
            var b = new Button { Style = S(style), Content = texte, Height = 30, Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
            b.Click += (_, _) => { clic(); ligne.Focus(); };
            outils.Children.Add(b);
            return b;
        }
        O("✂ Couper (S)", "Couper la séquence à la tête de lecture", Couper);
        O("Supprimer (Suppr)", "Supprimer ce qui est choisi", Supprimer);
        O("+ Titre", "Un titre à la tête de lecture (nom du prédicateur, référence…)", AjouterTitre);
        O("Générique de début", "Ajouter ou retirer le générique de début", () => BasculerGenerique(projet.Intro));
        O("Générique de fin", "Ajouter ou retirer le générique de fin", () => BasculerGenerique(projet.Outro));
        var aimant = new CheckBox { Content = "Aimant", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), ToolTip = "Les bords se collent à la tête de lecture et aux autres bords (Alt : sans aimant)" };
        aimant.Checked += (_, _) => ligne.Aimant = true;
        aimant.Unchecked += (_, _) => ligne.Aimant = false;
        outils.Children.Add(aimant);
        O("−", "Zoom arrière (−, Ctrl molette)", () => ligne.Zoomer(0.7));
        O("+", "Zoom avant (+, Ctrl molette)", () => ligne.Zoomer(1.4));
        O("Tout voir", "Tout le montage dans la ligne de temps", () => ligne.Ajuster());
        DockPanel.SetDock(outils, Dock.Top);
        bas.Children.Add(outils);
        DockPanel.SetDock(barre, Dock.Bottom);
        barre.Margin = new Thickness(80, 0, 22, 6);
        bas.Children.Add(barre);
        bas.Children.Add(new ScrollViewer { Content = ligne, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(22, 0, 22, 4) });
        SetRow(bas, 3);
        Children.Add(bas);

        ligne.SelectionChange += o =>
        {
            bool autre = !ReferenceEquals(o, selection);
            selection = o;
            if (autre) { memorise = false; MajInspecteur(); }
            else MajValeursInspecteur();
        };
        ligne.TeteChange += t => { tete = t; apercu.Aller(t); MajTemps(); };
        ligne.AvantModification += Memoriser;
        ligne.Modifie += () => Changer();

        minuterieSauvegarde.Tick += (_, _) => { minuterieSauvegarde.Stop(); Enregistrer(); };
        minuterieMedias.Tick += (_, _) => { minuterieMedias.Stop(); MajMedias(); };
        Publications.Change += () => Dispatcher.BeginInvoke(new Action(MajPublications));
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { ChercherEnregistrements(); MajPublications(); }
            else { apercu.Arreter(); if (modifie) Enregistrer(); }
        };
        Loaded += (_, _) => ligne.Ajuster();

        ChargerDernier();
    }

    // ================================================================================================ projet
    void ChargerDernier()
    {
        try
        {
            if (File.Exists(FichierDernier) && File.ReadAllText(FichierDernier).Trim() is { Length: > 0 } c && File.Exists(c)) { Charger(ProjetMontage.Ouvrir(c), c); return; }
        }
        catch (Exception e) { Journal.Erreur("montage : dernier projet", e); }
        Charger(new ProjetMontage { Nom = "Culte du " + DateTime.Now.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("fr-FR")) }, null);
    }

    void Charger(ProjetMontage p, string? fichier)
    {
        apercu.Arreter();
        projet = p;
        chemin = fichier;
        modifie = false;
        annulations.Clear();
        retablissements.Clear();
        selection = null;
        tete = 0;
        nom.Text = p.Nom;
        ligne.Projet = p;
        ligne.Choisir(null);
        ligne.ChoisirTete(0);
        apercu.Projet(p);
        apercu.Aller(0);
        foreach (var f in p.Fichiers().Concat(p.Medias)) Analyser(f);
        int manquants = p.Fichiers().Count(f => !File.Exists(f));
        if (manquants > 0) SonUi.Toast($"{manquants} fichier{(manquants > 1 ? "s" : "")} du montage introuvable{(manquants > 1 ? "s" : "")} (déplacé ou supprimé ?).");
        MajTout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ligne.Ajuster()));
    }

    void MajTout()
    {
        ligne.Rafraichir();
        MajResume();
        MajTemps();
        MajInspecteur();
        MajEtat();
        MajMedias();
        MajPublications();
    }

    /// <summary>Garde l'état actuel pour « Annuler » (une fois par geste).</summary>
    void Memoriser()
    {
        if (memorise) return;
        memorise = true;
        annulations.Add(projet.VersJson());
        if (annulations.Count > 100) annulations.RemoveAt(0);
        retablissements.Clear();
    }

    /// <summary>Le projet a changé : ligne de temps, aperçu, résumé (et les réglages si <paramref name="inspecteurAussi"/>), sauvegarde un peu plus tard.</summary>
    void Changer(bool inspecteurAussi = true)
    {
        modifie = true;
        if (inspecteurAussi) memorise = false;
        minuterieSauvegarde.Stop();
        minuterieSauvegarde.Start();
        ligne.Rafraichir();
        apercu.Projet(projet);
        MajResume();
        MajTemps();
        MajEtat();
        if (inspecteurAussi) MajInspecteur();
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
        var p = ProjetMontage.DepuisJson(json);
        projet = p;
        ligne.Projet = p;
        selection = null;
        ligne.Choisir(null);
        nom.Text = p.Nom;
        memorise = false;
        Changer();
    }

    void Enregistrer()
    {
        try
        {
            chemin ??= NomLibre(DossierProjets, projet.Nom);
            projet.Enregistrer(chemin);
            modifie = false;
            Directory.CreateDirectory(Path.GetDirectoryName(FichierDernier)!);
            File.WriteAllText(FichierDernier, chemin);
        }
        catch (Exception e) { Journal.Erreur("montage : sauvegarde", e); SonUi.Toast("Le montage n'a pas pu être enregistré : " + e.Message); }
        MajEtat();
    }

    static string NomLibre(string dossier, string nomProjet)
    {
        Directory.CreateDirectory(dossier);
        string b = RecordingFiles.SafeName(nomProjet);
        for (int i = 1; i < 1000; i++)
        {
            var c = Path.Combine(dossier, b + (i == 1 ? "" : $" ({i})") + ProjetMontage.Extension);
            if (!File.Exists(c)) return c;
        }
        return Path.Combine(dossier, b + " " + Guid.NewGuid().ToString("N")[..6] + ProjetMontage.Extension);
    }

    void MajEtat()
    {
        etat.Text = (modifie ? "Modifié · enregistrement automatique…" : chemin != null ? "Enregistré · " + Path.GetFileName(chemin) : "Pas encore enregistré")
            + (annulations.Count > 0 ? " · Ctrl Z pour annuler" : "");
    }

    void Nouveau()
    {
        if (modifie) Enregistrer();
        Charger(new ProjetMontage { Nom = "Culte du " + DateTime.Now.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("fr-FR")) }, null);
        SonUi.Toast("Nouveau montage. L'ancien reste enregistré (Ouvrir…).");
    }

    void Ouvrir()
    {
        var d = new OpenFileDialog { Title = "Ouvrir un montage", Filter = "Montage Siloé Stream|*" + ProjetMontage.Extension, InitialDirectory = Directory.Exists(DossierProjets) ? DossierProjets : AudioConfiguration.DossierEnregistrements };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        if (modifie) Enregistrer();
        try { Charger(ProjetMontage.Ouvrir(d.FileName), d.FileName); Enregistrer(); }
        catch (Exception e) { SonUi.Toast("Ce montage ne s'ouvre pas : " + e.Message); }
    }

    void EnregistrerSous()
    {
        Directory.CreateDirectory(DossierProjets);
        var d = new SaveFileDialog { Title = "Enregistrer le montage", Filter = "Montage Siloé Stream|*" + ProjetMontage.Extension, FileName = RecordingFiles.SafeName(projet.Nom) + ProjetMontage.Extension, InitialDirectory = DossierProjets };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        chemin = d.FileName;
        Enregistrer();
        SonUi.Toast("Montage enregistré : " + Path.GetFileName(chemin));
    }

    // ================================================================================================ médias
    async void Analyser(string fichier)
    {
        if (infos.ContainsKey(fichier) || enAnalyse.Contains(fichier) || erreursAnalyse.ContainsKey(fichier) || !File.Exists(fichier)) return;
        enAnalyse.Add(fichier);
        try
        {
            var i = await AnalyseMedia.LireAsync(Ffmpeg, fichier);
            infos[fichier] = i;
            // la durée réelle (un enregistrement relu après coup)
            foreach (var s in projet.Sequences.Where(s => string.Equals(s.Source, fichier, StringComparison.OrdinalIgnoreCase))) s.DureeSource = i.Duree;
            foreach (var a in projet.Audios.Where(a => string.Equals(a.Source, fichier, StringComparison.OrdinalIgnoreCase))) a.DureeSource = i.Duree;
        }
        catch (Exception e) { erreursAnalyse[fichier] = e.Message; }
        finally { enAnalyse.Remove(fichier); }
        minuterieMedias.Stop();
        minuterieMedias.Start();
        MajResume();
    }

    async Task<InfoMedia?> Info(string fichier)
    {
        if (infos.TryGetValue(fichier, out var i)) return i;
        try
        {
            i = await AnalyseMedia.LireAsync(Ffmpeg, fichier);
            infos[fichier] = i;
            return i;
        }
        catch (Exception e) { SonUi.Toast(e.Message); return null; }
    }

    /// <summary>Les enregistrements récents : Siloé Stream (dossier des enregistrements) et OBS (dossier Vidéos).</summary>
    async void ChercherEnregistrements()
    {
        var l = await Task.Run(() =>
        {
            var vus = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            void Fouiller(string dossier, SearchOption o)
            {
                try
                {
                    if (!Directory.Exists(dossier)) return;
                    foreach (var f in Directory.EnumerateFiles(dossier, "*.*", new EnumerationOptions { RecurseSubdirectories = o == SearchOption.AllDirectories, MaxRecursionDepth = 3, IgnoreInaccessible = true }))
                        if (AnalyseMedia.EstMedia(f) && !f.Contains(Path.DirectorySeparatorChar + "Montages" + Path.DirectorySeparatorChar)) vus[f] = new FileInfo(f);
                }
                catch (Exception) { }
            }
            Fouiller(AudioConfiguration.DossierEnregistrements, SearchOption.AllDirectories);
            Fouiller(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), SearchOption.TopDirectoryOnly);
            return vus.Values.Where(f => f.Length > 0).OrderByDescending(f => f.LastWriteTime).Take(60).ToList();
        });
        enregistrements = l;
        MajMedias();
    }

    void MajMedias()
    {
        listeMedias.Children.Clear();
        if (projet.Medias.Count > 0)
        {
            listeMedias.Children.Add(new Capitales { Libelle = "Dans ce montage", Margin = new Thickness(0, 0, 0, 8) });
            foreach (var f in projet.Medias) listeMedias.Children.Add(Carte(f, true));
        }
        var autres = enregistrements.Where(f => !projet.Medias.Contains(f.FullName, StringComparer.OrdinalIgnoreCase)).ToList();
        listeMedias.Children.Add(new Capitales { Libelle = "Enregistrements récents", Margin = new Thickness(0, projet.Medias.Count > 0 ? 12 : 0, 0, 8) });
        if (autres.Count == 0)
            listeMedias.Children.Add(new TextBlock { Text = "Aucun enregistrement trouvé. « + Importer… » ajoute n'importe quelle vidéo.", FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap });
        foreach (var f in autres) listeMedias.Children.Add(Carte(f.FullName, false));
    }

    Border Carte(string fichier, bool duMontage)
    {
        infos.TryGetValue(fichier, out var info);
        string ext = Path.GetExtension(fichier).ToLowerInvariant();
        bool video = info?.Video ?? AnalyseMedia.ExtensionsVideo.Contains(ext);
        bool existe = File.Exists(fichier);
        var pile = new StackPanel();
        var ligneNom = new DockPanel();
        var icone = SonUi.Icone(video ? SonUi.Video : SonUi.Musique, P(video ? "TypeChant" : "Vert"), 16);
        icone.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(icone, Dock.Left);
        ligneNom.Children.Add(icone);
        ligneNom.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(fichier), FontWeight = FontWeights.Bold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = P("Blanc") });
        pile.Children.Add(ligneNom);
        string details;
        if (!existe) details = "Introuvable (déplacé ou supprimé ?)";
        else
        {
            var fi = new FileInfo(fichier);
            details = $"{fi.LastWriteTime:d MMM yyyy HH:mm} · {SonUi.Taille(fi.Length)}";
            if (info != null) details += "\n" + info.Resume;
            else if (erreursAnalyse.TryGetValue(fichier, out var err)) details += "\n" + err;
            else { details += "\nAnalyse…"; Analyser(fichier); }
        }
        pile.Children.Add(new TextBlock { Text = details, FontSize = 11.5, Foreground = P(existe ? "Brume" : "Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 2, 0, 0), LineHeight = 16 });
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 6, 0, 0) };
        Button B(string texte, string tip, Action clic)
        {
            var b = new Button { Style = S("BtnPetit"), Content = texte, Height = 26, Padding = new Thickness(9, 0, 9, 0), FontSize = 12, Margin = new Thickness(0, 0, 6, 0), ToolTip = tip, IsEnabled = existe };
            b.Click += (_, e) => { e.Handled = true; clic(); };
            boutons.Children.Add(b);
            return b;
        }
        if (video) B("+ Vidéo", "À la suite sur la piste vidéo (après la séquence choisie)", () => AjouterVideo(fichier));
        B("+ Son", "Sur une piste son, à la tête de lecture", () => AjouterSon(fichier));
        pile.Children.Add(boutons);
        var carte = new Border
        {
            Background = P("Encre"), BorderBrush = P("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 8), Child = pile, Cursor = Cursors.Hand, ToolTip = fichier,
        };
        carte.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || !existe) return;
            e.Handled = true;
            if (video) AjouterVideo(fichier); else AjouterSon(fichier);
        };
        var menu = new ContextMenu();
        var montrer = new MenuItem { Header = "Montrer dans l'Explorateur", IsEnabled = existe };
        montrer.Click += (_, _) => SonUi.MontrerFichier(fichier);
        menu.Items.Add(montrer);
        if (duMontage)
        {
            var retirer = new MenuItem { Header = "Retirer de la liste (le fichier reste sur le disque)" };
            retirer.Click += (_, _) => { Memoriser(); projet.Medias.RemoveAll(m => string.Equals(m, fichier, StringComparison.OrdinalIgnoreCase)); Changer(); MajMedias(); };
            menu.Items.Add(retirer);
        }
        carte.ContextMenu = menu;
        return carte;
    }

    void Importer()
    {
        var d = new OpenFileDialog
        {
            Title = "Importer des vidéos ou des sons", Multiselect = true,
            Filter = "Vidéos et sons|" + string.Join(";", AnalyseMedia.ExtensionsVideo.Concat(AnalyseMedia.ExtensionsAudio).Select(e => "*" + e)) + "|Tous les fichiers|*.*",
            InitialDirectory = Directory.Exists(AudioConfiguration.DossierEnregistrements) ? AudioConfiguration.DossierEnregistrements : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        Memoriser();
        foreach (var f in d.FileNames)
        {
            if (!projet.Medias.Contains(f, StringComparer.OrdinalIgnoreCase)) projet.Medias.Add(f);
            Analyser(f);
        }
        Changer();
        MajMedias();
    }

    async void AjouterVideo(string fichier) => await AjouterVideoAsync(fichier);

    async Task AjouterVideoAsync(string fichier)
    {
        var info = await Info(fichier);
        if (info == null) return;
        if (!info.Video) { SonUi.Toast($"« {Path.GetFileName(fichier)} » n'a pas d'image : il va sur une piste son."); AjouterSon(fichier); return; }
        Memoriser();
        if (!projet.Medias.Contains(fichier, StringComparer.OrdinalIgnoreCase)) projet.Medias.Add(fichier);
        var s = new SequenceMontage { Source = fichier, Debut = 0, Fin = info.Duree > 0 ? info.Duree : 60, DureeSource = info.Duree };
        int i = selection is SequenceMontage choisie && projet.Sequences.IndexOf(choisie) is >= 0 and var k ? k + 1 : projet.Sequences.Count;
        projet.Sequences.Insert(i, s);
        bool premiere = projet.Sequences.Count == 1;
        Changer();
        ligne.Choisir(s);
        MajMedias();
        if (premiere) ligne.Ajuster();
        SonUi.Toast($"« {s.Nom} » ajouté ({ProjetMontage.Temps(s.Longueur, false)}). Coupez avec S ou I / O pour garder ce qu'il faut.");
    }

    async void AjouterSon(string fichier)
    {
        var info = await Info(fichier);
        if (info == null) return;
        if (!info.Audio) { SonUi.Toast($"« {Path.GetFileName(fichier)} » n'a pas de son."); return; }
        Memoriser();
        if (!projet.Medias.Contains(fichier, StringComparer.OrdinalIgnoreCase)) projet.Medias.Add(fichier);
        double longueur = info.Duree > 0 ? info.Duree : 60;
        // la première piste libre à cet endroit
        int piste = Enumerable.Range(1, ClipAudio.Pistes).FirstOrDefault(n => !projet.Audios.Any(a => a.Piste == n && a.Position < tete + longueur && a.Position + a.Longueur > tete), 1);
        var c = new ClipAudio { Source = fichier, Piste = piste, Position = tete, Debut = 0, Fin = longueur, DureeSource = info.Duree, Volume = 1 };
        projet.Audios.Add(c);
        Changer();
        ligne.Choisir(c);
        MajMedias();
    }

    // ================================================================================================ gestes
    void AllerA(double t)
    {
        tete = Math.Clamp(t, 0, Math.Max(0, projet.Duree));
        ligne.ChoisirTete(tete);
        apercu.Aller(tete);
        MajTemps();
    }

    void Couper()
    {
        if (projet.ElementA(tete) is not { Type: TypeElement.Sequence } e) { SonUi.Toast("Placez la tête de lecture dans une séquence."); return; }
        var s = projet.Sequences[e.Index];
        double dans = s.Debut + (tete - e.Debut);
        if (dans <= s.Debut + 0.1 || dans >= s.Fin - 0.1) { SonUi.Toast("Trop près du bord de la séquence pour couper."); return; }
        Memoriser();
        var n = projet.Couper(tete);
        Changer();
        if (n != null) ligne.Choisir(n);
    }

    void MarquerDebut()
    {
        if (projet.ElementA(tete) is not { Type: TypeElement.Sequence } e) { SonUi.Toast("Placez la tête de lecture dans une séquence."); return; }
        var s = projet.Sequences[e.Index];
        double dans = s.Debut + (tete - e.Debut);
        if (dans >= s.Fin - 0.1) { SonUi.Toast("Trop près de la fin de la séquence."); return; }
        Memoriser();
        s.Debut = dans;
        Changer();
        ligne.Choisir(s);
        AllerA(projet.DebutSequence(e.Index));
    }

    void MarquerFin()
    {
        if (projet.ElementA(tete) is not { Type: TypeElement.Sequence } e) { SonUi.Toast("Placez la tête de lecture dans une séquence."); return; }
        var s = projet.Sequences[e.Index];
        double dans = s.Debut + (tete - e.Debut);
        if (dans <= s.Debut + 0.1) { SonUi.Toast("Trop près du début de la séquence."); return; }
        Memoriser();
        s.Fin = dans;
        Changer();
        ligne.Choisir(s);
    }

    void Supprimer()
    {
        switch (selection)
        {
            case SequenceMontage s: Memoriser(); projet.Sequences.Remove(s); break;
            case TitreMontage t: Memoriser(); projet.Titres.Remove(t); break;
            case ClipAudio a: Memoriser(); projet.Audios.Remove(a); break;
            case GeneriqueMontage g: Memoriser(); g.Actif = false; break;
            default: SonUi.Toast("Choisissez d'abord ce qu'il faut supprimer (un clic sur la ligne de temps)."); return;
        }
        selection = null;
        ligne.Choisir(null);
        Changer();
    }

    void AjouterTitre()
    {
        Memoriser();
        var t = new TitreMontage { Debut = tete, Duree = 6, Texte = "Pasteur", SousTexte = "Prédication" };
        projet.Titres.Add(t);
        Changer();
        ligne.Choisir(t);
    }

    void BasculerGenerique(GeneriqueMontage g)
    {
        Memoriser();
        g.Actif = !g.Actif;
        Changer();
        if (g.Actif) ligne.Choisir(g);
    }

    /// <summary>Les touches de la page (appelé par la fenêtre quand aucun champ n'a le clavier). Vrai si la touche est traitée.</summary>
    public bool Touche(KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        bool ctrl = m.HasFlag(ModifierKeys.Control), maj = m.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Space: apercu.Basculer(); return true;
            case Key.S when ctrl: Enregistrer(); SonUi.Toast("Montage enregistré."); return true;
            case Key.S: Couper(); return true;
            case Key.I: MarquerDebut(); return true;
            case Key.O: MarquerFin(); return true;
            case Key.Delete or Key.Back: Supprimer(); return true;
            case Key.Z when ctrl && maj: Retablir(); return true;
            case Key.Z when ctrl: Annuler(); return true;
            case Key.Y when ctrl: Retablir(); return true;
            case Key.Left: AllerA(tete - (maj ? 1 : 1.0 / 30)); return true;
            case Key.Right: AllerA(tete + (maj ? 1 : 1.0 / 30)); return true;
            case Key.Home: AllerA(0); return true;
            case Key.End: AllerA(projet.Duree); return true;
            case Key.Add or Key.OemPlus: ligne.Zoomer(1.4); return true;
            case Key.Subtract or Key.OemMinus: ligne.Zoomer(0.7); return true;
            case Key.T when !ctrl: AjouterTitre(); return true;
        }
        return false;
    }

    // ================================================================================================ affichage
    void MajTemps()
    {
        temps.Text = $"{ProjetMontage.Temps(tete)} / {ProjetMontage.Temps(projet.Duree, false)}";
        var e = projet.ElementA(tete);
        infoElement.Text = e switch
        {
            null => projet.Sequences.Count == 0 ? "Le montage est vide." : "",
            { Type: TypeElement.Intro } => "Générique de début",
            { Type: TypeElement.Outro } => "Générique de fin",
            _ => $"Séquence {e.Index + 1}/{projet.Sequences.Count} · {projet.Sequences[e.Index].Nom} · dans le fichier : {ProjetMontage.Temps(projet.Sequences[e.Index].Debut + (tete - e.Debut))}",
        };
    }

    void MajResume()
    {
        var sources = projet.Sequences.Select(s => s.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string mode;
        if (projet.Sequences.Count == 0) mode = "Montage vide";
        else if (sources.Any(s => !infos.ContainsKey(s))) mode = "Analyse des fichiers…";
        else
        {
            var (copie, raison) = RenduMontage.CopiePossible(projet, infos);
            var i = infos[projet.Sequences[0].Source];
            var (l, h) = copie ? (i.Largeur, i.Hauteur) : projet.Sortie.Taille(i.Largeur, i.Hauteur);
            mode = copie ? $"Copie sans réencodage · qualité d'origine {l} × {h}" : $"Réencodage {l} × {h} ({raison})";
        }
        resume.Text = $"{ProjetMontage.Temps(projet.Duree, false)} · {projet.Sequences.Count} séquence{(projet.Sequences.Count > 1 ? "s" : "")}\n{mode}";
    }

    void MajPublications()
    {
        cartes.Children.Clear();
        var l = Publications.Toutes.Reverse().Take(6).ToList();
        titreCartes.Visibility = l.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var p in l) cartes.Children.Add(new CartePublication(p));
    }

    // ================================================================================================ réglages (à droite)
    readonly List<Action> valeurs = new();

    /// <summary>Remet les valeurs affichées (la sélection a bougé à la souris) sans refaire les champs.</summary>
    void MajValeursInspecteur() { foreach (var v in valeurs) v(); }

    static TextBlock Libelle(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 10, 0, 4) };

    TextBox Temps(string libelle, Func<double> lire, Action<double> ecrire)
    {
        inspecteur.Children.Add(Libelle(libelle));
        var tb = new TextBox { Style = S("Champ"), MinHeight = 34, FontSize = 14, Text = ProjetMontage.Temps(lire()) };
        void Valider()
        {
            if (ProjetMontage.LireTemps(tb.Text) is { } v)
            {
                if (Math.Abs(v - lire()) > 0.0005) { Memoriser(); ecrire(v); memorise = false; Changer(false); }
            }
            tb.Text = ProjetMontage.Temps(lire());
        }
        tb.LostKeyboardFocus += (_, _) => Valider();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Valider(); e.Handled = true; } };
        valeurs.Add(() => { if (!tb.IsKeyboardFocusWithin) tb.Text = ProjetMontage.Temps(lire()); });
        inspecteur.Children.Add(tb);
        return tb;
    }

    TextBox Texte(string libelle, Func<string> lire, Action<string> ecrire, bool lignes = false)
    {
        inspecteur.Children.Add(Libelle(libelle));
        var tb = new TextBox { Style = S("Champ"), MinHeight = lignes ? 64 : 34, FontSize = 14, Text = lire(), AcceptsReturn = lignes, TextWrapping = lignes ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (lignes) tb.VerticalContentAlignment = VerticalAlignment.Top;
        tb.GotKeyboardFocus += (_, _) => memorise = false;
        tb.TextChanged += (_, _) => { if (tb.Text == lire()) return; Memoriser(); ecrire(tb.Text); Changer(false); };
        inspecteur.Children.Add(tb);
        return tb;
    }

    Slider Curseur(string libelle, double min, double max, Func<double> lire, Action<double> ecrire, Func<double, string> format, double pas = 0.01)
    {
        var tete2 = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
        var val = new TextBlock { Text = format(lire()), FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = P("Blanc") };
        DockPanel.SetDock(val, Dock.Right);
        tete2.Children.Add(val);
        tete2.Children.Add(new TextBlock { Text = libelle, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume") });
        inspecteur.Children.Add(tete2);
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(lire(), min, max), SmallChange = pas, LargeChange = pas * 10, IsMoveToPointEnabled = true };
        bool interne = false;
        s.GotMouseCapture += (_, _) => memorise = false;
        s.ValueChanged += (_, e) =>
        {
            val.Text = format(e.NewValue);
            if (interne) return;
            Memoriser();
            ecrire(Math.Round(e.NewValue / pas) * pas);
            Changer(false);
        };
        valeurs.Add(() => { interne = true; s.Value = Math.Clamp(lire(), min, max); val.Text = format(lire()); interne = false; });
        inspecteur.Children.Add(s);
        return s;
    }

    CheckBox Case(string libelle, Func<bool> lire, Action<bool> ecrire)
    {
        var c = new CheckBox { Content = libelle, IsChecked = lire(), FontSize = 13.5, Margin = new Thickness(0, 10, 0, 0) };
        c.Checked += (_, _) => { if (lire()) return; memorise = false; Memoriser(); ecrire(true); Changer(); };
        c.Unchecked += (_, _) => { if (!lire()) return; memorise = false; Memoriser(); ecrire(false); Changer(); };
        inspecteur.Children.Add(c);
        return c;
    }

    ComboBox Liste(string libelle, (string Valeur, string Texte)[] options, Func<string> lire, Action<string> ecrire)
    {
        inspecteur.Children.Add(Libelle(libelle));
        var cb = new ComboBox { MinHeight = 34 };
        foreach (var (v, t) in options) cb.Items.Add(new ComboBoxItem { Content = t, Tag = v });
        cb.SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => o.Valeur == lire()));
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not ComboBoxItem { Tag: string v } || v == lire()) return;
            memorise = false; Memoriser(); ecrire(v); Changer(false);
        };
        inspecteur.Children.Add(cb);
        return cb;
    }

    void Boutons(params (string Texte, string Style, Action Clic)[] b)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (t, st, c) in b)
        {
            var bt = new Button { Style = S(st), Content = t, Height = 32, Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 0, 6, 6) };
            bt.Click += (_, _) => c();
            w.Children.Add(bt);
        }
        inspecteur.Children.Add(w);
    }

    void Titre(string t, string? sous = null)
    {
        inspecteur.Children.Add(new Capitales { Libelle = t, Margin = new Thickness(0, 0, 0, sous != null ? 2 : 4) });
        if (sous != null) inspecteur.Children.Add(new TextBlock { Text = sous, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = P("Blanc"), TextWrapping = TextWrapping.Wrap });
    }

    void Note(string t) => inspecteur.Children.Add(new TextBlock { Text = t, FontSize = 12, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 17 });

    static string Pourcent(double v) => $"{v * 100:0} %";
    static string Secondes(double v) => $"{v:0.0} s";

    void MajInspecteur()
    {
        inspecteur.Children.Clear();
        valeurs.Clear();
        switch (selection)
        {
            case SequenceMontage s: InspecterSequence(s); break;
            case TitreMontage t: InspecterTitre(t); break;
            case ClipAudio a: InspecterSon(a); break;
            case GeneriqueMontage g: InspecterGenerique(g); break;
            default: InspecterProjet(); break;
        }
    }

    void InspecterSequence(SequenceMontage s)
    {
        int index = projet.Sequences.IndexOf(s);
        Titre($"Séquence {index + 1}", s.Nom);
        if (infos.TryGetValue(s.Source, out var i)) Note(i.Resume);
        Temps("Début dans le fichier", () => s.Debut, v => s.Debut = Math.Clamp(v, 0, s.Fin - 0.1));
        Temps("Fin dans le fichier", () => s.Fin, v => s.Fin = Math.Clamp(v, s.Debut + 0.1, s.DureeSource > 0 ? s.DureeSource : double.MaxValue));
        var duree = new TextBlock { FontSize = 12.5, Foreground = P("Brume"), Margin = new Thickness(0, 6, 0, 0) };
        valeurs.Add(() => duree.Text = "Durée : " + ProjetMontage.Temps(s.Longueur));
        inspecteur.Children.Add(duree);
        Boutons(("Début = tête (I)", "BtnPetit", MarquerDebut), ("Fin = tête (O)", "BtnPetit", MarquerFin));
        Curseur("Volume", 0, 2, () => s.Volume, v => s.Volume = v, Pourcent);
        Case("Couper le son de cette séquence", () => s.Muet, v => s.Muet = v);
        if (index > 0)
        {
            Liste("Transition depuis la séquence précédente", new[] { (ProjetMontage.Aucune, "Coupe franche"), (ProjetMontage.Fondu, "Fondu enchaîné"), (ProjetMontage.Noir, "Fondu par le noir") },
                () => s.Transition, v => s.Transition = v);
            Curseur("Durée du fondu", 0.2, 3, () => s.DureeTransition, v => s.DureeTransition = v, Secondes, 0.1);
        }
        Boutons(("Dupliquer", "BtnPetit", () =>
        {
            Memoriser();
            var c = new SequenceMontage { Source = s.Source, Debut = s.Debut, Fin = s.Fin, DureeSource = s.DureeSource, Volume = s.Volume, Muet = s.Muet };
            projet.Sequences.Insert(projet.Sequences.IndexOf(s) + 1, c);
            Changer();
            ligne.Choisir(c);
        }), ("Supprimer", "BtnDanger", Supprimer));
        MajValeursInspecteur();
    }

    void InspecterTitre(TitreMontage t)
    {
        Titre("Titre");
        Texte("Texte", () => t.Texte, v => t.Texte = v);
        Texte("Sous-texte (facultatif)", () => t.SousTexte, v => t.SousTexte = v);
        Temps("Début (ligne de temps)", () => t.Debut, v => t.Debut = Math.Max(0, v));
        Curseur("Durée", 0.5, 30, () => t.Duree, v => t.Duree = v, Secondes, 0.5);
        Liste("Place", new[] { (TitreMontage.Bas, "Bandeau en bas à gauche"), (TitreMontage.Centre, "Au centre"), (TitreMontage.Haut, "En haut") }, () => t.Position, v => t.Position = v);
        Curseur("Taille", 24, 120, () => t.Taille, v => t.Taille = v, v => $"{v:0} px", 1);
        Boutons(("Début = tête", "BtnPetit", () => { Memoriser(); t.Debut = tete; Changer(); }), ("Supprimer", "BtnDanger", Supprimer));
        Note("Le titre apparaît et disparaît en fondu. Il est dessiné dans la vidéo : le montage sera réencodé.");
    }

    void InspecterSon(ClipAudio a)
    {
        Titre("Son ajouté", a.Nom);
        Liste("Piste", Enumerable.Range(1, ClipAudio.Pistes).Select(n => (n.ToString(), "A" + n)).ToArray(), () => a.Piste.ToString(), v => a.Piste = int.Parse(v));
        Temps("Commence à (ligne de temps)", () => a.Position, v => a.Position = Math.Max(0, v));
        Temps("Début dans le fichier", () => a.Debut, v => a.Debut = Math.Clamp(v, 0, a.Fin - 0.1));
        Temps("Fin dans le fichier", () => a.Fin, v => a.Fin = Math.Clamp(v, a.Debut + 0.1, a.DureeSource > 0 ? a.DureeSource : double.MaxValue));
        Curseur("Volume", 0, 2, () => a.Volume, v => a.Volume = v, Pourcent);
        Case("Muet", () => a.Muet, v => a.Muet = v);
        Curseur("Fondu d'entrée", 0, 5, () => a.FonduEntree, v => a.FonduEntree = v, Secondes, 0.1);
        Curseur("Fondu de sortie", 0, 5, () => a.FonduSortie, v => a.FonduSortie = v, Secondes, 0.1);
        Boutons(("Commence à la tête", "BtnPetit", () => { Memoriser(); a.Position = tete; Changer(); }), ("Supprimer", "BtnDanger", Supprimer));
        Note("Le son ajouté se mélange au son des séquences (l'aperçu ne le joue pas ; le rendu, si).");
    }

    void InspecterGenerique(GeneriqueMontage g)
    {
        Titre(g == projet.Intro ? "Générique de début" : "Générique de fin");
        Case("Montrer ce générique", () => g.Actif, v => g.Actif = v);
        Texte("Texte", () => g.Texte, v => g.Texte = v);
        Texte("Sous-texte", () => g.SousTexte, v => g.SousTexte = v);
        Curseur("Durée", 1, 20, () => g.Duree, v => g.Duree = v, Secondes, 0.5);
        Note("Fond noir, logo de l'église s'il est choisi (réglages du projet), texte, fondu au noir.");
    }

    void InspecterProjet()
    {
        Titre("Le montage");
        Note("Cliquez sur un élément de la ligne de temps pour le régler. Ici : le logo, le volume et la qualité de la vidéo.");
        inspecteur.Children.Add(new Capitales { Libelle = "Logo de l'église", Margin = new Thickness(0, 18, 0, 0) });
        Case("Logo dans un coin pendant les séquences", () => projet.Logo.Actif, v => projet.Logo.Actif = v);
        var nomLogo = new TextBlock { FontSize = 12.5, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Text = projet.Logo.Chemin.Length > 0 ? Path.GetFileName(projet.Logo.Chemin) + (File.Exists(projet.Logo.Chemin) ? "" : " (introuvable)") : "Aucune image choisie." };
        inspecteur.Children.Add(nomLogo);
        Boutons(("Choisir l'image…", "BtnPetit", () =>
        {
            var d = new OpenFileDialog { Title = "Logo de l'église", Filter = "Images (PNG conseillé : fond transparent)|*.png;*.jpg;*.jpeg" };
            if (d.ShowDialog(Window.GetWindow(this)) != true) return;
            Memoriser();
            projet.Logo.Chemin = d.FileName;
            projet.Logo.Actif = true;
            Changer();
        }));
        Liste("Coin", new[] { ("hd", "En haut à droite"), ("hg", "En haut à gauche"), ("bd", "En bas à droite"), ("bg", "En bas à gauche") }, () => projet.Logo.Coin, v => projet.Logo.Coin = v);
        Curseur("Taille", 0.04, 0.3, () => projet.Logo.Taille, v => projet.Logo.Taille = v, Pourcent);
        Curseur("Opacité", 0.2, 1, () => projet.Logo.Opacite, v => projet.Logo.Opacite = v, Pourcent);

        inspecteur.Children.Add(new Capitales { Libelle = "Son", Margin = new Thickness(0, 18, 0, 0) });
        Curseur("Volume des séquences", 0, 2, () => projet.VolumePrincipal, v => projet.VolumePrincipal = v, Pourcent);

        inspecteur.Children.Add(new Capitales { Libelle = "Vidéo produite", Margin = new Thickness(0, 18, 0, 0) });
        Liste("Mode", new[] { ("auto", "Automatique : copie sans perte quand c'est possible"), ("exact", "Toujours réencoder (coupes à l'image près)") }, () => projet.Sortie.Mode, v => projet.Sortie.Mode = v);
        Liste("Résolution", new[] { ("source", "Celle des enregistrements"), ("1080", "1080p (Full HD)"), ("1440", "1440p"), ("2160", "4K (2160p)") }, () => projet.Sortie.Resolution, v => projet.Sortie.Resolution = v);
        Liste("Codec", new[] { ("h264", "H.264 (conseillé pour YouTube)"), ("hevc", "HEVC / H.265 (plus léger)") }, () => projet.Sortie.Codec, v => projet.Sortie.Codec = v);
        var encodeur = Liste("Encodeur", new[] { ("auto", "Automatique (le meilleur de ce PC)") }.Concat(EncodeursVideo.Tous.Select(e => (e.Nom, e.Libelle))).ToArray(),
            () => projet.Sortie.Encodeur, v => projet.Sortie.Encodeur = v);
        var detecte = new TextBlock { FontSize = 12, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Text = "Recherche des encodeurs de ce PC…" };
        inspecteur.Children.Add(detecte);
        DetecterEncodeurs(detecte);
        Curseur("Qualité (plus bas = plus beau, plus lourd)", 14, 30, () => projet.Sortie.Qualite, v => projet.Sortie.Qualite = (int)Math.Round(v), v => $"{v:0}" + (v <= 17 ? " (presque sans perte)" : v <= 21 ? " (excellent)" : v <= 25 ? " (bon)" : " (léger)"), 1);
        Case("Décoder avec la carte graphique (plus rapide en 4K)", () => projet.Sortie.DecodageMateriel, v => projet.Sortie.DecodageMateriel = v);
        Note("Copie sans perte : les images d'origine (4K comprise) sont gardées telles quelles, sans réencodage ; chaque coupe se cale sur l'image clé précédente. Titres, logo, génériques, fondus ou son changé demandent un réencodage, fait par la carte graphique quand elle le peut.");
    }

    async void DetecterEncodeurs(TextBlock texte)
    {
        int l = 1920, h = 1080;
        if (projet.Sequences.Count > 0 && infos.TryGetValue(projet.Sequences[0].Source, out var i)) (l, h) = projet.Sortie.Taille(i.Largeur, i.Hauteur);
        else if (projet.Sortie.Resolution != "source") (l, h) = projet.Sortie.Taille(0, 0);
        List<EncodeurVideo> liste;
        if (!encodeursDetectes.TryGetValue((l, h), out liste!))
        {
            try { liste = await EncodeursVideo.DetecterAsync(Ffmpeg, l, h); encodeursDetectes[(l, h)] = liste; }
            catch (Exception e) { texte.Text = "Encodeurs : " + e.Message; return; }
        }
        var materiels = liste.Where(e => e.Materiel).ToList();
        texte.Text = liste.Count == 0 ? $"Aucun encodeur ne marche en {l} × {h} sur ce PC."
            : $"En {l} × {h}, ce PC sait encoder avec : " + string.Join(", ", liste.Select(e => e.Libelle)) + "."
              + (materiels.Count == 0 ? " Pas de carte graphique utilisable : le processeur encode (plus lent)." : "");
    }

    // ================================================================================================ 2.3.0 : vers Miniature
    async void EnvoyerMiniature() => await EnvoyerMiniatureAsync();

    /// <summary>
    /// L'image de la tête de lecture, prise dans l'enregistrement d'origine en pleine qualité (4K comprise), devient le fond de la
    /// miniature YouTube ; la page Miniature s'ouvre.
    /// </summary>
    internal async Task<string?> EnvoyerMiniatureAsync()
    {
        if (projet.ElementA(tete) is not { Type: TypeElement.Sequence } e) { SonUi.Toast("Placez la tête de lecture sur une séquence (pas sur un générique)."); return null; }
        var s = projet.Sequences[e.Index];
        double t = s.Debut + (tete - e.Debut);
        var dossier = Path.Combine(Creation.PageMiniature.Dossier, "Images");
        Directory.CreateDirectory(dossier);
        var fichier = Path.Combine(dossier, $"{RecordingFiles.SafeName(projet.Nom)} - {ProjetMontage.Temps(t, false).Replace(':', '-')} - {DateTime.Now:HHmmss}.png");
        apercu.Arreter();
        SonUi.Toast("Image envoyée vers Miniature…");
        try
        {
            var (code, err, _) = await Processus.ExecuterAsync(Ffmpeg, new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                "-ss", t.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "-i", s.Source, "-frames:v", "1", fichier }, TimeSpan.FromMinutes(1));
            if (code != 0 || !File.Exists(fichier)) { SonUi.Toast("L'image n'a pas pu être prise : " + Processus.Fin(err)); return null; }
        }
        catch (Exception x) { Journal.Erreur("montage : image pour la miniature", x); SonUi.Toast("L'image n'a pas pu être prise : " + x.Message); return null; }
        (Window.GetWindow(this) as MainWindow)?.EnvoyerVersMiniature(fichier, projet.Nom);
        return fichier;
    }

    // ================================================================================================ pour le test automatique
    internal ProjetMontage TestProjet => projet;
    internal double TestTete => tete;
    internal void TestNouveau() => Charger(new ProjetMontage { Nom = "Montage de test" }, null);
    internal Task TestAjouterVideo(string fichier) => AjouterVideoAsync(fichier);
    internal void TestAller(double t) => AllerA(t);
    internal void TestCouper() => Couper();
    internal void TestChoisir(object? o) => ligne.Choisir(o);
    internal void TestTitre() => AjouterTitre();
    internal void TestGenerique() => BasculerGenerique(projet.Intro);
    internal void TestAnnuler() => Annuler();
    internal void TestModifie() => Changer();
    internal Task<string?> TestEnvoyerMiniature() => EnvoyerMiniatureAsync();

    // ================================================================================================ publier, exporter
    void OuvrirPublication()
    {
        if (projet.Sequences.Count == 0) { SonUi.Toast("Le montage est vide : ajoutez au moins un enregistrement."); return; }
        apercu.Arreter();
        Enregistrer();
        var w = new PublicationWindow(projet, () => Changer());
        SonUi.Placer(w, this);
        w.Show();
    }

    void Exporter()
    {
        if (projet.Sequences.Count == 0) { SonUi.Toast("Le montage est vide : ajoutez au moins un enregistrement."); return; }
        apercu.Arreter();
        Enregistrer();
        var dossier = Path.Combine(DossierProjets, "Exports");
        Directory.CreateDirectory(dossier);
        var d = new SaveFileDialog
        {
            Title = "Exporter le montage", Filter = "Vidéo MP4|*.mp4|Vidéo MKV|*.mkv", FileName = RecordingFiles.SafeName(projet.Nom) + ".mp4", InitialDirectory = dossier,
        };
        if (d.ShowDialog(Window.GetWindow(this)) != true) return;
        Publications.Exporter(projet, d.FileName, Ffmpeg);
        SonUi.Toast("Export lancé : suivi à droite, dans « Publications et exports ».");
    }
}
