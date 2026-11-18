using System.Windows;
using System.Windows.Controls;
using MyVerse.Audio;

namespace MyVerse.Ui.Audio;

/// <summary>
/// « Régler » un son : son nom, ce que c'est, où il est branché (les barres qui bougent), et trois interrupteurs expliqués
/// en une phrase : Dans l'église, Enlever le bruit, Volume régulier. Rien ne change avant « Terminé ».
/// </summary>
public sealed class ReglerSonWindow : FenetreDialogue
{
    readonly Son source;
    readonly Son copie;
    readonly TextBox nom;
    readonly StackPanel zoneBranchement = new();
    readonly StackPanel aides = new();
    readonly TextBlock erreur = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    ChoixAppareil? appareil;
    readonly List<(Button Bouton, GenreSon Genre)> genres = new();

    /// <summary>« Retirer ce son » a été demandé et confirmé.</summary>
    public bool Retire { get; private set; }
    public bool Valide { get; private set; }

    public ReglerSonWindow(Son s)
    {
        source = s;
        copie = AudioConfiguration.Copy(s);
        Title = "Régler « " + s.Nom + " »";
        Width = 760;
        Height = Math.Min(780, SystemParameters.WorkArea.Height - 40);
        MinWidth = 620;
        MinHeight = 460;
        ResizeMode = ResizeMode.CanResize;
        Background = Pinceau("Page");
        erreur.Foreground = Pinceau("Danger");

        var corps = new StackPanel();
        var tete = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var pastille = SonUi.Pastille(s.Genre, 48);
        pastille.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(pastille, Dock.Left);
        tete.Children.Add(pastille);
        var titres = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titres.Children.Add(SonUi.Texte("Régler", 12.5, "Or", FontWeights.Bold));
        titres.Children.Add(SonUi.Texte(s.Nom, 24, "Blanc", FontWeights.ExtraBold));
        tete.Children.Add(titres);
        corps.Children.Add(tete);

        corps.Children.Add(Libelle("Son nom"));
        nom = new TextBox { Style = Style_("Champ"), FontSize = 16, Text = s.Nom, Margin = new Thickness(0, 0, 0, 18) };
        corps.Children.Add(nom);

        if (s.EstMicro)
        {
            corps.Children.Add(Libelle("C'est"));
            var choix = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            foreach (var g in new[] { GenreSon.Voix, GenreSon.Instrument, GenreSon.Table })
            {
                var b = SonUi.Bouton(AudioConfiguration.NomGenre(g), SonUi.IconeGenre(g), "Btn", 42, SonUi.CouleurGenre(g));
                b.Margin = new Thickness(0, 0, 10, 0);
                var genre = g;
                b.Click += (_, _) => ChangerGenre(genre);
                genres.Add((b, g));
                choix.Children.Add(b);
            }
            corps.Children.Add(choix);
        }

        corps.Children.Add(zoneBranchement);
        corps.Children.Add(Libelle("Aides"));
        corps.Children.Add(aides);

        var bas = new DockPanel { Margin = new Thickness(28, 12, 28, 22), LastChildFill = false };
        // 2.4.0 : le son des vidéos se retire aussi (on le remet avec « Ajouter un son »)
        {
            var retirer = new Button { Style = Style_("BtnDanger"), Content = "Retirer ce son", Height = 42, Padding = new Thickness(16, 0, 16, 0) };
            retirer.Click += (_, _) =>
            {
                if (!Dialogue.Confirmer(this, $"Retirer « {s.Nom} » ? Il ne sera plus entendu nulle part (ni enregistrement, ni Zoom).", "Retirer")) return;
                Retire = true;
                Close();
            };
            DockPanel.SetDock(retirer, Dock.Left);
            bas.Children.Add(retirer);
        }
        var ok = new Button { Style = Style_("BtnOr"), Content = "Terminé", IsDefault = true, MinWidth = 130, Height = 42, Margin = new Thickness(10, 0, 0, 0) };
        ok.Click += (_, _) => Valider();
        DockPanel.SetDock(ok, Dock.Right);
        bas.Children.Add(ok);
        var annuler = new Button { Style = Style_("Btn"), Content = "Annuler", IsCancel = true, MinWidth = 110, Height = 42 };
        annuler.Click += (_, _) => Close();
        DockPanel.SetDock(annuler, Dock.Right);
        bas.Children.Add(annuler);
        var pied = new StackPanel();
        erreur.Margin = new Thickness(28, 0, 28, 0);
        pied.Children.Add(erreur);
        pied.Children.Add(bas);

        var racine = new DockPanel();
        DockPanel.SetDock(pied, Dock.Bottom);
        racine.Children.Add(pied);
        racine.Children.Add(new ScrollViewer { Content = corps, Padding = new Thickness(28, 22, 22, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = racine;
        Closed += (_, _) => appareil?.Fermer();
        Loaded += (_, _) => nom.Focus();
        RemplirBranchement();
        RemplirAides();
        MajGenres();
    }

    static TextBlock Libelle(string texte) => new Capitales { Libelle = texte, Margin = new Thickness(0, 0, 0, 8) };

    void ChangerGenre(GenreSon g)
    {
        if (g == copie.Genre) return;
        copie.Genre = g;
        // les aides du nouveau genre (une voix se nettoie, la musique non) ; le branchement reste
        var capture = copie.Capture;
        AudioConfiguration.AppliquerDefauts(copie);
        copie.Capture = capture;
        RemplirAides();
        MajGenres();
    }

    void MajGenres()
    {
        foreach (var (b, g) in genres)
        {
            bool oui = g == copie.Genre;
            b.Background = Pinceau(oui ? "OrLeger" : "Encre");
            b.BorderBrush = Pinceau(oui ? "OrVif" : "Nuit3");
            b.FontWeight = oui ? FontWeights.Bold : FontWeights.Medium;
        }
    }

    void RemplirBranchement()
    {
        zoneBranchement.Children.Clear();
        switch (copie.Genre)
        {
            case GenreSon.Video:
                zoneBranchement.Children.Add(Libelle("D'où vient-il"));
                var t = SonUi.Texte("Du son des vidéos que vous projetez avec Display : il suit tout seul la vidéo à l'écran.", 14, "Encre2");
                t.Margin = new Thickness(0, 0, 0, 18);
                zoneBranchement.Children.Add(t);
                break;
            case GenreSon.Fichier:
                zoneBranchement.Children.Add(Libelle("Le fichier"));
                var ligne = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var choisir = new Button { Style = Style_("Btn"), Content = "Changer…", Height = 40, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(10, 0, 0, 0) };
                var nomFichier = new TextBox { Style = Style_("Champ"), IsReadOnly = true, Text = copie.Fichier, FontSize = 14 };
                choisir.Click += (_, _) =>
                {
                    if (AjoutSonWindow.ChoisirFichier(this, copie.Fichier) is not { } f) return;
                    copie.Fichier = f;
                    nomFichier.Text = f;
                };
                DockPanel.SetDock(choisir, Dock.Right);
                ligne.Children.Add(choisir);
                ligne.Children.Add(nomFichier);
                zoneBranchement.Children.Add(ligne);
                var boucle = new CheckBox { Content = "La jouer en boucle", IsChecked = copie.Boucle, FontSize = 14.5, Margin = new Thickness(0, 4, 0, 18) };
                boucle.Click += (_, _) => copie.Boucle = boucle.IsChecked == true;
                zoneBranchement.Children.Add(boucle);
                break;
            default:
                zoneBranchement.Children.Add(Libelle(copie.Genre == GenreSon.Ordinateur ? "Quel son de l'ordinateur" : "Où est-il branché"));
                appareil = new ChoixAppareil(copie.Genre, Branchement.De(copie)) { Margin = new Thickness(0, 0, 0, 18) };
                zoneBranchement.Children.Add(appareil);
                break;
        }
    }

    void RemplirAides()
    {
        aides.Children.Clear();
        var (eglise, ie) = SonUi.Interrupteur("Dans l'église", copie.EstMicro
            ? "Le faire sortir par les haut-parleurs de l'ordinateur. À laisser éteint si le micro passe déjà par la table de mixage : sinon, ça peut siffler (larsen)."
            : copie.Genre == GenreSon.Ordinateur ? "Inutile : le son de l'ordinateur s'entend déjà dans l'église." : "Le faire entendre dans l'église, par les haut-parleurs de l'ordinateur.", copie.Eglise);
        ie.Click += (_, _) =>
        {
            if (ie.IsChecked == true && copie.EstMicro && !Dialogue.Confirmer(this, "Un micro dans les haut-parleurs de l'église peut siffler (larsen). Seulement si ce micro ne passe pas déjà par la table de mixage. Continuer ?", "Oui, dans l'église"))
            {
                ie.IsChecked = false;
                return;
            }
            copie.Eglise = ie.IsChecked == true;
        };
        aides.Children.Add(eglise);
        if (copie.Genre == GenreSon.Voix)
        {
            var (bruit, ib) = SonUi.Interrupteur("Enlever le bruit", "Enlève le souffle, la ventilation et les bruits de fond. Conseillé pour une voix.", copie.EnleverBruit);
            ib.Click += (_, _) => copie.EnleverBruit = ib.IsChecked == true;
            aides.Children.Add(bruit);
        }
        var (regulier, ir) = SonUi.Interrupteur("Volume régulier", "Les passages faibles remontent, les cris ne saturent pas : on n'a presque plus besoin de toucher au volume.", copie.VolumeRegulier);
        ir.Click += (_, _) => copie.VolumeRegulier = ir.IsChecked == true;
        aides.Children.Add(regulier);
    }

    void Valider()
    {
        string n = nom.Text.Trim();
        if (n.Length == 0) { Erreur("Donnez-lui un nom."); nom.Focus(); return; }
        if (SonUi.C.Sons.Any(x => x != source && string.Equals(x.Nom, n, StringComparison.OrdinalIgnoreCase))) { Erreur($"Un autre son s'appelle déjà « {n} »."); nom.Focus(); return; }
        if (copie.Genre == GenreSon.Fichier && copie.Fichier.Length == 0) { Erreur("Choisissez le fichier de musique."); return; }
        appareil?.Choix.Appliquer(copie);
        source.Nom = n;
        source.Genre = copie.Genre;
        source.Capture = copie.Capture; source.Appareil = copie.Appareil; source.NomAppareil = copie.NomAppareil;
        source.Entree = copie.Entree; source.EntreesAsio = copie.EntreesAsio.ToList();
        source.Fichier = copie.Fichier; source.Boucle = copie.Boucle;
        source.Eglise = copie.Eglise; source.EnleverBruit = copie.EnleverBruit; source.VolumeRegulier = copie.VolumeRegulier;
        Valide = true;
        Close();
    }

    void Erreur(string texte)
    {
        erreur.Text = texte;
        erreur.Visibility = Visibility.Visible;
    }
}
