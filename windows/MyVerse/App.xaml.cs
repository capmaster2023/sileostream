using System.Windows;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>MyVerse 4.0 : démarrage, une seule copie à la fois, services (Bibles, régie, téléphone, OBS).</summary>
public partial class App : Application
{
    /// <summary>La version compilée (fichier VERSION du dépôt, « 3.3.0 »), sans le numéro de commit ajouté par la compilation.</summary>
    public static string Version { get; } = LireVersion();
    static string LireVersion()
    {
        var v = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion ?? "";
        int plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        return v.Trim().Length > 0 ? v.Trim() : "4.1.0";
    }

    /// <summary>
    /// Thème de l'interface : « sombre » (par défaut, 2.0.1) ou « clair ». Appliqué tout de suite à toutes les fenêtres
    /// (les pinceaux de Theme.xaml suivent la palette) et enregistré dans les réglages.
    /// </summary>
    public static void AppliquerApparence(string nom)
    {
        Palette.Actuelle.Appliquer(nom);
        if (Donnees != null)
        {
            Donnees.Reglages.Apparence = Palette.Actuelle.Nom;
            try { Donnees.SauverReglages(); } catch (Exception e) { Journal.Erreur("apparence", e); }
        }
        // barre de titre de Windows claire ou sombre, comme l'intérieur (la projection et l'écran de scène gardent la leur)
        if (Current != null)
            foreach (Window w in Current.Windows)
                if (w is global::MyVerse.Ui.MainWindow or FenetreDialogue) Natif.TitreSombre(w, Palette.Actuelle.Sombre);
    }
    public static string Ressources => Path.Combine(AppContext.BaseDirectory, "Ressources");
    public static Donnees Donnees { get; private set; } = null!;
    public static Bibles Bibles { get; private set; } = null!;
    public static Regie Regie { get; private set; } = null!;
    public static SortieObs SortieObs { get; private set; } = null!;
    public static SourcesDisplay Sources { get; private set; } = null!;
    /// <summary>Capture du contenu épinglé (Duo), en même temps que celle de l'écran principal.</summary>
    public static SourcesDisplay SourcesDuo { get; private set; } = null!;
    /// <summary>
    /// Capture de l'écran par défaut montré à la place du direct : dans la salle pendant une source « En streaming » (1.3.1),
    /// sur MyVirtualCam pendant une source « Dans l'église » (1.3.2). Jamais les deux à la fois ; rien n'est publié pour OBS.
    /// </summary>
    public static SourcesDisplay SourcesDefaut { get; private set; } = null!;
    public static MyVirtualCam? CameraVirtuelle { get; private set; }
    /// <summary>MyStream (1.6.0) : l'image de Zoom et du direct YouTube, composée sur son propre fil.</summary>
    public static MyStreamMoteur? MyStream { get; private set; }
    /// <summary>Capture de la source principale de MyStream (sa caméra, son MyCam…), quand l'église ne la montre pas déjà.</summary>
    public static SourcesDisplay SourcesStream { get; private set; } = null!;
    /// <summary>Le direct (YouTube ou un autre service).</summary>
    public static Audio.DirectService Direct { get; private set; } = null!;
    /// <summary>Les directs automatiques (1.6.4) : chaque semaine, à l'heure dite, le direct démarre tout seul.</summary>
    public static Audio.PlanificateurDirect Planificateur { get; private set; } = null!;
    public static Audio.ChurchAudioService Audio { get; private set; } = null!;
    public static Telephone Telephone { get; private set; } = null!;
    /// <summary>1.9.5 : la caméra Sony reçue par son Wi-Fi (source MyCam « sony-wifi »).</summary>
    public static CameraWifi CameraWifi { get; private set; } = null!;
    /// <summary>Dossier du test automatique (« SiloeStream.exe --autotest dossier »), sinon null.</summary>
    public static string? AutoTest { get; private set; }
    /// <summary>Résultat du contrôle du pare-feu : « ok », « missing », « refused », « error », « demande » ou vide (en cours).</summary>
    public static string Parefeu_
    {
        get => parefeu;
        set { parefeu = value; ParefeuChange?.Invoke(); }
    }
    static string parefeu = "";
    /// <summary>L'état du pare-feu a changé (fil de l'interface).</summary>
    public static event Action? ParefeuChange;

