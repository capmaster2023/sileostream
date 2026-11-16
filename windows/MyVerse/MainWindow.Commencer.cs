using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GenreSon = MyVerse.Audio.GenreSon;
using RecordingProfile = MyVerse.Audio.RecordingProfile;
using VerdictNiveau = MyVerse.Audio.VerdictNiveau;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 2.1.0 : « Commencer le culte ». Un clic vérifie tout ce qu'il faut pour le culte (le projecteur, YouTube, le son,
/// les caméras), lance l'enregistrement du culte dans le dossier choisi (Réglages › Son), puis montre le bilan, mis à jour en direct.
/// Pendant l'enregistrement, le bouton devient « Terminer le culte » : l'enregistrement s'arrête, le fichier est gardé.
/// </summary>
public partial class MainWindow
{
    /// <summary>Une vérification : « ok », « attention » ou « erreur », une phrase, et ce qu'on peut faire (un bouton).</summary>
    sealed record VerifCulte(string Titre, string Etat, string Detail, string? Action = null, Action? Faire = null, Func<(string Etat, string Detail)>? Vivant = null);

    readonly DispatcherTimer minuterieCulte = new() { Interval = TimeSpan.FromSeconds(1) };
    bool? culteAffiche;

    void InitCommencer()
    {
        CommencerBtn.Click += (_, _) =>
        {
            if (CulteEnregistre() is { } p) TerminerCulte(p);
            else CommencerCulte();
        };
        minuterieCulte.Tick += (_, _) => MajCommencer();
        minuterieCulte.Start();
        MajCommencer();
    }

    /// <summary>L'enregistrement du culte (le premier des enregistrements de la page Son), s'il tourne.</summary>
    static RecordingProfile? CulteEnregistre() =>
        App.Audio.Config.Profiles.FirstOrDefault() is { } p && App.Audio.Recorder.Active(p.Id) ? p : null;

    /// <summary>Le bouton : vert « Commencer le culte », ou rouge « Terminer le culte » pendant l'enregistrement.</summary>
    void MajCommencer()
    {
        bool enCours = CulteEnregistre() != null;
        if (culteAffiche == enCours) return;
        culteAffiche = enCours;
        CommencerTexte.Text = enCours ? "Terminer le culte" : "Commencer le culte";
        CommencerBtn.Background = Pinceau(enCours ? "RougeLeger" : "Vert");
        CommencerBtn.BorderBrush = Pinceau(enCours ? "Direct" : "Vert");
        var texte = Pinceau(enCours ? "RougeTexte" : "SurVert");
        CommencerTexte.Foreground = texte;
        CommencerIcone.Fill = texte;
        CommencerIcone.Data = Geometry.Parse(enCours ? "M0,1 H10 V11 H0 Z" : "M0,0 L9,6 L0,12 Z");
        CommencerBtn.ToolTip = enCours
            ? "L'enregistrement du culte tourne : un clic l'arrête (le fichier est gardé)"
            : "Vérifie le projecteur, YouTube, le son et les caméras, puis lance l'enregistrement du culte dans le dossier choisi (Réglages › Son)";
    }

