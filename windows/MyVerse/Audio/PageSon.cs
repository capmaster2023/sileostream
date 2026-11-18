using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;
using Shapes = System.Windows.Shapes;

namespace MyVerse.Ui.Audio;

/// <summary>Les gestes du son, les mêmes partout : la page Son, la colonne de droite et la barre du bas.</summary>
internal static class ActionsSon
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;

    /// <summary>« Ajouter un son » : l'assistant en trois questions, puis le son entre dans la liste.</summary>
    public static Son? Ajouter(DependencyObject? depuis, GenreSon? genre = null)
    {
        if (C.Sons.Count >= AudioConfiguration.MaxSources) { SonUi.Toast($"{AudioConfiguration.MaxSources} sons au plus."); return null; }
        var w = new AjoutSonWindow(genre);
        SonUi.Placer(w, depuis);
        w.ShowDialog();
        if (w.Resultat is not { } s) return null;
        C.Ajouter(s);
        A.Apply();
        SonUi.Toast($"« {s.Nom} » est prêt.");
        return s;
    }

    /// <summary>« Régler » un son ; il peut aussi y être retiré.</summary>
    public static void Regler(Son s, DependencyObject? depuis)
    {
        var w = new ReglerSonWindow(s);
        SonUi.Placer(w, depuis);
        w.ShowDialog();
        if (w.Retire)
        {
            if (C.Retirer(s)) { A.Apply(); SonUi.Toast($"« {s.Nom} » est retiré."); }
        }
        else if (w.Valide) A.Apply();
    }

    public static void Couper(Son s, bool coupe)
    {
        s.Coupe = coupe;
        A.Apply();
    }

    /// <summary>Le premier enregistrement, d'un clic : « Culte », le son en MP3 (tout le son), dans Vidéos › Siloé Stream › Culte.</summary>
    public static RecordingProfile NouveauCulte()
    {
        string nom = C.Profiles.Any(p => p.Name == "Culte") ? "Culte " + (C.Profiles.Count + 1) : "Culte";
        var p = new RecordingProfile
        {
            Name = nom, AudioOnly = true, Format = "mp3", ChoixSons = null,
            Directory = Path.Combine(AudioConfiguration.DossierEnregistrements, RecordingFiles.SafeName(nom)),
        };
        C.Profiles.Add(p);
        A.Apply();
        return p;
    }

    /// <summary>Démarre ou arrête un enregistrement ; un message simple dit où est le fichier, ou ce qui ne va pas.</summary>
    public static async void Basculer(RecordingProfile p)
    {
        bool tournait = A.Recorder.Active(p.Id);
        try { await A.Recorder.ToggleAsync(p); }
        catch (Exception e) { Journal.Erreur("enregistrement", e); }
        var etat = A.Recorder.State(p.Id);
        if (etat == ChurchRecordingState.Error) SonUi.Toast($"« {p.Name} » : {A.Recorder.Message(p.Id)}");
        else if (tournait && etat == ChurchRecordingState.Ready && A.Recorder.LastFile(p.Id) is { } f) SonUi.Toast("Enregistré : " + Path.GetFileName(f));
        else if (!tournait && A.Recorder.Active(p.Id)) SonUi.Toast($"« {p.Name} » enregistre.");
    }

    /// <summary>« Enregistrer le culte » : le premier enregistrement (créé s'il n'y en a pas), démarré tout de suite.</summary>
    public static void EnregistrerLeCulte() => Basculer(C.Profiles.FirstOrDefault() ?? NouveauCulte());

    /// <summary>« Envoyer le son à Zoom » : un envoi par le câble audio virtuel, tout le son, allumé tout de suite.</summary>
    public static VirtualSoundSettings NouvelEnvoiZoom()
    {
        var v = new VirtualSoundSettings { Name = C.VirtualSounds.Any(x => x.Name == "Zoom") ? "Envoi " + (C.VirtualSounds.Count + 1) : "Zoom", Enabled = true };
        C.VirtualSounds.Add(v);
        A.Apply();
        return v;
    }

    public static void Allumer(VirtualSoundSettings v, bool actif)
    {
        v.Enabled = actif;
        A.Apply();
    }

    public static void NouvelEnregistrement(DependencyObject? depuis)
    {
        if (C.Profiles.Count >= AudioConfiguration.MaxEnregistrements) { SonUi.Toast($"{AudioConfiguration.MaxEnregistrements} enregistrements au plus."); return; }
        var w = new EnregistrementWindow(null);
        SonUi.Placer(w, depuis);
        w.ShowDialog();
    }

    public static void ReglerEnregistrement(RecordingProfile p, DependencyObject? depuis)
    {
        var w = new EnregistrementWindow(p);
        SonUi.Placer(w, depuis);
        w.ShowDialog();
    }

    public static void NouvelEnvoi(DependencyObject? depuis)
    {
        if (C.VirtualSounds.Count >= AudioConfiguration.MaxSortiesVirtuelles) { SonUi.Toast($"{AudioConfiguration.MaxSortiesVirtuelles} envois au plus."); return; }
        var w = new EnvoiWindow(null);
        SonUi.Placer(w, depuis);
        w.ShowDialog();
    }

    public static void ReglerEnvoi(VirtualSoundSettings v, DependencyObject? depuis)
    {
        var w = new EnvoiWindow(v);
        SonUi.Placer(w, depuis);
        w.ShowDialog();
    }

    /// <summary>Installe VB-CABLE (le câble audio virtuel), puis rouvre les envois.</summary>
    public static async Task InstallerCable(IProgress<string> etat)
    {
        try { await VbCable.InstallerAsync(etat); }
        catch (Exception e) { etat.Report("Installation impossible : " + e.Message); Journal.Erreur("installation de VB-CABLE", e); }
        finally { A.Sorties.Reset(); A.Apply(); }
    }

    /// <summary>« Réparer » : Siloé Stream se télécharge de nouveau et se réinstalle (chants et réglages gardés).</summary>
    public static async void Reparer(Button bouton, Window? fenetre)
    {
        if (!Dialogue.Confirmer(fenetre, "Réparer le moteur du son : Siloé Stream télécharge sa dernière version sur siloestream.tech et la réinstalle. Vos chants, cultes et réglages sont gardés. Siloé Stream se fermera puis se rouvrira.", "Réparer")) return;
        object texte = bouton.Content;
        bouton.IsEnabled = false;
        try
        {
            var r = await MiseAJour.Verifier("0.0.0");
            if (r.Erreur != null || r.Url.Length == 0) { SonUi.Toast(r.Erreur ?? "Aucun installateur de Siloé Stream trouvé sur siloestream.tech."); return; }
            await MiseAJour.Installer(r.Url, new Progress<int>(n => bouton.Content = $"Téléchargement… {n} %"));
            Journal.Info("réparation du moteur du son : réinstallation de Siloé Stream " + r.Version);
            SonUi.Toast("Installation… Siloé Stream va se fermer puis se rouvrir.");
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception e)
        {
            SonUi.Toast("Réparation impossible : " + e.Message);
            Journal.Erreur("réparation du moteur du son", e);
        }
        finally
        {
            bouton.IsEnabled = true;
            bouton.Content = texte;
        }
    }

    public static void OuvrirDossier(string dossier)
    {
        try { Directory.CreateDirectory(dossier); } catch (Exception) { }
        SonUi.Ouvrir(dossier);
    }

    /// <summary>Deux sorties de l'ordinateur sont le même appareil (« default » = la sortie de Windows du moment).</summary>
    public static bool MemeSortie(string a, string b)
    {
        static string Reel(string id)
        {
            if (!string.IsNullOrEmpty(id) && id != "default") return id;
            try
            {
                using var e = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                using var d = e.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                return d.ID;
            }
            catch (Exception) { return "default"; }
        }
        return string.Equals(Reel(a), Reel(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>« Son seul · MP3 », « Vidéo et son · MKV », avec les sons choisis s'il ne reçoit pas tout.</summary>
    public static string Resume(RecordingProfile p)
    {
        string quoi = !p.AudioOnly ? "La vidéo et le son" : p.Format is "wav" or "flac" ? "Le son · qualité studio (WAV)" : "Le son · MP3";
        return p.ChoixSons == null ? quoi : quoi + " · " + C.Recoit(p);
    }

    /// <summary>Le dossier où vont les fichiers d'un enregistrement.</summary>
    public static string Dossier(RecordingProfile? p) => p != null && p.Directory.Length > 0 ? p.Directory : AudioConfiguration.DossierEnregistrements;

    /// <summary>
    /// 2.1.0 : le dossier des enregistrements par défaut, choisi dans Réglages › Son (celui de « Commencer le culte »). Les
    /// enregistrements qui étaient dans l'ancien dossier par défaut y passent ; ceux rangés ailleurs à la main ne bougent pas.
    /// Faux si un enregistrement tourne (le dossier ne change pas en cours de route).
    /// </summary>
    public static bool ChoisirDossierEnregistrements(string nouveau)
    {
        if (A.Recorder.AnyBusy) { SonUi.Toast("Un enregistrement tourne : arrêtez-le d'abord, puis changez de dossier."); return false; }
        var ancien = AudioConfiguration.DossierEnregistrements;
        C.DossierChoisi = nouveau;
        AudioConfiguration.DossierParDefautChoisi = nouveau;
        foreach (var p in C.Profiles)
            if (p.Directory.Length == 0 || p.Directory.StartsWith(ancien, StringComparison.OrdinalIgnoreCase))
                p.Directory = Path.Combine(nouveau, RecordingFiles.SafeName(p.Name));
        A.Apply();
        SonUi.Toast("Les enregistrements iront dans " + nouveau + ".");
        return true;
    }
}

/// <summary>
/// La page Son (Siloé Stream 1.3), toute simple :
/// CE QU'ON ENTEND : une carte par son (micro, instrument, table de mixage, ordinateur, musique, vidéos) avec sa barre qui bouge,
/// un mot (« Bon niveau », « Trop fort ! »), son volume en %, Couper et Régler ; « + Ajouter un son » pose trois questions.
/// OÙ VA LE SON : dans l'église, l'enregistrement, Zoom (ou OBS) et le casque. Les pistes, le monitoring et les filtres d'OBS
/// sont décidés par Siloé Stream : on ne les voit jamais.
/// </summary>
public sealed class PageSon : Grid
{
    static ChurchAudioService A => App.Audio;
    static AudioConfiguration C => App.Audio.Config;
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    readonly DispatcherTimer minuterie = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    readonly StackPanel pile = new() { Margin = new Thickness(30, 24, 16, 40), MaxWidth = 1560, HorizontalAlignment = HorizontalAlignment.Left };
    readonly ScrollViewer defil;
    readonly Border moteur;
    readonly TextBlock moteurTexte = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = 20 };
    readonly Button reparer;
    readonly UniformGrid grilleSons = new() { Columns = 3 };
    readonly UniformGrid grilleDestinations = new() { Columns = 2 };
    readonly StackPanel alertes = new();
    /// <summary>Mis à jour 20 fois par seconde (les barres qui bougent) et 5 fois par seconde (les mots, les chronos, les boutons).</summary>
    readonly List<Action> vifs = new(), lents = new();
    string signature = "";
    int tic, versionAppareils;
    bool aRefaire = true;

    public PageSon()
    {
        // ---------------- en-tête
        var tete = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var ajouter = SonUi.Bouton("Ajouter un son", SonUi.Plus, "BtnOr", 44);
        ajouter.VerticalAlignment = VerticalAlignment.Top;
        ajouter.ToolTip = "Un micro, un instrument, la table de mixage, le son de l'ordinateur ou une musique";
        ajouter.Click += (_, _) => ActionsSon.Ajouter(this);
        DockPanel.SetDock(ajouter, Dock.Right);
        tete.Children.Add(ajouter);
        var titres = new StackPanel();
        titres.Children.Add(new TextBlock { Text = "Son", Style = S("GrandTitre") });
        titres.Children.Add(new TextBlock
        {
            Text = "Ce qu'on entend, et où ça va. Branchez, choisissez : Siloé Stream règle le reste (bruit, volume, synchro).",
            Style = S("Muet"), FontSize = 14, Margin = new Thickness(0, 4, 0, 0),
        });
        tete.Children.Add(titres);
        pile.Children.Add(tete);

        // ---------------- le moteur du son absent ou en panne : « Réparer »
        reparer = new Button { Style = S("BtnOr"), Content = "Réparer", Height = 40, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        reparer.ToolTip = "Télécharge Siloé Stream sur siloestream.tech et le réinstalle (chants, cultes et réglages gardés)";
        reparer.Click += (_, _) => ActionsSon.Reparer(reparer, Window.GetWindow(this));
        var ligneMoteur = new DockPanel();
        DockPanel.SetDock(reparer, Dock.Right);
        ligneMoteur.Children.Add(reparer);
        ligneMoteur.Children.Add(moteurTexte);
        moteur = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 14, 12), Margin = new Thickness(0, 18, 14, 0), Child = ligneMoteur, Visibility = Visibility.Collapsed };
        pile.Children.Add(moteur);

        // ---------------- ce qu'on entend
        pile.Children.Add(Section("Ce qu'on entend", "Chaque son a sa barre qui bouge quand il y a du son. Couper le fait taire partout."));
        pile.Children.Add(grilleSons);

        // ---------------- où va le son
        pile.Children.Add(Section("Où va le son", "Tout le son va partout où c'est allumé. Rien d'autre à régler."));
        pile.Children.Add(alertes);
        pile.Children.Add(grilleDestinations);

        defil = new ScrollViewer { Content = pile, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(defil);

        A.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        A.Recorder.Changed += () => Dispatcher.BeginInvoke(new Action(Maj));
        A.PeripheriquesChange += () => Dispatcher.BeginInvoke(new Action(() => { versionAppareils++; Maj(); }));
        minuterie.Tick += (_, _) => Battre();
        // les cartes prennent les couleurs du thème à leur construction : un changement de thème les refait à la prochaine visite
        Palette.Change += () => { aRefaire = true; if (IsVisible) Maj(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Maj(); minuterie.Start(); }
            else minuterie.Stop();
        };
        SizeChanged += (_, e) => Colonnes(e.NewSize.Width);
    }

    static StackPanel Section(string titre, string aide)
    {
        var p = new StackPanel { Margin = new Thickness(0, 28, 14, 14) };
        p.Children.Add(new Capitales { Libelle = titre, FontSize = 12, Margin = new Thickness(0) });
        p.Children.Add(new TextBlock { Text = aide, Style = S("Muet"), FontSize = 13, Margin = new Thickness(0, 4, 0, 0) });
        return p;
    }

    /// <summary>Le nombre de cartes par ligne suit la largeur de la page.</summary>
    void Colonnes(double largeur)
    {
        double utile = Math.Max(0, largeur - 46);
        grilleSons.Columns = Math.Max(1, (int)(utile / 340));
        grilleDestinations.Columns = utile >= 760 ? 2 : 1;
    }

    // ------------------------------------------------------------ mise à jour
    /// <summary>Ce qui oblige à refaire les cartes : les sons, les destinations, les appareils branchés. Le volume ou Couper ne refont rien.</summary>
    string Signature() => string.Join("|", C.Sons.Select(s => s.Id + "/" + s.Nom + "/" + s.Genre + "/" + AudioConfiguration.Description(s)))
        + "#" + string.Join("|", C.Profiles.Select(p => p.Id + "/" + p.Name + "/" + ActionsSon.Resume(p)))
        + "#" + string.Join("|", C.VirtualSounds.Select(v => v.Id + "/" + v.Name + "/" + v.DeviceId + "/" + C.Recoit(v)))
        + "#" + C.Eglise.Appareil + "/" + C.Casque.Appareil + "#" + A.Running + "/" + A.ObsAbsent + "/" + A.TracksActive
        + "#" + string.Join("|", A.Plan.Alertes) + "#" + versionAppareils;

    /// <summary>Refait les cartes si quelque chose a vraiment changé, puis met à jour les mots et les boutons.</summary>
    public void Maj()
    {
        // 1.8.0 : page cachée : rien à faire maintenant ; à l'ouverture, les cartes ne sont refaites que si quelque chose a vraiment
        // changé (la signature), au lieu de tout reconstruire à chaque visite (la page Son mettait des secondes à s'ouvrir)
        if (!IsVisible) return;
        string sig = Signature();
        if (sig != signature || aRefaire)
        {
            signature = sig;
            aRefaire = false;
            Construire();
        }
        foreach (var maj in lents) maj();
    }

    void Battre()
    {
        if (!IsVisible) return;
        foreach (var maj in vifs) maj();
        if (++tic % 4 == 0) foreach (var maj in lents) maj();
    }

    void Construire()
    {
        vifs.Clear();
        lents.Clear();
        MajMoteur();
        grilleSons.Children.Clear();
        foreach (var s in C.Sons) grilleSons.Children.Add(CarteSon(s));
        grilleSons.Children.Add(TuileAjout());
        alertes.Children.Clear();
        foreach (var a in A.Plan.Alertes)
        {
            var t = SonUi.Note(a, 0);
            t.Foreground = P("Or");
            t.Margin = new Thickness(0, 0, 14, 10);
            alertes.Children.Add(t);
        }
        grilleDestinations.Children.Clear();
        grilleDestinations.Children.Add(CarteEglise());
        grilleDestinations.Children.Add(CarteEnregistrer());
        grilleDestinations.Children.Add(CarteEnLigne());
        grilleDestinations.Children.Add(CarteCasque());
        Colonnes(ActualWidth);
    }

    void MajMoteur()
    {
        bool panne = A.ObsAbsent || (!A.Running && A.Diagnostic.StartsWith("Le moteur du son ne démarre pas", StringComparison.Ordinal));
        bool attente = !A.Running && !panne;
        bool sansPistes = A.Running && !A.TracksActive;
        moteur.Visibility = panne || attente || sansPistes ? Visibility.Visible : Visibility.Collapsed;
        moteur.Background = P(panne ? "RougeLeger" : "OrLeger");
        moteur.BorderBrush = P(panne ? "RougeBord" : "OrBord");
        moteurTexte.Text = A.Diagnostic;
        moteurTexte.Foreground = P(panne ? "RougeTexte" : "Encre2");
        reparer.Visibility = panne ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ la carte d'un son
    FrameworkElement CarteSon(Son s)
    {
        var corps = new StackPanel();

        // tête : l'icône et « Régler » en haut ; dessous, le nom en entier (2.4.0 : sur deux lignes au besoin) et ce que c'est
        var tete = new StackPanel();
        var hautCarte = new DockPanel();
        var regler = new Button { Style = S("BtnPetit"), Content = "Régler", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        regler.ToolTip = s.Genre == GenreSon.Video ? "Dans l'église, volume régulier" : "Son nom, où il est branché, ses aides";
        regler.Click += (_, _) => ActionsSon.Regler(s, this);
        DockPanel.SetDock(regler, Dock.Right);
        hautCarte.Children.Add(regler);
        var pastille = SonUi.Pastille(s.Genre, 42);
        pastille.HorizontalAlignment = HorizontalAlignment.Left;
        hautCarte.Children.Add(pastille);
        tete.Children.Add(hautCarte);
        var nom = SonUi.Texte(s.Nom, 16, "Blanc", FontWeights.Bold, true);
        nom.Margin = new Thickness(0, 10, 0, 0);
        nom.MaxHeight = 46;
        nom.TextTrimming = TextTrimming.CharacterEllipsis;
        nom.ToolTip = s.Nom;
        tete.Children.Add(nom);
        var description = SonUi.Texte(AudioConfiguration.Description(s), 12.5, "Brume", null, true);
        description.MaxHeight = 36;
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        description.Margin = new Thickness(0, 2, 0, 0);
        description.ToolTip = description.Text;
        tete.Children.Add(description);
        corps.Children.Add(tete);

        // la barre qui bouge
        var vu = new Vumetre(10) { Margin = new Thickness(0, 14, 0, 0) };
        corps.Children.Add(vu);

        // le mot : « Bon niveau », « Trop fort ! », « Coupé »… et « dans l'église »
        var ligneEtat = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var eglise = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Ce son sort aussi par les haut-parleurs de l'église" };
        var iconeEglise = SonUi.Icone(SonUi.HautParleur, P("Brume"), 14, 1.8);
        iconeEglise.Margin = new Thickness(0, 0, 5, 0);
        eglise.Children.Add(iconeEglise);
        eglise.Children.Add(SonUi.Texte("dans l'église", 12, "Brume", null, false));
        DockPanel.SetDock(eglise, Dock.Right);
        ligneEtat.Children.Add(eglise);
        var point = SonUi.Point();
        DockPanel.SetDock(point, Dock.Left);
        ligneEtat.Children.Add(point);
        var etat = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(etat, Dock.Left);
        ligneEtat.Children.Add(etat);
        var conseil = new TextBlock { FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 0, 0) };
        ligneEtat.Children.Add(conseil);
        corps.Children.Add(ligneEtat);

        // le volume, en % (100 % = normal)
        var ligneVolume = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var pourcent = new TextBlock { FontFamily = SonUi.Mono, FontSize = 13, FontWeight = FontWeights.SemiBold, Width = 54, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = VolumeCurseur.Texte(s.VolumeDb) };
        DockPanel.SetDock(pourcent, Dock.Right);
        ligneVolume.Children.Add(pourcent);
        var libelleVolume = SonUi.Texte("Volume", 12.5, "Brume", null, false);
        libelleVolume.VerticalAlignment = VerticalAlignment.Center;
        libelleVolume.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(libelleVolume, Dock.Left);
        ligneVolume.Children.Add(libelleVolume);
        var curseur = new Slider
        {
            Minimum = 0, Maximum = 1, SmallChange = 0.01, LargeChange = 0.05, Value = VolumeCurseur.Position(s.VolumeDb), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "100 % : le volume normal. Double-clic : revenir à 100 %.",
        };
        curseur.ValueChanged += (_, e) =>
        {
            double v = e.NewValue;
            // le volume normal s'attrape facilement
            if (Math.Abs(v - VolumeCurseur.Normal) < 0.015) v = VolumeCurseur.Normal;
            s.VolumeDb = Math.Round(VolumeCurseur.Db(v), 1);
            pourcent.Text = VolumeCurseur.Texte(s.VolumeDb);
            A.Apply();
        };
        curseur.MouseDoubleClick += (_, _) => curseur.Value = VolumeCurseur.Normal;
        ligneVolume.Children.Add(curseur);
        corps.Children.Add(ligneVolume);

        // Couper ; pour une musique : Lire, Pause, Arrêter
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var (couper, majCouper) = SonUi.BoutonCouper(s);
        actions.Children.Add(couper);
        Action? majLecture = null;
        if (s.Genre == GenreSon.Fichier)
        {
            var iconeLire = SonUi.Icone(SonUi.Lire, P("SurPrimaire"), 15, 1.8, true);
            iconeLire.Margin = new Thickness(0, 0, 8, 0);
            var texteLire = new TextBlock { Text = "Lire", VerticalAlignment = VerticalAlignment.Center };
            var contenuLire = new StackPanel { Orientation = Orientation.Horizontal };
            contenuLire.Children.Add(iconeLire);
            contenuLire.Children.Add(texteLire);
            var lire = new Button { Style = S("BtnOr"), Content = contenuLire, Height = 38, Padding = new Thickness(14, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0) };
            lire.Click += (_, _) =>
            {
                if (A.Lecture(s).Etat == EtatLecture.Lecture) A.Pause(s);
                else A.Jouer(s);
                majLecture?.Invoke();
            };
            var arreter = new Button { Style = S("Btn"), Width = 38, Height = 38, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), Content = SonUi.Icone(SonUi.Stop, P("Blanc"), 13, 1.8, true), ToolTip = "Arrêter (la prochaine fois, elle repart du début)" };
            arreter.Click += (_, _) => { A.Arreter(s); majLecture?.Invoke(); };
            var temps = new TextBlock { FontFamily = SonUi.Mono, FontSize = 12.5, Foreground = P("Brume"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            actions.Children.Add(lire);
            actions.Children.Add(arreter);
            actions.Children.Add(temps);
            majLecture = () =>
            {
                var (e, position, duree) = A.Lecture(s);
                bool joue = e == EtatLecture.Lecture;
                iconeLire.Data = joue ? SonUi.PauseG : SonUi.Lire;
                texteLire.Text = joue ? "Pause" : "Lire";
                lire.ToolTip = joue ? "Mettre en pause" : "Jouer la musique";
                lire.IsEnabled = A.Running && !A.Debranche(s);
                arreter.IsEnabled = e is EtatLecture.Lecture or EtatLecture.Pause;
                temps.Text = duree > 0 ? SonUi.Duree(TimeSpan.FromMilliseconds(Math.Max(0, position))) + " / " + SonUi.Duree(TimeSpan.FromMilliseconds(duree)) : "";
            };
        }
        corps.Children.Add(actions);

        lents.Add(() =>
        {
            var e = A.Etat(s);
            var couleur = P(e.Couleur);
            point.Fill = couleur;
            etat.Text = e.Texte;
            etat.Foreground = couleur;
            conseil.Text = Conseil(s, e);
            ligneEtat.ToolTip = A.Probleme(s) is { Length: > 0 } pb ? pb : null;
            vu.Gris = !e.Actif || s.Coupe;
            eglise.Visibility = s.Eglise ? Visibility.Visible : Visibility.Collapsed;
            majCouper();
            majLecture?.Invoke();
        });
        vifs.Add(() =>
        {
            var (l, r) = A.Level(s.Id);
            vu.Niveau(Math.Max(l, r));
        });
        return new Border { Style = S("Carte"), Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 14, 14), Child = corps };
    }

    /// <summary>Un conseil en quelques mots, à côté du mot de la carte.</summary>
    static string Conseil(Son s, EtatSon e)
    {
        if (!e.Actif)
            return e.Texte switch
            {
                "Débranché" => "il revient tout seul quand il est rebranché",
                "Fichier introuvable" => "choisissez-le de nouveau dans Régler",
                "Coupé" => "cliquez Couper pour le rallumer",
                _ => "",
            };
        return e.Verdict switch
        {
            VerdictNiveau.TropFort => "baissez un peu le volume",
            VerdictNiveau.Faible => s.Genre == GenreSon.Voix ? "parlez plus près, ou montez le volume" : "montez un peu le volume",
            _ => "",
        };
    }

    /// <summary>La tuile « + Ajouter un son », en pointillés, au bout des cartes.</summary>
    FrameworkElement TuileAjout()
    {
        var tuile = new Grid { MinHeight = 196, Margin = new Thickness(0, 0, 14, 14), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Un micro, un instrument, la table de mixage, le son de l'ordinateur ou une musique" };
        var survol = new Border { CornerRadius = new CornerRadius(14), Opacity = 0 };
        survol.SetResourceReference(Border.BackgroundProperty, "Nuit2");
        tuile.Children.Add(survol);
        var trait = new Shapes.Rectangle { RadiusX = 14, RadiusY = 14, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 5, 4 } };
        trait.SetResourceReference(Shapes.Shape.StrokeProperty, "Trait2");
        tuile.Children.Add(trait);
        var centre = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        var rond = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            Background = P("OrLeger"), BorderBrush = P("OrBord"), Child = SonUi.Icone(SonUi.Plus, P("Or"), 22, 2.2),
        };
        centre.Children.Add(rond);
        var titre = SonUi.Texte("Ajouter un son", 16, "Blanc", FontWeights.Bold);
        titre.HorizontalAlignment = HorizontalAlignment.Center;
        titre.Margin = new Thickness(0, 12, 0, 4);
        centre.Children.Add(titre);
        var aide = SonUi.Texte(C.Sons.Count <= 1 ? "Pour commencer : le micro du pasteur, la table de mixage, le piano…" : "Micro, instrument, table de mixage, ordinateur, musique", 12.5, "Brume");
        aide.TextAlignment = TextAlignment.Center;
        aide.MaxWidth = 250;
        centre.Children.Add(aide);
        tuile.Children.Add(centre);
        tuile.MouseEnter += (_, _) => survol.Opacity = 1;
        tuile.MouseLeave += (_, _) => survol.Opacity = 0;
        tuile.MouseLeftButtonUp += (_, e) => { e.Handled = true; ActionsSon.Ajouter(this); };
        return tuile;
    }

    // ------------------------------------------------------------ où va le son
    static (Border Cadre, StackPanel Corps) Destination(Geometry icone, string couleur, string titre, string sousTitre, bool pleine = false)
    {
        var corps = new StackPanel();
        var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var pastille = SonUi.Pastille(icone, couleur, 42, pleine);
        pastille.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(pastille, Dock.Left);
        tete.Children.Add(pastille);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(SonUi.Texte(titre, 17, "Blanc", FontWeights.ExtraBold));
        textes.Children.Add(SonUi.Texte(sousTitre, 12.5, "Brume"));
        tete.Children.Add(textes);
        corps.Children.Add(tete);
        return (new Border { Style = S("Carte"), Padding = new Thickness(20, 18, 20, 18), Margin = new Thickness(0, 0, 14, 14), Child = corps }, corps);
    }

    /// <summary>Une case avec l'icône et le nom d'un son.</summary>
    static StackPanel NomSon(Son s)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var i = SonUi.Icone(SonUi.IconeGenre(s.Genre), P(SonUi.CouleurGenre(s.Genre)), 16);
        i.Margin = new Thickness(0, 0, 7, 0);
        p.Children.Add(i);
        p.Children.Add(new TextBlock { Text = s.Nom, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    /// <summary>Dans l'église : les haut-parleurs de l'ordinateur, et les sons qu'on y entend.</summary>
    FrameworkElement CarteEglise()
    {
        var (cadre, corps) = Destination(SonUi.Eglise, "Or", "Dans l'église", "Ce qui sort par les haut-parleurs de l'ordinateur");
        corps.Children.Add(SonUi.Etiquette("Haut-parleurs", 0));
        corps.Children.Add(SonUi.ListeSorties(C.Eglise.Appareil, C.Eglise.NomAppareil, (id, nom) =>
        {
            C.Eglise.Appareil = id;
            C.Eglise.NomAppareil = nom;
            if (C.Casque.Actif && ActionsSon.MemeSortie(C.Casque.Appareil, id))
            {
                C.Casque.Actif = false;
                SonUi.Toast("Casque éteint : il avait la même sortie que l'église.");
            }
            A.Apply();
        }, "Haut-parleurs de Windows (conseillé)"));
        corps.Children.Add(SonUi.Etiquette("On y entend"));
        foreach (var s in C.Sons)
        {
            var cb = new CheckBox { Content = NomSon(s), IsChecked = s.Eglise, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
            cb.Click += (_, _) =>
            {
                bool oui = cb.IsChecked == true;
                var w = Window.GetWindow(this);
                if (oui && s.EstMicro && !Dialogue.Confirmer(w, $"« {s.Nom} » dans les haut-parleurs de l'église peut siffler (larsen). Seulement s'il ne passe pas déjà par la table de mixage. Continuer ?", "Oui, dans l'église"))
                {
                    cb.IsChecked = false;
                    return;
                }
                if (oui && s.Genre == GenreSon.Ordinateur && !Dialogue.Confirmer(w, "Le son de l'ordinateur s'entend déjà dans l'église : il s'entendrait deux fois (écho). Continuer ?", "Oui, quand même"))
                {
                    cb.IsChecked = false;
                    return;
                }
                s.Eglise = oui;
                A.Apply();
            };
            lents.Add(() => { if (cb.IsChecked != s.Eglise) cb.IsChecked = s.Eglise; });
            corps.Children.Add(cb);
        }
        if (C.Sons.Any(s => s.EstMicro))
            corps.Children.Add(SonUi.Note("Les micros et les instruments passent d'habitude par la table de mixage : laissez-les décochés, sinon ça siffle.", 4));

        // 2.4.3 : le son de Zoom (les personnes à distance), sur ces haut-parleurs, avec son volume
        var zoom = new CheckBox { Content = "Le son de Zoom (les personnes à distance)", IsChecked = C.Eglise.Zoom, Margin = new Thickness(0, 8, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        var ligneZoom = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var pourcentZoom = new TextBlock { FontFamily = SonUi.Mono, FontSize = 13, FontWeight = FontWeights.SemiBold, Width = 54, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = VolumeCurseur.Texte(C.Eglise.ZoomVolumeDb) };
        DockPanel.SetDock(pourcentZoom, Dock.Right);
        ligneZoom.Children.Add(pourcentZoom);
        var libelleZoom = SonUi.Texte("Volume de Zoom", 12.5, "Brume", null, false);
        libelleZoom.VerticalAlignment = VerticalAlignment.Center;
        libelleZoom.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(libelleZoom, Dock.Left);
        ligneZoom.Children.Add(libelleZoom);
        var curseurZoom = new Slider
        {
            Minimum = 0, Maximum = 1, SmallChange = 0.01, LargeChange = 0.05, Value = VolumeCurseur.Position(C.Eglise.ZoomVolumeDb), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "100 % : le volume de Zoom tel quel. Double-clic : revenir à 100 %.",
        };
        curseurZoom.ValueChanged += (_, e) =>
        {
            double v = e.NewValue;
            if (Math.Abs(v - VolumeCurseur.Normal) < 0.015) v = VolumeCurseur.Normal;
            C.Eglise.ZoomVolumeDb = Math.Round(VolumeCurseur.Db(v), 1);
            pourcentZoom.Text = VolumeCurseur.Texte(C.Eglise.ZoomVolumeDb);
            A.Apply();
        };
        curseurZoom.MouseDoubleClick += (_, _) => curseurZoom.Value = VolumeCurseur.Normal;
        ligneZoom.Children.Add(curseurZoom);
        var noteZoom = SonUi.Note("Dans Zoom › Paramètres › Audio, choisissez comme haut-parleur une autre sortie que celle de l'église (par exemple « CABLE Input »), sinon on l'entend deux fois. Si les personnes à distance s'entendent en écho, baissez ce volume.", 2);
        void MajZoom()
        {
            bool oui = C.Eglise.Zoom;
            ligneZoom.Visibility = noteZoom.Visibility = oui ? Visibility.Visible : Visibility.Collapsed;
            if (zoom.IsChecked != oui) zoom.IsChecked = oui;
        }
        zoom.Click += (_, _) => { C.Eglise.Zoom = zoom.IsChecked == true; MajZoom(); A.Apply(); };
        lents.Add(MajZoom);
        corps.Children.Add(zoom);
        corps.Children.Add(ligneZoom);
        corps.Children.Add(noteZoom);
        MajZoom();
        return cadre;
    }

    /// <summary>Enregistrer : un clic sur le rond rouge, un fichier par culte.</summary>
    FrameworkElement CarteEnregistrer()
    {
        var (cadre, corps) = Destination(SonUi.Enregistrer, "Direct", "Enregistrer", "Gardez le culte dans un fichier : le son, ou la vidéo et le son", true);
        if (C.Profiles.Count == 0)
        {
            var b = SonUi.Bouton("Enregistrer le culte", SonUi.Enregistrer, "BtnRouge", 46, "Direct", true);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.FontWeight = FontWeights.Bold;
            b.ToolTip = "Démarre tout de suite : le son du culte en MP3";
            b.Click += (_, _) => ActionsSon.EnregistrerLeCulte();
            corps.Children.Add(b);
            corps.Children.Add(SonUi.Note("Un fichier MP3 avec tout le son, prêt à partager (WhatsApp, site). Il se range dans Vidéos › Siloé Stream."));
        }
        foreach (var p in C.Profiles) corps.Children.Add(LigneEnregistrement(p));
        var liens = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (C.Profiles.Count < AudioConfiguration.MaxEnregistrements)
            liens.Children.Add(SonUi.Lien(C.Profiles.Count == 0 ? "Autre chose (vidéo, qualité studio…)" : "+ Autre enregistrement", () => ActionsSon.NouvelEnregistrement(this)));
        liens.Children.Add(SonUi.Lien("Ouvrir le dossier", () => ActionsSon.OuvrirDossier(C.Profiles.Count == 1 ? ActionsSon.Dossier(C.Profiles[0]) : AudioConfiguration.DossierEnregistrements)));
        corps.Children.Add(liens);
        return cadre;
    }

    /// <summary>Une ligne d'enregistrement : le rond rouge (le carré pour arrêter), son nom, son chrono et sa taille.</summary>
    FrameworkElement LigneEnregistrement(RecordingProfile p)
    {
        var r = A.Recorder;
        var ligne = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var rond = new Shapes.Ellipse { Width = 22, Height = 22, StrokeThickness = 2 };
        rond.SetResourceReference(Shapes.Shape.StrokeProperty, "Direct");
        var dedans = new Shapes.Rectangle { Width = 12, Height = 12 };
        dedans.SetResourceReference(Shapes.Shape.FillProperty, "Direct");
        var symbole = new Grid { Width = 22, Height = 22 };
        symbole.Children.Add(rond);
        symbole.Children.Add(dedans);
        var bouton = new Button { Style = S("Btn"), Width = 48, Height = 48, Padding = new Thickness(0), Content = symbole, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        bouton.Click += (_, _) => ActionsSon.Basculer(p);
        DockPanel.SetDock(bouton, Dock.Left);
        ligne.Children.Add(bouton);
        var regler = new Button { Style = S("BtnPetit"), Content = "Régler", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Son nom, le son ou la vidéo, ce qu'il entend, son dossier" };
        regler.Click += (_, _) => ActionsSon.ReglerEnregistrement(p, this);
        DockPanel.SetDock(regler, Dock.Right);
        ligne.Children.Add(regler);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(SonUi.Texte(p.Name, 15.5, "Blanc", FontWeights.Bold, false));
        var detail = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        textes.Children.Add(detail);
        var voir = SonUi.Lien("Voir le fichier", () => { if (r.LastFile(p.Id) is { } f && File.Exists(f)) SonUi.MontrerFichier(f); else ActionsSon.OuvrirDossier(ActionsSon.Dossier(p)); }, 12.5);
        voir.Margin = new Thickness(0, 2, 0, 0);
        textes.Children.Add(voir);
        var vu = new Vumetre(6) { Margin = new Thickness(0, 6, 0, 0) };
        textes.Children.Add(vu);
        ligne.Children.Add(textes);
        lents.Add(() =>
        {
            bool actif = r.Active(p.Id), occupe = r.Busy(p.Id);
            var etat = r.State(p.Id);
            dedans.RadiusX = dedans.RadiusY = actif ? 2 : 6;
            dedans.Width = dedans.Height = actif ? 10 : 12;
            rond.Visibility = actif ? Visibility.Collapsed : Visibility.Visible;
            bouton.IsEnabled = !occupe;
            bouton.ToolTip = actif ? $"Arrêter « {p.Name} »" : $"Démarrer « {p.Name} »";
            bouton.BorderBrush = actif ? P("Direct") : P("Nuit3");
            bouton.Background = actif ? P("RougeLeger") : P("Encre");
            vu.Visibility = actif ? Visibility.Visible : Visibility.Collapsed;
            bool termine = !actif && !occupe && etat == ChurchRecordingState.Ready && r.LastFile(p.Id) != null;
            voir.Visibility = termine ? Visibility.Visible : Visibility.Collapsed;
            if (actif)
            {
                detail.Text = $"En cours · {SonUi.Duree(r.Duration(p.Id))} · {SonUi.Taille(r.Bytes(p.Id))}";
                detail.Foreground = P("Direct");
                detail.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                detail.FontWeight = FontWeights.Normal;
                detail.Text = occupe ? r.Message(p.Id)
                    : etat == ChurchRecordingState.Error ? r.Message(p.Id)
                    : termine ? "Enregistré : " + Path.GetFileName(r.LastFile(p.Id))
                    : ActionsSon.Resume(p);
                detail.Foreground = P(etat == ChurchRecordingState.Error ? "RougeTexte" : termine ? "Vert" : "Brume");
            }
        });
        vifs.Add(() => { if (r.Active(p.Id)) vu.Niveau(A.TrackLevel(A.PisteDe(p.Id) - 1)); });
        return ligne;
    }

    /// <summary>Zoom et en ligne : Siloé Stream devient un micro (« CABLE Output ») pour Zoom, OBS, Teams.</summary>
    FrameworkElement CarteEnLigne()
    {
        var (cadre, corps) = Destination(SonUi.EnLigne, "TypeChant", "Zoom et en ligne", "Zoom, OBS (YouTube, Facebook), Teams entendent le culte");
        if (C.VirtualSounds.Count == 0)
        {
            var b = SonUi.Bouton("Envoyer le son à Zoom", SonUi.EnLigne, "BtnOr", 46);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.ToolTip = "Zoom (ou OBS, Teams) reçoit tout le son du culte comme un micro";
            b.Click += (_, _) => ActionsSon.NouvelEnvoiZoom();
            corps.Children.Add(b);
            corps.Children.Add(SonUi.Note("Ensuite, dans Zoom (ou OBS, Teams), choisissez le micro « CABLE Output »."));
        }
        foreach (var v in C.VirtualSounds) corps.Children.Add(LigneEnvoi(v));
        if (C.VirtualSounds.Any(v => v.ParCable))
        {
            if (VbCable.Entree() == null) corps.Children.Add(Cable());
            else
            {
                var t = SonUi.Texte("Dans Zoom, OBS ou Teams : choisissez le micro « CABLE Output ».", 13.5, "Blanc", FontWeights.SemiBold);
                t.Margin = new Thickness(0, 2, 0, 0);
                corps.Children.Add(t);
            }
        }
        if (C.VirtualSounds.Count > 0 && C.VirtualSounds.Count < AudioConfiguration.MaxSortiesVirtuelles)
        {
            var liens = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            liens.Children.Add(SonUi.Lien("+ Autre envoi", () => ActionsSon.NouvelEnvoi(this)));
            corps.Children.Add(liens);
        }
        return cadre;
    }

    /// <summary>Il manque le câble audio virtuel : un clic l'installe (gratuit).</summary>
    FrameworkElement Cable()
    {
        var cadre = new Border { Background = P("OrLeger"), BorderBrush = P("OrBord"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 4, 0, 0) };
        var p = new StackPanel();
        p.Children.Add(SonUi.Texte("Il manque le câble audio virtuel : c'est lui qui fait entendre Siloé Stream à Zoom. Gratuit, une minute.", 13.5, "Blanc"));
        var etat = SonUi.Texte("", 12.5, "Encre2");
        etat.Margin = new Thickness(0, 6, 0, 0);
        etat.Visibility = Visibility.Collapsed;
        var installer = new Button { Style = S("BtnOr"), Content = "Installer le câble", Height = 38, Padding = new Thickness(16, 0, 16, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        installer.ToolTip = "Télécharge VB-CABLE sur vb-audio.com puis lance son installateur (Windows demande votre accord)";
        installer.Click += async (_, _) =>
        {
            installer.IsEnabled = false;
            etat.Visibility = Visibility.Visible;
            await ActionsSon.InstallerCable(new Progress<string>(t => etat.Text = t));
            installer.IsEnabled = true;
            aRefaire = true;
            Maj();
        };
        p.Children.Add(installer);
        p.Children.Add(etat);
        cadre.Child = p;
        return cadre;
    }

    /// <summary>Une ligne d'envoi : l'interrupteur, son nom, où il part, la barre de ce qui part.</summary>
    FrameworkElement LigneEnvoi(VirtualSoundSettings v)
    {
        var ligne = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var inter = new CheckBox { Style = S("Interrupteur"), IsChecked = v.Enabled, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 14, 0), ToolTip = "Allumer ou éteindre cet envoi" };
        inter.Click += (_, _) => ActionsSon.Allumer(v, inter.IsChecked == true);
        DockPanel.SetDock(inter, Dock.Left);
        ligne.Children.Add(inter);
        var regler = new Button { Style = S("BtnPetit"), Content = "Régler", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Son nom, vers où, ce qu'il entend" };
        regler.Click += (_, _) => ActionsSon.ReglerEnvoi(v, this);
        DockPanel.SetDock(regler, Dock.Right);
        ligne.Children.Add(regler);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(SonUi.Texte(v.Name, 15.5, "Blanc", FontWeights.Bold, false));
        var detail = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        textes.Children.Add(detail);
        var vu = new Vumetre(6) { Margin = new Thickness(0, 6, 0, 0) };
        textes.Children.Add(vu);
        ligne.Children.Add(textes);
        lents.Add(() =>
        {
            if (inter.IsChecked != v.Enabled) inter.IsChecked = v.Enabled;
            string statut = v.Enabled ? A.Sorties.Status(v.Id) : "Éteint";
            bool marche = v.Enabled && A.Sorties.Actif(v.Id);
            string ou = v.ParCable ? "vers Zoom, OBS ou Teams (« CABLE Output »)" : "vers " + (A.Sorties.Appareil(v.Id) is { Length: > 0 } a ? AudioConfiguration.NomCourt(a) : v.DeviceName.Length > 0 ? AudioConfiguration.NomCourt(v.DeviceName) : "la sortie de Windows");
            detail.Text = marche ? "En marche · " + ou + (v.ChoixSons != null ? " · " + C.Recoit(v) : "") : statut;
            detail.Foreground = P(marche ? "Vert" : !v.Enabled ? "Brume" : "RougeTexte");
            vu.Visibility = marche ? Visibility.Visible : Visibility.Collapsed;
        });
        vifs.Add(() => { if (v.Enabled) vu.Niveau(A.Sorties.Level(v.Id)); });
        return ligne;
    }

    /// <summary>Le casque de la régie : tout le son, comme il part à Zoom et dans l'enregistrement.</summary>
    FrameworkElement CarteCasque()
    {
        var (cadre, corps) = Destination(SonUi.Casque, "TypeDuo", "Casque", "Écouter tout le son, comme il part dans l'enregistrement et à Zoom");
        var (ligne, inter) = SonUi.Interrupteur("Écouter dans un casque", "Pour vérifier le son pendant le culte, sans déranger la salle.", C.Casque.Actif);
        inter.Click += (_, _) =>
        {
            bool oui = inter.IsChecked == true;
            if (oui && ActionsSon.MemeSortie(C.Casque.Appareil, C.Eglise.Appareil))
            {
                Dialogue.Informer(Window.GetWindow(this), "Choisissez d'abord la sortie du casque (juste en dessous) : elle doit être différente des haut-parleurs de l'église, sinon les micros sortiraient dans la salle et siffleraient.");
                inter.IsChecked = false;
                return;
            }
            C.Casque.Actif = oui;
            A.Apply();
        };
        corps.Children.Add(ligne);
        corps.Children.Add(SonUi.Etiquette("Sortie du casque", 0));
        corps.Children.Add(SonUi.ListeSorties(C.Casque.Appareil, C.Casque.NomAppareil, (id, nom) =>
        {
            C.Casque.Appareil = id;
            C.Casque.NomAppareil = nom;
            if (C.Casque.Actif && ActionsSon.MemeSortie(id, C.Eglise.Appareil))
            {
                C.Casque.Actif = false;
                SonUi.Toast("Casque éteint : c'est la sortie de l'église.");
            }
            A.Apply();
        }));
        var etat = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        corps.Children.Add(etat);
        var vu = new Vumetre(6) { Margin = new Thickness(0, 6, 0, 0) };
        corps.Children.Add(vu);
        lents.Add(() =>
        {
            if (inter.IsChecked != C.Casque.Actif) inter.IsChecked = C.Casque.Actif;
            bool marche = C.Casque.Actif && A.Sorties.Actif(AudioConfiguration.IdCasque);
            etat.Text = !C.Casque.Actif ? "Éteint" : marche ? "En marche · " + AudioConfiguration.NomCourt(A.Sorties.Appareil(AudioConfiguration.IdCasque)) : A.Sorties.Status(AudioConfiguration.IdCasque);
            etat.Foreground = P(marche ? "Vert" : !C.Casque.Actif ? "Brume" : "RougeTexte");
            vu.Visibility = marche ? Visibility.Visible : Visibility.Collapsed;
        });
        vifs.Add(() => { if (C.Casque.Actif) vu.Niveau(A.Sorties.Level(AudioConfiguration.IdCasque)); });
        return cadre;
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>Les noms des cartes de sons affichées (test automatique).</summary>
    public string TestCartes() { Maj(); return string.Join(",", C.Sons.Select(s => s.Nom)) + " · " + grilleSons.Children.Count + " tuiles"; }
    /// <summary>Le mot d'un son tel que la carte le montre (test automatique).</summary>
    public string TestEtat(Son s) => A.Etat(s).Texte;
    /// <summary>Fait défiler la page jusqu'en bas (« Où va le son »), pour la capture du test.</summary>
    public void TestDefiler(bool bas) { UpdateLayout(); if (bas) defil.ScrollToEnd(); else defil.ScrollToHome(); }
}
