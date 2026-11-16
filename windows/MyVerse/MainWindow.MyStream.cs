using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MyVerse.Ui.Audio;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.6.0 : les deux écrans de la colonne de droite. L'église (ce que montre le projecteur) et MyStream (ce qui part sur Zoom
/// et sur YouTube). 1.6.4 : MyStream montre sa source principale (choisie avec « Source »), quoi que montre l'église ;
/// « Envoyer dans le stream » (touche S, sous l'église) : MyStream montre aussi ce que montre l'église et le suit ;
/// « Source principale » (touche M) le ramène sur sa source principale. En haut, la pastille YouTube ouvre le direct.
/// </summary>
public partial class MainWindow
{
    /// <summary>Le lecteur qui donne l'image vivante de MyStream : celui de l'église, de l'écran de repos, ou celui de la source principale.</summary>
    SourcesDisplay? lecteurStream;
    /// <summary>MyStream montre le contenu épinglé (pas un contenu « Dans l'église »).</summary>
    bool streamDuo = true;
    EtatEcran etatStream = new();

    void InitMyStream()
    {
        StreamRetour.Click += (_, _) => RetourStream();
        // 1.6.8 : allumé (« MyStream suit l'église »), un clic l'arrête, comme « Source principale » (M)
        EnvoyerStream.Click += (_, _) => { if (R.StreamSuitEglise) RetourStream(); else EnvoyerDansStream(); };
        ToolTipService.SetShowOnDisabled(EnvoyerStream, true);
        StreamChoisir.Click += (_, _) => Ouvrir(MenuStream(), StreamChoisir, PlacementMode.Bottom);
        // 1.8.0 : l'église a sa source principale (son écran par défaut) et son bouton pour y revenir (Échap)
        EgliseRetour.Click += (_, _) => R.Effacer();
        EgliseChoisir.Click += (_, _) => Ouvrir(MenuEglise(), EgliseChoisir, PlacementMode.Bottom);
        DirectBtn.Click += (_, _) => OuvrirDirect();
        App.SourcesStream.Change += () =>
        {
            if (lecteurStream == App.SourcesStream) ImageStream();
            MajInfoStream();
        };
        App.Direct.Change += () => { MajDirect(); MajVeille(); };
        minuterieHorloge.Tick += (_, _) => { if (App.Direct.EnDirect) MajDirect(); };
        // 1.6.4 : les directs automatiques le disent dans Siloé Stream (deux minutes avant, au démarrage, à la fin)
        App.Planificateur.Annonce += Toast;
        App.Planificateur.Change += () => { MajDirect(); MajVeille(); };
        MajDirect();
        MajVeille();
    }

    /// <summary>
    /// Le lecteur de l'image vivante de MyStream. La source principale reste captée tant qu'elle est choisie : MyStream ne clignote pas
    /// quand l'église la montre puis la retire (une caméra n'est jamais ouverte deux fois : la capture est partagée).
    /// </summary>
    void ChoisirLecteurStream(EtatEcran stream)
    {
        var principale = R.SourcePrincipaleStream?.Source;
        App.SourcesStream.ChoisirSource(principale);
        var m = stream.Media;
        lecteurStream = m == null ? null
            : m == principale ? App.SourcesStream
            : App.Sources.Source == m ? App.Sources
            : App.SourcesDefaut.Source == m ? App.SourcesDefaut
            : null;
    }

    /// <summary>L'image vivante de MyStream, vers son aperçu et vers Zoom et YouTube.</summary>
    void ImageStream()
    {
        var b = lecteurStream?.Image;
        StreamRendu.ImageDisplay(b);
        App.MyStream?.Media(b);
    }

    /// <summary>Après chaque changement de l'écran : l'aperçu de MyStream, son moteur, et les mots sous l'aperçu.</summary>
    void MajStream(EtatEcran stream)
    {
        etatStream = stream;
        streamDuo = stream.Duo != null;
        App.MyStream?.Afficher(stream);
        StreamRendu.Afficher(stream);
        ImageStream();
        StreamApercu.Background = stream.Vide && stream.Duo == null ? Pinceau("Damier") : Brushes.Black;
        MajInfoStream();
        MajInfoEglise();
    }

    /// <summary>1.8.0 : sous l'église, sa source principale ; les boutons « Église » / « Stream » des tuiles s'allument.</summary>
    void MajInfoEglise()
    {
        // 2.0.2 : le menu de l'écran montre le nom de sa source principale
        EgliseSourceNom.Text = R.LibelleEcranDefaut;
        EgliseInfo.Text = R.Cible is CibleDefaut
            ? $"L'écran montre sa source principale : {R.LibelleEcranDefaut}."
            : "Échap ou « Revenir » : l'écran revient sur sa source principale.";
        MajBoutonsTuiles();
    }

