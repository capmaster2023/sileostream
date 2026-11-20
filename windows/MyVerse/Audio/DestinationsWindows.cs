using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>Le cadre commun des fenêtres « Enregistrement » et « Envoi » : titre, phrase d'aide, zone qui défile, boutons en bas.</summary>
public abstract class FenetreSon : FenetreDialogue
{
    protected static ChurchAudioService A => App.Audio;
    protected static AudioConfiguration C => App.Audio.Config;
    protected readonly StackPanel Corps = new();
    protected readonly Button Valider, Supprimer;
    readonly TextBlock erreur;

    protected FenetreSon(string titre, string aide, bool existe, string ok)
    {
        Title = titre;
        Width = 700;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        Background = Pinceau("Page");
        var racine = new DockPanel { Margin = new Thickness(28, 22, 28, 22) };
        var tete = new StackPanel();
        tete.Children.Add(new TextBlock { Text = titre, Style = Style_("Titre2"), TextTrimming = TextTrimming.CharacterEllipsis });
        tete.Children.Add(new TextBlock { Text = aide, Style = Style_("Muet"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8), LineHeight = 21 });
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);
        var pied = new StackPanel();
        erreur = new TextBlock { Foreground = Pinceau("Danger"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        pied.Children.Add(erreur);
        var bas = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        Supprimer = new Button { Style = Style_("BtnDanger"), Content = "Supprimer", Height = 42, Padding = new Thickness(16, 0, 16, 0), Visibility = existe ? Visibility.Visible : Visibility.Collapsed };
        DockPanel.SetDock(Supprimer, Dock.Left);
        bas.Children.Add(Supprimer);
        Valider = new Button { Style = Style_("BtnOr"), Content = ok, IsDefault = true, MinWidth = 130, Height = 42, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(Valider, Dock.Right);
        bas.Children.Add(Valider);
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 42 };
        annuler.Click += (_, _) => Close();
        DockPanel.SetDock(annuler, Dock.Right);
        bas.Children.Add(annuler);
        pied.Children.Add(bas);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);
        racine.Children.Add(new ScrollViewer { Content = Corps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0) });
        Content = racine;
    }

    protected static TextBlock Libelle(string texte, double haut = 20) => new Capitales { Libelle = texte, Margin = new Thickness(0, haut, 0, 8) };
    protected static TextBlock Note(string texte, double haut = 6) => new() { Text = texte, FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, haut, 0, 0), LineHeight = 18 };

    /// <summary>Un grand choix à cliquer (« Le son seulement », « La vidéo et le son ») : un titre et une phrase.</summary>
    protected static RadioButton Option(string titre, string detail, bool coche, string groupe)
    {
        var p = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        p.Children.Add(new TextBlock { Text = titre, FontSize = 15, FontWeight = FontWeights.SemiBold });
        if (detail.Length > 0) p.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return new RadioButton { Content = p, IsChecked = coche, GroupName = groupe, Margin = new Thickness(0, 0, 0, 10), VerticalContentAlignment = VerticalAlignment.Top };
    }

    protected void Erreur(string message)
    {
        erreur.Text = message;
        erreur.Visibility = Visibility.Visible;
    }
}

/// <summary>
/// Un enregistrement : son nom, le son seulement (MP3 léger ou WAV studio) ou la vidéo et le son (ce qui est à l'écran, des caméras),
/// ce qu'il entend (tout le son ou les sons choisis) et son dossier. La synchro du son et de l'image est automatique.
/// </summary>
public sealed class EnregistrementWindow : FenetreSon
{
    readonly RecordingProfile? existant;
    readonly TextBox nom;
    readonly RadioButton sonSeul, video, leger, studio, incrustation, cote;
    readonly StackPanel optionsSon = new(), optionsVideo = new(), disposition = new();
    readonly List<(CheckBox Case, string Id, Border Pastille, TextBlock Rang)> display = new();
    readonly List<string> ordre = new();
    readonly ChoixSons sons;
    readonly TextBox dossier;
    bool dossierChoisi;

