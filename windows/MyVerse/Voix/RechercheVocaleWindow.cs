using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MyVerse.Audio;
using MyVerse.Ui.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Whisper.net;

namespace MyVerse.Ui.Voix;

/// <summary>Les réglages de la recherche vocale (micro, langue, modèle, durée, écoute continue).</summary>
public sealed class ReglagesVoix
{
    /// <summary>L'identifiant Windows du micro ; vide : celui de Windows par défaut ; « ordinateur » : ce que joue l'ordinateur.</summary>
    public string Peripherique { get; set; } = "";
    /// <summary>« auto » ou un code de langue de Whisper (fr, en, sw, ln…).</summary>
    public string Langue { get; set; } = "auto";
    /// <summary>« base » (rapide, 142 Mo) ou « small » (plus précis, 466 Mo).</summary>
    public string Modele { get; set; } = "base";
    public int Secondes { get; set; } = 10;
    public bool Continue { get; set; }

    static string Fichier => Path.Combine(App.Donnees.Dossier, "voix.json");
    public static ReglagesVoix Lire()
    {
        try { if (File.Exists(Fichier)) return JsonSerializer.Deserialize<ReglagesVoix>(File.ReadAllText(Fichier)) ?? new(); }
        catch (Exception e) { Journal.Erreur("réglages de la recherche vocale", e); }
        return new();
    }
    public void Sauver()
    {
        try { File.WriteAllText(Fichier, JsonSerializer.Serialize(this)); } catch (Exception e) { Journal.Erreur("réglages de la recherche vocale", e); }
    }
}

/// <summary>
/// Le modèle de reconnaissance (Whisper de OpenAI, version whisper.cpp, licence MIT) : téléchargé une seule fois (avec l'accord de
/// l'utilisateur) dans le dossier des données ; ensuite tout se fait sur le PC, sans Internet.
/// </summary>
public static class ModeleVoix
{
    public static string Dossier => Path.Combine(App.Donnees.Dossier, "modeles");
    public static string Chemin(string nom) => Path.Combine(Dossier, $"ggml-{nom}.bin");
    public static string Taille(string nom) => nom == "small" ? "466 Mo" : "142 Mo";
    public static bool Present(string nom) => File.Exists(Chemin(nom)) && new FileInfo(Chemin(nom)).Length > 50_000_000;

    static WhisperFactory? usine;
    static string usineNom = "";
    static readonly object verrou = new();

    public static WhisperFactory Usine(string nom)
    {
        lock (verrou)
        {
            if (usine != null && usineNom == nom) return usine;
            usine?.Dispose();
            usine = WhisperFactory.FromPath(Chemin(nom));
            usineNom = nom;
            // pas de WhisperFactory.GetRuntimeInfo() : dans Whisper.net 1.8.1, il corrompt la mémoire (plantage 0xC0000374)
            Journal.Info("recherche vocale : modèle " + nom + " chargé");
            return usine;
        }
    }

    /// <summary>Télécharge le modèle depuis le dépôt officiel de whisper.cpp (Hugging Face), dans un fichier à part puis remplacé d'un coup.</summary>
    public static async Task TelechargerAsync(string nom, IProgress<double> progres, CancellationToken annuler)
    {
        Directory.CreateDirectory(Dossier);
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{nom}.bin";
        var partiel = Chemin(nom) + ".part";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SiloeStream/" + App.Version);
        using var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, annuler);
        r.EnsureSuccessStatusCode();
        long total = r.Content.Headers.ContentLength ?? -1;
        await using (var source = await r.Content.ReadAsStreamAsync(annuler))
        await using (var f = new FileStream(partiel, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            var t = new byte[1 << 20];
            long lu = 0;
            int n;
            while ((n = await source.ReadAsync(t, annuler)) > 0)
            {
                await f.WriteAsync(t.AsMemory(0, n), annuler);
                lu += n;
                if (total > 0) progres.Report((double)lu / total);
            }
            if (total > 0 && lu != total) throw new IOException("Téléchargement incomplet : recommencez.");
        }
        File.Move(partiel, Chemin(nom), true);
    }

