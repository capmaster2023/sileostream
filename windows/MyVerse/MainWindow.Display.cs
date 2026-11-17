using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MyVerse.Ui;

public partial class MainWindow
{
    void InitDisplay()
    {
        DisplayAjouter.Click += (_, _) => Ouvrir(MenuAjouterSource(), DisplayAjouter, PlacementMode.Bottom);
        DisplayPeripherique.Click += (_, _) => Ouvrir(MenuAjouterPeripherique(), DisplayPeripherique, PlacementMode.Bottom);
        DisplayReconnecter.Click += (_, _) => ReconnecterSources();
        ListeAnnonces.MouseDoubleClick += Display_DoubleClick;
        ListeChants.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(ListeChants, o) is ListBoxItem { DataContext: Chant c })
            { R.AfficherSection(c.Id, 0); e.Handled = true; }
        };
        ChantTitre.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && chantOuvert != null) { R.AfficherSection(chantOuvert, 0); e.Handled = true; } };
        AnnonceTitre.MouseDoubleClick += (_, e) => { if (EnregistrerAnnonce() is { } a) { R.AfficherAnnonce(a); e.Handled = true; } };
        App.Telephone.MessageVu += (id, texte) => Dispatcher.BeginInvoke(new Action(() => Toast($"{NomMyCam(id)} a vu : « {texte} »")));
        App.Telephone.Cameras.Change += () => Dispatcher.BeginInvoke(new Action(AnnoncerMyCam));
        var minuterieMyCam = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        minuterieMyCam.Tick += (_, _) => ApercusMyCam();
        minuterieMyCam.Start();
        App.Sources.Change += () =>
        {
            var b = App.Sources.Image;
            ApercuRendu.ImageDisplay(b); CulteRendu.ImageDisplay(b);
            // la salle (1.3.1) montre peut-être l'écran par défaut à la place du direct : il a sa capture
            if (mediaSalle == null) { ecran?.Rendu.ImageDisplay(b); EgliseRendu.ImageDisplay(b); }
            // 1.6.0 : MyStream (Zoom, YouTube) suit le lecteur de son image : celui-ci, l'écran de repos ou sa source principale
            if (lecteurStream == App.Sources) ImageStream();
            if (duoMemeSource || lecteurStreamDuo == App.Sources) ImagesDuo();
            DisplaySourceInfo.Text = App.Sources.Statut;
        };
        App.SourcesDefaut.Change += () =>
        {
            if (mediaSalle != null) { ecran?.Rendu.ImageDisplay(App.SourcesDefaut.Image); EgliseRendu.ImageDisplay(App.SourcesDefaut.Image); }
            if (lecteurStream == App.SourcesDefaut) ImageStream();
        };
    }

    /// <summary>
    /// Le média montré à la place du direct, avec sa propre capture (<see cref="App.SourcesDefaut"/>) : sur l'écran de projection,
    /// l'écran par défaut pendant une source « En streaming » (1.3.1) ; sur MyVirtualCam, l'écran par défaut pendant une source
    /// « Dans l'église » (1.3.2). Jamais les deux à la fois. Null : le direct.
    /// </summary>
    SourceDisplay? mediaSalle, mediaCamera;

    ContextMenu MenuAjouterSource()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Article("Nouveau texte", AjouterTexteDisplay));
        menu.Items.Add(Article("Images", AjouterImage));
        menu.Items.Add(Article("Vidéos avec audio intégré", AjouterVideo));
        // 2.1.0 : les caméras ont leur propre bouton, juste en dessous (« + Ajouter un périphérique vidéo »)
        menu.Items.Add(Article("Capture de fenêtre", AjouterFenetre));
        // les dossiers de l'onglet Duo : un clic les met dans la bibliothèque et dans la grille
        var duos = R.Duos.ToList();
        if (duos.Count > 0) menu.Items.Add(new Separator());
        foreach (var x in duos)
        {
            var d = x;
            menu.Items.Add(Article("Duo · " + d.Nom, () =>
            {
                if (!d.A.Choisi || !d.B.Choisi) { Toast($"« {d.Nom} » n'a pas encore ses 2 éléments : complétez-le dans l'onglet Duo."); return; }
                R.AjouterDuoDisplay(d);
                Toast($"« {d.Nom} » est dans la grille : un clic l'envoie en direct.");
            }));
        }
        return menu;
    }

    /// <summary>
    /// 2.1.0 : « + Ajouter un périphérique vidéo », à part des médias : MyVirtualCam d'abord (la caméra officielle de Siloé Stream,
    /// comme l'OBS Virtual Camera), puis chaque webcam ou carte de capture branchée (un clic l'ajoute), le téléphone MyCam et la caméra Sony.
    /// </summary>
    ContextMenu MenuAjouterPeripherique()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Article("MyVirtualCam (l'image de MyStream)", () => AjouterMyVirtualCam(null), true));
        List<SourcesDisplay.Peripherique> devices;
        try { devices = SourcesDisplay.Peripheriques(); }
        catch (Exception ex) { Journal.Erreur("périphériques vidéo", ex); devices = new(); }
        menu.Items.Add(new Separator());
        if (devices.Count == 0) menu.Items.Add(new MenuItem { Header = "(aucune webcam ni carte de capture branchée)", IsEnabled = false });
        foreach (var d in devices)
        {
            var dev = d;
            menu.Items.Add(Article(dev.Nom, () => AjouterPeripherique(dev)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Article("Téléphone MyCam (caméra Android)", AjouterMyCam));
        menu.Items.Add(Article("Caméra Sony par Wi-Fi (sans câble)", AjouterCameraWifi));
        return menu;
    }

    /// <summary>2.1.0 : une webcam ou une carte de capture choisie dans le menu des périphériques : un média, comme avant.</summary>
    void AjouterPeripherique(SourcesDisplay.Peripherique d)
    {
        try { ChoisirDisplay(R.NouveauDisplay(d.Nom, new SourceDisplay { Type = "camera", Peripherique = d.Id })); }
        catch (Exception ex) { Toast("Périphérique inaccessible : " + ex.Message); }
    }

    void AjouterTexteDisplay()
    {
        if (Dialogue.DemanderTexte(this, "Nouveau texte", "Nouveau texte", "") is not { } t || t.Texte.Trim().Length == 0) return;
        var a = R.NouvelleAnnonce(t.Titre, t.Texte);
        R.GrilleAnnonce(a.Id, true);
        ChoisirDisplay(a);
    }

    void Display_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject o) return;
        // A double click on the add button or on empty space must not send an unrelated item live.
        for (var p = o; p != null && p is not ListBoxItem; p = p is FrameworkContentElement f ? f.Parent : VisualTreeHelper.GetParent(p))
            if (p is ButtonBase) return;
        if (ItemsControl.ContainerFromElement(ListeAnnonces, o) is not ListBoxItem { DataContext: Annonce a }) return;
        if (a == annonceChoisie && EnregistrerAnnonce() is { } sauvegardee) a = sauvegardee;
        R.AfficherAnnonce(a); e.Handled = true;
    }

    void ChoisirDisplay(Annonce a)
    {
        annonceChoisie = a;
        AnnonceTitre.Text = a.Titre;
        AnnonceTexte.Text = a.Source != null ? a.Description : a.Texte;
        AnnonceTexte.IsReadOnly = a.Source != null;
        DisplayReconnecter.Visibility = a.Source?.Type is "camera" or "fenetre" ? Visibility.Visible : Visibility.Collapsed;
        AnnoncesMaj();
    }

    void AjouterImage()
    {
        var dialog = new OpenFileDialog { Title = "Ajouter des images aux Médias", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var dir = Path.Combine(App.Donnees.Dossier, "display-images"); Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, Guid.NewGuid().ToString("N") + Path.GetExtension(file));
                File.Copy(file, dest);
                ChoisirDisplay(R.NouveauDisplay(Path.GetFileNameWithoutExtension(file), new SourceDisplay { Type = "image", Chemin = dest }));
            }
            catch (Exception ex) { Toast("Image inaccessible : " + ex.Message); }
        }
    }
    void AjouterVideo()
    {
        var dialog = new OpenFileDialog { Title = "Ajouter une vidéo aux Médias", Filter = "Vidéos|*.mp4;*.mkv;*.wmv;*.avi;*.mov", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        ChoisirDisplay(R.NouveauDisplay(Path.GetFileNameWithoutExtension(dialog.FileName), new SourceDisplay { Type = "video", Chemin = dialog.FileName }));
        // le mélangeur reste choisi par l'utilisateur : aucune source audio n'est ajoutée toute seule
        Toast("Vidéo ajoutée. Son de la vidéo : ajoutez « Son de la vidéo projetée » dans le panneau Audio.");
    }
    void AjouterCamera()
    {
        try
        {
            // 1.6.8 : MyVirtualCam (l'image de MyStream) se choisit aussi ; elle reste « Dans l'église seulement » (sinon MyStream
            // se filmerait lui-même, en tunnel sans fin)
            // 2.0.3 : c'est une caméra officielle de Siloé Stream, comme l'OBS Virtual Camera : toujours en tête de liste, même pas
            // encore installée (la choisir l'installe)
            var devices = SourcesDisplay.Peripheriques(true);
            var mvc = devices.FirstOrDefault(d => SourcesDisplay.EstMyVirtualCam(d.Nom));
            if (mvc != null) devices.Remove(mvc);
            devices.Insert(0, mvc ?? new SourcesDisplay.Peripherique(-1, "MyVirtualCam", ""));
            if (ChoisirSource("Périphérique de capture vidéo", "Webcam, carte de capture USB/HDMI, ou MyVirtualCam (l'image de MyStream)", devices,
                    d => SourcesDisplay.EstMyVirtualCam(d.Nom)
                        ? (d.Id.Length > 0 ? "MyVirtualCam · l'image de MyStream (Zoom, YouTube)" : "MyVirtualCam · l'image de MyStream (s'installe au premier choix)")
                        : d.Nom) is not { } device) return;
            if (SourcesDisplay.EstMyVirtualCam(device.Nom)) { AjouterMyVirtualCam(device.Id.Length > 0 ? device : null); return; }
            ChoisirDisplay(R.NouveauDisplay(device.Nom, new SourceDisplay { Type = "camera", Peripherique = device.Id }));
        }
        catch (Exception ex) { Toast("Liste des périphériques inaccessible : " + ex.Message); }
    }
    /// <summary>
    /// 1.6.8 : MyVirtualCam comme média : l'image de MyStream (ce que voient Zoom et YouTube), à montrer dans l'église. Une seule fois
    /// dans les médias ; « Dans l'église seulement » (jamais sur MyStream) ; 720p à 30 images/s (son format). Pas encore installée :
    /// Siloé Stream l'installe (Windows demande l'autorisation), puis l'ajoute.
    /// </summary>
    async void AjouterMyVirtualCam(SourcesDisplay.Peripherique? device)
    {
        if (R.Annonces.FirstOrDefault(x => x.Source is { EstMyVirtualCam: true }) is { } deja)
        {
            R.GrilleAnnonce(deja.Id, true);
            ChoisirDisplay(deja);
            Toast($"MyVirtualCam est déjà dans les médias (« {deja.Titre} ») : un clic sur sa tuile montre l'image de MyStream dans l'église.");
            return;
        }
        if (await SourceMyVirtualCam(device) is not { } a) return;
        R.GrilleAnnonce(a.Id, true);
        ChoisirDisplay(a);
        Toast("MyVirtualCam ajoutée aux médias : un clic sur sa tuile montre dans l'église l'image de MyStream (Zoom, YouTube). Elle ne repart jamais sur MyStream.");
    }

    /// <summary>
    /// 2.0.3 : MyVirtualCam est une caméra officielle de Siloé Stream, comme l'OBS Virtual Camera dans OBS : elle est proposée partout
    /// où l'on choisit une caméra (médias, Duo, source principale de l'écran, programme), même pas encore ajoutée aux médias ni installée.
    /// Rend sa source des médias : celle qui existe, sinon créée une seule fois (installée d'abord au besoin, Windows demande l'autorisation) ;
    /// null si Windows ne la donne pas. « Dans l'église seulement » : elle ne repart jamais à l'antenne (pas d'image en tunnel sans fin).
    /// </summary>
    async Task<Annonce?> SourceMyVirtualCam(SourcesDisplay.Peripherique? device = null)
    {
        if (R.Annonces.FirstOrDefault(x => x.Source is { EstMyVirtualCam: true }) is { } deja) return deja;
        try
        {
            device ??= TrouverMyVirtualCam();
            if (device == null && !MyVirtualCam.Installee)
            {
                Toast("Installation de MyVirtualCam : acceptez la demande de Windows.");
                if (!await MyVirtualCam.InstallerEtAttendre(this)) { Toast("MyVirtualCam n'est pas installée."); return null; }
                for (int i = 0; i < 10 && (device = TrouverMyVirtualCam()) == null; i++) await Task.Delay(300);
            }
            if (device == null) { Toast("MyVirtualCam est installée, mais Windows ne la montre pas encore : fermez puis rouvrez Siloé Stream, et réessayez."); return null; }
            return R.NouveauDisplay("MyVirtualCam (MyStream)", new SourceDisplay { Type = "camera", Peripherique = device.Id, Diffusion = SourceDisplay.Eglise, Ips = 30, Taille = "720p" });
        }
        catch (Exception ex) { Toast("MyVirtualCam inaccessible : " + ex.Message); return null; }
    }

    /// <summary>2.0.3 : l'article « MyVirtualCam » des menus, s'il n'est pas déjà dans les médias (sinon il y est sous son nom).</summary>
    bool MyVirtualCamAbsente => !R.Annonces.Any(x => x.Source is { EstMyVirtualCam: true });

    static SourcesDisplay.Peripherique? TrouverMyVirtualCam() => SourcesDisplay.Peripheriques(true).FirstOrDefault(d => SourcesDisplay.EstMyVirtualCam(d.Nom));

    /// <summary>MyCam (4.0) : un téléphone jumelé qui diffuse devient une source, comme une caméra USB.</summary>
    void AjouterMyCam()
    {
        var liste = App.Telephone.Cameras.Liste();
        if (liste.Count == 0)
        {
            Dialogue.Informer(this, "Aucun téléphone MyCam pour l'instant.\n\nSur le téléphone jumelé : Siloé Stream › onglet MyCam › Diffuser (même Wi-Fi que ce PC, ou câble USB avec le partage de connexion). Il apparaîtra ici tout seul.");
            return;
        }
        if (ChoisirSource("Téléphone MyCam", "Les téléphones jumelés qui ont ouvert MyCam. Sur le téléphone, touchez Diffuser pour voir l'image ici.", liste,
                i => i.Nom + " · " + (i.EnDirect ? i.Resume : "ne diffuse pas en ce moment")) is not { } c) return;
        if (R.Annonces.FirstOrDefault(x => x.Source is { Type: "mycam" } s && s.Peripherique == c.DeviceId) is { } deja)
        {
            ChoisirDisplay(deja);
            Toast($"« {deja.Titre} » est déjà dans les Médias.");
            return;
        }
        var a = R.NouveauDisplay(c.Nom, new SourceDisplay { Type = "mycam", Peripherique = c.DeviceId });
        R.GrilleAnnonce(a.Id, true);
        ChoisirDisplay(a);
        Toast($"« {c.Nom} » ajoutée : un clic sur sa tuile l'envoie à l'écran. Le téléphone voit son témoin (rouge : à l'écran, orange : ensuite).");
    }

    /// <summary>1.9.5 : la caméra Sony reçue par son Wi-Fi, comme Imaging Edge Mobile : une source MyCam de plus.</summary>
    void AjouterCameraWifi()
    {
        var w = new CameraWifiWindow { Owner = this };
        w.ShowDialog();
        if (!w.Connectee) return;
        var nom = App.CameraWifi.Nom;
        if (R.Annonces.FirstOrDefault(x => x.Source is { Type: "mycam" } s && s.Peripherique == CameraWifi.Id) is { } deja)
        {
            ChoisirDisplay(deja);
            Toast($"« {deja.Titre} » est déjà dans les Médias : son image revient dès que la caméra répond.");
            return;
        }
        var a = R.NouveauDisplay(nom, new SourceDisplay { Type = "mycam", Peripherique = CameraWifi.Id });
        R.GrilleAnnonce(a.Id, true);
        ChoisirDisplay(a);
        Toast($"« {nom} » ajoutée : un clic sur sa tuile l'envoie à l'écran dès que l'image arrive.");
    }

    /// <summary>Sous-menu d'une source MyCam : consignes toutes prêtes, ou un message libre.</summary>
    MenuItem MenuMessageCam(Annonce a)
    {
        var m = new MenuItem { Header = "Message au caméraman" };
        foreach (var t in new[] { "Plan serré sur le pasteur", "Plan large", "Vous passez à l'écran", "Merci, c'est bon" })
        {
            var texte = t;
            m.Items.Add(Article(texte, () => EnvoyerMessageCam(a, texte)));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Article("Autre message…", () =>
        {
            if (Dialogue.Demander(this, "Message affiché sur le téléphone du caméraman :", "", "MyCam") is { } t && t.Trim().Length > 0) EnvoyerMessageCam(a, t);
        }));
        return m;
    }

    void EnvoyerMessageCam(Annonce a, string texte)
    {
        if (a.Source is not { Type: "mycam" } s) return;
        Toast(App.Telephone.EnvoyerMessageCam(s.Peripherique, texte)
            ? $"Message envoyé à « {a.Titre} » : « {texte} »"
            : $"« {a.Titre} » n'est pas connecté : ouvrez Siloé Stream sur le téléphone.");
    }

    readonly HashSet<string> myCamsAnnoncees = new();

    /// <summary>Un téléphone se met à diffuser sans être dans Display : on dit comment l'ajouter (une fois).</summary>
    void AnnoncerMyCam()
    {
        foreach (var i in App.Telephone.Cameras.Liste().Where(x => x.EnDirect))
        {
            if (!myCamsAnnoncees.Add(i.DeviceId)) continue;
            if (R.Annonces.Any(x => x.Source is { Type: "mycam" } s && s.Peripherique == i.DeviceId)) continue;
            Toast($"MyCam « {i.Nom} » diffuse : Médias › Ajouter › Téléphone MyCam pour la mettre à l'écran.");
        }
    }

    string NomMyCam(string deviceId) =>
        R.Annonces.FirstOrDefault(x => x.Source is { Type: "mycam" } s && s.Peripherique == deviceId)?.Titre ?? App.Telephone.Cameras.InfoDe(deviceId)?.Nom ?? "Le caméraman";

    /// <summary>Les tuiles MyCam qui ne sont pas à l'écran montrent l'image du téléphone (2 à 3 fois par seconde).</summary>
    void ApercusMyCam()
    {
        foreach (var (vue, id, annonce) in tuilesMyCam)
        {
            if (R.Cible is CibleDisplay cd && cd.Id == annonce && !R.Etat.Vide) continue;
            var c = App.Telephone.Cameras.Dernier(id);
            if (c == null) { vue.Source = null; vue.Tag = null; continue; }
            if (vue.Source != null && vue.Tag is long n && n == c.Numero) continue;
            vue.Tag = c.Numero;
            vue.Source = SourcesDisplay.DecoderMyCam(c, 360);
        }
    }

    /// <summary>1.3.1 : la fenêtre de capture comme celle d'OBS (image en direct, « [Zoom.exe] : titre », zone client) et où va la fenêtre.</summary>
    void AjouterFenetre()
    {
        var d = new CaptureFenetreWindow(null, "") { Owner = this };
        if (d.ShowDialog() != true || d.Source is not { } s) return;
        var a = R.NouveauDisplay(d.Nom, s);
        R.GrilleAnnonce(a.Id, true);
        ChoisirDisplay(a);
        Toast($"« {a.Titre} » ajoutée ({SourceDisplay.NomDiffusion(s.Diffusion).ToLowerInvariant()}) : un clic sur sa tuile l'envoie en direct.");
    }

    /// <summary>1.3.1 : clic droit › Modifier sur une fenêtre : la même fenêtre qu'à l'ajout, avec les réglages de la source.</summary>
    void ModifierFenetre(Annonce a)
    {
        if (a.Source is not { Type: "fenetre" } s) return;
        var d = new CaptureFenetreWindow(s, a.Titre) { Owner = this };
        if (d.ShowDialog() != true || d.Source is not { } nouvelle) return;
        if (!R.ModifierSource(a.Id, d.Nom, nouvelle)) return;
        if (annonceChoisie?.Id == a.Id) ChoisirDisplay(a);
        Toast($"« {a.Titre} » : {SourceDisplay.NomDiffusion(nouvelle.Diffusion).ToLowerInvariant()}.");
    }

    /// <summary>1.3.1 : clic droit › Où va cette fenêtre : dans l'église, en streaming ou les deux, sans rouvrir les propriétés.</summary>
    MenuItem MenuDiffusion(Annonce a, SourceDisplay s)
    {
        var m = new MenuItem { Header = "Où va cette fenêtre" };
        foreach (var (valeur, texte) in new (string?, string)[] { (SourceDisplay.Eglise, "Dans l'église seulement"), (SourceDisplay.Streaming, "MyStream seulement"), (null, "Les deux") })
        {
            var v = valeur;
            var choix = Article(texte, () =>
            {
                if (!R.ModifierSource(a.Id, "", s with { Diffusion = v })) return;
                if (annonceChoisie?.Id == a.Id) ChoisirDisplay(a);
                Toast($"« {a.Titre} » : {SourceDisplay.NomDiffusion(v).ToLowerInvariant()}.");
            });
            choix.IsChecked = s.Diffusion == v;
            m.Items.Add(choix);
        }
        return m;
    }
    /// <summary>
    /// 1.6.5 : clic droit › Images par seconde d'une caméra USB ou d'une carte de capture : 60 (fluide, par défaut), 40 ou 30.
    /// La caméra se rouvre avec ; en haut, ce qu'elle donne vraiment en ce moment.
    /// </summary>
    MenuItem MenuIpsCamera(Annonce a, SourceDisplay s)
    {
        var m = new MenuItem { Header = $"Images par seconde : {s.IpsVoulues}" };
        var info = MyVerse.Ui.Audio.CameraFrames.Info(s.Peripherique);
        if (info.Length > 0) m.Items.Add(new MenuItem { Header = "En ce moment : " + info, IsEnabled = false });
        foreach (var (valeur, texte) in new[] { (60, "60 : fluide (conseillé)"), (40, "40"), (30, "30 : plus léger pour l'ordinateur") })
        {
            int ips = valeur;
            var choix = Article(texte, () =>
            {
                if (s.IpsVoulues == ips || !R.ModifierSource(a.Id, "", s with { Ips = ips })) return;
                if (annonceChoisie?.Id == a.Id) ChoisirDisplay(a);
                Toast($"« {a.Titre} » : {ips} images par seconde. La caméra se rouvre (une ou deux secondes).");
            });
            choix.IsChecked = s.IpsVoulues == ips;
            m.Items.Add(choix);
        }
        return m;
    }

    /// <summary>1.6.5 : clic droit › Taille de l'image d'une caméra USB : 1080p (par défaut), 4K (pour le direct en 4K) ou 720p.</summary>
    MenuItem MenuTailleCamera(Annonce a, SourceDisplay s)
    {
        var m = new MenuItem { Header = "Taille de l'image : " + (s.TailleVoulue == "4k" ? "4K" : s.TailleVoulue) };
        foreach (var (valeur, texte) in new[] { ("1080p", "1080p (conseillé)"), ("4k", "4K : pour le direct en 4K (30 images par seconde au plus)"), ("720p", "720p : plus léger") })
        {
            string taille = valeur;
            var choix = Article(texte, () =>
            {
                if (s.TailleVoulue == taille || !R.ModifierSource(a.Id, "", s with { Taille = taille == "1080p" ? null : taille })) return;
                if (annonceChoisie?.Id == a.Id) ChoisirDisplay(a);
                Toast($"« {a.Titre} » : {(taille == "4k" ? "4K" : taille)}. La caméra se rouvre (une ou deux secondes).");
            });
            choix.IsChecked = s.TailleVoulue == taille;
            m.Items.Add(choix);
        }
        return m;
    }

    T? ChoisirSource<T>(string titre, string aide, List<T> items, Func<T, string> nom) where T : class
    {
        var list = new ListBox { ItemsSource = items.Select(nom).ToList(), SelectedIndex = 0, MinHeight = 160, Margin = new Thickness(0, 12, 0, 12) };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var w = new Window { Title = titre, Owner = this, Width = 550, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Pinceau("Nuit") };
        panel.Children.Add(new TextBlock { Text = aide, TextWrapping = TextWrapping.Wrap, Foreground = Pinceau("Blanc"), FontSize = 15 });
        panel.Children.Add(list);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annuler", IsCancel = true, Style = Style_("Btn"), Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "Ajouter la source", IsDefault = true, Style = Style_("BtnOr") };
        add.Click += (_, _) => w.DialogResult = true;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) w.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(add); panel.Children.Add(buttons); w.Content = panel;
        return w.ShowDialog() == true && list.SelectedIndex >= 0 ? items[list.SelectedIndex] : null;
    }

    public string TestStatutProjection => PastilleTexte.Text;

    public bool TestMenuSources()
    {
        var menu = MenuAjouterSource();
        var commandes = menu.Items.OfType<MenuItem>().ToList();
        Ouvrir(menu, DisplayAjouter, PlacementMode.Bottom);
        // 2.1.0 : les médias seulement (texte, images, vidéos, fenêtre, Duo) ; les caméras ont leur bouton
        bool ok = menu.IsOpen && commandes.Count == 4 + R.Duos.Count && commandes[0].Header?.ToString() == "Nouveau texte"
            && !commandes.Any(c => c.Header?.ToString() is "Caméra Sony par Wi-Fi (sans câble)" or "MyVirtualCam (l'image de MyStream)");
        menu.IsOpen = false;
        var cameras = MenuAjouterPeripherique().Items.OfType<MenuItem>().ToList();
        ok &= cameras.Count >= 3 && cameras[0].Header?.ToString() == "MyVirtualCam (l'image de MyStream)"
            && cameras.Any(c => c.Header?.ToString() == "Téléphone MyCam (caméra Android)") && cameras.Any(c => c.Header?.ToString() == "Caméra Sony par Wi-Fi (sans câble)");
        return ok;
    }

    public void TestDisplayDoubleClick(Annonce a)
    {
        ChoisirDisplay(a); ListeAnnonces.UpdateLayout();
        if (ListeAnnonces.ItemContainerGenerator.ContainerFromItem(a) is ListBoxItem item)
        {
            var ev = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = item };
            Display_DoubleClick(ListeAnnonces, ev);
        }
    }
}