    /// <summary>1.8.0 : le menu « Source » de l'église : noir, fond seul, ou un média.</summary>
    ContextMenu MenuEglise()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Source principale de l'église", IsEnabled = false, FontWeight = FontWeights.Bold });
        var actuel = R.EcranDefaut;
        var actuelMedia = R.AnnoncePrincipaleEglise;
        foreach (var (id, nom) in new[] { ("noir", "Noir"), ("fond", "Fond seul") })
        {
            var item = Article(nom, () => { R.ChoisirEcranDefaut(id); Toast($"Source principale de l'église : {nom}."); });
            item.IsCheckable = true;
            item.IsChecked = actuel == id;
            menu.Items.Add(item);
        }
        var medias = R.Annonces.Where(a => a.Source is { Type: "image" or "camera" or "mycam" or "fenetre" or "video" }).ToList();
        if (medias.Count > 0) menu.Items.Add(new Separator());
        foreach (var a in medias)
        {
            var id = a.Id;
            var item = Article(a.Titre, () => DefinirPrincipaleEglise(id));
            item.IsCheckable = true;
            item.IsChecked = id == actuelMedia;
            item.ToolTip = a.Description;
            menu.Items.Add(item);
        }
        return menu;
    }

    void DefinirPrincipaleEglise(string id)
    {
        var a = R.Annonces.FirstOrDefault(x => x.Id == id);
        if (a == null) return;
        if (R.ChoisirPrincipaleEglise(id)) Toast($"Source principale de l'église : « {a.Titre} » (Échap pour y revenir).");
        MajInfoEglise();
    }

    void DefinirPrincipaleStream(string id)
    {
        var a = R.Annonces.FirstOrDefault(x => x.Id == id);
        if (a == null) return;
        if (!Regie.SourceStreamPossible(a.Source)) { Toast($"« {a.Titre} » ne peut pas aller sur MyStream (dans l'église seulement)."); return; }
        R.ChoisirSourceStream(id);
        Toast($"MyStream montre « {a.Titre} », quoi que montre l'église.");
        MajInfoEglise();
    }

    /// <summary>
    /// Sous MyStream : ce qu'il montre (« Caméra · source principale », « suit l'église » ou noir) ; les boutons « Envoyer dans le stream »
    /// (allumé quand MyStream suit l'église) et « Source principale ».
    /// </summary>
    void MajInfoStream()
    {
        var p = R.SourcePrincipaleStream;
        bool suit = R.StreamSuitEglise;
        StreamRetour.IsEnabled = p != null || suit;
        EnvoyerStream.IsEnabled = true;
        string retour = p != null ? "revenir sur " + p.Titre : "arrêter (antenne noire)";
        // 2.0.2 : le menu de l'antenne montre le nom de sa source principale
        StreamSourceNom.Text = p != null ? p.Titre : "Aucune";
        // 2.0.2, design « Conducteur » : « ENVOYER À L'ANTENNE » en bordeaux ; quand l'antenne suit l'écran, le bouton le dit (un clic ou M : l'antenne revient)
        var texte = Pinceau(suit ? "RougeTexte" : "SurAntenne");
        if (suit)
        {
            EnvoyerStream.Background = Pinceau("RougeLeger");
            EnvoyerStream.BorderBrush = Pinceau("Antenne");
            EnvoyerStreamTexte.Text = "L'ANTENNE SUIT L'ÉCRAN";
            EnvoyerStreamSous.Text = "Un clic (ou M) : " + retour;
            EnvoyerStream.ToolTip = "L'antenne (YouTube, Zoom, MyStream) montre ce qui est à l'écran et le suit. Cliquez ici (ou M) pour " + retour + ".";
            StreamRetour.BorderBrush = Pinceau("Direct");
        }
        else
        {
            EnvoyerStream.Background = Pinceau("Antenne");
            EnvoyerStream.BorderBrush = Pinceau("Antenne");
            EnvoyerStreamTexte.Text = "ENVOYER À L'ANTENNE";
            EnvoyerStreamSous.Text = "YouTube · Zoom · MyStream";
            EnvoyerStream.ToolTip = "Touche S : ce qui est À L'ÉCRAN part aussi à l'antenne (YouTube, Zoom, MyStream) et la suit, jusqu'à « Revenir » de l'antenne (M)";
            StreamRetour.ClearValue(BorderBrushProperty);
        }
        EnvoyerStreamTexte.Foreground = EnvoyerStreamSous.Foreground = EnvoyerStreamToucheTexte.Foreground = texte;
        EnvoyerStreamIcone.Stroke = EnvoyerStreamTouche.BorderBrush = texte;
        if (suit)
        {
            StreamInfo.Text = $"L'antenne suit l'écran : ce qu'il montre part sur YouTube, Zoom et MyStream. Un clic sur le bouton (ou M) : {retour}.";
            return;
        }
        if (p == null)
        {
            // 1.6.6 : rien de choisi pour Internet : MyStream reste noir (les paroles et les versets restent dans l'église)
            StreamInfo.Text = etatStream is { Mode: "media", Media: not null }
                ? "L'antenne montre une source « MyStream seulement ». Sans source principale, elle est noire le reste du temps."
                : R.AucuneSourceStream
                    ? "Aucune source : l'antenne reste noire. Choisissez sa source principale, ou « ENVOYER À L'ANTENNE »."
                    : R.SourcesStreamPossibles.Count == 0
                        ? "Pas de source : l'antenne reste noire. Ajoutez une caméra (Bibliothèque › Médias) : montrée, elle devient sa source principale."
                        : "Pas de source principale : l'antenne reste noire. Montrez une caméra (elle le devient), ou choisissez sa source principale.";
            return;
        }
        string statut = App.SourcesStream.Statut is { Length: > 0 } s && App.SourcesStream.Image == null ? " (" + s.TrimEnd('.') + ")" : "";
        StreamInfo.Text = etatStream is { Mode: "media" } e && e.Media != null && e.Media != p.Source
            ? $"L'antenne montre une source « MyStream seulement ». Source principale : {p.Titre}{statut}."
            : $"L'antenne montre {p.Titre}{statut}, quoi que montre l'écran.";
    }

    /// <summary>
    /// Touche M ou « Source principale » : MyStream ne suit plus l'église et revient sur sa source principale (noir sans source
    /// principale) ; l'église garde ce qu'elle montre. Sans source principale ni église suivie : le menu « Source ».
    /// </summary>
    void RetourStream()
    {
        bool suivait = R.StreamSuitEglise;
        if (!suivait && R.SourcePrincipaleStream == null)
        {
            Ouvrir(MenuStream(), StreamChoisir, PlacementMode.Bottom);
            return;
        }
        if (!R.StreamPrincipale()) return;
        var p = R.SourcePrincipaleStream;
        Toast(!suivait ? $"MyStream montre déjà « {p?.Titre} »."
            : p != null ? $"MyStream : « {p.Titre} ». L'église garde ce qu'elle montre ; MyStream ne la suit plus."
            : "MyStream ne suit plus l'église : sans source principale, il reste noir.");
    }

    /// <summary>
    /// Touche S ou « Envoyer dans le stream » : ce que montre l'église part aussi sur MyStream, et la suite aussi, jusqu'à « Source principale ».
    /// </summary>
    void EnvoyerDansStream()
    {
        string retour = R.SourcePrincipaleStream is { } p ? "revenir sur " + p.Titre : "arrêter";
        if (R.StreamSuitEglise) { Toast($"MyStream suit déjà l'église. Re-cliquez le bouton (ou M) pour {retour}."); return; }
        if (R.EnvoyerDansStream())
            Toast(R.Etat is { Mode: "vide" or "fond" } || R.Cible is CibleDefaut
                ? $"MyStream suit l'église : ce que vous montrerez partira aussi sur Zoom et YouTube. Re-cliquez (ou M) pour {retour}."
                : $"Envoyé dans le stream : MyStream montre ce que montre l'église, et la suit. Re-cliquez (ou M) pour {retour}.");
    }

    /// <summary>Le menu « Source » : la source principale de MyStream (ce qu'il montre), aucune, le direct.</summary>
    ContextMenu MenuStream()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Source principale de MyStream", IsEnabled = false, FontWeight = FontWeights.Bold });
        var actuelle = R.SourcePrincipaleStream?.Id;
        var possibles = R.SourcesStreamPossibles;
        foreach (var a in possibles)
        {
            var id = a.Id;
            var item = Article(a.Titre, () => { R.ChoisirSourceStream(id); Toast($"MyStream montre « {a.Titre} », quoi que montre l'église."); });
            item.IsCheckable = true;
            item.IsChecked = id == actuelle;
            item.ToolTip = a.Description;
            menu.Items.Add(item);
        }
        if (possibles.Count == 0) menu.Items.Add(new MenuItem { Header = "Aucune caméra : ajoutez-en une dans Bibliothèque › Médias", IsEnabled = false });
        var aucune = Article("Aucune (MyStream reste noir)", () => { R.ChoisirSourceStream(null); Toast("MyStream reste noir : « Envoyer dans le stream » pour y mettre l'église, ou choisissez une source."); });
        aucune.IsCheckable = true;
        aucune.IsChecked = actuelle == null && R.AucuneSourceStream;
        menu.Items.Add(aucune);
        menu.Items.Add(new Separator());
        menu.Items.Add(Article("Le direct sur YouTube…", OuvrirDirect));
        return menu;
    }

    // ------------------------------------------------------------ le direct (pastille en haut, fenêtre)
    DirectWindow? fenetreDirect;

    void OuvrirDirect()
    {
        if (fenetreDirect is { IsLoaded: true }) { fenetreDirect.Activate(); return; }
        fenetreDirect = new DirectWindow { Owner = this };
        fenetreDirect.Closed += (_, _) => fenetreDirect = null;
        fenetreDirect.Show();
    }

    /// <summary>La pastille « YouTube » : grise à l'arrêt, rouge en direct (avec la durée), ambre pendant une reconnexion.</summary>
    void MajDirect()
    {
        var d = App.Direct;
        var duree = d.Duree;
        string temps = $"{(int)duree.TotalHours}:{duree.Minutes:00}:{duree.Seconds:00}";
        switch (d.Etat)
        {
            case EtatDirect.EnDirect:
                DirectTexte.Text = "En direct " + temps;
                DirectBtn.Background = Pinceau("Direct"); DirectBtn.BorderBrush = Pinceau("Direct");
                DirectTexte.Foreground = Brushes.White; DirectPoint.Fill = Brushes.White;
                StreamPastilleTexte.Text = "● En direct";
                StreamPastille.Background = Pinceau("Direct"); StreamPastilleTexte.Foreground = Brushes.White;
                break;
            case EtatDirect.Reconnexion:
                DirectTexte.Text = "Reconnexion…";
                DirectBtn.Background = Pinceau("Ensuite"); DirectBtn.BorderBrush = Pinceau("Ensuite");
                DirectTexte.Foreground = Pinceau("SurEnsuite"); DirectPoint.Fill = Pinceau("SurEnsuite");
                StreamPastilleTexte.Text = "Reconnexion…";
                StreamPastille.Background = Pinceau("Ensuite"); StreamPastilleTexte.Foreground = Pinceau("SurEnsuite");
                break;
            default:
                DirectTexte.Text = d.Etat is EtatDirect.Preparation ? "Démarrage…" : d.Etat is EtatDirect.Arret ? "Arrêt…" : "YouTube";
                DirectBtn.ClearValue(BackgroundProperty); DirectBtn.ClearValue(BorderBrushProperty);
                DirectTexte.ClearValue(TextBlock.ForegroundProperty);
                DirectPoint.Fill = Pinceau(d.Etat == EtatDirect.Erreur ? "Danger" : "Brume2");
                StreamPastilleTexte.Text = "YouTube · Zoom · MyStream";
                StreamPastille.Background = Pinceau("StreamLeger"); StreamPastilleTexte.Foreground = Pinceau("Stream");
                break;
        }
        var prochain = App.Planificateur?.Resume(DateTime.Now) ?? "";
        DirectBtn.ToolTip = (d.Message.Length > 0 ? d.Message : "Le direct sur YouTube : MyStream et le son partent sur Internet")
            + (prochain.Length > 0 ? "\n" + prochain : "");
    }

    // ------------------------------------------------------------ pour le test automatique
    public EtatEcran TestEtatStream => etatStream;
    /// <summary>1.6.6 : où est le texte sur l'écran de l'église (dessin 1920 × 1080).</summary>
    public Rect TestZoneTexteEglise => EgliseRendu.TestZoneTexte;
    public bool TestTexteEntierEglise => EgliseRendu.TestTexteEntier;
    public string TestInfoStream => StreamInfo.Text;
    public string TestDirectTexte => DirectTexte.Text;
    public void TestRetourStream() => RetourStream();
    public void TestEnvoyerStream() => EnvoyerDansStream();
    public string TestEnvoyerTexte => EnvoyerStreamTexte.Text;
    public void TestOuvrirDirect() => OuvrirDirect();
    public DirectWindow? TestFenetreDirect => fenetreDirect;
    public int TestMenuStream() => MenuStream().Items.Count;
}