    /// <summary>Le texte entendu (16 kHz mono).</summary>
    public static Task<string> ReconnaitreAsync(float[] son, string modele, string langue, string mode, CancellationToken annuler) => Task.Run(async () =>
    {
        // Whisper veut au moins une seconde de son
        if (son.Length < 16000) Array.Resize(ref son, 16000);
        var b = Usine(modele).CreateBuilder().WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 8)).WithNoContext().WithLanguage(langue);
        if (mode == RechercheVocale.Bible) b = b.WithPrompt("Lecture de la Bible : Jean 3:16, Psaume 23, Romains 8:28, Matthieu 5:3.");
        using var p = b.Build();
        var sb = new StringBuilder();
        await foreach (var s in p.ProcessAsync(son, annuler)) sb.Append(s.Text).Append(' ');
        return sb.ToString().Trim();
    }, annuler);
}

/// <summary>L'écoute d'un micro (ou de ce que joue l'ordinateur), gardée en mémoire en mono.</summary>
public sealed class EcouteMicro : IDisposable
{
    readonly IWaveIn capture;
    readonly WaveFormat format;
    readonly List<float> mono = new();
    public double NiveauDb { get; private set; } = -90;
    public string? Erreur { get; private set; }

    public EcouteMicro(string peripherique)
    {
        if (peripherique == "ordinateur") capture = new WasapiLoopbackCapture();
        else
        {
            var e = new MMDeviceEnumerator();
            var d = peripherique.Length > 0 ? e.GetDevice(peripherique) : e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            capture = new WasapiCapture(d);
        }
        format = capture.WaveFormat;
        capture.DataAvailable += (_, a) => Recevoir(a.Buffer, a.BytesRecorded);
        capture.RecordingStopped += (_, a) => { if (a.Exception != null) Erreur = a.Exception.Message; };
        capture.StartRecording();
    }

    public double Secondes { get { lock (mono) return (double)mono.Count / Math.Max(1, format.SampleRate); } }

    static readonly Guid Flottant = new("00000003-0000-0010-8000-00aa00389b71");

    void Recevoir(byte[] b, int n)
    {
        int ch = Math.Max(1, format.Channels), octets = Math.Max(1, format.BitsPerSample / 8);
        bool flottant = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible x && x.SubFormat == Flottant;
        int images = n / (octets * ch);
        var t = new float[images];
        float crete = 0;
        for (int i = 0; i < images; i++)
        {
            float somme = 0;
            for (int c = 0; c < ch; c++)
            {
                int o = (i * ch + c) * octets;
                somme += octets switch
                {
                    4 when flottant => BitConverter.ToSingle(b, o),
                    4 => BitConverter.ToInt32(b, o) / 2147483648f,
                    3 => (b[o] | b[o + 1] << 8 | (sbyte)b[o + 2] << 16) / 8388608f,
                    2 => BitConverter.ToInt16(b, o) / 32768f,
                    _ => 0,
                };
            }
            t[i] = somme / ch;
            crete = Math.Max(crete, Math.Abs(t[i]));
        }
        lock (mono) mono.AddRange(t);
        NiveauDb = crete > 1e-5 ? 20 * Math.Log10(crete) : -90;
    }

    /// <summary>Le son entendu, en 16 kHz mono (ce que Whisper attend).</summary>
    public float[] Son16k()
    {
        float[] s;
        lock (mono) s = mono.ToArray();
        int de = Math.Max(1, format.SampleRate);
        if (de == 16000) return s;
        // un passe-bas simple (moyenne glissante) puis une interpolation linéaire
        int fenetre = Math.Max(1, (int)Math.Round(de / 16000.0));
        var f = s;
        if (fenetre > 1)
        {
            f = new float[s.Length];
            double somme = 0;
            for (int i = 0; i < s.Length; i++)
            {
                somme += s[i];
                if (i >= fenetre) somme -= s[i - fenetre];
                f[i] = (float)(somme / Math.Min(i + 1, fenetre));
            }
        }
        int n = (int)((long)s.Length * 16000 / de);
        var r = new float[n];
        double pas = de / 16000.0;
        for (int i = 0; i < n; i++)
        {
            double x = i * pas;
            int k = (int)x;
            double a = x - k;
            r[i] = k + 1 < f.Length ? (float)(f[k] * (1 - a) + f[k + 1] * a) : f[Math.Min(k, f.Length - 1)];
        }
        return r;
    }

    public void Dispose()
    {
        try { capture.StopRecording(); } catch (Exception) { }
        try { capture.Dispose(); } catch (Exception) { }
    }
}

