using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Éditeur de chant : un chant collé (même en désordre) est rangé tout seul en diapositives de 3 à 5 lignes ;
/// Entrée devant une ligne = nouvelle diapositive ; aperçu des diapositives à droite.
/// Siloé Stream 1.5.1 : tout s'enregistre tout seul (à chaque pause de frappe, et tout de suite après Ranger ou un collage) ;
/// « Retour en arrière » (ou Ctrl+Z) défait la dernière modification, plusieurs fois de suite ; « Fermer » ferme.
/// </summary>
public partial class EditeurWindow : FenetreDialogue
{
    /// <summary>Le chant (null tant qu'un nouveau chant n'a rien d'enregistré).</summary>
    string? id;
    readonly bool nouveau;
    readonly string recueil;
    string brut = "";
    /// <summary>3.0.0 : brut vient des paroles déjà rangées (pas d'un collage) : il est refait à chaque rangement (les corrections restent).</summary>
    bool brutDuTexte;
    readonly DispatcherTimer minuterie = new() { Interval = TimeSpan.FromMilliseconds(120) };
    /// <summary>Enregistrement automatique, un instant après la dernière touche.</summary>
    readonly DispatcherTimer sauvegarde = new() { Interval = TimeSpan.FromMilliseconds(700) };
    /// <summary>Les états enregistrés avant (titre, paroles) : « Retour en arrière » y revient, un par un.</summary>
    readonly Stack<(string Titre, string Texte)> historique = new();
    (string Titre, string Texte) enregistre;
    bool restauration, aEnregistre;

    /// <summary>Identifiant du chant enregistré (null si rien n'a été enregistré, ou un nouveau chant revenu à vide).</summary>
    public string? IdEnregistre { get; private set; }

    public EditeurWindow(Chant? c, int? focus = null, string? recueilNouveau = null)
    {
        InitializeComponent();
        id = c?.Id;
        nouveau = c == null;
        IdEnregistre = id;
        recueil = recueilNouveau ?? "";
        Titre.Text = c?.Title ?? "";
        Texte.Text = c != null ? ChantsFormats.VersTexte(c.Sections) : "";
        enregistre = (Titre.Text, Texte.Text);
        int l = App.Donnees.Reglages.LignesDiapo;
        Lignes.SelectedItem = Lignes.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == l.ToString()) ?? Lignes.Items[0];
        Apercu_();