    public EnregistrementWindow(RecordingProfile? p) : base(
        p == null ? "Nouvel enregistrement" : $"Régler « {p.Name} »",
        "Gardez le culte : le son (pour le partager sur WhatsApp ou le site) ou la vidéo avec le son. Plusieurs enregistrements peuvent tourner en même temps.",
        p != null, p == null ? "Créer" : "Terminé")
    {
        existant = p;
        bool tourne = p != null && (A.Recorder.Active(p.Id) || A.Recorder.Busy(p.Id));
        if (tourne)
        {
            var info = Note("Il enregistre en ce moment : les changements serviront au prochain départ (sauf les sons, tout de suite).", 0);
            info.Foreground = Pinceau("Or");
            info.FontWeight = FontWeights.SemiBold;
            Corps.Children.Add(info);
        }
        Corps.Children.Add(Libelle("Nom", tourne ? 14 : 6));
        nom = new TextBox { Style = Style_("Champ"), FontSize = 15, Text = p?.Name ?? (C.Profiles.Count == 0 ? "Culte" : $"Enregistrement {C.Profiles.Count + 1}") };
        Corps.Children.Add(nom);

        Corps.Children.Add(Libelle("Quoi"));
        bool estVideo = p != null && !p.AudioOnly;
        sonSeul = Option("Le son seulement", "Un fichier son : à écouter, partager, mettre sur le site.", !estVideo, "quoi");
        video = Option("La vidéo et le son", "Ce qui est à l'écran (Bible, chants, Display) et des caméras, avec le son.", estVideo, "quoi");
        Corps.Children.Add(sonSeul);
        Corps.Children.Add(video);

        // son seul : léger (MP3) ou studio (WAV)
        optionsSon.Children.Add(Libelle("Qualité", 10));
        leger = Option("Léger : MP3", "Petit fichier, lisible partout (téléphone, WhatsApp, site). Conseillé.", p?.Format is not ("wav" or "flac"), "qualite");
        studio = Option("Studio : WAV", "Sans perte, pour retravailler le son. Fichier environ 10 fois plus gros.", p?.Format is "wav" or "flac", "qualite");
        optionsSon.Children.Add(leger);
        optionsSon.Children.Add(studio);
        Corps.Children.Add(optionsSon);

        // vidéo : ce qu'on voit, dans l'ordre (la 1re en grand)
        optionsVideo.Children.Add(Libelle("Ce qu'on voit", 10));
        optionsVideo.Children.Add(Note("Cochez dans l'ordre : la 1re est en grand.", 0));
        var liste = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        AjouterDisplay(liste, RecordingProfile.Projection, "Ce qui est à l'écran", "Bible, chants, Médias et leurs calques");
        foreach (var a in App.Donnees.Annonces)
            AjouterDisplay(liste, a.Id, a.Titre.Length > 0 ? a.Titre : "Sans titre", a.Description.Split('\n')[0].Trim());
        optionsVideo.Children.Add(liste);
        disposition.Children.Add(Libelle("Disposition", 8));
        incrustation = Option("Une en grand, les autres en petit", "En bas à droite, comme une caméra dans un coin.", p?.Layout != "cote", "disposition");
        cote = Option("Côte à côte", "Toutes de la même taille.", p?.Layout == "cote", "disposition");
        disposition.Children.Add(incrustation);
        disposition.Children.Add(cote);
        optionsVideo.Children.Add(disposition);
        optionsVideo.Children.Add(Note("Vidéo 1280 × 720, 30 images par seconde, fichier MKV (lisible par le Lecteur multimédia de Windows et VLC, même après une coupure de courant). Le son tombe sur les lèvres tout seul."));
        Corps.Children.Add(optionsVideo);
        if (p != null) ordre.AddRange(p.DisplayIds.Where(id => display.Any(d => d.Id == id)));
        if (ordre.Count == 0) ordre.Add(RecordingProfile.Projection);

        Corps.Children.Add(Libelle("Ce qu'on entend"));
        sons = new ChoixSons(p?.ChoixSons);
        Corps.Children.Add(sons);

        Corps.Children.Add(Libelle("Dossier"));
        dossier = new TextBox { Style = Style_("Champ"), IsReadOnly = true, FontSize = 14, Text = p?.Directory ?? "" };
        var choisir = new Button { Style = Style_("Btn"), Content = "Choisir…", Height = 40, Padding = new Thickness(14, 0, 14, 0), FontSize = 14, Margin = new Thickness(8, 0, 0, 0) };
        choisir.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier de cet enregistrement" };
            if (Directory.Exists(dossier.Text)) d.InitialDirectory = dossier.Text;
            if (d.ShowDialog(this) != true) return;
            dossier.Text = d.FolderName;
            dossierChoisi = true;
        };
        var ligneDossier = new DockPanel();
        DockPanel.SetDock(choisir, Dock.Right);
        ligneDossier.Children.Add(choisir);
        ligneDossier.Children.Add(dossier);
        Corps.Children.Add(ligneDossier);
        Corps.Children.Add(Note("Chaque fichier porte le nom, la date et l'heure : rien n'est jamais écrasé."));
        dossierChoisi = p != null && !string.Equals(p.Directory, DossierParDefaut(p.Name), StringComparison.OrdinalIgnoreCase);
        if (!dossierChoisi) dossier.Text = DossierParDefaut(nom.Text);
        nom.TextChanged += (_, _) => { if (!dossierChoisi) dossier.Text = DossierParDefaut(nom.Text); };