/// <summary>
/// Siloé Stream 2.2.0 : la recherche vocale. Le petit micro de la barre de recherche demande « Chant ou Bible ? », écoute (le chant
/// de l'assemblée, ou la prédication), reconnaît ce qui est dit sur le PC (Whisper) et propose le chant ou le verset : un clic y va,
/// le montre ou l'ajoute au programme. L'écoute continue suit la prédication (un nouveau verset à chaque fois qu'il est cité).
/// </summary>
public sealed class RechercheVocaleWindow : FenetreDialogue
{
    static Brush P(string c) => MainWindow.Pinceau(c);
    static Style S(string c) => MainWindow.Style_(c);

    readonly ReglagesVoix r = ReglagesVoix.Lire();
    readonly StackPanel choix = new(), ecoute = new(), modele = new(), resultats = new();
    readonly TextBlock etat = new() { FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock entendu = new() { FontSize = 13.5, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6) };
    readonly ProgressBar temps = new() { Height = 6, Margin = new Thickness(0, 10, 0, 0) };
    readonly ProgressBar telechargement = new() { Height = 8, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock texteModele = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    readonly Vumetre vumetre = new(10) { Margin = new Thickness(0, 12, 0, 0) };
    readonly Button maintenant, arreter, encore, changerMode;
    readonly CheckBox continu;
    readonly DispatcherTimer tic = new() { Interval = TimeSpan.FromMilliseconds(50) };
    string mode = RechercheVocale.Chant;
    EcouteMicro? micro;
    CancellationTokenSource? travail;
    bool reconnaissance;

    public RechercheVocaleWindow()
    {
        Title = "Recherche vocale";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 60);
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;
        Background = P("Page");
        var racine = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // ------------------------------------------------------------ « Chant ou Bible ? »
        choix.Children.Add(new TextBlock { Text = "Que faut-il reconnaître ?", Style = S("Titre2") });
        choix.Children.Add(new TextBlock { Text = "Siloé Stream écoute, reconnaît ce qui est dit ou chanté (sur ce PC, sans Internet) et trouve le chant ou le verset.", Style = S("Muet"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) });
        var deux = new UniformGrid { Columns = 2 };
        Button Grand(string titre, string sous, Geometry icone, string couleur, string m)
        {
            var pile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var i = SonUi.Icone(icone, P(couleur), 34, 2);
            i.HorizontalAlignment = HorizontalAlignment.Center;
            pile.Children.Add(i);
            pile.Children.Add(new TextBlock { Text = titre, FontSize = 18, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 2) });
            pile.Children.Add(new TextBlock { Text = sous, FontSize = 12.5, Foreground = P("Brume"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 200 });
            var b = new Button { Style = S("Btn"), Content = pile, Height = 150, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10) };
            b.Click += (_, _) => Commencer(m);
            return b;
        }
        deux.Children.Add(Grand("Chant", "Les paroles chantées par l'assemblée ou la chorale", SonUi.Musique, "TypeChant", RechercheVocale.Chant));
        var bible = Grand("Bible", "Une référence citée (« Jean 3, 16 ») ou un verset lu pendant la prédication", (Geometry)FindResource("IconeBible"), "TypeLecture", RechercheVocale.Bible);
        bible.Margin = new Thickness(8, 0, 0, 0);
        deux.Children.Add(bible);
        choix.Children.Add(deux);
        racine.Children.Add(choix);

        // ------------------------------------------------------------ le modèle à télécharger (une fois)
        modele.Visibility = Visibility.Collapsed;
        modele.Children.Add(new TextBlock { Text = "Un téléchargement, une seule fois", Style = S("Titre2") });
        modele.Children.Add(texteModele);
        var rang = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var rapide = new Button { Style = S("BtnOr"), Content = "Télécharger (rapide, 142 Mo)", Height = 40, Padding = new Thickness(14, 0, 14, 0) };
        var precis = new Button { Style = S("Btn"), Content = "Plus précis (466 Mo)", Height = 40, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(10, 0, 0, 0) };
        rapide.Click += (_, _) => Telecharger("base");
        precis.Click += (_, _) => Telecharger("small");
        rang.Children.Add(rapide);
        rang.Children.Add(precis);
        modele.Children.Add(rang);
        modele.Children.Add(telechargement);
        racine.Children.Add(modele);