    /// <summary>Tout ce qu'on vérifie avant le culte (sans rien lancer).</summary>
    List<VerifCulte> VerifierCulte()
    {
        var l = new List<VerifCulte>();

        // le projecteur
        if (ecran != null) l.Add(new("Projecteur", "ok", $"L'écran de l'église est allumé ({NomEcranCourt()})."));
        else if (AttenteProjecteur) l.Add(new("Projecteur", "erreur", "Le projecteur est débranché : rebranchez le câble, l'image revient toute seule."));
        else l.Add(new("Projecteur", "attention", "La projection n'est pas ouverte.", "Ouvrir", MenuProjection));

        // YouTube
        var d = App.Direct;
        var reg = App.Donnees.Reglages.Direct;
        bool internet;
        try { internet = NetworkInterface.GetIsNetworkAvailable(); } catch (Exception) { internet = true; }
        string enDirect = d.EnDirect ? " En direct depuis " + d.Duree.ToString(@"h\:mm\:ss") + "." : "";
        if (!internet) l.Add(new("YouTube", "erreur", "Pas d'Internet : le direct ne pourra pas partir.", "Direct", OuvrirDirect));
        else if (reg.Mode == ReglagesDirect.Compte)
            l.Add(d.Compte.Connecte
                ? new VerifCulte("YouTube", "ok", "Le compte YouTube est relié" + (reg.Chaine.Length > 0 ? " : " + reg.Chaine : "") + "." + enDirect)
                : new VerifCulte("YouTube", "erreur", "Le compte YouTube n'est pas relié.", "Relier", OuvrirDirect));
        else
            l.Add(AdresseDirect.Construire(reg, d.Coffre) != null
                ? new VerifCulte("YouTube", "ok", "La clé de stream est enregistrée." + enDirect)
                : new VerifCulte("YouTube", "erreur", "Aucune clé de stream enregistrée.", "Régler", OuvrirDirect));

        // le son
        var audio = App.Audio;
        if (!audio.Running)
            l.Add(new("Son", "erreur", audio.ObsAbsent ? "Le moteur du son manque : Réglages › Son › Réparer." : "Le moteur du son est arrêté.", "Son", MontrerSon));
        else
        {
            var voix = audio.Config.Sons.Where(s => s.Genre is GenreSon.Voix or GenreSon.Table or GenreSon.Instrument).ToList();
            var debranches = voix.Where(s => audio.Debranche(s)).Select(s => s.Nom).ToList();
            var coupes = voix.Where(s => s.Coupe && !audio.Debranche(s)).Select(s => s.Nom).ToList();
            if (voix.Count == 0) l.Add(new("Son", "attention", "Aucun micro ni table de mixage n'est ajouté.", "Son", MontrerSon));
            else if (debranches.Count > 0) l.Add(new("Son", "erreur", "Débranché : " + string.Join(", ", debranches) + ".", "Son", MontrerSon));
            else if (coupes.Count > 0) l.Add(new("Son", "attention", "Coupé : " + string.Join(", ", coupes) + ".", "Son", MontrerSon));
            else l.Add(new("Son", "ok", "Branché : " + string.Join(", ", voix.Select(s => s.Nom)) + ".", "Son", MontrerSon, () => SonVivant(voix)));
        }

        // les caméras (MyVirtualCam à part : c'est l'image de l'antenne)
        var cams = R.Annonces.Where(a => a.Source is { Type: "camera" or "mycam", EstMyVirtualCam: false }).ToList();
        if (cams.Count == 0) l.Add(new("Caméras", "attention", "Aucune caméra dans les médias.", "Ajouter", () => { MontrerOnglet("annonces"); Ouvrir(MenuAjouterPeripherique(), DisplayPeripherique, System.Windows.Controls.Primitives.PlacementMode.Bottom); }));
        else
        {
            List<SourcesDisplay.Peripherique> branches;
            try { branches = SourcesDisplay.Peripheriques(); } catch (Exception) { branches = new(); }
            var telephones = App.Telephone.Cameras.Liste();
            var presentes = new List<string>();
            var absentes = new List<string>();
            foreach (var c in cams)
            {
                var s = c.Source!;
                bool la = s.Type == "camera" ? branches.Any(b => b.Id == s.Peripherique) : telephones.Any(t => t.DeviceId == s.Peripherique);
                (la ? presentes : absentes).Add(c.Titre);
            }
            string antenne = R.SourcePrincipaleStream is { } sp ? " Antenne : " + sp.Titre + "." : "";
            l.Add(absentes.Count == 0
                ? new VerifCulte("Caméras", "ok", "Prête" + (presentes.Count > 1 ? "s" : "") + " : " + string.Join(", ", presentes) + "." + antenne)
                : new VerifCulte("Caméras", "attention", "Pas branchée" + (absentes.Count > 1 ? "s" : "") + " : " + string.Join(", ", absentes) + "." + (presentes.Count > 0 ? " Prête" + (presentes.Count > 1 ? "s" : "") + " : " + string.Join(", ", presentes) + "." : ""),
                    "Médias", () => MontrerOnglet("annonces")));
        }
        return l;
    }

    /// <summary>Le son en direct dans le bilan : le micro le plus fort et son niveau (« bon niveau », « un peu faible »…).</summary>
    static (string Etat, string Detail) SonVivant(List<MyVerse.Audio.Son> voix)
    {
        var audio = App.Audio;
        MyVerse.Audio.Son? fort = null;
        double max = double.NegativeInfinity;
        foreach (var s in voix)
        {
            var (g, d) = audio.Level(s.Id);
            double n = Math.Max(g, d);
            if (!double.IsNaN(n) && n > max) { max = n; fort = s; }
        }
        if (fort == null) return ("ok", "Branché : " + string.Join(", ", voix.Select(s => s.Nom)) + ".");
        var e = audio.Etat(fort);
        string etat = e.Verdict switch { VerdictNiveau.TropFort => "attention", VerdictNiveau.Bon or VerdictNiveau.Faible => "ok", _ => "attention" };
        string mot = e.Verdict == VerdictNiveau.Silence ? "rien pour l'instant : parlez dans le micro" : e.Texte.ToLowerInvariant();
        return (etat, $"On entend : {fort.Nom} · {mot}.");
    }

    /// <summary>« Commencer le culte » : les vérifications, l'enregistrement dans le dossier choisi, puis le bilan.</summary>
    void CommencerCulte()
    {
        var l = VerifierCulte();
        var audio = App.Audio;
        if (!audio.Running) l.Add(new("Enregistrement", "erreur", "Impossible sans le moteur du son."));
        else
        {
            var p = audio.Config.Profiles.FirstOrDefault() ?? Audio.ActionsSon.NouveauCulte();
            if (!audio.Recorder.Active(p.Id)) Audio.ActionsSon.Basculer(p);
            string dossier = Audio.ActionsSon.Dossier(p);
            l.Add(new("Enregistrement", "ok", $"« {p.Name} » ({Audio.ActionsSon.Resume(p)}) enregistre dans {dossier}.", "Dossier", () => Audio.ActionsSon.OuvrirDossier(dossier),
                () => EnregistrementVivant(p)));
        }
        MajCommencer();
        MontrerBilanCulte(l);
    }