        DataObject.AddPastingHandler(Texte, Coller);
        Texte.TextChanged += (_, _) => { minuterie.Stop(); minuterie.Start(); Modifie(); };
        Titre.TextChanged += (_, _) => Modifie();
        minuterie.Tick += (_, _) => { minuterie.Stop(); Apercu_(); };
        sauvegarde.Tick += (_, _) => SauverMaintenant();
        Ranger.Click += (_, _) =>
        {
            SauverMaintenant();
            if (brut.Length == 0 || brutDuTexte) { brut = MyVerse.Ranger.Rassembler(Texte.Text); brutDuTexte = true; }
            RangerTexte(brut);
            SauverMaintenant();
        };
        Lignes.SelectionChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            App.Donnees.Reglages.LignesDiapo = NbLignes;
            App.Donnees.SauverReglages();
            SauverMaintenant();
            if (brut.Length == 0 || brutDuTexte) { brut = MyVerse.Ranger.Rassembler(Texte.Text); brutDuTexte = true; }
            if (brut.Trim().Length > 0) RangerTexte(brut);
            SauverMaintenant();
        };
        Brut.Click += (_, _) => { SauverMaintenant(); Texte.Text = brut; Apercu_(); SauverMaintenant(); };
        // 3.0.0 : un chant swahili (Wokovu) : sa traduction française, ligne par ligne, à corriger à la main
        MajBoutonTraduction();
        TraductionBtn.Click += (_, _) =>
        {
            SauverMaintenant();
            if (id != null && App.Regie.Trouver(id) is { } ch) new TraductionWindow(ch) { Owner = this }.ShowDialog();
            else Dialogue.Informer(this, "Écrivez d'abord les paroles du chant : la traduction vient ensuite.");
        };
        Annuler.Click += (_, _) => Close();
        Retour.Click += (_, _) => RetourArriere();
        // Ctrl+Z : le même retour en arrière, par étapes (plutôt que lettre par lettre)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { RetourArriere(); e.Handled = true; }
        };
        // en fermant : ce qui vient d'être tapé est enregistré, et le fichier est écrit tout de suite
        Closing += (_, _) =>
        {
            SauverMaintenant();
            if (aEnregistre) App.Donnees.SauverChants();
        };
        Loaded += (_, _) =>
        {
            if (c != null && focus is int f)
            {
                // curseur au début de la strophe double-cliquée
                int pos = c.Sections.Take(f).Sum(s => $"[{s.Label}]\n{s.Text}".Length + 2);
                Texte.Focus();
                Texte.Select(Math.Min(pos, Texte.Text.Length), 0);
                int ligne = Texte.GetLineIndexFromCharacterIndex(Math.Min(pos, Math.Max(0, Texte.Text.Length - 1)));
                if (ligne >= 0) Texte.ScrollToLine(ligne);
            }
            else if (c == null) Texte.Focus();
            else Titre.Focus();
        };
    }

    int NbLignes => int.TryParse((Lignes.SelectedItem as ComboBoxItem)?.Tag as string, out var n) ? n : 0;

    /// <summary>3.0.0 : « Traduction… » seulement pour un chant d'un recueil swahili (Wokovu).</summary>
    void MajBoutonTraduction()
    {
        var rec = id != null && App.Regie.Trouver(id) is { } ch ? ch.Author : recueil;
        TraductionBtn.Visibility = TraductionChants.RecueilSwahili(rec, App.Donnees.Reglages.Traduction) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Un chant complet collé dans l'éditeur vide (ou tout sélectionné) : rangé directement.</summary>
    void Coller(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        var t = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        bool tout = Texte.SelectionLength == Texte.Text.Length;
        if (t.Replace("\r\n", "\n").Split('\n').Count(x => x.Trim().Length > 0) >= 3 && (Texte.Text.Trim().Length == 0 || tout))
        {
            e.CancelCommand();
            brut = t.Replace("\r\n", "\n");
            brutDuTexte = false;
            Dispatcher.BeginInvoke(new Action(() => { SauverMaintenant(); RangerTexte(brut); SauverMaintenant(); }));
        }
    }

    void RangerTexte(string texte, bool silence = false)
    {
        var r = MyVerse.Ranger.Ranger_(texte, NbLignes, Titre.Text);
        if (r.Sections.Count == 0) return;
        if (Titre.Text.Trim().Length == 0 && r.Titre.Length > 0) Titre.Text = r.Titre;
        Texte.Text = ChantsFormats.VersTexte(r.Sections);
        Brut.Visibility = brut.Length > 0 && !brutDuTexte ? Visibility.Visible : Visibility.Collapsed;
        Apercu_();
        if (!silence && Owner is MainWindow m) m.Toast($"Chant rangé : {r.Sections.Count} diapositive{(r.Sections.Count > 1 ? "s" : "")}.");
    }

    void Apercu_()
    {
        var secs = ChantsFormats.SplitSections(Texte.Text);
        Compte.Text = secs.Count > 0 ? $"({secs.Count})" : "";
        Dialogue.Miniatures(Apercu, secs.Select((s, i) => ($"{i + 1}. {(s.Label.Length > 0 ? s.Label : "Strophe")}", s.Text)));
    }

    // ------------------------------------------------------------ 1.5.1 : enregistrement automatique et retour en arrière
    void Modifie()
    {
        if (restauration) return;
        sauvegarde.Stop();
        sauvegarde.Start();
        Etat("Modification en cours…", "Brume2");
    }

    /// <summary>Enregistre ce qui est à l'écran s'il a changé ; l'état d'avant part dans l'historique (une étape de « Retour en arrière »).</summary>
    void SauverMaintenant()
    {
        sauvegarde.Stop();
        var actuel = (Titre.Text, Texte.Text);
        if (actuel == enregistre) { Etat("Tout est enregistré.", "Vert"); return; }
        if (!Ecrire(actuel)) return;
        historique.Push(enregistre);
        enregistre = actuel;
        Retour.IsEnabled = true;
    }

    /// <summary>Écrit un état dans le chant. Paroles vides : un nouveau chant disparaît, un chant existant reste comme il était.</summary>
    bool Ecrire((string Titre, string Texte) e)
    {
        var sections = ChantsFormats.SplitSections(e.Texte);
        if (sections.Count == 0)
        {
            if (nouveau && id != null)
            {
                App.Regie.SupprimerChant(id);
                id = null;
                IdEnregistre = null;
                aEnregistre = true;
                Etat("Chant vide : il n'est plus dans la liste.", "Brume");
                return true;
            }
            if (nouveau) return true;
            Etat("Paroles vides : le chant garde ses anciennes paroles.", "RougeTexte");
            return false;
        }
        id = App.Regie.EnregistrerChant(id, e.Titre, sections, recueil, sauverPlusTard: true);
        IdEnregistre = id;
        aEnregistre = true;
        Etat($"Enregistré tout seul à {DateTime.Now:HH:mm:ss}.", "Vert");
        return true;
    }

    /// <summary>Défait la dernière modification (ce qui vient d'être tapé compte comme une étape).</summary>
    void RetourArriere()
    {
        SauverMaintenant();
        if (historique.Count == 0) { Retour.IsEnabled = false; return; }
        var avant = historique.Pop();
        restauration = true;
        try
        {
            Titre.Text = avant.Titre;
            Texte.Text = avant.Texte;
            Texte.CaretIndex = Texte.Text.Length;
        }
        finally { restauration = false; }
        Ecrire(avant);
        enregistre = avant;
        Apercu_();
        Retour.IsEnabled = historique.Count > 0;
        Etat(historique.Count > 0 ? $"Retour en arrière fait (encore {historique.Count} possible{(historique.Count > 1 ? "s" : "")})." : "Retour en arrière fait : c'est le chant du début.", "Vert");
    }

    void Etat(string texte, string couleur)
    {
        EtatTexte.Text = texte;
        EtatPoint.Fill = MainWindow.Pinceau(couleur);
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>Comme un chant collé.</summary>
    public void TestColler(string t)
    {
        brut = t.Replace("\r\n", "\n");
        brutDuTexte = false;
        RangerTexte(brut, true);
    }
    /// <summary>Enregistre tout de suite (sans attendre la pause de frappe) et rend l'identifiant du chant.</summary>
    public string? TestSauver() { SauverMaintenant(); return id; }
    public void TestRetour() => RetourArriere();
    public bool TestRetourPossible => Retour.IsEnabled;
}