        // ------------------------------------------------------------ l'écoute
        ecoute.Visibility = Visibility.Collapsed;
        ecoute.Children.Add(etat);
        ecoute.Children.Add(vumetre);
        ecoute.Children.Add(temps);
        var boutons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        maintenant = new Button { Style = S("BtnOr"), Content = "Chercher maintenant", Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8) };
        arreter = new Button { Style = S("Btn"), Content = "Arrêter", Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8) };
        encore = new Button { Style = S("BtnOr"), Content = "Écouter encore", Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8) };
        changerMode = new Button { Style = S("Btn"), Height = 38, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 8) };
        maintenant.Click += (_, _) => FinEcoute();
        arreter.Click += (_, _) => { r.Continue = false; continu!.IsChecked = false; Arreter(); etat.Text = "Écoute arrêtée."; MajBoutons(); };
        encore.Click += (_, _) => Commencer(mode);
        changerMode.Click += (_, _) => Commencer(mode == RechercheVocale.Chant ? RechercheVocale.Bible : RechercheVocale.Chant);
        boutons.Children.Add(maintenant);
        boutons.Children.Add(arreter);
        boutons.Children.Add(encore);
        boutons.Children.Add(changerMode);
        ecoute.Children.Add(boutons);
        ecoute.Children.Add(entendu);
        ecoute.Children.Add(resultats);
        racine.Children.Add(ecoute);

        // ------------------------------------------------------------ réglages
        var reglages = new Expander { Header = "Réglages de l'écoute", Margin = new Thickness(0, 16, 0, 0), FontSize = 13.5 };
        var pr = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        pr.Children.Add(Libelle("Micro"));
        var micros = new ComboBox { MinHeight = 34 };
        micros.Items.Add(new ComboBoxItem { Content = "Le micro de Windows par défaut", Tag = "" });
        try { foreach (var d in PeripheriquesAudio.Lister().Entrees) micros.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Id }); } catch (Exception e) { Journal.Erreur("micros", e); }
        micros.Items.Add(new ComboBoxItem { Content = "Ce que joue l'ordinateur (vidéo, YouTube…)", Tag = "ordinateur" });
        micros.SelectedIndex = Math.Max(0, micros.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == r.Peripherique));
        micros.SelectionChanged += (_, _) => { r.Peripherique = (string)((ComboBoxItem)micros.SelectedItem).Tag; r.Sauver(); };
        pr.Children.Add(micros);
        pr.Children.Add(Libelle("Langue"));
        var langues = new ComboBox { MinHeight = 34 };
        foreach (var (code, nom) in new[] { ("auto", "Reconnue toute seule"), ("fr", "Français"), ("en", "Anglais"), ("sw", "Swahili"), ("ln", "Lingala"), ("es", "Espagnol"), ("pt", "Portugais"), ("ht", "Créole haïtien") })
            langues.Items.Add(new ComboBoxItem { Content = nom, Tag = code });
        langues.SelectedIndex = Math.Max(0, langues.Items.Cast<ComboBoxItem>().ToList().FindIndex(i => (string)i.Tag == r.Langue));
        langues.SelectionChanged += (_, _) => { r.Langue = (string)((ComboBoxItem)langues.SelectedItem).Tag; r.Sauver(); };
        pr.Children.Add(langues);
        pr.Children.Add(new TextBlock { Text = "Kirundi, kinyarwanda… : laissez « Reconnue toute seule » ; la recherche tolère les mots écrits « à l'oreille ».", FontSize = 12, Foreground = P("Brume"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        pr.Children.Add(Libelle("Précision"));
        var precision = new ComboBox { MinHeight = 34 };
        precision.Items.Add(new ComboBoxItem { Content = "Rapide (modèle de 142 Mo)", Tag = "base" });
        precision.Items.Add(new ComboBoxItem { Content = "Plus précis, plus lent (466 Mo)", Tag = "small" });
        precision.SelectedIndex = r.Modele == "small" ? 1 : 0;
        precision.SelectionChanged += (_, _) => { r.Modele = (string)((ComboBoxItem)precision.SelectedItem).Tag; r.Sauver(); };
        pr.Children.Add(precision);
        var duree = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 10, 0, 4) };
        var curseur = new Slider { Minimum = 4, Maximum = 25, Value = Math.Clamp(r.Secondes, 4, 25), SmallChange = 1, IsSnapToTickEnabled = true, TickFrequency = 1 };
        void Duree() => duree.Text = $"Durée d'écoute : {curseur.Value:0} s";
        Duree();
        curseur.ValueChanged += (_, _) => { r.Secondes = (int)curseur.Value; r.Sauver(); Duree(); };
        pr.Children.Add(duree);
        pr.Children.Add(curseur);
        continu = new CheckBox { Content = "Écoute continue (suivre la prédication : un nouveau résultat à chaque écoute)", IsChecked = r.Continue, Margin = new Thickness(0, 12, 0, 0) };
        continu.Checked += (_, _) => { r.Continue = true; r.Sauver(); };
        continu.Unchecked += (_, _) => { r.Continue = false; r.Sauver(); };
        pr.Children.Add(continu);
        reglages.Content = pr;
        racine.Children.Add(reglages);

        Content = new ScrollViewer { Content = racine, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        etat.Foreground = P("Blanc");
        entendu.Foreground = P("Brume");
        texteModele.Foreground = P("Brume");
        tic.Tick += (_, _) => Battre();
        Closed += (_, _) => { tic.Stop(); Arreter(); };
        MajBoutons();
    }

    static TextBlock Libelle(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = P("Brume"), Margin = new Thickness(0, 10, 0, 4) };

    /// <summary>Repart de la question « Chant ou Bible ? ».</summary>
    public void Demander()
    {
        Arreter();
        choix.Visibility = Visibility.Visible;
        ecoute.Visibility = modele.Visibility = Visibility.Collapsed;
    }

    void MajBoutons()
    {
        bool ecoutant = micro != null;
        maintenant.Visibility = arreter.Visibility = ecoutant || reconnaissance ? Visibility.Visible : Visibility.Collapsed;
        maintenant.IsEnabled = ecoutant;
        encore.Visibility = !ecoutant && !reconnaissance ? Visibility.Visible : Visibility.Collapsed;
        changerMode.Content = mode == RechercheVocale.Chant ? "Chercher un verset à la place" : "Chercher un chant à la place";
        changerMode.Visibility = encore.Visibility;
        vumetre.Visibility = temps.Visibility = ecoutant ? Visibility.Visible : Visibility.Collapsed;
    }

    void Commencer(string m)
    {
        mode = m;
        Arreter();
        choix.Visibility = Visibility.Collapsed;
        if (!ModeleVoix.Present(r.Modele) && !ModeleVoix.Present(r.Modele == "base" ? "small" : "base"))
        {
            texteModele.Text = "Pour reconnaître la voix sur ce PC (ensuite sans Internet, rien n'est envoyé nulle part), Siloé Stream télécharge une fois le modèle de reconnaissance Whisper (whisper.cpp, licence MIT) depuis son dépôt officiel sur Hugging Face. Le modèle rapide suffit d'habitude ; le plus précis reconnaît mieux le chant.";
            modele.Visibility = Visibility.Visible;
            ecoute.Visibility = Visibility.Collapsed;
            return;
        }
        if (!ModeleVoix.Present(r.Modele)) r.Modele = r.Modele == "base" ? "small" : "base";
        modele.Visibility = Visibility.Collapsed;
        ecoute.Visibility = Visibility.Visible;
        resultats.Children.Clear();
        entendu.Text = "";
        try { micro = new EcouteMicro(r.Peripherique); }
        catch (Exception e)
        {
            Journal.Erreur("recherche vocale : micro", e);
            etat.Text = "Le micro ne s'ouvre pas : " + e.Message + " (choisissez-en un autre dans les réglages ci-dessous).";
            etat.Foreground = P("Danger");
            MajBoutons();
            return;
        }
        etat.Foreground = P("Blanc");
        etat.Text = mode == RechercheVocale.Chant ? "J'écoute le chant…" : "J'écoute la prédication…";
        temps.Maximum = r.Secondes;
        temps.Value = 0;
        tic.Start();
        MajBoutons();
    }

    void Battre()
    {
        if (micro == null) return;
        vumetre.Niveau(micro.NiveauDb);
        double s = micro.Secondes;
        temps.Value = Math.Min(s, temps.Maximum);
        etat.Text = (mode == RechercheVocale.Chant ? "J'écoute le chant… " : "J'écoute la prédication… ") + $"{Math.Max(0, r.Secondes - s):0} s";
        if (micro.Erreur != null) { etat.Text = "Le micro s'est arrêté : " + micro.Erreur; Arreter(); MajBoutons(); return; }
        if (s >= r.Secondes) FinEcoute();
    }

    void Arreter()
    {
        tic.Stop();
        micro?.Dispose();
        micro = null;
        travail?.Cancel();
        travail = null;
        reconnaissance = false;
    }

    async void FinEcoute()
    {
        if (micro == null) return;
        tic.Stop();
        var son = micro.Son16k();
        micro.Dispose();
        micro = null;
        reconnaissance = true;
        MajBoutons();
        etat.Text = "Je reconnais…";
        travail = new CancellationTokenSource();
        var jeton = travail.Token;
        string texte;
        try { texte = await ModeleVoix.ReconnaitreAsync(son, r.Modele, r.Langue, mode, jeton); }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Journal.Erreur("recherche vocale : reconnaissance", e);
            reconnaissance = false;
            etat.Text = "La reconnaissance a échoué : " + e.Message;
            etat.Foreground = P("Danger");
            MajBoutons();
            return;
        }
        if (jeton.IsCancellationRequested) return;
        reconnaissance = false;
        var propre = RechercheVocale.Nettoyer(texte);
        entendu.Text = propre.Length > 0 ? "Entendu : « " + propre + " »" : "Rien de clair n'a été entendu (trop de bruit, ou personne ne parlait).";
        var rec = App.Donnees.Reglages.Recueil;
        var l = propre.Length == 0 ? new List<ResultatCommande>() : await Task.Run(() => RechercheVocale.Chercher(App.Regie, propre, mode, rec.Length > 0 ? rec : null));
        Journal.Info($"recherche vocale ({mode}) : « {propre} » → {l.Count} résultat(s)");
        etat.Text = l.Count > 0 ? (mode == RechercheVocale.Chant ? "Le chant est peut-être :" : "Le verset est peut-être :") : propre.Length > 0 ? "Rien trouvé pour ce qui a été entendu." : "Rien trouvé.";
        Resultats(l);
        MajBoutons();
        if (r.Continue && IsVisible) Commencer(mode);
    }

    void Resultats(List<ResultatCommande> l)
    {
        resultats.Children.Clear();
        var mw = Application.Current.MainWindow as MainWindow;
        foreach (var x in l)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = x.Titre, FontWeight = FontWeights.Bold, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = P("Blanc") });
            txt.Children.Add(new TextBlock { Text = x.Sous, FontSize = 12.5, Foreground = P("Brume"), TextTrimming = TextTrimming.CharacterEllipsis });
            g.Children.Add(txt);
            var b = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button B(string t, string tip, string style, string action)
            {
                var bt = new Button { Style = S(style), Content = t, Height = 30, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
                bt.Click += (_, _) => mw?.ExecuterResultat(x, action);
                b.Children.Add(bt);
                return bt;
            }
            B("Y aller", "L'ouvrir dans la Bibliothèque (sans le montrer)", "BtnPetit", "aller");
            B("Montrer", "Le montrer tout de suite à l'écran", "BtnPetitOr", "montrer");
            B("+", "L'ajouter au programme du culte", "BtnPetit", "ajouter");
            Grid.SetColumn(b, 1);
            g.Children.Add(b);
            resultats.Children.Add(new Border { Background = P("Encre"), BorderBrush = P("Nuit3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 10, 8), Child = g, Margin = new Thickness(0, 0, 0, 6) });
        }
    }

    async void Telecharger(string nom)
    {
        telechargement.Visibility = Visibility.Visible;
        telechargement.Value = 0;
        texteModele.Text = $"Téléchargement du modèle ({ModeleVoix.Taille(nom)})… Vous pouvez continuer à utiliser Siloé Stream.";
        travail = new CancellationTokenSource();
        try
        {
            await ModeleVoix.TelechargerAsync(nom, new Progress<double>(v => telechargement.Value = v), travail.Token);
            r.Modele = nom;
            r.Sauver();
            telechargement.Visibility = Visibility.Collapsed;
            Commencer(mode);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Journal.Erreur("recherche vocale : téléchargement du modèle", e);
            texteModele.Text = "Le téléchargement a échoué : " + e.Message + " (Internet ?). Réessayez.";
            telechargement.Visibility = Visibility.Collapsed;
        }
    }
}
