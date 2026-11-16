using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.5.0, design « Télécommande » :
/// - trois pages dans la barre de gauche (Culte, Bibliothèque, Son) ; la Bibliothèque a ses onglets (Bible, Chants, Médias, Duo) ;
/// - en bas, la télécommande, la même sur toutes les pages : ce qui est à l'écran (petit aperçu, titre, « Refrain, 2 sur 6 »),
///   Précédent, le gros Suivant, Noir, Effacer ;
/// - à droite, les outils de l'écran repliés en lignes (Style, Taille, Coin, Écran de repos, Raccourcis) : un clic ouvre la ligne.
/// </summary>
public partial class MainWindow
{
    /// <summary>La dernière page de la Bibliothèque ouverte : le bouton « Bibliothèque » y revient.</summary>
    string dernierBiblio = "bible";
    readonly List<(ToggleButton Tete, FrameworkElement Corps)> tiroirs = new();
    /// <summary>Signature de la barre de progression (nombre de diapositives, position) : elle n'est refaite que si elle change.</summary>
    string signatureProgres = "";

    static readonly Dictionary<string, string> NomsFonds = new()
    {
        ["aube"] = "Aube", ["lin"] = "Lin", ["ciel"] = "Ciel", ["nuit"] = "Nuit", ["noir"] = "Noir", ["vert"] = "Vert", ["transparent"] = "Transparent",
    };

    void InitTelecommande()
    {
        OngletBiblio.Click += (_, _) => MontrerOnglet(dernierBiblio == "annonces" ? "bible" : dernierBiblio);
        OngletMediasRail.Click += (_, _) => MontrerOnglet("annonces");
        SegBible.Click += (_, _) => MontrerOnglet("bible");
        SegChants.Click += (_, _) => MontrerOnglet("chants");
        SegMedias.Click += (_, _) => MontrerOnglet("annonces");
        SegDuo.Click += (_, _) => MontrerOnglet("duo");

        Tiroir(TiroirStyle, CorpsStyle);
        Tiroir(TiroirTaille, CorpsTaille);
        Tiroir(TiroirCoin, CorpsCoin);
        Tiroir(TiroirRepos, CarteDefaut);
        Tiroir(TiroirAide, CorpsAide);
        Taille.ValueChanged += (_, _) => MajOutils();
        MajOutils();
    }

    /// <summary>Une ligne d'outil : un clic ouvre son contenu juste dessous, et referme la ligne ouverte avant.</summary>
    void Tiroir(ToggleButton tete, FrameworkElement corps)
    {
        tiroirs.Add((tete, corps));
        tete.Checked += (_, _) =>
        {
            corps.Visibility = Visibility.Visible;
            foreach (var (t, _) in tiroirs) if (t != tete) t.IsChecked = false;
        };
        tete.Unchecked += (_, _) => corps.Visibility = Visibility.Collapsed;
    }

    /// <summary>La page ouverte : le bouton « Bibliothèque » s'allume sur Bible, Chants, Médias et Duo, avec l'onglet de la page.</summary>
    void MajPagesBiblio(string o)
    {
        bool biblio = o is "bible" or "chants" or "annonces" or "duo";
        if (biblio) dernierBiblio = o;
        OngletBiblio.IsChecked = biblio && o != "annonces";
        OngletMediasRail.IsChecked = o == "annonces";
        BiblioOnglets.Visibility = biblio ? Visibility.Visible : Visibility.Collapsed;
        SegBible.IsChecked = o == "bible";
        SegChants.IsChecked = o == "chants";
        SegMedias.IsChecked = o == "annonces";
        SegDuo.IsChecked = o == "duo";
    }

    /// <summary>Les valeurs écrites au bout des lignes d'outils : le fond, la taille, l'écran de repos.</summary>
    void MajOutils()
    {
        var reg = App.Donnees.Reglages;
        TiroirStyleValeur.Text = NomsFonds.TryGetValue(reg.Theme ?? "", out var n) ? n : reg.Theme ?? "";
        if (reg.Presentation == "bandeau") TiroirStyleValeur.Text += ", bandeau";
        TiroirTailleValeur.Text = Math.Round(Taille.Value * 100) + " %";
    }