    /// <summary>L'enregistrement en direct dans le bilan : il démarre, il tourne, ou ce qui ne va pas.</summary>
    static (string Etat, string Detail) EnregistrementVivant(RecordingProfile p)
    {
        var r = App.Audio.Recorder;
        string dossier = Audio.ActionsSon.Dossier(p);
        return r.State(p.Id) switch
        {
            Audio.ChurchRecordingState.Error => ("erreur", "L'enregistrement ne démarre pas : " + r.Message(p.Id)),
            Audio.ChurchRecordingState.Preparing => ("attention", "L'enregistrement démarre…"),
            _ when r.Active(p.Id) => ("ok", $"« {p.Name} » enregistre dans {dossier}."),
            _ => ("attention", "L'enregistrement ne tourne pas encore."),
        };
    }

    void TerminerCulte(RecordingProfile p)
    {
        if (!Dialogue.Confirmer(this, "Terminer le culte ? L'enregistrement s'arrête et le fichier est gardé dans " + Audio.ActionsSon.Dossier(p) + ".", "Terminer le culte")) return;
        Audio.ActionsSon.Basculer(p);
        culteAffiche = null;
        MajCommencer();
    }

    /// <summary>Le bilan : une ligne par vérification (✓, ! ou ×), un bouton pour régler ce qui ne va pas.</summary>
    void MontrerBilanCulte(List<VerifCulte> verifs)
    {
        var w = new Window
        {
            Title = "Commencer le culte", Owner = this, Width = 580, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Pinceau("Page"), Foreground = Pinceau("Blanc"),
            FontFamily = (FontFamily)FindResource("Ui"), FontSize = 14,
        };
        w.SourceInitialized += (_, _) => Natif.TitreSombre(w, Palette.Actuelle.Sombre);
        var pile = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pile.Children.Add(new TextBlock { Text = "Commencer le culte", FontSize = 22, FontWeight = FontWeights.ExtraBold });
        var resume = new TextBlock { FontSize = 14, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) };
        pile.Children.Add(resume);
        var lignes = new List<(VerifCulte V, Border Rond, TextBlock Signe, TextBlock Detail)>();
        foreach (var v in verifs)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var signe = new TextBlock { FontSize = 15, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var rond = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = signe, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 14, 0) };
            g.Children.Add(rond);
            var textes = new StackPanel();
            textes.Children.Add(new TextBlock { Text = v.Titre, FontSize = 15, FontWeight = FontWeights.Bold });
            var detail = new TextBlock { Text = v.Detail, FontSize = 13, Foreground = Pinceau("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            textes.Children.Add(detail);
            Grid.SetColumn(textes, 1);
            g.Children.Add(textes);
            if (v.Action != null && v.Faire != null)
            {
                var faire = v.Faire;
                var b = new Button { Style = Style_("BtnPetit"), Content = v.Action, Height = 34, Padding = new Thickness(12, 0, 12, 0), FontSize = 13, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                // le bilan se ferme d'abord : le réglage s'ouvre dans la régie
                b.Click += (_, _) => { w.Close(); Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => faire())); };
                Grid.SetColumn(b, 2);
                g.Children.Add(b);
            }
            pile.Children.Add(g);
            lignes.Add((v, rond, signe, detail));
        }
        var fermer = new Button { Style = Style_("BtnOr"), Content = "C'est parti", Height = 44, Padding = new Thickness(22, 0, 22, 0), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), IsDefault = true, IsCancel = true };
        fermer.Click += (_, _) => w.Close();
        pile.Children.Add(fermer);
        w.Content = pile;

        void Peindre()
        {
            int aVoir = 0;
            foreach (var (v, rond, signe, detail) in lignes)
            {
                var (etat, texte) = v.Vivant?.Invoke() ?? (v.Etat, v.Detail);
                if (etat != "ok") aVoir++;
                detail.Text = texte;
                rond.Background = Pinceau(etat switch { "ok" => "Vert", "erreur" => "Danger", _ => "Or" });
                signe.Text = etat switch { "ok" => "✓", "erreur" => "×", _ => "!" };
                signe.Foreground = Pinceau(etat == "ok" ? "SurVert" : "Page");
            }
            resume.Text = aVoir == 0
                ? "Tout est prêt et l'enregistrement tourne. Bon culte !"
                : $"{aVoir} point{(aVoir > 1 ? "s" : "")} à vérifier. Le reste est prêt ; l'enregistrement tourne s'il est en vert.";
        }
        Peindre();
        var vivant = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        vivant.Tick += (_, _) => Peindre();
        vivant.Start();
        w.Closed += (_, _) => vivant.Stop();
        w.ShowDialog();
    }

    // ------------------------------------------------------------ pour le test automatique
    /// <summary>Les vérifications de « Commencer le culte » (sans rien lancer) : « Projecteur:ok|YouTube:erreur|… ».</summary>
    public string TestVerifierCulte => string.Join("|", VerifierCulte().Select(v => v.Titre + ":" + v.Etat));
    public string TestCommencerTexte => CommencerTexte.Text;
}
