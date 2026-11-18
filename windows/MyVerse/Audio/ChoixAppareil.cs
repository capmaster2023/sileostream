using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>Où un son est branché : comment le capter, l'appareil, l'entrée (0 = toutes), ou les entrées d'un pilote ASIO.</summary>
internal sealed record Branchement(AudioInputKind Capture, string Appareil, string NomAppareil, int Entree, List<int> EntreesAsio)
{
    public static Branchement De(Son s) => new(s.Capture, s.Appareil, s.NomAppareil, s.Entree, s.EntreesAsio.ToList());
    public bool Meme(Branchement b) => b.Capture == Capture && string.Equals(b.Appareil, Appareil, StringComparison.OrdinalIgnoreCase)
        && b.Entree == Entree && (Capture != AudioInputKind.Asio || b.EntreesAsio.SequenceEqual(EntreesAsio));
    public void Appliquer(Son s)
    {
        s.Capture = Capture; s.Appareil = Appareil; s.NomAppareil = NomAppareil; s.Entree = Entree; s.EntreesAsio = EntreesAsio.ToList();
    }
}

/// <summary>
/// « Où est-il branché ? » : la liste des micros et cartes son, chaque entrée avec sa barre qui bouge.
/// On parle dans le micro, et on clique la barre qui bouge. Les cartes son professionnelles (ASIO) sont plus bas.
/// Pour le son de l'ordinateur : tout ce qu'il joue, ou ce qui sort d'un seul appareil.
/// </summary>
internal sealed class ChoixAppareil : StackPanel
{
    static Brush P(string cle) => SonUi.P(cle);
    readonly GenreSon genre;
    readonly EcouteApercu apercu = new();
    readonly DispatcherTimer minuterie = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
    readonly List<(Button Ligne, Vumetre? Vu, Branchement B)> lignes = new();
    /// <summary>2.4.2 : les barres des listes déroulantes (l'appareil, l'entrée) et ce qu'elles écoutent.</summary>
    readonly List<(Vumetre Vu, string Appareil, int Entree)> vus = new();
    ComboBox? appareils;
    bool remplissage;
    readonly StackPanel liste = new();
    readonly StackPanel listeAsio = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    readonly Action peripheriques;
    Branchement choix;

    public event Action? Change;
    public Branchement Choix => choix;

    public ChoixAppareil(GenreSon genre, Branchement depart)
    {
        this.genre = genre;
        choix = depart;
        if (genre != GenreSon.Ordinateur)
        {
            var aide = SonUi.Texte(genre == GenreSon.Table
                ? "Faites jouer la table de mixage : la barre qui bouge, c'est la bonne entrée."
                : genre == GenreSon.Instrument ? "Jouez de l'instrument : la barre qui bouge, c'est le bon." : "Parlez dans le micro : la barre qui bouge, c'est le bon.", 14, "Encre2", FontWeights.SemiBold);
            aide.Margin = new Thickness(0, 0, 0, 10);
            Children.Add(aide);
        }
        Children.Add(liste);
        if (genre != GenreSon.Ordinateur)
        {
            var pro = new Button { Style = SonUi.S("BtnLien"), Content = "Carte son professionnelle (pilote ASIO) : Focusrite, Behringer, PreSonus…", Margin = new Thickness(2, 14, 0, 0) };
            pro.Click += (_, _) =>
            {
                listeAsio.Visibility = listeAsio.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (listeAsio.Visibility == Visibility.Visible) RemplirAsio();
            };
            Children.Add(pro);
            Children.Add(listeAsio);
            if (depart.Capture == AudioInputKind.Asio) { listeAsio.Visibility = Visibility.Visible; }
        }
        peripheriques = () => Dispatcher.BeginInvoke(new Action(Remplir));
        minuterie.Tick += (_, _) => Battre();
        Loaded += (_, _) =>
        {
            SonUi.A.PeripheriquesChange -= peripheriques;
            SonUi.A.PeripheriquesChange += peripheriques;
            minuterie.Start();
        };
        Unloaded += (_, _) => { minuterie.Stop(); SonUi.A.PeripheriquesChange -= peripheriques; };
        Remplir();
        if (listeAsio.Visibility == Visibility.Visible) RemplirAsio();
    }

