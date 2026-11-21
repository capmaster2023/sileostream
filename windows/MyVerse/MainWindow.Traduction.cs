using System.Windows;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 3.0.0 : la traduction française des chants swahili (Wokovu). Elle se fait toute seule, en arrière-plan, et reste
/// dans les chants ; elle ne s'affiche sous les paroles que si on le veut : la ligne « Traduction française » à droite, ou la touche T.
/// </summary>
public partial class MainWindow
{
    /// <summary>Les cases sont remises à jour par le code : leurs clics ne comptent pas.</summary>
    bool majTraduction;

    void InitTraduction()
    {
        Tiroir(TiroirTraduction, CorpsTraduction);
        var r = App.Donnees.Reglages.Traduction;
        TraductionCourriel.Text = r.Courriel;
        TraductionMontrer.Click += (_, _) => { if (!majTraduction) MontrerTraduction(TraductionMontrer.IsChecked == true); };
        TraductionAuto.Click += (_, _) =>
        {
            if (majTraduction) return;
            r.Actif = TraductionAuto.IsChecked == true;
            App.Donnees.SauverReglages();
            if (r.Actif) App.Traductions.DemanderTout();
            MajTraduction();
        };
        TraductionCourriel.LostFocus += (_, _) =>
        {
            var c = TraductionCourriel.Text.Trim();
            if (c == r.Courriel) return;
            r.Courriel = c;
            // une autre adresse : sa propre limite gratuite ; la traduction réessaie tout de suite
            r.Reprise = null;
            App.Donnees.SauverReglages();
            App.Traductions.DemanderTout();
            MajTraduction();
        };
        R.TraductionChange += () => Dispatcher.BeginInvoke(new Action(MajTraduction));
        App.Traductions.Message += _ => MajTraduction();
        // l'avancement (« Traduction de … ») change sans prévenir : relu de temps en temps quand la ligne est ouverte
        var minuterie = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        minuterie.Tick += (_, _) => { if (CorpsTraduction.IsVisible) MajTraduction(); };
        minuterie.Start();
        MajTraduction();
    }

    /// <summary>Montre ou cache la traduction (case, touche T) : l'écran, Zoom, YouTube et OBS suivent tout de suite.</summary>
    void MontrerTraduction(bool montrer)
    {
        R.AfficherTraduction(montrer);
        MajTraduction();
        Toast(montrer
            ? "Traduction française montrée sous les chants swahili (T pour la cacher)."
            : "Traduction française cachée (T pour la montrer).");
    }

    void MajTraduction()
    {
        var r = App.Donnees.Reglages.Traduction;
        majTraduction = true;
        try
        {
            TraductionMontrer.IsChecked = r.Afficher;
            TraductionAuto.IsChecked = r.Actif;
        }
        finally { majTraduction = false; }
        TiroirTraductionValeur.Text = r.Afficher ? "Montrée" : "Cachée";
        var (faits, total) = TraductionChants.Compte(R.Chants, r);
        string etat = total == 0
            ? "Aucun chant swahili pour l'instant : la traduction se fait pour le recueil « " + string.Join(", ", r.Recueils) + " »."
            : $"{faits} chant{(faits > 1 ? "s" : "")} traduit{(faits > 1 ? "s" : "")} sur {total}.";
        if (!r.Actif) etat += " Traduction automatique arrêtée.";
        else if (App.Traductions.Etat is { Length: > 0 } en) etat += " " + en;
        TraductionEtat.Text = etat;
    }

    // ------------------------------------------------------------ pour le test automatique
    public void TestMontrerTraduction(bool montrer) => MontrerTraduction(montrer);
    public string TestTraductionValeur => TiroirTraductionValeur.Text;
}