        sonSeul.Checked += (_, _) => MajOptions();
        video.Checked += (_, _) => MajOptions();
        Valider.Click += (_, _) => Enregistrer();
        Supprimer.IsEnabled = !tourne;
        if (tourne) { Supprimer.ToolTip = "Arrêtez d'abord cet enregistrement"; ToolTipService.SetShowOnDisabled(Supprimer, true); }
        Supprimer.Click += (_, _) =>
        {
            if (existant == null || !Dialogue.Confirmer(this, $"Supprimer l'enregistrement « {existant.Name} » ? Les fichiers déjà enregistrés restent dans leur dossier.", "Supprimer")) return;
            C.Profiles.Remove(existant);
            A.Apply();
            Close();
        };
        Loaded += (_, _) => { nom.Focus(); nom.SelectAll(); };
        MajOptions();
    }

    static string DossierParDefaut(string nom) =>
        Path.Combine(AudioConfiguration.DossierEnregistrements, RecordingFiles.SafeName(nom.Trim().Length > 0 ? nom.Trim() : "Enregistrement"));

    void AjouterDisplay(UniformGrid liste, string id, string titre, string detail)
    {
        var rang = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Pinceau("SurOr"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pastille = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Pinceau("OrVif"), Margin = new Thickness(0, 0, 8, 0), Child = rang, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        var textes = new StackPanel { MaxWidth = 240 };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = titre });
        if (detail.Length > 0) textes.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Pinceau("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        var contenu = new DockPanel();
        contenu.Children.Add(pastille);
        contenu.Children.Add(textes);
        var cb = new CheckBox { Content = contenu, IsChecked = existant == null ? id == RecordingProfile.Projection : existant.DisplayIds.Contains(id), Margin = new Thickness(0, 0, 12, 10), HorizontalAlignment = HorizontalAlignment.Left };
        cb.Click += (_, _) =>
        {
            if (cb.IsChecked == true)
            {
                if (ordre.Count >= 6) { cb.IsChecked = false; Erreur("6 choses à l'image au plus."); return; }
                ordre.Add(id);
            }
            else ordre.Remove(id);
            MajOptions();
        };
        display.Add((cb, id, pastille, rang));
        liste.Children.Add(cb);
    }

    void MajOptions()
    {
        bool estVideo = video.IsChecked == true;
        foreach (var d in display)
        {
            int i = ordre.IndexOf(d.Id);
            d.Case.IsChecked = i >= 0;
            d.Pastille.Visibility = i >= 0 ? Visibility.Visible : Visibility.Collapsed;
            d.Rang.Text = (i + 1).ToString();
        }
        optionsVideo.Visibility = estVideo ? Visibility.Visible : Visibility.Collapsed;
        disposition.Visibility = ordre.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        optionsSon.Visibility = estVideo ? Visibility.Collapsed : Visibility.Visible;
    }

    void Enregistrer()
    {
        string n = nom.Text.Trim();
        if (n.Length == 0) { Erreur("Donnez un nom à l'enregistrement (par exemple « Culte »)."); nom.Focus(); return; }
        if (existant == null && C.Profiles.Count >= AudioConfiguration.MaxEnregistrements) { Erreur($"{AudioConfiguration.MaxEnregistrements} enregistrements au plus."); return; }
        if (C.Profiles.Any(x => x != existant && string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase))) { Erreur($"Un autre enregistrement s'appelle déjà « {n} »."); return; }
        bool estVideo = video.IsChecked == true;
        if (estVideo && ordre.Count == 0) { Erreur("Cochez au moins une chose à voir (par exemple « Ce qui est à l'écran »)."); return; }
        var p = existant ?? new RecordingProfile();
        p.Name = n;
        p.DisplayIds = estVideo ? ordre.ToList() : new();
        p.Layout = cote.IsChecked == true ? "cote" : "incrustation";
        p.AudioOnly = !estVideo;
        p.Format = estVideo ? "mkv" : studio.IsChecked == true ? "wav" : "mp3";
        p.ChoixSons = sons.Choix;
        p.Directory = dossier.Text;
        if (existant == null) C.Profiles.Add(p);
        A.Apply();
        Close();
    }
}