    static Mutex? instance;
    static EventWaitHandle? signal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Contains("--virtualcam-install") || args.Contains("--virtualcam-uninstall"))
        {
            Shutdown(MyVirtualCam.Enregistrer(args.Contains("--virtualcam-install")));
            return;
        }
        // lancé en administrateur par l'installateur ou par « Autoriser dans le pare-feu »
        if (args.Contains("--parefeu"))
        {
            int code = 1;
            try { code = MyVerse.Parefeu.Appliquer(); } catch (Exception) { }
            Shutdown(code);
            return;
        }
        // vérification (dépannage, compilation sur GitHub) : code 0 si les règles du pare-feu sont en place
        if (args.Contains("--parefeu-verifier"))
        {
            bool ok = false;
            try { ok = MyVerse.Parefeu.Present(); } catch (Exception) { }
            Shutdown(ok ? 0 : 2);
            return;
        }
        // 1.7.0 : l'agent « à distance » (lancé à l'ouverture de session Windows), sans fenêtre
        if (args.Contains("--agent"))
        {
            DemarrerAgent();
            return;
        }
        int i = Array.IndexOf(args, "--autotest");
        if (i >= 0) AutoTest = i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "myverse-autotest");
        if (AutoTest == null && !UneSeuleCopie(args.Contains("--redemarrage")))
        {
            Shutdown(0);
            return;
        }

        var dossier = Donnees.DossierParDefaut();
        try { Directory.CreateDirectory(dossier); } catch (Exception) { }
        Journal.Ouvrir(dossier);
        Journal.Info($"— démarrage de Siloé Stream {Version} (Windows) —");
        DispatcherUnhandledException += (_, ev) =>
        {
            Journal.Erreur("erreur", ev.Exception);
            ev.Handled = true;
            if (AutoTest != null) Ui.AutoTest.Echec("exception : " + ev.Exception);
            else if (MainWindow is MainWindow mw) mw.Toast("Erreur : " + ev.Exception.Message + " (détails dans Réglages › Journal)");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            if (ev.ExceptionObject is Exception x) Journal.Erreur("erreur fatale", x);
            Journal.Sauver();
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Journal.Erreur("tâche", ev.Exception);
            ev.SetObserved();
        };

        try
        {
            Donnees = Donnees.Charger(dossier, Ressources);
            Bibles = new Bibles(Donnees);
            Regie = new Regie(Donnees, Bibles);
        }
        catch (Exception x)
        {
            Journal.Erreur("démarrage", x);
            Journal.Sauver();
            MessageBox.Show("Siloé Stream n'a pas pu démarrer : " + x.Message + "\n\nJournal : " + Path.Combine(dossier, "journal-myverse.txt"), "MyVerse", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        // 2.0.1 : le design « régie pro » est sombre ; mis une fois pour tout le monde (le choix reste dans Réglages › Apparence)
        if (Donnees.Reglages.Design < 2)
        {
            Donnees.Reglages.Design = 2;
            Donnees.Reglages.Apparence = "sombre";
            try { Donnees.SauverReglages(); } catch (Exception exDesign) { Journal.Erreur("design 2.0.1", exDesign); }
        }
        // thème de l'interface (sombre par défaut), avant la première fenêtre
        Palette.Actuelle.Appliquer(Donnees.Reglages.Apparence);
        SortieObs = new SortieObs(Path.Combine(Ressources, "obs"), AutoTest != null ? Path.Combine(dossier, "SiloeStream-OBS") : null);
        Telephone = new Telephone(dossier, Regie, f => Dispatcher.InvokeAsync(f).Task.Unwrap());
        Telephone.DistanceChange += () => Dispatcher.InvokeAsync(MajAgent);
        try { Telephone.Demarrer(); }
        catch (Exception x) { Journal.Erreur("téléphone : démarrage", x); }
        CameraWifi = new CameraWifi(Telephone.Cameras);
        try
        {
            // la caméra Wi-Fi est dans Display : on la cherche dès le démarrage (sans toucher au Wi-Fi du PC)
            if (Donnees.Annonces.Any(a => a.Source is { Type: "mycam" } s && s.Peripherique == CameraWifi.Id))
            {
                var rc = ReglagesCameraWifi.Lire(Donnees.Dossier);
                CameraWifi.Nom = rc.Nom;
                CameraWifi.Adresse = rc.Adresse;
                CameraWifi.Demarrer();
            }
        }
        catch (Exception x) { Journal.Erreur("caméra Wi-Fi : démarrage", x); }
        _ = Task.Run(() =>
        {
            if (SortieObs.Preparer()) SortieObs.Ecrire(Regie.Etat);
        });

        // 1.8.0 : les caméras ne fabriquent pas d'images plus grandes que l'écran de l'église ou le direct (4K seulement si utile)
        MyVerse.Ui.Audio.CameraFrames.LargeurMax = () =>
        {
            var (l, _, _) = Donnees.Reglages.Direct.Taille;
            return Math.Clamp(Math.Max(l, Donnees.Reglages.Ecran?.W ?? 1920), 1920, 3840);
        };
        Sources = new SourcesDisplay();
        SourcesDuo = new SourcesDisplay("display-duo.jpg");
        SourcesDefaut = new SourcesDisplay("");
        SourcesStream = new SourcesDisplay("");
        if (AutoTest == null || Environment.GetEnvironmentVariable("SILOE_TEST_CAM") == "1")
            try { CameraVirtuelle = new MyVirtualCam(); } catch (Exception ex) { Journal.Erreur("MyVirtualCam démarrage", ex); }
        try
        {
            var (l, h, _) = Donnees.Reglages.Direct.Taille;
            MyStream = new MyStreamMoteur(CameraVirtuelle, l, h);
        }
        catch (Exception ex) { Journal.Erreur("MyStream démarrage", ex); }
        // 2.1.0 : le dossier des enregistrements choisi dans Réglages › Son
        MyVerse.Audio.AudioConfiguration.DossierParDefautChoisi = Donnees.Reglages.Audio.DossierChoisi ?? "";
        Audio = new Audio.ChurchAudioService(Donnees.Reglages.Audio);
        Direct = new Audio.DirectService(Audio, Donnees);
        // le test automatique fait tourner les directs automatiques lui-même (heure simulée)
        Planificateur = new Audio.PlanificateurDirect(Direct, Donnees, Regie, AutoTest == null);
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
        // 1.7.0 : « Fermer Siloé Stream » depuis le téléphone (par Internet) ; jamais pendant un direct
        Telephone.FermerDemande = () =>
        {
            if (Direct.EnDirect) return "Un direct YouTube est en cours : arrêtez-le d'abord.";
            if (Audio.Recorder.AnyBusy) return "Un enregistrement est en cours : arrêtez-le d'abord.";
            Journal.Info("à distance : fermeture de Siloé Stream demandée par un téléphone");
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1000);
                (MainWindow as MainWindow)?.Close();
            });
            return null;
        };
        MajAgent();
        if (AutoTest == null) Natif.MigrerDemarrage();
        // 1.5.1 : de quoi comprendre une lenteur chez l'utilisateur (Réglages › Journal) ; sans carte graphique, l'aperçu en grand se fait léger
        try
        {
            int niveau = System.Windows.Media.RenderCapability.Tier >> 16;
            Journal.Info($"affichage : {(niveau >= 2 ? "carte graphique" : niveau == 1 ? "carte graphique en partie" : "sans carte graphique (le processeur dessine tout, plus lent)")}, "
                + $"{Donnees.Chants.Count} chants, {Donnees.Chants.Select(c => c.Author ?? "").Distinct().Count()} recueils");
            if (niveau < 2) w.ModeLeger();
        }
        catch (Exception x) { Journal.Erreur("diagnostic de l'affichage", x); }
        // le moteur audio d'OBS démarre une fois la fenêtre affichée (quelques dixièmes de seconde) ;
        // sans le dossier « obs », Siloé Stream fonctionne normalement et le panneau Audio l'indique
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Audio.Start()));
        if (Donnees.Migration.Length > 0) w.Toast("Données de Siloé Stream 3 reprises : " + Donnees.Migration + ".");
        if (AutoTest != null) Ui.AutoTest.Lancer(w, AutoTest);
    }

    static Mutex? agentUnique;
    static AgentDistance? agent;
    const string NomAgent = @"Local\SiloeStream-agent-distance";

    /// <summary>
    /// 1.7.0 : l'agent « à distance ». Il tourne sans fenêtre dès l'ouverture de session Windows, garde le lien avec le relais
    /// Siloé Stream (VPS) et peut démarrer Siloé Stream quand un téléphone le demande de l'extérieur. Il s'arrête tout seul quand le
    /// contrôle à distance est coupé dans les Réglages.
    /// </summary>
    void DemarrerAgent()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        agentUnique = new Mutex(true, NomAgent, out bool premier);
        if (!premier)
        {
            Shutdown(0);
            return;
        }
        var dossier = Donnees.DossierParDefaut();
        try { Directory.CreateDirectory(dossier); } catch (Exception) { }
        Journal.Ouvrir(dossier, "journal-agent.txt");
        Journal.Info($"— agent à distance de Siloé Stream {Version} —");
        agent = new AgentDistance(dossier, DemarrerSiloe);
        _ = Task.Run(async () =>
        {
            try { await agent.Executer(); }
            catch (Exception x) { Journal.Erreur("agent à distance", x); }
            Journal.Sauver();
            _ = Dispatcher.BeginInvoke(new Action(() => Shutdown(0)));
        });
    }

    /// <summary>L'agent lance Siloé Stream (demandé par un téléphone, de l'extérieur).</summary>
    static bool DemarrerSiloe()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
            return true;
        }
        catch (Exception e) { Journal.Erreur("à distance : démarrage de Siloé Stream", e); return false; }
    }

    /// <summary>L'agent tourne-t-il en ce moment (sur cette session Windows) ?</summary>
    public static bool AgentEnMarche()
    {
        try
        {
            using var m = Mutex.OpenExisting(NomAgent);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// 1.7.0 : contrôle à distance actif : l'agent démarre avec Windows (clé « Run ») et tourne dès maintenant.
    /// Coupé : plus au démarrage de Windows (l'agent en marche s'arrête tout seul en relisant les réglages).
    /// </summary>
    public static void MajAgent()
    {
        if (AutoTest != null) return;
        var r = ReglagesDistance.Lire(Donnees.Dossier);
        Natif.ChangerAgentAuDemarrage(r.Actif);
        if (!r.Actif || AgentEnMarche()) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--agent") { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
            Journal.Info("à distance : agent démarré");
        }
        catch (Exception e) { Journal.Erreur("à distance : démarrage de l'agent", e); }
    }

    /// <summary>
    /// Une seule copie de Siloé Stream : un 2e lancement ramène la fenêtre déjà ouverte.
    /// <paramref name="attendre"/> : redémarrage demandé par Siloé Stream (Réglages › Audio) ; on attend que l'ancienne copie se ferme.
    /// </summary>
    static bool UneSeuleCopie(bool attendre = false)
    {
        instance = new Mutex(true, @"Local\MyVerse4", out bool premier);
        if (!premier && attendre)
        {
            try { premier = instance.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { premier = true; }
        }
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MyVerse4-montrer");
        if (!premier)
        {
            signal.Set();
            return false;
        }
        var t = new Thread(() =>
        {
            while (true)
            {
                signal.WaitOne();
                Current?.Dispatcher.BeginInvoke(new Action(() => (Current.MainWindow as MainWindow)?.Ramener()));
            }
        }) { IsBackground = true, Name = "une seule copie" };
        t.Start();
        return true;
    }

    /// <summary>Redémarre Siloé Stream (réglages audio qui, comme dans OBS, ne s'appliquent qu'au démarrage).</summary>
    public static void Redemarrer()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--redemarrage") { UseShellExecute = false }); }
        catch (Exception e) { Journal.Erreur("redémarrage de Siloé Stream", e); return; }
        Journal.Info("redémarrage demandé (réglages audio)");
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (agent != null)
        {
            agent.Dispose();
            Journal.Sauver();
            base.OnExit(e);
            return;
        }
        Journal.Info("fermeture de MyVerse");
        try { Planificateur?.Dispose(); } catch (Exception x) { Journal.Erreur("fin des directs automatiques", x); }
        try { Direct?.Dispose(); } catch (Exception x) { Journal.Erreur("fin du direct", x); }
        Audio?.Dispose();
        Sources?.Dispose();
        SourcesDuo?.Dispose();
        SourcesDefaut?.Dispose();
        SourcesStream?.Dispose();
        MyStream?.Dispose();
        CameraVirtuelle?.Dispose();
        try { CameraWifi?.Dispose(); } catch (Exception) { }
        try { Telephone?.Dispose(); } catch (Exception) { }
        Journal.Sauver();
        base.OnExit(e);
    }
}
