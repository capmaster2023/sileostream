using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// « Ajouter un son », en trois questions simples :
/// 1. Qu'est-ce que c'est ? (un micro, un instrument, la table de mixage, le son de l'ordinateur, une musique)
/// 2. Où est-il branché ? (on parle dans le micro, et on clique la barre qui bouge) — ou quel fichier ?
/// 3. Comment s'appelle-t-il ? Puis Siloé Stream règle tout le reste (bruit enlevé, volume régulier pour une voix).
/// </summary>
public sealed class AjoutSonWindow : FenetreDialogue
{
    static readonly GenreSon[] TousGenres = { GenreSon.Voix, GenreSon.Instrument, GenreSon.Table, GenreSon.Ordinateur, GenreSon.Fichier, GenreSon.Video };
    /// <summary>2.4.0 : le son des vidéos se choisit ici (une seule fois) ; il n'est plus ajouté tout seul.</summary>
    static IEnumerable<GenreSon> Genres => TousGenres.Where(g => g != GenreSon.Video || SonUi.C.SonVideos == null);
    readonly ContentControl page = new() { Focusable = false };
    readonly TextBlock titre = new() { FontSize = 24, FontWeight = FontWeights.ExtraBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock etape = new() { FontSize = 12.5, FontWeight = FontWeights.Bold };
    readonly Button retour, suivant;
    readonly TextBlock erreur = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    int numero = 1;
    GenreSon genre = GenreSon.Voix;
    ChoixAppareil? appareil;
    /// <summary>Où le son est branché : gardé quand on quitte l'étape 2.</summary>
    Branchement? branchement;
    string fichier = "";
    bool boucle;
    TextBox? nom;

    /// <summary>Le son ajouté (null si l'utilisateur a annulé).</summary>
    public Son? Resultat { get; private set; }

    public AjoutSonWindow(GenreSon? depart = null)
    {
        Title = "Ajouter un son";
        Width = 780;
        Height = Math.Min(720, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640;
        MinHeight = 480;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");
        titre.Foreground = Pinceau("Blanc");
        etape.Foreground = Pinceau("Or");
        erreur.Foreground = Pinceau("Danger");

        var tete = new StackPanel { Margin = new Thickness(28, 22, 28, 12) };
        tete.Children.Add(etape);
        titre.Margin = new Thickness(0, 4, 0, 0);
        tete.Children.Add(titre);
        var bas = new DockPanel { Margin = new Thickness(28, 0, 28, 22), LastChildFill = false };
        retour = new Button { Style = Style_("Btn"), Content = "Retour", MinWidth = 110, Height = 42 };
        retour.Click += (_, _) => Aller(numero - 1);
        DockPanel.SetDock(retour, Dock.Left);
        bas.Children.Add(retour);
        suivant = new Button { Style = Style_("BtnOr"), Content = "Suivant", MinWidth = 140, Height = 42, IsDefault = true, Margin = new Thickness(10, 0, 0, 0) };
        suivant.Click += (_, _) => Suivant();
        DockPanel.SetDock(suivant, Dock.Right);
        bas.Children.Add(suivant);
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 42 };
        annuler.Click += (_, _) => Close();
        DockPanel.SetDock(annuler, Dock.Right);
        bas.Children.Add(annuler);
        var pied = new StackPanel();
        erreur.Margin = new Thickness(28, 0, 28, 10);
        pied.Children.Add(erreur);
        pied.Children.Add(bas);

        var racine = new DockPanel();
        DockPanel.SetDock(tete, Dock.Top);
        racine.Children.Add(tete);
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);
        racine.Children.Add(new ScrollViewer { Content = page, Padding = new Thickness(28, 6, 22, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = racine;
        Closed += (_, _) => appareil?.Fermer();
        if (depart is { } g) { genre = g; Aller(2); }
        else Aller(1);
    }

    void Erreur(string? texte)
    {
        erreur.Text = texte ?? "";
        erreur.Visibility = texte == null ? Visibility.Collapsed : Visibility.Visible;
    }

    void Aller(int n)
    {
        Erreur(null);
        if (n < 1) n = 1;
        if (numero == 2 && n != 2 && appareil != null) { branchement = appareil.Choix; appareil.Fermer(); appareil = null; }
        if (n == 2 && genre == GenreSon.Video) n = numero > 2 ? 1 : 3;
        numero = n;
        bool video = genre == GenreSon.Video && n > 1;
        etape.Text = video ? $"ÉTAPE {n - 1} SUR 2" : $"ÉTAPE {n} SUR 3";
        retour.Visibility = n > 1 ? Visibility.Visible : Visibility.Hidden;
        suivant.Visibility = n > 1 ? Visibility.Visible : Visibility.Collapsed;
        suivant.Content = n == 3 ? "Ajouter" : "Suivant";
        page.Content = n switch { 1 => PageGenre(), 2 => PageBranchement(), _ => PageNom() };
    }

    void Suivant()
    {
        if (numero == 2)
        {
            if (genre == GenreSon.Fichier && fichier.Length == 0) { Erreur("Choisissez d'abord le fichier de musique."); return; }
            Aller(3);
        }
        else if (numero == 3) Ajouter();
    }

    // ------------------------------------------------------------ 1. qu'est-ce que c'est ?
    UIElement PageGenre()
    {
        titre.Text = "Qu'est-ce que vous voulez ajouter ?";
        var tuiles = new WrapPanel();
        foreach (var g in Genres)
        {
            var pile = new StackPanel();
            pile.Children.Add(SonUi.Pastille(g, 52));
            ((Border)pile.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
            var n = SonUi.Texte(AudioConfiguration.NomGenre(g), 17, "Blanc", FontWeights.Bold);
            n.Margin = new Thickness(0, 12, 0, 4);
            pile.Children.Add(n);
            pile.Children.Add(SonUi.Texte(AudioConfiguration.AideGenre(g), 13, "Brume"));
            var b = new Button
            {
                Style = Style_("Btn"), Content = pile, Width = 222, Height = 190, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(18, 16, 16, 14),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            };
            var choisi = g;
            b.Click += (_, _) => { genre = choisi; Aller(2); };
            tuiles.Children.Add(b);
        }
        var p = new StackPanel();
        p.Children.Add(tuiles);
        if (SonUi.C.SonVideos != null)
        {
            var note = SonUi.Texte("Le son des vidéos (Médias) est déjà dans la liste.", 13, "Brume");
            note.Margin = new Thickness(2, 6, 0, 0);
            p.Children.Add(note);
        }
        return p;
    }

    // ------------------------------------------------------------ 2. où est-il branché ? (ou quel fichier ?)
    UIElement PageBranchement()
    {
        if (genre == GenreSon.Fichier)
        {
            titre.Text = "Quelle musique ?";
            var p = new StackPanel();
            var nomFichier = SonUi.Texte(fichier.Length > 0 ? Path.GetFileName(fichier) : "Aucun fichier choisi", 15, fichier.Length > 0 ? "Blanc" : "Brume", FontWeights.SemiBold);
            var choisir = SonUi.Bouton("Choisir le fichier…", SonUi.Dossier, "BtnOr", 44);
            choisir.HorizontalAlignment = HorizontalAlignment.Left;
            choisir.Margin = new Thickness(0, 0, 0, 14);
            choisir.Click += (_, _) =>
            {
                if (ChoisirFichier(this, fichier) is not { } f) return;
                fichier = f;
                nomFichier.Text = Path.GetFileName(f);
                nomFichier.Foreground = Pinceau("Blanc");
                Erreur(null);
            };
            p.Children.Add(choisir);
            p.Children.Add(nomFichier);
            var b = new CheckBox { Content = "La jouer en boucle (musique d'attente)", IsChecked = boucle, FontSize = 14.5, Margin = new Thickness(0, 16, 0, 0) };
            b.Click += (_, _) => boucle = b.IsChecked == true;
            p.Children.Add(b);
            var aide = SonUi.Texte("Elle ne part pas toute seule : sur sa carte, cliquez Lire quand vous voulez l'entendre, Pause pour l'arrêter.", 13, "Brume");
            aide.Margin = new Thickness(0, 16, 0, 0);
            p.Children.Add(aide);
            if (fichier.Length == 0) Dispatcher.BeginInvoke(new Action(() => { if (numero == 2 && fichier.Length == 0) choisir.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return p;
        }
        titre.Text = genre switch
        {
            GenreSon.Ordinateur => "Quel son de l'ordinateur ?",
            GenreSon.Table => "Où est branchée la table de mixage ?",
            GenreSon.Instrument => "Où est branché l'instrument ?",
            _ => "Où est branché le micro ?",
        };
        // un autre genre (retour à l'étape 1) : on repart du premier appareil
        if (branchement != null && (branchement.Capture == AudioInputKind.Sortie) != (genre == GenreSon.Ordinateur)) branchement = null;
        var depart = branchement ?? (genre == GenreSon.Ordinateur ? new Branchement(AudioInputKind.Sortie, "default", "", 0, new() { 0 }) : PremierMicro());
        appareil?.Fermer();
        appareil = new ChoixAppareil(genre, depart);
        return appareil;
    }

    /// <summary>Le premier micro trouvé, sinon celui de Windows.</summary>
    static Branchement PremierMicro()
    {
        var d = SonUi.A.Microphones().FirstOrDefault();
        return d == null ? new(AudioInputKind.Microphone, "default", "", 0, new() { 0 }) : new(AudioInputKind.Microphone, d.Id, d.Name, 0, new() { 0 });
    }

    internal static string? ChoisirFichier(Window? owner, string actuel)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Musique ou fichier audio",
            Filter = "Musique et vidéo|*.mp3;*.wav;*.flac;*.ogg;*.opus;*.m4a;*.aac;*.wma;*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm|Tous les fichiers|*.*",
        };
        if (File.Exists(actuel)) dialog.InitialDirectory = Path.GetDirectoryName(actuel);
        bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileName : null;
    }

    // ------------------------------------------------------------ 3. son nom
    string NomPropose()
    {
        if (genre == GenreSon.Fichier && fichier.Length > 0) return Path.GetFileNameWithoutExtension(fichier);
        return genre switch
        {
            GenreSon.Voix => "Micro",
            GenreSon.Instrument => "Instrument",
            GenreSon.Table => "Table de mixage",
            GenreSon.Ordinateur => "Son de l'ordinateur",
            GenreSon.Video => "Vidéos de Display",
            _ => "Musique",
        };
    }

    UIElement PageNom()
    {
        titre.Text = "Comment l'appelle-t-on ?";
        var p = new StackPanel();
        nom = new TextBox { Style = Style_("Champ"), FontSize = 17, Text = SonUi.C.NomLibre(NomPropose()), Margin = new Thickness(0, 0, 0, 12) };
        p.Children.Add(nom);
        string[] idees = genre switch
        {
            GenreSon.Voix => new[] { "Pasteur", "Chaire", "Chorale", "Conducteur de louange", "Micro sans fil", "Annonces", "Interprète" },
            GenreSon.Instrument => new[] { "Piano", "Clavier", "Guitare", "Basse", "Batterie", "Orgue" },
            GenreSon.Table => new[] { "Table de mixage", "Sono" },
            GenreSon.Ordinateur => new[] { "Son de l'ordinateur", "YouTube" },
            GenreSon.Video => new[] { "Vidéos de Display", "Vidéos" },
            _ => new[] { "Musique d'attente", "Fond musical" },
        };
        var puces = new WrapPanel();
        foreach (var i in idees)
        {
            var b = new Button { Style = Style_("BtnPetit"), Content = i, Margin = new Thickness(0, 0, 8, 8) };
            string texte = i;
            b.Click += (_, _) => { nom.Text = SonUi.C.NomLibre(texte); nom.Focus(); nom.SelectAll(); };
            puces.Children.Add(b);
        }
        p.Children.Add(puces);
        Dispatcher.BeginInvoke(new Action(() => { nom.Focus(); nom.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        return p;
    }

    void Ajouter()
    {
        string n = nom?.Text.Trim() ?? "";
        if (n.Length == 0) { Erreur("Donnez-lui un nom (par exemple « Pasteur »)."); nom?.Focus(); return; }
        if (SonUi.C.Sons.Count >= AudioConfiguration.MaxSources) { Erreur($"{AudioConfiguration.MaxSources} sons au plus."); return; }
        if (SonUi.C.Sons.Any(s => string.Equals(s.Nom, n, StringComparison.OrdinalIgnoreCase))) { Erreur($"Un autre son s'appelle déjà « {n} »."); nom?.Focus(); return; }
        var son = Son.Nouveau(genre, n);
        if (genre == GenreSon.Fichier) { son.Fichier = fichier; son.Boucle = boucle; }
        else if (genre == GenreSon.Video) { }
        else (branchement ?? (genre == GenreSon.Ordinateur ? new Branchement(AudioInputKind.Sortie, "default", "", 0, new() { 0 }) : PremierMicro())).Appliquer(son);
        Resultat = son;
        Close();
    }
}