    /// <summary>Arrête l'écoute (la fenêtre se ferme).</summary>
    public void Fermer()
    {
        minuterie.Stop();
        SonUi.A.PeripheriquesChange -= peripheriques;
        apercu.Dispose();
    }

    /// <summary>Une ligne à cliquer : un rond, le nom, une précision et (pour un micro) sa barre qui bouge.</summary>
    Button Ligne(string titre, string detail, Branchement b, bool vu, double retrait = 0)
    {
        var rond = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(rond);
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0) textes.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        Grid.SetColumn(textes, 1);
        g.Children.Add(textes);
        Vumetre? barre = null;
        if (vu)
        {
            barre = new Vumetre(8) { Width = 150, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(barre, 2);
            g.Children.Add(barre);
        }
        var bouton = new Button
        {
            Style = SonUi.S("Btn"), Content = g, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 9, 14, 9),
            Margin = new Thickness(retrait, 0, 0, 6), Tag = rond,
        };
        bouton.Click += (_, _) => { choix = b; MajChoix(); if (appareils != null) { remplissage = true; appareils.SelectedIndex = -1; remplissage = false; } Change?.Invoke(); };
        lignes.Add((bouton, barre, b));
        return bouton;
    }

    void MajChoix()
    {
        foreach (var (ligne, _, b) in lignes)
        {
            bool oui = b.Meme(choix);
            ligne.Background = P(oui ? "OrLeger" : "Encre");
            ligne.BorderBrush = P(oui ? "OrVif" : "Nuit3");
            if (ligne.Tag is Border rond)
            {
                rond.BorderBrush = P(oui ? "OrVif" : "Brume2");
                rond.Background = oui ? P("OrVif") : Brushes.Transparent;
            }
        }
    }

    /// <summary>Une option de liste déroulante : le nom, une précision, et (pour un micro) sa barre qui bouge.</summary>
    ComboBoxItem Option(string titre, string detail, object tag, string? appareil, int entree)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var textes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textes.Children.Add(new TextBlock { Text = titre, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0) textes.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
        g.Children.Add(textes);
        if (appareil != null)
        {
            var vu = new Vumetre(8) { Width = 130, Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(vu, 1);
            g.Children.Add(vu);
            vus.Add((vu, appareil, entree));
        }
        return new ComboBoxItem { Content = g, Tag = tag, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6, 8, 6) };
    }

    static ComboBox Liste() => new() { MinHeight = 46, FontSize = 14.5, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };

    sealed record Appareil(string Id, string Nom, int Entrees, string Titre, string Detail);

    void Remplir()
    {
        lignes.RemoveAll(l => liste.Children.Contains(l.Ligne));
        liste.Children.Clear();
        vus.Clear();
        remplissage = true;
        try
        {
            if (genre == GenreSon.Ordinateur)
            {
                var sorties = Liste();
                sorties.Items.Add(Option("Tout le son de l'ordinateur (conseillé)", "YouTube, musique, vidéos : tout ce qui sort par la sortie de Windows", new Branchement(AudioInputKind.Sortie, "default", "", 0, new() { 0 }), null, 0));
                foreach (var d in SonUi.A.Outputs())
                    sorties.Items.Add(Option(AudioConfiguration.NomCourt(d.Name), "Seulement ce qui sort de « " + d.Name + " »", new Branchement(AudioInputKind.Sortie, d.Id, d.Name, 0, new() { 0 }), null, 0));
                sorties.SelectedIndex = Math.Max(0, sorties.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => i.Tag is Branchement b && b.Meme(choix)));
                sorties.SelectionChanged += (_, _) =>
                {
                    if (remplissage || sorties.SelectedItem is not ComboBoxItem { Tag: Branchement b }) return;
                    choix = b; Change?.Invoke();
                };
                liste.Children.Add(sorties);
                return;
            }
            var micros = SonUi.A.Microphones();
            var options = new List<Appareil>();
            if (choix.Capture == AudioInputKind.Microphone && choix.Appareil == "default")
                options.Add(new("default", "", 1, "Micro de Windows (par défaut)", "Celui choisi dans les paramètres de Windows"));
            foreach (var d in micros)
            {
                apercu.Ecouter(d.Id);
                int n = apercu.Entrees(d.Id);
                if (n <= 0) n = ChurchAudioService.Entrees(d.Id);
                options.Add(new(d.Id, d.Name, n, AudioConfiguration.NomCourt(d.Name), d.Name + (n > 1 ? $" · {n} entrées" : "")));
            }
            // un choix enregistré mais débranché reste visible
            if (choix.Capture == AudioInputKind.Microphone && choix.Appareil != "default" && micros.All(d => d.Id != choix.Appareil))
                options.Add(new(choix.Appareil, choix.NomAppareil, 0, (choix.NomAppareil.Length > 0 ? AudioConfiguration.NomCourt(choix.NomAppareil) : "Appareil choisi") + " (débranché)", "Il reviendra tout seul quand il sera rebranché"));
            if (options.Count == 0)
            {
                var vide = SonUi.Texte("Aucun micro ni carte son trouvé. Branchez-le à l'ordinateur : il apparaît ici tout seul.", 14, "Brume");
                vide.Margin = new Thickness(2, 6, 2, 6);
                liste.Children.Add(vide);
                appareils = null;
                return;
            }
            liste.Children.Add(SonUi.Texte("Appareil", 13, "Brume", FontWeights.Bold));
            var cbAppareils = Liste();
            appareils = cbAppareils;
            foreach (var o in options)
                cbAppareils.Items.Add(Option(o.Titre, o.Detail, o, o.Id == "default" || o.Entrees == 0 ? null : o.Id, 0));
            liste.Children.Add(cbAppareils);
            var titreEntree = SonUi.Texte("Entrée", 13, "Brume", FontWeights.Bold);
            titreEntree.Margin = new Thickness(0, 6, 0, 0);
            var cbEntrees = Liste();
            liste.Children.Add(titreEntree);
            liste.Children.Add(cbEntrees);

            void Entrees(Appareil o)
            {
                vus.RemoveAll(v => cbEntrees.Items.Cast<ComboBoxItem>().Any(i => i.Content is Grid g && g.Children.Contains(v.Vu)));
                cbEntrees.Items.Clear();
                // une table de mixage arrive en stéréo ; un micro simple n'a qu'une entrée
                bool detail = o.Entrees > 2 || (o.Entrees == 2 && genre != GenreSon.Table);
                titreEntree.Visibility = cbEntrees.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
                if (!detail) return;
                for (int k = 1; k <= Math.Min(o.Entrees, AudioConfiguration.MaxEntrees); k++)
                    cbEntrees.Items.Add(Option($"Entrée {k}", "", k, o.Id, k));
                cbEntrees.Items.Add(Option("Toutes les entrées ensemble (stéréo)", genre == GenreSon.Voix ? "Si votre micro est sur l'entrée 1 et la 2" : "Gauche et droite, comme Windows les donne", 0, o.Id, 0));
                int voulue = choix.Appareil == o.Id ? choix.Entree : 0;
                cbEntrees.SelectedIndex = Math.Max(0, cbEntrees.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => i.Tag is int k && k == voulue));
            }
            cbAppareils.SelectionChanged += (_, _) =>
            {
                if (remplissage || cbAppareils.SelectedItem is not ComboBoxItem { Tag: Appareil o }) return;
                remplissage = true;
                Entrees(o);
                int entree = cbEntrees.Visibility == Visibility.Visible && cbEntrees.SelectedItem is ComboBoxItem { Tag: int k } ? k : 0;
                remplissage = false;
                choix = new(AudioInputKind.Microphone, o.Id, o.Nom, entree, new() { 0 });
                MajChoix();
                Change?.Invoke();
            };
            cbEntrees.SelectionChanged += (_, _) =>
            {
                if (remplissage || cbEntrees.SelectedItem is not ComboBoxItem { Tag: int k } || choix.Capture != AudioInputKind.Microphone) return;
                choix = choix with { Entree = k };
                Change?.Invoke();
            };
            int choisi = choix.Capture == AudioInputKind.Microphone ? options.FindIndex(o => string.Equals(o.Id, choix.Appareil, StringComparison.OrdinalIgnoreCase)) : -1;
            cbAppareils.SelectedIndex = choisi;
            if (choisi >= 0) Entrees(options[choisi]);
            else titreEntree.Visibility = cbEntrees.Visibility = Visibility.Collapsed;
        }
        finally
        {
            remplissage = false;
            MajChoix();
        }
    }

    void RemplirAsio()
    {
        lignes.RemoveAll(l => listeAsio.Children.Contains(l.Ligne));
        listeAsio.Children.Clear();
        var pilotes = ChurchAudioService.PilotesAsio();
        if (pilotes.Count == 0)
        {
            listeAsio.Children.Add(SonUi.Texte("Aucun pilote ASIO installé. Installez le pilote de votre carte son (Focusrite Control, Behringer UMC…), puis revenez ici.", 13.5, "Brume"));
            return;
        }
        foreach (var pilote in pilotes)
        {
            var tete = new DockPanel { Margin = new Thickness(2, 8, 0, 6) };
            var panneau = new Button { Style = SonUi.S("BtnPetit"), Content = "Réglages du fabricant" };
            string p = pilote;
            panneau.Click += (_, _) => { try { SonUi.A.PanneauAsio(p); } catch (Exception x) { SonUi.Toast("Le panneau de la carte son ne s'ouvre pas : " + x.Message); } };
            DockPanel.SetDock(panneau, Dock.Right);
            tete.Children.Add(panneau);
            tete.Children.Add(SonUi.Texte(pilote, 13, "Brume", FontWeights.Bold));
            listeAsio.Children.Add(tete);
            var noms = SonUi.A.EntreesAsio(pilote);
            if (noms.Count == 0)
            {
                string st = SonUi.A.StatutAsio(pilote);
                listeAsio.Children.Add(SonUi.Texte(st.Length > 0 ? st : "Cette carte son ne s'ouvre pas (débranchée, ou utilisée par un autre logiciel ?).", 13, "RougeTexte"));
                continue;
            }
            for (int k = 0; k < noms.Count; k++)
                listeAsio.Children.Add(Ligne($"Entrée {k + 1}", noms[k], new(AudioInputKind.Asio, pilote, pilote, 0, new() { k }), false, 14));
            if (genre != GenreSon.Voix)
                for (int k = 0; k + 1 < noms.Count; k += 2)
                    listeAsio.Children.Add(Ligne($"Entrées {k + 1} et {k + 2} (stéréo)", noms[k] + " + " + noms[k + 1], new(AudioInputKind.Asio, pilote, pilote, 0, new() { k, k + 1 }), false, 14));
        }
        MajChoix();
    }

    void Battre()
    {
        if (!IsVisible) return;
        apercu.Relever();
        foreach (var (_, vu, b) in lignes)
            if (vu != null && b.Capture == AudioInputKind.Microphone) vu.Niveau(apercu.Crete(b.Appareil, b.Entree));
        foreach (var (vu, appareil, entree) in vus) vu.Niveau(apercu.Crete(appareil, entree));
    }
}
