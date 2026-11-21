using System.Windows;
using System.Windows.Controls;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 3.0.0 : la traduction française d'un chant swahili, ligne par ligne : chaque ligne du chant et sa traduction, à corriger
/// à la main. C'est gardé avec le chant (en fermant). Une traduction effacée est refaite toute seule (par Internet).
/// </summary>
public sealed class TraductionWindow : FenetreDialogue
{
    readonly string id;
    readonly List<(string Cle, TextBox Champ)> champs = new();
    readonly Action<string> traduit;

    public TraductionWindow(Chant c)
    {
        id = c.Id;
        Title = "Traduction · " + c.Title;
        Width = 980; Height = 700; MinWidth = 640; MinHeight = 420;
        ResizeMode = ResizeMode.CanResize;

        var dock = new DockPanel { Margin = new Thickness(22, 18, 22, 20) };
        var titre = new TextBlock { Text = "Traduction française · " + c.Title, Style = Style_("Titre2"), Margin = new Thickness(0, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel.SetDock(titre, Dock.Top);
        dock.Children.Add(titre);
        var aide = new TextBlock
        {
            Text = "Chaque ligne du chant et sa traduction. Corrigez ce qui ne va pas : c'est gardé avec le chant, et l'écran suit. "
                + "Effacez une traduction pour qu'elle soit refaite toute seule (par Internet). Sous les paroles, elle n'est montrée que si on le veut (touche T).",
            TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Pinceau("Brume"), Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(aide, Dock.Top);
        dock.Children.Add(aide);

        var bas = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var fermer = new Button { Content = "Fermer", Style = Style_("BtnOr"), MinWidth = 130, IsCancel = true };
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Right);
        var traduire = new Button { Content = "Traduire ce qui manque", Style = Style_("Btn"), MinWidth = 200, Margin = new Thickness(0, 0, 10, 0) };
        traduire.Click += (_, _) => TraduireManquant();
        DockPanel.SetDock(traduire, Dock.Right);
        bas.Children.Add(fermer);
        bas.Children.Add(traduire);
        bas.Children.Add(new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(bas, Dock.Bottom);
        dock.Children.Add(bas);

        var grille = new Grid();
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grille.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        int rang = 0;
        void Ligne(UIElement gauche, UIElement droite)
        {
            grille.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(gauche, rang); Grid.SetColumn(gauche, 0);
            Grid.SetRow(droite, rang); Grid.SetColumn(droite, 2);
            grille.Children.Add(gauche);
            grille.Children.Add(droite);
            rang++;
        }
        Ligne(new TextBlock { Text = "SWAHILI", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume2"), Margin = new Thickness(0, 0, 0, 6) },
              new TextBlock { Text = "FRANÇAIS", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Pinceau("Brume2"), Margin = new Thickness(0, 0, 0, 6) });
        var vues = new HashSet<string>();
        foreach (var s in c.Sections)
        {
            foreach (var l in TraductionChants.Lignes(s.Text))
            {
                var cle = TraductionChants.Cle(l);
                if (!vues.Add(cle)) continue;
                var champ = new TextBox { Style = Style_("Champ"), Text = c.Traductions != null && c.Traductions.TryGetValue(cle, out var t) ? t : "", Margin = new Thickness(0, 0, 0, 6) };
                champs.Add((cle, champ));
                Ligne(new TextBlock { Text = l, FontSize = 14, Foreground = Pinceau("Blanc"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) }, champ);
            }
        }
        dock.Children.Add(new ScrollViewer { Content = grille, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = dock;

        // les lignes traduites pendant que la fenêtre est ouverte apparaissent dans les cases encore vides
        traduit = chant => { if (chant == id) Dispatcher.BeginInvoke(new Action(Remplir)); };
        App.Traductions.Traduit += traduit;
        Closing += (_, _) => Enregistrer();
        Closed += (_, _) => App.Traductions.Traduit -= traduit;
    }

    Chant? Chant_ => App.Regie.Trouver(id);

    /// <summary>Les cases vides reçoivent les traductions arrivées (celles qu'on a remplies ne bougent pas).</summary>
    void Remplir()
    {
        if (Chant_?.Traductions is not { } t) return;
        foreach (var (cle, champ) in champs)
            if (champ.Text.Trim().Length == 0 && !champ.IsKeyboardFocusWithin && t.TryGetValue(cle, out var f)) champ.Text = f;
    }

    /// <summary>Ce qui est écrit va dans le chant : une case vide efface la traduction de la ligne (elle sera refaite).</summary>
    void Enregistrer()
    {
        if (Chant_ is not { } c) return;
        bool change = false;
        foreach (var (cle, champ) in champs)
        {
            var f = champ.Text.Trim();
            var avant = c.Traductions != null && c.Traductions.TryGetValue(cle, out var a) ? a : "";
            if (f == avant) continue;
            change = true;
            if (f.Length > 0) (c.Traductions ??= new Dictionary<string, string>())[cle] = f;
            else c.Traductions?.Remove(cle);
        }
        if (!change) return;
        App.Donnees.SauverChantsPlusTard();
        App.Regie.ChantTraduit(id);
    }

    void TraduireManquant()
    {
        Enregistrer();
        if (Chant_ is not { } c) return;
        var r = App.Donnees.Reglages.Traduction;
        if (!r.Actif)
        {
            Dialogue.Informer(this, "La traduction automatique est arrêtée (à droite, « Traduction française » › Traduire tout seul).");
            return;
        }
        if (TraductionChants.Manquantes(c).Count == 0)
        {
            Dialogue.Informer(this, "Tout est déjà traduit. Effacez une case pour qu'elle soit refaite.");
            return;
        }
        App.Traductions.Demander(c);
        Dialogue.Informer(this, App.Traductions.Pause is { } reprise
            ? $"La limite gratuite du jour est atteinte : la traduction reprendra vers {reprise:HH:mm}."
            : "C'est demandé : les cases vides se remplissent dans un instant (il faut Internet).");
    }

    // ------------------------------------------------------------ pour le test automatique
    public int TestLignes => champs.Count;
    public void TestEcrire(int i, string texte) => champs[i].Champ.Text = texte;
    public void TestEnregistrer() => Enregistrer();
}