/// <summary>
/// Un envoi du son : vers un autre logiciel (Zoom, OBS, Teams, Facebook dans le navigateur) par le câble audio virtuel,
/// ou vers une sortie de l'ordinateur. Il reçoit tout le son, ou les sons choisis.
/// </summary>
public sealed class EnvoiWindow : FenetreSon
{
    readonly VirtualSoundSettings? existant;
    readonly TextBox nom;
    readonly RadioButton parCable, versSortie;
    readonly ComboBox sortie = new() { MinHeight = 40, Margin = new Thickness(28, 0, 0, 0) };
    readonly ChoixSons sons;
    readonly TextBlock etatCable = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
    readonly Button installer;

    public EnvoiWindow(VirtualSoundSettings? v) : base(
        v == null ? "Nouvel envoi" : $"Régler « {v.Name} »",
        "Siloé Stream devient un micro pour vos autres logiciels : Zoom, OBS (pour YouTube ou Facebook en direct), Teams… Ils entendent le son du culte.",
        v != null, v == null ? "Créer" : "Terminé")
    {
        existant = v;
        Corps.Children.Add(Libelle("Nom", 6));
        nom = new TextBox { Style = Style_("Champ"), FontSize = 15, Text = v?.Name ?? (C.VirtualSounds.Count == 0 ? "Zoom" : "Envoi " + (C.VirtualSounds.Count + 1)) };
        Corps.Children.Add(nom);

        Corps.Children.Add(Libelle("Vers"));
        parCable = Option("Un autre logiciel (Zoom, OBS, Teams, navigateur)", "Par le câble audio virtuel : dans ce logiciel, choisissez le micro « CABLE Output ».", v == null || v.ParCable, "vers");
        Corps.Children.Add(parCable);
        var cadre = new Border { Background = Pinceau("Nuit2"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(28, 0, 0, 12) };
        var pile = new StackPanel();
        pile.Children.Add(etatCable);
        installer = new Button { Style = Style_("BtnOr"), Content = "Installer le câble (gratuit, 1 minute)", Height = 38, Padding = new Thickness(14, 0, 14, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        installer.Click += async (_, _) =>
        {
            installer.IsEnabled = false;
            var etat = new Progress<string>(t => etatCable.Text = t);
            try { await VbCable.InstallerAsync(etat); }
            catch (Exception e) { etatCable.Text = "Installation impossible : " + e.Message; Journal.Erreur("installation de VB-CABLE", e); }
            finally { installer.IsEnabled = true; A.Sorties.Reset(); A.Apply(); MajCable(); }
        };
        pile.Children.Add(installer);
        cadre.Child = pile;
        Corps.Children.Add(cadre);
        versSortie = Option("Une sortie de l'ordinateur", "Des haut-parleurs, une autre table de mixage, une salle à côté…", v is { ParCable: false }, "vers");
        Corps.Children.Add(versSortie);
        sortie.Items.Add(new ComboBoxItem { Content = "Sortie de Windows (par défaut)", Tag = "default" });
        var sorties = A.Outputs();
        foreach (var d in sorties) sortie.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id });
        string choisi = v?.DeviceId ?? "";
        if (choisi.Length > 0 && choisi != "default" && sorties.All(d => d.Id != choisi))
            sortie.Items.Add(new ComboBoxItem { Content = (v!.DeviceName.Length > 0 ? v.DeviceName : "Sortie choisie") + " (débranchée)", Tag = choisi });
        sortie.SelectedItem = sortie.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == choisi) ?? sortie.Items[0];
        Corps.Children.Add(sortie);
        Corps.Children.Add(Note("Le son de l'ordinateur ne repart jamais vers la sortie d'où il vient : pas de larsen.", 6));

        Corps.Children.Add(Libelle("Ce qu'il entend"));
        sons = new ChoixSons(v?.ChoixSons);
        Corps.Children.Add(sons);
        Corps.Children.Add(Note(VbCable.Mention, 16));

        parCable.Checked += (_, _) => MajVers();
        versSortie.Checked += (_, _) => MajVers();
        Valider.Click += (_, _) => Enregistrer();
        Supprimer.Click += (_, _) =>
        {
            if (existant == null || !Dialogue.Confirmer(this, $"Supprimer l'envoi « {existant.Name} » ?", "Supprimer")) return;
            C.VirtualSounds.Remove(existant);
            A.Apply();
            Close();
        };
        Loaded += (_, _) => { nom.Focus(); nom.SelectAll(); };
        MajCable();
        MajVers();
    }