    /// <summary>
    /// La télécommande, à gauche : ce qui est à l'écran (le titre de l'élément, « Refrain, 2 sur 6 »)
    /// et une petite barre par diapositive de l'élément (les passées et celle à l'écran en rouge).
    /// </summary>
    void MajTelecommande(EtatEcran e)
    {
        var cur = R.ElementCourant;
        int? pos = cur != null && R.Agenda.Elements.Contains(cur) ? R.PositionAgenda(cur) : null;
        IReadOnlyList<DiapoAgenda>? diapos = null;
        if (cur != null && pos != null)
        {
            try { diapos = R.Diapos(cur); }
            catch (Exception x) { Journal.Erreur("télécommande", x); }
        }
        bool rien = e.Vide && e.Duo == null;
        if (noir) { BarreTitre.Text = "Écran noir"; BarreSous.Text = "Touche B ou Suivant pour rallumer"; }
        else if (cur != null && diapos is { Count: > 0 } d && pos is int p && p >= 0 && p < d.Count)
        {
            BarreTitre.Text = TitreElement(cur);
            BarreSous.Text = d.Count > 1 ? $"{NomDiapo(cur, d[p])}, {p + 1} sur {d.Count}" : NomDiapo(cur, d[p]);
        }
        else if (rien) { BarreTitre.Text = "Rien à l'écran"; BarreSous.Text = R.Cible is CibleDefaut ? "Écran de repos : " + R.LibelleEcranDefaut : "Suivant ou un clic sur une image"; }
        else
        {
            var a = AEcran(e);
            BarreTitre.Text = a.Length > 0 ? char.ToUpperInvariant(a[0]) + a[1..] : "À l'écran";
            BarreSous.Text = e.Mode == "media" ? "Média" : e.Ref is { Length: > 0 } r && r != BarreTitre.Text ? r : "";
        }
        BarreTitre.ToolTip = BarreTitre.Text;
        BarreSous.Visibility = BarreSous.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // une barre par diapositive (au-delà de 14, une seule barre qui se remplit)
        int n = diapos?.Count ?? 0, k = pos ?? -1;
        string sig = noir ? "noir" : n + "|" + k;
        if (sig == signatureProgres) return;
        signatureProgres = sig;
        BarreProgres.Children.Clear();
        if (noir || n <= 1 || k < 0) { BarreProgres.Visibility = Visibility.Collapsed; return; }
        BarreProgres.Visibility = Visibility.Visible;
        if (n <= 14)
        {
            for (int i = 0; i < n; i++)
                BarreProgres.Children.Add(new Border
                {
                    Width = 20, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 4, 0),
                    Background = Pinceau(i <= k ? "Ecran" : "Nuit3"),
                });
        }
        else
        {
            var fond = new Grid { Width = 160, Height = 4 };
            fond.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Pinceau("Nuit3") });
            fond.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Pinceau("Ecran"), HorizontalAlignment = HorizontalAlignment.Left, Width = 160.0 * (k + 1) / n });
            BarreProgres.Children.Add(fond);
        }
    }

    /// <summary>1.5.1 : sans carte graphique (dessin par le processeur), le grand aperçu du culte perd aussi l'ombre floue du texte.</summary>
    public void ModeLeger()
    {
        CulteRendu.Leger = true;
        Journal.Info("affichage léger : le grand aperçu du culte sans ombre floue (plus rapide sans carte graphique)");
    }

    // ------------------------------------------------------------ pour le test automatique
    public string TestTelecommande => BarreTitre.Text + " / " + BarreSous.Text;
    /// <summary>Ouvre (ou referme) une ligne d'outil : « style », « taille », « coin », « repos », « aide ».</summary>
    public void TestTiroir(string nom, bool ouvert)
    {
        var t = nom switch { "style" => TiroirStyle, "taille" => TiroirTaille, "coin" => TiroirCoin, "repos" => TiroirRepos, _ => TiroirAide };
        t.IsChecked = ouvert;
    }
}