    void MajCable()
    {
        var cable = VbCable.Entree();
        etatCable.Text = cable is { } c ? $"Câble prêt : {c.Nom}. Dans Zoom (ou OBS, Teams…), choisissez le micro « CABLE Output »." : "Il manque le câble audio virtuel : installez-le d'un clic (Windows demande votre accord).";
        etatCable.Foreground = Pinceau(cable != null ? "Encre2" : "RougeTexte");
        installer.Visibility = VbCable.Installe ? Visibility.Collapsed : Visibility.Visible;
    }

    void MajVers() => sortie.IsEnabled = versSortie.IsChecked == true;

    void Enregistrer()
    {
        string n = nom.Text.Trim();
        if (n.Length == 0) { Erreur("Donnez un nom à l'envoi (par exemple « Zoom »)."); nom.Focus(); return; }
        if (existant == null && C.VirtualSounds.Count >= AudioConfiguration.MaxSortiesVirtuelles) { Erreur($"{AudioConfiguration.MaxSortiesVirtuelles} envois au plus."); return; }
        var v = existant ?? new VirtualSoundSettings { Enabled = true };
        v.Name = n;
        if (parCable.IsChecked == true) { v.DeviceId = ""; v.DeviceName = ""; }
        else if (sortie.SelectedItem is ComboBoxItem { Tag: string id } item)
        {
            v.DeviceId = id;
            v.DeviceName = id == "default" ? "" : (item.Content as string ?? "").Replace(" (débranchée)", "");
        }
        v.ChoixSons = sons.Choix;
        if (existant == null) C.VirtualSounds.Add(v);
        A.Apply();
        Close();
    }
}
