using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GenreSon = MyVerse.Audio.GenreSon;
using Son = MyVerse.Audio.Son;

namespace MyVerse.Ui;

/// <summary>
/// Test automatique (« SiloeStream.exe --autotest dossier », lancé par la compilation sur GitHub) :
/// ouvre la régie, affiche des versets et des chants, ouvre la projection et chaque fenêtre,
/// enregistre des captures d'écran et un compte rendu (autotest.txt), puis ferme Siloé Stream (code 0 = tout va bien).
/// </summary>
public static class AutoTest
{
    static readonly List<string> echecs = new();
    static readonly List<string> log = new();
    static string dossier = "";

    public static void Echec(string m)
    {
        lock (echecs) echecs.Add(m);
        lock (log) log.Add("✗ " + m);
    }

    static void Ok(bool c, string quoi)
    {
        if (c) lock (log) log.Add("✓ " + quoi);
        else Echec(quoi);
    }

    static void Note(string m) { lock (log) log.Add("  " + m); }

    static Task Attendre(int ms) => Task.Delay(ms);

    /// <summary>Image de la fenêtre (fond compris), sans dépendre de l'écran du PC.</summary>
    static void Capture(Window? w, string nom)
    {
        if (w == null) { Echec("capture impossible (fenêtre absente) : " + nom); return; }
        try
        {
            w.UpdateLayout();
            // la racine visuelle de la fenêtre : tout l'intérieur, fond et marges compris
            if ((VisualTreeHelper.GetChildrenCount(w) > 0 ? VisualTreeHelper.GetChild(w, 0) : w.Content) is not FrameworkElement el) { Echec("capture sans contenu : " + nom); return; }
            int wd = (int)Math.Ceiling(el.ActualWidth), h = (int)Math.Ceiling(el.ActualHeight);
            if (wd <= 0 || h <= 0) { Echec("capture vide : " + nom); return; }
            var rtb = new RenderTargetBitmap(wd, h, 96, 96, PixelFormats.Pbgra32);
            var fond = new DrawingVisual();
            using (var dc = fond.RenderOpen()) dc.DrawRectangle(w.Background ?? Brushes.Black, null, new Rect(0, 0, wd, h));
            rtb.Render(fond);
            rtb.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var f = File.Create(Path.Combine(dossier, nom));
            enc.Save(f);
            Note($"capture {nom} ({wd}×{h})");
        }
        catch (Exception x) { Echec($"capture {nom} : {x.Message}"); }
    }

    static async Task Fenetre(Window owner, Func<Window> creer, string nom, Action<Window>? avant = null)
    {
        Window? d = null;
        try
        {
            d = creer();
            d.Owner = owner;
            d.ShowActivated = false;
            d.Show();
            avant?.Invoke(d);
            await Attendre(700);
            Capture(d, nom);
            Ok(true, "fenêtre " + d.Title);
        }
        catch (Exception x) { Echec($"fenêtre {nom} : {x}"); }
        finally { try { d?.Close(); } catch (Exception) { } }
    }

    /// <summary>
    /// Siloé Stream 1.0 : Échap dans une recherche n'efface plus l'écran ; pas de veille pendant la projection ; projecteur débranché
    /// puis rebranché (écrans simulés : la machine du CI n'a qu'un écran) ; la projection se rouvre au démarrage sur le dernier écran.
    /// </summary>
    static async Task TestEchapEtProjecteur(MainWindow w)
    {
        var R = App.Regie;
        // ---- Échap : annule la recherche, sans retirer le verset en direct
        w.TestRecherche("jn 3 16");
        await Attendre(300);
        w.TestAfficherSelection();
        await Attendre(400);
        var verset = R.Etat.Ref;
        Ok(verset.StartsWith("Jean 3:16") && R.Cible is CibleVerset, "Échap : Jean 3:16 en direct (" + verset + ")");
        Ok(w.TestEchap("bible", "romains 8 28") == "" && R.Etat.Ref == verset, "Échap dans la recherche Bible : la recherche s'annule, le verset reste à l'écran");
        Ok(w.TestEchap("bible") == "" && R.Etat.Ref == verset, "2e Échap (recherche vide) : on quitte le champ, le verset reste");
        Ok(w.TestEchap("chants", "grâce infinie") == "" && R.Etat.Ref == verset, "Échap dans la recherche de chants : le verset reste");
        Ok(w.TestEchap("numero", "45") == "" && R.Etat.Ref == verset, "Échap dans le numéro de chant : le verset reste");
        Ok(w.TestEchap("cmd", "psaume 23") == "" && R.Etat.Ref == verset, "Échap dans la recherche du culte (Ctrl K) : le verset reste");
        Ok(w.TestEchap("autre", "Repas fraternel") == "Repas fraternel" && R.Etat.Ref == verset, "Échap dans un autre champ : rien n'est effacé, le verset reste");
        await Attendre(900);
        Ok(R.Etat.Ref == verset && R.Cible is CibleVerset, "les recherches annulées n'ont rien changé à l'écran");
        w.TestEchap(null);
        await Attendre(300);
        Ok(R.Cible is not CibleVerset, "Échap hors d'un champ : l'écran se vide, comme avant");

        // ---- pas de veille tant que la projection est ouverte
        Ok(w.Ecran != null && w.TestVeille.StartsWith("écran"), "anti-veille : projection ouverte, Windows n'éteint ni l'écran ni le PC (" + w.TestVeille + ")");

        // ---- projecteur débranché, rebranché (écrans simulés)
        var principal = Natif.Ecrans().First(e => e.Principal);
        var pj = new Moniteur("EPSON PJ", principal.X + principal.W, principal.Y, 1280, 720, false, @"\\.\DISPLAY9");
        w.OuvrirSur(pj, true);
        await Attendre(500);
        Ok(w.Ecran?.Moniteur == pj && App.Donnees.Reglages.Ecran is { Ouvert: true, Nom: "EPSON PJ", Mode: "plein" } c0 && c0.Peripherique == pj.Peripherique,
            "projection sur le projecteur (simulé), retenue pour le prochain démarrage");
        w.VerifierEcrans(new() { principal });
        await Attendre(300);
        Ok(w.Ecran == null && w.AttenteProjecteur && App.Donnees.Reglages.Ecran?.Ouvert == true, "projecteur débranché : la projection se ferme (elle ne recouvre pas la régie) et Siloé Stream l'attend");
        Ok(w.TestStatutProjection.Contains("débranché"), "état affiché : " + w.TestStatutProjection);
        Ok(!w.TestVeille.StartsWith("écran"), "projection fermée : Windows peut de nouveau éteindre l'écran (" + w.TestVeille + ")");
        Capture(w, "50-projecteur-debranche.png");
        var scene = new Moniteur("DELL P2419H", pj.X, pj.Y, pj.W, pj.H, false, @"\\.\DISPLAY3");
        w.VerifierEcrans(new() { principal, scene });
        Ok(w.Ecran == null && w.AttenteProjecteur, "un autre écran branché (écran de scène) n'est pas pris pour le projecteur");
        var pj2 = pj with { X = principal.X - 1920, W = 1920, H = 1080 };
        w.VerifierEcrans(new() { principal, scene, pj2 });
        await Attendre(500);
        Ok(w.Ecran?.Moniteur == pj2 && !w.AttenteProjecteur && w.TestVeille.StartsWith("écran"), "projecteur rebranché (autre place, autre résolution) : la projection se rouvre toute seule dessus");
        var pj3 = pj2 with { W = 1024, H = 768 };
        w.VerifierEcrans(new() { principal, pj3 });
        Ok(w.Ecran?.Moniteur == pj3 && App.Donnees.Reglages.Ecran is { W: 1024, H: 768 }, "nouvelle résolution du projecteur : la projection la suit sans se fermer");
        w.VerifierEcrans(new() { pj3 with { Principal = true } });
        Ok(w.Ecran == null && w.AttenteProjecteur, "le projecteur devenu l'écran principal : pas de projection plein écran par-dessus la régie");
        w.VerifierEcrans(new() { principal, pj3 });
        Ok(w.Ecran?.Moniteur == pj3, "l'écran de la régie revient : la projection se rouvre sur le projecteur");

        // ---- arrêtée par l'opérateur : elle ne se rouvre plus toute seule
        w.ArreterProjection();
        Ok(w.Ecran == null && !w.AttenteProjecteur && App.Donnees.Reglages.Ecran?.Ouvert == false, "Arrêter la projection : elle se ferme et n'attend rien");
        w.VerifierEcrans(new() { principal, pj3 });
        w.RouvrirProjection(new() { principal, pj3 });
        Ok(w.Ecran == null, "arrêtée par l'opérateur : ni le rebranchement ni le démarrage ne la rouvrent");

        // ---- démarrage : la projection laissée ouverte se rouvre sur le dernier écran
        App.Donnees.Reglages.Ecran = Projecteur.Choix(pj, true);
        w.RouvrirProjection(new() { principal, pj });
        await Attendre(500);
        Ok(w.Ecran?.Moniteur == pj, "démarrage : la projection se rouvre toute seule sur le dernier écran");
        w.Ecran?.Close();
        Ok(App.Donnees.Reglages.Ecran?.Ouvert == false && !w.AttenteProjecteur, "fermée par l'opérateur (croix, Alt+F4) : elle ne se rouvrira pas au démarrage");
        App.Donnees.Reglages.Ecran = Projecteur.Choix(pj, true);
        w.RouvrirProjection(new() { principal });
        Ok(w.Ecran == null && w.AttenteProjecteur, "démarrage sans le projecteur : Siloé Stream l'attend");
        w.VerifierEcrans(new() { principal, pj });
        await Attendre(300);
        Ok(w.Ecran?.Moniteur == pj, "projecteur branché après le démarrage : la projection s'ouvre toute seule");
        w.ArreterProjection();
        App.Donnees.Reglages.Ecran = Projecteur.Choix(principal, true);
        w.RouvrirProjection(new() { principal });
        Ok(w.Ecran == null && !w.AttenteProjecteur, "démarrage : jamais de plein écran automatique sur l'écran de la régie");
        App.Donnees.Reglages.Ecran = Projecteur.Choix(principal, false);
        w.RouvrirProjection(new() { principal });
        await Attendre(300);
        Ok(w.Ecran is { Plein: false }, "démarrage : une projection en fenêtre se rouvre en fenêtre");
        // la suite du test : projection plein écran sur l'écran principal, comme avant
        w.OuvrirSur(principal, true);
        await Attendre(500);
        Ok(w.Ecran is { Plein: true } && w.TestVeille.StartsWith("écran"), "projection rouverte pour la suite du test");
    }

    /// <summary>
    /// Siloé Stream 1.3 : le son simple, sur le vrai moteur d'OBS (dossier « obs » à côté de SiloeStream.exe). Au départ, seulement le son
    /// des vidéos ; des sons créés comme par « Ajouter un son » (réglés tout seuls) ; personne ne choisit de piste (Siloé Stream les donne,
    /// sans jamais renvoyer le son de l'ordinateur vers sa propre sortie) ; une musique qui attend « Lire » ; le volume en % ;
    /// deux enregistrements en même temps (la vidéo en MKV, la prédication en MP3) ; « Enregistrer le culte » d'un clic ;
    /// un son débranché puis rebranché ; les fenêtres Ajouter un son, Régler, Enregistrement, Envoi ; Réglages › Son.
    /// </summary>
    static async Task TestSon(MainWindow w, Annonce texte, Annonce image)
    {
        var A = App.Audio;
        var cfg = A.Config;
        var avant = MyVerse.Audio.AudioConfiguration.Copy(cfg);
        string dossierTest = Path.Combine(Path.GetTempPath(), "siloestream-autotest-" + Guid.NewGuid().ToString("N")[..8]);
        string? fichierUnClic = null;
        Son? musique = null;
        try
        {
            for (int i = 0; i < 40 && !A.Running && !A.ObsAbsent && !A.Diagnostic.Contains("ne démarre pas"); i++) await Attendre(250);
            Note("son : " + A.Diagnostic);
            Ok(A.Running, "moteur du son (OBS) démarré dans Siloé Stream (" + A.Diagnostic + ")");
            Ok(A.EntreesSeparees, "OBS accepte les entrées séparées d'une carte son");
            Ok(File.Exists(Audio.ChurchAudioService.FfmpegInclus), "ffmpeg livré avec Siloé Stream (" + Audio.ChurchAudioService.FfmpegInclus + ")");

            // 1. au départ : seulement le son des vidéos de Display, et « Ajouter un son »
            cfg.Sons.RemoveAll(s => s.Genre != GenreSon.Video);
            cfg.Profiles.Clear();
            cfg.VirtualSounds.Clear();
            A.Apply();
            w.MontrerSon();
            await Attendre(900);
            var videos = cfg.SonVideos;
            Ok(cfg.Sons.Count == 1 && videos.Eglise && !videos.Coupe, "1.3 : au départ, le son des vidéos de Display seulement, entendu dans l'église");
            string cartes = w.PageSon.TestCartes();
            Ok(cartes.EndsWith("2 tuiles"), "page Son : la carte des vidéos et « Ajouter un son » (" + cartes + ")");
            if (A.Running) Ok(A.Etat(videos).Texte == "Pas de vidéo à l'écran", "le son des vidéos attend une vidéo : « " + A.Etat(videos).Texte + " »");
            Ok(!cfg.Retirer(videos) && cfg.Sons.Contains(videos), "le son des vidéos ne se retire pas (on peut le couper)");
            Capture(w, "32-son-vide.png");
            w.PageSon.TestDefiler(true);
            await Attendre(400);
            Capture(w, "32b-son-ou-va-le-son.png");
            w.PageSon.TestDefiler(false);

            // 2. des sons, comme « Ajouter un son » les crée : réglés tout seuls selon ce qu'ils sont
            string sinus = Path.Combine(App.Donnees.Dossier, "test-sinus.wav");
            EcrireSinus(sinus);
            var pasteur = Son.Nouveau(GenreSon.Voix, "Pasteur");
            var chorale = Son.Nouveau(GenreSon.Voix, "Chorale");
            chorale.Entree = 2;
            var piano = Son.Nouveau(GenreSon.Instrument, "Piano");
            piano.Entree = 3;
            piano.VolumeDb = -6;
            // le son d'une autre sortie que celle de l'église : si c'était la même, OBS 33 ferait taire dans les pistes les sons
            // « dans l'église » (il les entend déjà par cette capture), et la machine du CI n'a pas de sortie à capturer
            var ordi = Son.Nouveau(GenreSon.Ordinateur, "Son de l'ordinateur");
            ordi.Appareil = "{autotest-hp}";
            ordi.NomAppareil = "Haut-parleurs (Realtek)";
            ordi.Coupe = true;
            musique = Son.Nouveau(GenreSon.Fichier, "Musique d'attente");
            musique.Fichier = sinus;
            musique.Boucle = true;
            foreach (var s in new[] { pasteur, chorale, piano, ordi, musique }) cfg.Ajouter(s);
            Ok(pasteur.EnleverBruit && pasteur.VolumeRegulier && !pasteur.Eglise, "une voix : bruit enlevé, volume régulier, pas dans l'église (pas de larsen)");
            Ok(!piano.EnleverBruit && !piano.VolumeRegulier && !piano.Eglise, "un instrument : gardé tel quel (la musique ne se « nettoie » pas)");
            Ok(musique.Eglise && musique.Capture == MyVerse.Audio.AudioInputKind.Media, "une musique : entendue dans l'église");
            Ok(cfg.Sons[^1] == videos && cfg.Sons.Count == 6, "les sons ajoutés passent avant le son des vidéos, qui reste le dernier");

            // 3. où va le son : Zoom (par le câble, tout le son), une salle à côté (la sortie du son de l'ordinateur), le culte en vidéo, la prédication en MP3 (le pasteur seul)
            var zoom = new MyVerse.Audio.VirtualSoundSettings { Name = "Zoom", Enabled = true };
            var salle = new MyVerse.Audio.VirtualSoundSettings { Name = "Salle (test)", Enabled = false, DeviceId = "{autotest-hp}", DeviceName = "Haut-parleurs (Realtek)" };
            var culte = new MyVerse.Audio.RecordingProfile { Name = "Culte (test)", DisplayIds = { MyVerse.Audio.RecordingProfile.Projection, texte.Id, image.Id }, Directory = Path.Combine(dossierTest, "culte") };
            var predication = new MyVerse.Audio.RecordingProfile { Name = "Prédication (test)", Format = "mp3", ChoixSons = new() { pasteur.Id }, Directory = Path.Combine(dossierTest, "predication") };
            cfg.VirtualSounds.Add(zoom);
            cfg.VirtualSounds.Add(salle);
            cfg.Profiles.Add(culte);
            cfg.Profiles.Add(predication);
            A.Apply();
            Ok(!culte.AudioOnly && culte.Format == "mkv" && predication.AudioOnly && predication.Format == "mp3", "le culte en vidéo (MKV), la prédication en son seul (MP3)");
            int pZoom = A.PisteDe(zoom.Id), pCulte = A.PisteDe(culte.Id), pSalle = A.PisteDe(salle.Id), pPred = A.PisteDe(predication.Id);
            Ok(pZoom == 1 && pCulte == 1 && pSalle == 2 && pPred == 3, $"pistes données par Siloé Stream : tout le son sur la 1 (Zoom {pZoom}, Culte {pCulte}), la salle sur la {pSalle}, le pasteur seul sur la {pPred}");
            Ok(A.Plan.SonsDesPistes.TryGetValue(pSalle, out var sonsSalle) && !sonsSalle.Contains(ordi.Id) && sonsSalle.Contains(pasteur.Id),
                "pas de larsen : le son de l'ordinateur ne repart jamais vers la sortie d'où il vient");
            Ok(cfg.Recoit(predication) == "Pasteur seulement" && cfg.Recoit(culte) == "Tout le son", "ce que chacun reçoit, en mots : « " + cfg.Recoit(predication) + " », « " + cfg.Recoit(culte) + " »");
            Note("envoi « Zoom » : " + A.Sorties.Status(zoom.Id));
            await Attendre(1200);
            Capture(w, "32c-son-sons.png");

            if (A.Running)
            {
                Ok(A.InputReady(pasteur.Id) && A.InputReady(ordi.Id) && A.InputReady(musique.Id), "chaque son est une source d'OBS");
                Ok(A.EstEntree(chorale.Id) && A.EstEntree(piano.Id) && !A.EstEntree(pasteur.Id), "entrées 2 et 3 d'une carte son : chacune son son ; le pasteur (toutes les entrées) reste une capture");

                // la musique attend « Lire »
                await Attendre(600);
                var (l0, r0) = A.Level(musique.Id);
                Ok(Math.Max(l0, r0) < -60 && A.Etat(musique).Texte == "Prête : cliquez Lire", $"la musique ne part pas toute seule : « {A.Etat(musique).Texte} » ({Math.Max(l0, r0):0.0} dBFS)");
                A.Jouer(musique);
                float m = float.NegativeInfinity;
                for (int i = 0; i < 25 && !(m > -12); i++) { await Attendre(200); var (l, r) = A.Level(musique.Id); m = Math.Max(l, r); }
                Ok(m > -12 && m < -2, $"Lire : la barre bouge, sinus à −6 dBFS ({m:0.0} dBFS)");
                Ok(A.Lecture(musique).Etat == MyVerse.Audio.EtatLecture.Lecture, "Lire : la musique joue");
                await Attendre(1500);
                Ok(A.Etat(musique).Texte == "Bon niveau", "le mot de la carte : « " + A.Etat(musique).Texte + " »");
                // le volume en % : 50 % paraît deux fois moins fort (environ −18 dB)
                musique.VolumeDb = MyVerse.Audio.VolumeCurseur.Db(MyVerse.Audio.VolumeCurseur.Normal * 0.5);
                A.Apply();
                await Attendre(700);
                var (l50, r50) = A.Level(musique.Id);
                Ok(MyVerse.Audio.VolumeCurseur.Texte(musique.VolumeDb) == "50 %" && Math.Max(l50, r50) < -18 && Math.Max(l50, r50) > -30, $"volume à {MyVerse.Audio.VolumeCurseur.Texte(musique.VolumeDb)} : {Math.Max(l50, r50):0.0} dBFS");
                musique.VolumeDb = 0;
                A.Apply();
                A.Pause(musique);
                await Attendre(400);
                Ok(A.Lecture(musique).Etat == MyVerse.Audio.EtatLecture.Pause && A.Etat(musique).Texte == "En pause", "Pause : « " + A.Etat(musique).Texte + " »");
                A.Jouer(musique);
                await Attendre(300);
                // Couper : rien ne passe, partout
                Audio.ActionsSon.Couper(musique, true);
                await Attendre(300);
                Ok(A.Etat(musique).Texte == "Coupé", "Couper : « " + A.Etat(musique).Texte + " »");
                Audio.ActionsSon.Couper(musique, false);
                await Attendre(500);

                if (A.TracksActive)
                {
                    float p1 = float.NegativeInfinity;
                    for (int i = 0; i < 25 && !(p1 > -12); i++) { await Attendre(200); p1 = A.TrackLevel(A.PisteDe(zoom.Id) - 1); }
                    Ok(p1 > -12, $"la piste de Zoom (tout le son) reçoit la musique ({p1:0.0} dBFS)");

                    // deux enregistrements en même temps, chacun sur sa piste (elles ne bougent pas au démarrage)
                    await A.Recorder.ToggleAsync(culte);
                    Ok(A.Recorder.Active(culte.Id), "enregistrement vidéo « Culte » démarré : " + A.Recorder.Message(culte.Id));
                    await A.Recorder.ToggleAsync(predication);
                    Ok(A.Recorder.Active(predication.Id), "2e enregistrement en même temps (« Prédication », MP3) : " + A.Recorder.Message(predication.Id));
                    Ok(A.PisteDe(culte.Id) == pCulte && A.PisteDe(predication.Id) == pPred, $"les pistes ne bougent pas quand un enregistrement démarre : Culte {A.PisteDe(culte.Id)}, Prédication {A.PisteDe(predication.Id)}");
                    float pc = float.NegativeInfinity;
                    for (int i = 0; i < 25 && !(pc > -12); i++) { await Attendre(200); pc = A.TrackLevel(A.PisteDe(culte.Id) - 1); }
                    float pp = A.TrackLevel(A.PisteDe(predication.Id) - 1);
                    Ok(pc > -12, $"le culte (tout le son) reçoit la musique ({pc:0.0} dBFS)");
                    Ok(float.IsNegativeInfinity(pp) || pp < -40, $"la prédication (le pasteur seul) ne reçoit pas la musique ({pp:0.0} dBFS)");
                    await Attendre(1500);
                    w.PageSon.TestDefiler(true);
                    await Attendre(400);
                    Capture(w, "36-enregistrements-en-cours.png");
                    await A.Recorder.ToggleAsync(predication);
                    Ok(!A.Recorder.Active(predication.Id) && A.Recorder.Active(culte.Id), "« Prédication » arrêtée, « Culte » continue");
                    await Attendre(2500);
                    await A.Recorder.ToggleAsync(culte);
                    string? video = A.Recorder.LastFile(culte.Id), mp3 = A.Recorder.LastFile(predication.Id);
                    long tv = video != null && File.Exists(video) ? new FileInfo(video).Length : 0, ta = mp3 != null && File.Exists(mp3) ? new FileInfo(mp3).Length : 0;
                    Ok(A.Recorder.State(culte.Id) == Audio.ChurchRecordingState.Ready && tv > 20000, $"vidéo MKV finalisée : {Path.GetFileName(video)} ({tv} octets, {A.Recorder.Message(culte.Id)})");
                    Ok(mp3 != null && mp3.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && ta > 20000, $"MP3 de la prédication : {Path.GetFileName(mp3)} ({ta} octets, {A.Recorder.Encoder(predication.Id)})");
                    if (tv > 0)
                    {
                        var (code, journal) = await Decrire(A.Ffmpeg, video!);
                        Ok(code == 0 && journal.Contains("Video: ") && journal.Contains("Audio: aac"), "MKV relu en entier par ffmpeg : une piste vidéo et une piste audio");
                        Ok(Duree(journal) > 3, $"MKV : durée {Duree(journal):0.0} s");
                        foreach (var ligne in journal.Split('\n').Where(l => l.Contains("Stream #"))) Note("  " + ligne.Trim());
                    }
                    if (ta > 0)
                    {
                        var (code, journal) = await Decrire(A.Ffmpeg, mp3!);
                        Ok(code == 0 && journal.Contains("Audio: mp3") && Duree(journal) > 1, $"MP3 relu en entier par ffmpeg ({Duree(journal):0.0} s)");
                    }
                    await Attendre(600);
                    Capture(w, "36b-enregistrements-termines.png");

                    // « Enregistrer le culte » d'un clic : un enregistrement « Culte » en MP3 (tout le son), créé et démarré
                    cfg.Profiles.Clear();
                    A.Apply();
                    Audio.ActionsSon.EnregistrerLeCulte();
                    for (int i = 0; i < 60 && !(cfg.Profiles.Count == 1 && A.Recorder.Active(cfg.Profiles[0].Id)); i++) await Attendre(150);
                    var unClic = cfg.Profiles.FirstOrDefault();
                    Ok(unClic is { Name: "Culte", AudioOnly: true, Format: "mp3", ChoixSons: null } && A.Recorder.Active(unClic.Id),
                        "« Enregistrer le culte » d'un clic : « Culte », MP3, tout le son, démarré (" + (unClic != null ? A.Recorder.Message(unClic.Id) : "rien") + ")");
                    await Attendre(2200);
                    Capture(w, "36c-enregistrer-le-culte.png");
                    if (unClic != null)
                    {
                        Audio.ActionsSon.Basculer(unClic);
                        for (int i = 0; i < 80 && (A.Recorder.Active(unClic.Id) || A.Recorder.Busy(unClic.Id)); i++) await Attendre(150);
                        fichierUnClic = A.Recorder.LastFile(unClic.Id);
                        long t = fichierUnClic != null && File.Exists(fichierUnClic) ? new FileInfo(fichierUnClic).Length : 0;
                        Ok(fichierUnClic != null && fichierUnClic.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && t > 10000, $"le fichier du culte : {Path.GetFileName(fichierUnClic)} ({t} octets) dans {Path.GetDirectoryName(fichierUnClic)}");
                    }
                    cfg.Profiles.Clear();
                    cfg.Profiles.Add(culte);
                    cfg.Profiles.Add(predication);
                    A.Apply();
                }
                else Note("pistes d'OBS inactives sur cette machine (pas de carte graphique) : essais des pistes et des enregistrements non faits");
            }

            // 4. la page en sombre
            w.PageSon.TestDefiler(false);
            App.AppliquerApparence("sombre");
            await Attendre(600);
            Capture(w, "32d-son-sombre.png");
            App.AppliquerApparence("clair");
            await Attendre(300);

            // 5. un son débranché, puis rebranché (relevé simulé : la machine du CI n'a pas de carte son)
            var sansFil = Son.Nouveau(GenreSon.Voix, "Micro sans fil");
            sansFil.Appareil = "{autotest-umc}";
            sansFil.NomAppareil = "Line In (Behringer UMC404HD)";
            cfg.Ajouter(sansFil);
            A.Apply();
            var webcam = new MyVerse.Audio.AudioDevice("{autotest-webcam}", "Microphone (Webcam C920)");
            var carte = new MyVerse.Audio.AudioDevice("{autotest-umc}", "Line In (Behringer UMC404HD)");
            var hp = new MyVerse.Audio.AudioDevice("{autotest-hp}", "Haut-parleurs (Realtek)");
            A.Traiter(new() { webcam, carte }, new() { hp });
            Ok(!A.Debranche(sansFil), "carte son branchée : le son est là");
            A.Traiter(new() { webcam }, new() { hp });
            await Attendre(600);
            Ok(A.Debranche(sansFil) && (!A.Running || A.Etat(sansFil).Texte == "Débranché") && cfg.Sons.Contains(sansFil), "débranchée : « Débranché », le son reste (rien n'est retiré)");
            Capture(w, "38-son-debranche.png");
            int combien = cfg.Sons.Count;
            A.Traiter(new() { webcam, carte, new MyVerse.Audio.AudioDevice("{autotest-usb}", "Microphone (Blue Yeti)") }, new() { hp });
            Ok(!A.Debranche(sansFil) && cfg.Sons.Count == combien, "rebranchée : le son revient ; un nouveau micro branché n'est pas ajouté tout seul");
            A.OublierReleve();

            // 6. les fenêtres
            await Fenetre(w, () => new Audio.AjoutSonWindow(), "33-ajouter-un-son.png");
            await Fenetre(w, () => new Audio.AjoutSonWindow(GenreSon.Voix), "33b-ajouter-un-micro.png");
            await Fenetre(w, () => new Audio.ReglerSonWindow(pasteur), "34-regler-un-son.png");
            await Fenetre(w, () => new Audio.EnregistrementWindow(culte), "34b-enregistrement-video.png");
            await Fenetre(w, () => new Audio.EnregistrementWindow(null), "34c-nouvel-enregistrement.png");
            await Fenetre(w, () => new Audio.EnvoiWindow(zoom), "35-envoi-zoom.png");
            w.TestPage("reglages");
            w.Reglages.Aller("son", false);
            await Attendre(1200);
            Capture(w, "39d-reglages-son.png");
        }
        catch (Exception x) { Echec("son : " + x); }
        finally
        {
            try { await A.Recorder.StopAllAsync(); } catch (Exception x) { Echec("arrêt des enregistrements : " + x.Message); }
            if (musique != null) try { A.Arreter(musique); } catch (Exception) { }
            cfg.Sons.Clear(); cfg.Sons.AddRange(avant.Sons);
            cfg.Profiles.Clear(); cfg.Profiles.AddRange(avant.Profiles);
            cfg.VirtualSounds.Clear(); cfg.VirtualSounds.AddRange(avant.VirtualSounds);
            cfg.Eglise = avant.Eglise;
            cfg.Casque = avant.Casque;
            A.OublierReleve();
            A.Apply();
            try { Directory.Delete(dossierTest, true); } catch (Exception) { }
            try { if (fichierUnClic != null) File.Delete(fichierUnClic); } catch (Exception) { }
        }
    }

    /// <summary>La durée lue par ffmpeg (« Duration: 00:00:04.12 »), en secondes.</summary>
    static double Duree(string journal)
    {
        var d = System.Text.RegularExpressions.Regex.Match(journal, @"Duration: (\d+):(\d+):([0-9.]+)");
        return d.Success ? int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60 + double.Parse(d.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>ffmpeg relit tout le fichier (sans rien écrire) : code de sortie et journal (flux, durée).</summary>
    /// <summary>Images différentes de la précédente, par seconde, au milieu d'une vidéo (le début et la fin écartés) ; et le nombre d'images.</summary>
    static async Task<(double Ips, int Images)> ImagesDifferentes(string ffmpeg, string fichier)
    {
        var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-i", fichier, "-map", "0:v:0", "-vf", "signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-f", "null", "-" }) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info)!;
        var sortie = p.StandardOutput.ReadToEndAsync();
        string journal = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        await sortie;
        var valeurs = new List<(double T, double Y)>();
        double t = 0;
        foreach (var ligne in journal.Split('\n'))
        {
            var mt = System.Text.RegularExpressions.Regex.Match(ligne, @"pts_time:([0-9.]+)");
            if (mt.Success) t = double.Parse(mt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var my = System.Text.RegularExpressions.Regex.Match(ligne, @"YAVG=([0-9.]+)");
            if (my.Success) valeurs.Add((t, double.Parse(my.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (valeurs.Count < 10) return (0, valeurs.Count);
        double debut = valeurs[0].T + 0.7, fin = valeurs[^1].T - 0.7;
        var milieu = valeurs.Where(v => v.T >= debut && v.T <= fin).ToList();
        int changements = milieu.Zip(milieu.Skip(1)).Count(x => Math.Abs(x.Second.Y - x.First.Y) > 2);
        return (fin > debut ? changements / (fin - debut) : 0, valeurs.Count);
    }

    static async Task<(int Code, string Journal)> Decrire(string ffmpeg, string fichier)
    {
        var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-i", fichier, "-map", "0", "-f", "null", "-" }) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info)!;
        var erreurs = p.StandardError.ReadToEndAsync();
        var sortie = p.StandardOutput.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch { try { p.Kill(true); } catch (Exception) { } throw; }
        await sortie;
        return (p.ExitCode, await erreurs);
    }

    static void EcrireSinus(string chemin)
    {
        const int rate = 48000, secondes = 4;
        using var f = new BinaryWriter(File.Create(chemin));
        int octets = rate * secondes * 4;
        f.Write("RIFF"u8); f.Write(36 + octets); f.Write("WAVEfmt "u8); f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(rate); f.Write(rate * 4); f.Write((short)4); f.Write((short)16);
        f.Write("data"u8); f.Write(octets);
        for (int i = 0; i < rate * secondes; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5 * 32767);
            f.Write(v); f.Write(v);
        }
    }

    static string Police(string cle)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            var t = new Typeface(f, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return t.TryGetGlyphTypeface(out var g) ? string.Join("/", g.FamilyNames.Values) : "aucune police";
        }
        catch (Exception x) { return "erreur " + x.Message; }
    }

    /// <summary>Le poids réel (100 à 900) de la police trouvée pour un poids demandé : vérifie les fichiers Manrope inclus.</summary>
    static int Poids(string cle, FontWeight poids)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            return new Typeface(f, FontStyles.Normal, poids, FontStretches.Normal).TryGetGlyphTypeface(out var g) ? g.Weight.ToOpenTypeWeight() : 0;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>La police a-t-elle l'espace très fine (U+200A) que Capitales glisse entre les lettres ?</summary>
    static bool EspaceFine(string cle)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            return new Typeface(f, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal).TryGetGlyphTypeface(out var g) && g.CharacterToGlyphMap.ContainsKey(0x200A);
        }
        catch (Exception) { return false; }
    }

    /// <summary>Le pixel (B, V, R) de MyVirtualCam au point (x, y) compté depuis le haut (la mémoire est en lignes de bas en haut).</summary>
    static (byte B, byte G, byte R)? PixelCamera(int x, int y)
    {
        if (App.CameraVirtuelle == null) return null;
        using var mm = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
        using var vue = mm.CreateViewAccessor();
        int i = 64 + ((719 - y) * 1280 + x) * 3;
        return (vue.ReadByte(i), vue.ReadByte(i + 1), vue.ReadByte(i + 2));
    }

    static string Png(string nom, byte r, byte g, byte b)
    {
        var chemin = Path.Combine(App.Donnees.Dossier, nom);
        var dessin = new DrawingVisual();
        using (var dc = dessin.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), null, new Rect(0, 0, 1280, 720));
        var bmp = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bmp.Render(dessin);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(chemin)) png.Save(f);
        return chemin;
    }

    /// <summary>
    /// Siloé Stream 1.6.4 : MyStream (l'écran de Zoom et de YouTube) montre sa source principale, quoi que montre l'église ;
    /// « Envoyer dans le stream » (S) : il montre ce que montre l'église et la suit ; « Source principale » (M) l'y ramène.
    /// Le direct part vraiment (vers un serveur RTMP local : ffmpeg en écoute, comme YouTube), à la main, puis tout seul
    /// (direct automatique, à l'heure dite, arrêté après sa durée).
    /// </summary>
    static async Task TestMyStream(MainWindow w)
    {
        var R = App.Regie;
        bool Vert((byte B, byte G, byte R)? p) => p is { } q && q.G > 130 && q.R < 70 && q.B < 100;
        var cam = R.NouveauDisplay("Caméra de test", new SourceDisplay { Type = "image", Chemin = Png("mystream-camera.png", 20, 160, 60) });
        R.ChoisirSourceStream(cam.Id);
        R.ChoisirEcranDefaut("noir");
        R.Effacer();
        await Attendre(1500);
        Ok(w.TestEtatStream is { Mode: "media" } s0 && s0.Media == cam.Source, "1.6.0 : l'église vide : MyStream montre sa source principale");
        Ok(w.TestInfoStream.Contains("Caméra de test"), "1.6.4 : sous MyStream : " + w.TestInfoStream);
        if (App.CameraVirtuelle != null)
        {
            var c = PixelCamera(640, 360);
            Ok(Vert(c), $"1.6.0 : Zoom (MyVirtualCam) reçoit la source principale de MyStream ({c})");
        }
        // un verset à l'église : MyStream garde la caméra, sans paroles par-dessus
        R.AfficherVerset(42, 3, 16, 16);
        await Attendre(1500);
        Ok(w.TestEtatStream is { Mode: "media" } s1 && s1.Media == cam.Source && R.Etat.Mode == "verset",
            "1.6.4 : un verset à l'église : MyStream garde la caméra (sans les paroles)");
        if (App.CameraVirtuelle != null)
        {
            var haut = PixelCamera(640, 200);
            var bas = PixelCamera(90, 600);
            Ok(Vert(haut) && Vert(bas), $"1.6.4 : Zoom voit la caméra seule, sans bandeau de paroles ({haut} / {bas})");
        }
        Capture(w, "64-v164-mystream-camera.png");
        // 1.6.6 : le texte prend tout l'écran, sans jamais être coupé (même avec « Taille du texte » au plus grand)
        var zone = w.TestZoneTexteEglise;
        Ok(!zone.IsEmpty && zone.Left >= 0 && zone.Top >= 0 && zone.Right <= 1920.5 && zone.Bottom <= 1080.5 && zone.Width >= 0.6 * 1920,
            $"1.6.6 : le verset prend tout l'écran sans être coupé ({zone.Left:0},{zone.Top:0} — {zone.Width:0} × {zone.Height:0})");
        double tailleAvant = App.Donnees.Reglages.Taille;
        R.Taille(1.6);
        await Attendre(600);
        var zone2 = w.TestZoneTexteEglise;
        Ok(!zone2.IsEmpty && zone2.Left >= 0 && zone2.Top >= 0 && zone2.Right <= 1920.5 && zone2.Bottom <= 1080.5,
            $"1.6.6 : « Taille du texte » à 160 % : le texte reste dans l'écran ({zone2.Left:0},{zone2.Top:0} — {zone2.Width:0} × {zone2.Height:0})");
        R.Taille(tailleAvant);
        await Attendre(300);
        // « Envoyer dans le stream » (touche S) : le verset part sur MyStream, et la suite aussi
        w.TestEnvoyerStream();
        await Attendre(1500);
        Ok(R.StreamSuitEglise && w.TestEtatStream.Mode == "verset" && w.TestEnvoyerTexte == "L'ANTENNE SUIT L'ÉCRAN",
            $"1.6.4 : « Envoyer dans le stream » : le verset part sur MyStream (« {w.TestEnvoyerTexte} »)");
        if (App.CameraVirtuelle != null)
        {
            var c2 = PixelCamera(640, 360);
            Ok(!Vert(c2), $"1.6.4 : Zoom voit le verset envoyé dans le stream ({c2})");
        }
        Capture(w, "64b-v164-envoye-dans-le-stream.png");
        R.Bouger(1);
        await Attendre(800);
        Ok(R.StreamSuitEglise && w.TestEtatStream is { Mode: "verset" } s2 && s2.Ref.Contains("3:17"), "1.6.4 : le verset suivant part aussi sur MyStream : " + w.TestEtatStream.Ref);
        // « Source principale » (touche M) : MyStream revient sur la caméra, l'église garde le verset
        w.TestRetourStream();
        await Attendre(800);
        Ok(R.StreamSurPrincipale && w.TestEtatStream is { Mode: "media" } && R.Etat.Mode == "verset" && w.TestEnvoyerTexte == "ENVOYER À L'ANTENNE",
            "1.6.4 : « Source principale » : MyStream revient sur la caméra, l'église garde le verset");
        R.Bouger(1);
        await Attendre(800);
        Ok(R.StreamSurPrincipale && w.TestEtatStream is { Mode: "media" } && R.Etat.Ref.Contains("3:18"), "1.6.4 : le verset suivant reste dans l'église, MyStream garde la caméra");
        int lignes = w.TestMenuStream();
        Ok(lignes == R.SourcesStreamPossibles.Count + 4, $"1.6.4 : le menu « Source » sans les options des paroles ({lignes} lignes)");

        // ------------------------------------------------------------ le direct, pour de vrai, vers un serveur RTMP local
        w.TestOuvrirDirect();
        await Attendre(800);
        var fen = w.TestFenetreDirect;
        Ok(fen != null && fen.TestBouton is "Connecter le compte YouTube" or "Créer et démarrer le direct" && fen.TestTitre.Length > 0,
            $"1.6.0 : fenêtre du direct ouverte (« {fen?.TestBouton} », titre « {fen?.TestTitre} »)");
        if (fen != null) Capture(fen, "65-v160-youtube.png");
        string ffmpeg = App.Audio.Ffmpeg;
        bool directPossible = fen != null && File.Exists(ffmpeg) && App.Audio.Running && App.Audio.TracksActive;
        if (directPossible)
        {
            var (serveur, recu, port, journalServeur) = await ServeurRtmp(ffmpeg, "direct-recu.flv", "test");
            await fen!.TestDemarrerAutre($"rtmp://127.0.0.1:{port}/live", "test");
            for (int i = 0; i < 40 && App.Direct.Etat != Audio.EtatDirect.EnDirect; i++) await Attendre(250);
            await Attendre(5000);
            Ok(App.Direct.Etat == Audio.EtatDirect.EnDirect && w.TestDirectTexte.StartsWith("En direct"),
                $"1.6.0 : le direct part (« {w.TestDirectTexte} », encodeur {App.Direct.Encodeur}) : {App.Direct.Message}");
            Ok(w.TestVeille.EndsWith("PC"), "1.6.4 : pendant le direct, le PC reste éveillé (" + w.TestVeille + ")");
            Capture(fen, "66-v160-youtube-en-direct.png");
            Capture(w, "67-v160-en-direct.png");
            await App.Direct.ArreterAsync();
            await Attendre(500);
            Ok(App.Direct.Etat == Audio.EtatDirect.Arrete && w.TestDirectTexte == "YouTube", $"1.6.0 : Arrêter le direct (« {w.TestDirectTexte} ») : " + App.Direct.Message);
            await FinServeur(serveur, journalServeur);
            var (code, journal) = await Decrire(ffmpeg, recu);
            Note("direct reçu : " + string.Join(" | ", journal.Split('\n').Where(l => l.Contains("Duration") || l.Contains("Stream #")).Select(l => l.Trim())));
            // 1.6.4 : MyStream montre l'image fixe de la caméra de test (une couleur unie) : H.264 la compresse en très peu d'octets
            Ok(File.Exists(recu) && new FileInfo(recu).Length > 8_000 && journal.Contains("Video: h264") && journal.Contains("Audio: aac") && journal.Contains("1280x720"),
                $"1.6.0 : le serveur a reçu MyStream (H.264 1280 × 720) et le son (AAC) : {(File.Exists(recu) ? new FileInfo(recu).Length : 0)} octets");
        }
        else Note($"1.6.0 : direct réel non essayé (ffmpeg {File.Exists(ffmpeg)}, son {App.Audio.Running}/{App.Audio.TracksActive})");
        fen?.Close();
        await Attendre(300);

        // ------------------------------------------------------------ 1.6.4 : un direct automatique (chaque jour à l'heure qu'il est)
        var D = App.Donnees.Reglages.Direct;
        var maintenant = DateTime.Now;
        var auto = new DirectProgramme { Jours = new() { 0, 1, 2, 3, 4, 5, 6 }, Heure = maintenant.AddMinutes(-1).ToString("HH:mm"), Duree = 60, Qualite = "720p" };
        auto.Modele.Titre = "Culte du {date}";
        auto.Normaliser();
        Ok(auto.AFaire(maintenant) != null && App.Planificateur.Prochain(maintenant) == null, "1.6.4 : direct automatique préparé (" + auto.Quand + ")");
        if (directPossible)
        {
            var (serveur, recu, port, journalServeur) = await ServeurRtmp(ffmpeg, "direct-automatique.flv", "auto");
            D.Mode = ReglagesDirect.Autre;
            D.Serveur = $"rtmp://127.0.0.1:{port}/live";
            App.Direct.Coffre.Ecrire(AdresseDirect.CleAutre, "auto");
            D.Programmes.Add(auto);
            App.Donnees.SauverReglages();
            App.Planificateur.Modifie();
            await App.Planificateur.VerifierAsync(maintenant);
            for (int i = 0; i < 40 && App.Direct.Etat != Audio.EtatDirect.EnDirect; i++) await Attendre(250);
            await Attendre(4000);
            Ok(App.Direct.Etat == Audio.EtatDirect.EnDirect && App.Planificateur.EnCours == auto && auto.Dernier != null && App.Planificateur.ArretPrevu != null,
                $"1.6.4 : le direct automatique démarre tout seul à l'heure ({auto.Quand}) : {App.Direct.Message}");
            w.TestOuvrirDirect();
            await Attendre(800);
            var fen2 = w.TestFenetreDirect;
            Ok(fen2 != null && fen2.TestAutomatiques == 1 && fen2.TestTexteAutomatiques.Contains("En direct maintenant"),
                "1.6.4 : la fenêtre du direct le montre : " + fen2?.TestTexteAutomatiques);
            if (fen2 != null) Capture(fen2, "68-v164-direct-automatique.png");
            await App.Planificateur.VerifierAsync(maintenant.AddSeconds(20));
            Ok(App.Planificateur.EnCours == auto && App.Direct.EnDirect, "1.6.4 : un 2e tour à la même heure : le direct continue, rien ne repart");
            // après sa durée (1 h), il s'arrête tout seul
            await App.Planificateur.VerifierAsync(DateTime.Now.AddMinutes(61));
            for (int i = 0; i < 40 && App.Direct.Etat != Audio.EtatDirect.Arrete; i++) await Attendre(250);
            Ok(App.Direct.Etat == Audio.EtatDirect.Arrete && App.Planificateur.EnCours == null, "1.6.4 : après sa durée, le direct automatique s'arrête tout seul : " + App.Direct.Message);
            await App.Planificateur.VerifierAsync(DateTime.Now);
            await Attendre(500);
            Ok(!App.Direct.Actif && auto.Dernier != null, "1.6.4 : il ne repart pas le même jour");
            await FinServeur(serveur, journalServeur);
            var (code2, journal2) = await Decrire(ffmpeg, recu);
            Note("direct automatique reçu : " + string.Join(" | ", journal2.Split('\n').Where(l => l.Contains("Duration") || l.Contains("Stream #")).Select(l => l.Trim())));
            Ok(File.Exists(recu) && new FileInfo(recu).Length > 8_000 && journal2.Contains("Video: h264") && journal2.Contains("Audio: aac") && journal2.Contains("1280x720"),
                $"1.6.4 : le serveur a reçu le direct automatique (H.264 et AAC) : {(File.Exists(recu) ? new FileInfo(recu).Length : 0)} octets");
            fen2?.Close();
            D.Programmes.Remove(auto);
            App.Donnees.SauverReglages();
            App.Planificateur.Modifie();
        }
        else Note("1.6.4 : direct automatique réel non essayé (pas de ffmpeg ou pas de son)");
        // la fenêtre « Programmer un direct automatique » (jours, heure, arrêt, MyStream, qualité, réglages YouTube)
        await Fenetre(w, () => new ProgrammeDirectWindow(null), "69-v164-programmer-direct.png");
        R.ChoisirSourceStream(null);
        R.SupprimerAnnonce(cam.Id);
        await Attendre(500);
        Ok(w.TestEtatStream.Mode == "vide" && R.Etat.Mode != "vide" && w.TestInfoStream.Contains("noir"), "1.6.6 : « Aucune » : MyStream reste noir, l'église garde le verset : " + w.TestInfoStream);
        Capture(w, "64c-v166-mystream-noir.png");
        w.TestEnvoyerStream();
        await Attendre(500);
        Ok(w.TestEtatStream.Mode == R.EtatCamera(R.Etat).Mode, "1.6.6 : sans source principale, « Envoyer dans le stream » : MyStream montre l'église");
        w.TestRetourStream();
        await Attendre(300);
        Ok(!R.StreamSuitEglise && w.TestEtatStream.Mode == "vide", "1.6.6 : M : MyStream ne suit plus l'église (noir)");

        // 1.6.8 : un mot qui ne se coupe pas (une adresse courriel en gras) n'est plus rogné (« DIME ET OFFRAN »), à toutes les tailles
        var dime = R.NouvelleAnnonce("DIME ET OFFRANDE", "DIME ET OFFRANDE\nMEPCMTL01@GMAIL.COM");
        double tailleAvantDime = App.Donnees.Reglages.Taille;
        foreach (var t in new[] { 1.0, 0.5, 1.6 })
        {
            R.Taille(t);
            R.AfficherAnnonce(dime);
            await Attendre(700);
            var zd = w.TestZoneTexteEglise;
            Ok(w.TestTexteEntierEglise && !zd.IsEmpty && zd.Left >= 0 && zd.Right <= 1920.5,
                $"1.6.8 : « DIME ET OFFRANDE / MEPCMTL01@GMAIL.COM » entier à {t:P0} ({zd.Left:0} — {zd.Width:0} px de large)");
            if (t == 1.0) Capture(w, "64d-v168-texte-entier.png");
        }
        R.Taille(tailleAvantDime);
        R.Effacer();
        R.SupprimerAnnonce(dime.Id);
    }

    /// <summary>
    /// Siloé Stream 1.6.5 : une caméra USB (ici MyVirtualCam elle-même, une vraie caméra DirectShow quand elle est installée) : Siloé Stream
    /// essaie MJPG puis le format non compressé, aux images par seconde voulues, mesure, garde le meilleur ; les images arrivent.
    /// </summary>
    /// <summary>
    /// 1.7.0 : une fenêtre de la grille reste en direct dans sa tuile sans être à l'écran ; Réglages › À distance (coupé par défaut,
    /// activé : l'état demandé au relais, le relais dans le code QR).
    /// </summary>
    static async Task TestTuilesEtDistance(MainWindow w)
    {
        var R = App.Regie;
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        int lecteursAvant = w.TestLecteursTuiles, vivantesAvant = w.TestTuilesVivantes;
        var fen = R.NouveauDisplay("Régie Siloé Stream", new SourceDisplay { Type = "fenetre", Fenetre = h.ToInt64(), Processus = Environment.ProcessId, Titre = w.Title });
        R.GrilleAnnonce(fen.Id, true);
        R.Effacer();
        w.TestOngletAnnonces();
        for (int i = 0; i < 30 && w.TestTuilesVivantes <= vivantesAvant; i++) await Attendre(200);
        Ok(w.TestLecteursTuiles == lecteursAvant + 1 && w.TestTuilesVivantes > vivantesAvant && !(R.Cible is CibleDisplay cdf && cdf.Id == fen.Id),
            $"1.7.0 : une fenêtre de la grille reste en direct dans sa tuile sans être à l'écran ({w.TestTuilesVivantes} tuile(s) vivante(s))");
        Capture(w, "64e-v170-tuiles-vivantes.png");
        R.AfficherAnnonce(fen);
        await Attendre(400);
        Ok(App.Sources.Image != null, "1.7.0 : la fenêtre choisie apparaît tout de suite à l'écran (dernière image de sa tuile)");
        R.Effacer();
        R.SupprimerAnnonce(fen.Id);
        await Attendre(400);
        Ok(w.TestLecteursTuiles == lecteursAvant, "1.7.0 : tuile retirée : son lecteur s'arrête");

        var dossier = App.Donnees.Dossier;
        new ReglagesDistance { Actif = false, Relais = "127.0.0.1:9" }.Ecrire(dossier);
        w.TestPage("reglages");
        w.Reglages.Aller("distance", false);
        w.Reglages.MajDistance();
        await Attendre(900);
        Ok(w.Reglages.TestDistance.Contains("coupé"), "1.7.0 : Réglages › À distance : coupé par défaut (« " + w.Reglages.TestDistance + " »)");
        var r = ReglagesDistance.Lire(dossier);
        r.Actif = true;
        r.Ecrire(dossier);
        w.Reglages.MajDistance();
        for (int i = 0; i < 40 && !w.Reglages.TestDistance.Contains("injoignable"); i++) await Attendre(250);
        Ok(w.Reglages.TestDistance.Contains("injoignable"), "1.7.0 : activé, relais éteint : « injoignable » (« " + w.Reglages.TestDistance + " »)");
        Ok(App.Telephone.OuvrirJumelage().Contains("&relay=127.0.0.1%3A9"), "1.7.0 : contrôle à distance actif : le relais est dans le code QR");
        App.Telephone.FermerJumelage();
        Capture(w, "64f-v170-reglages-distance.png");
        new ReglagesDistance { Actif = false, Relais = ReglagesDistance.RelaisDefaut }.Ecrire(dossier);
        w.Reglages.MajDistance();
        w.TestOngletAnnonces();
    }

    static async Task TestCameraUsb()
    {
        // 1.6.7 : une caméra qui ne s'ouvre pas (ici : absente) n'arrête pas la source : un message, et un nouvel essai toutes les 2 secondes
        using (var lecteur = new SourcesDisplay(""))
        {
            int changements = 0;
            lecteur.Change += () => changements++;
            lecteur.ChoisirSource(new SourceDisplay { Type = "camera", Peripherique = "@device:absente-autotest" });
            await Attendre(4800);
            Ok(lecteur.Statut.Contains("réessaie") && lecteur.Image == null, "1.6.7 : caméra introuvable : la source attend et réessaie (« " + lecteur.Statut + " »)");
            Ok(Journal.Lignes().Count(l => l.Contains("@device:absente-autotest") && l.Contains("nouvel essai")) >= 2, "1.6.7 : plusieurs essais en 5 secondes (la source ne s'arrête pas)");
            lecteur.ChoisirSource(null);
        }
        var appareils = SourcesDisplay.Peripheriques(true);
        Ok(SourcesDisplay.Peripheriques().All(d => !SourcesDisplay.EstMyVirtualCam(d.Nom)), "1.6.8 : MyVirtualCam se choisit à part (liste des médias), pas parmi les caméras ordinaires");
        Note("caméras DirectShow : " + (appareils.Count == 0 ? "aucune" : string.Join(", ", appareils.Select(d => d.Nom))));
        var vcam = appareils.FirstOrDefault(d => d.Nom.Contains("MyVirtualCam", StringComparison.OrdinalIgnoreCase)) ?? appareils.FirstOrDefault();
        if (vcam == null) { Note("1.6.5 : pas de caméra DirectShow sur cet ordinateur : réglage non essayé"); return; }
        var source = new SourceDisplay { Type = "camera", Peripherique = vcam.Id, Ips = 30 };
        Ok(source.IpsVoulues == 30 && source.TailleVoulue == "1080p" && new SourceDisplay { Type = "camera" }.IpsVoulues == 60, "1.6.5 : 60 images par seconde par défaut, 30 au choix");
        var debut = DateTime.Now;
        using (var lease = Audio.CameraFrames.Acquire(vcam.Id, source))
        {
            for (int i = 0; i < 100 && lease.Image == null && lease.Error == null; i++) await Attendre(200);
            Ok(lease.Image != null, $"1.6.5 : « {vcam.Nom} » ouverte en {(DateTime.Now - debut).TotalSeconds:0.0} s : {Audio.CameraFrames.Info(vcam.Id)} {lease.Error}");
            int images = 0;
            var fin = DateTime.Now.AddSeconds(2);
            while (DateTime.Now < fin && lease.Error == null)
            {
                var prochaine = lease.Prochaine;
                if (await Task.WhenAny(prochaine, Task.Delay(600)) == prochaine) images++;
            }
            Ok(lease.Error == null && images >= 5, $"1.6.5 : les images arrivent dès qu'elles sont prêtes ({images} en 2 s)");
        }
        await Attendre(800);
        // 1.6.6 : elle reste ouverte, prête (une image fraîche) : elle revient tout de suite
        Ok(Audio.CameraFrames.Info(vcam.Id) != "", "1.6.6 : la caméra reste ouverte, prête, quand plus personne ne s'en sert : " + Audio.CameraFrames.Info(vcam.Id));
        var t0 = DateTime.Now;
        using (var reprise = Audio.CameraFrames.Acquire(vcam.Id, source))
            Ok(reprise.Image != null && reprise.Error == null, $"1.6.6 : elle revient tout de suite ({(DateTime.Now - t0).TotalMilliseconds:0} ms, image prête)");
    }

    /// <summary>Un serveur RTMP local (ffmpeg en écoute, comme YouTube) qui garde ce qu'il reçoit dans <paramref name="nom"/>.</summary>
    static async Task<(System.Diagnostics.Process Serveur, string Recu, int Port, Task<string> Journal)> ServeurRtmp(string ffmpeg, string nom, string cle)
    {
        var libre = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); libre.Start();
        int port = ((System.Net.IPEndPoint)libre.LocalEndpoint).Port; libre.Stop();
        string recu = Path.Combine(dossier, nom);
        var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "warning", "-y", "-listen", "1", "-i", $"rtmp://127.0.0.1:{port}/live/{cle}", "-c", "copy", "-f", "flv", recu }) info.ArgumentList.Add(a);
        var serveur = System.Diagnostics.Process.Start(info)!;
        var journal = serveur.StandardError.ReadToEndAsync();
        await Attendre(1500);
        return (serveur, recu, port, journal);
    }

    static async Task FinServeur(System.Diagnostics.Process serveur, Task<string> journal)
    {
        try { await serveur.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } catch (Exception) { try { serveur.Kill(true); } catch (Exception) { } }
        Note("serveur RTMP de test : " + (await journal).Trim());
        serveur.Dispose();
    }

    /// <summary>
    /// 2.2.0 : le montage dans la vraie application : un clip fabriqué par FFmpeg, ajouté, coupé, un fondu, un titre, un générique,
    /// annuler, puis un vrai export (vérifié par FFmpeg) et la fenêtre « Publier sur YouTube ».
    /// </summary>
    static async Task TestMontage(MainWindow w)
    {
        string ffmpeg = App.Audio.Ffmpeg;
        if (!File.Exists(ffmpeg)) { Note("2.2.0 : montage non essayé (ffmpeg absent : " + ffmpeg + ")"); return; }
        var dm = Path.Combine(dossier, "montage");
        Directory.CreateDirectory(dm);
        var clip = Path.Combine(dm, "culte-test.mkv");
        var (code, err, _) = await MyVerse.Montages.Processus.ExecuterAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=8", "-f", "lavfi", "-i", "sine=f=440:d=8:sample_rate=48000",
            "-c:v", "libx264", "-preset", "ultrafast", "-g", "30", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", clip }, TimeSpan.FromMinutes(2));
        Ok(code == 0 && File.Exists(clip), "2.2.0 : clip de test fabriqué" + (code == 0 ? "" : " : " + err));
        if (code != 0) return;
        w.TestPage("montage");
        await Attendre(900);
        var page = w.TestPageMontage;
        Ok(page != null, "2.2.0 : page Montage ouverte");
        if (page == null) return;
        page.TestNouveau();
        await page.TestAjouterVideo(clip);
        var p = page.TestProjet;
        Ok(p.Sequences.Count == 1 && Math.Abs(p.Duree - 8) < 0.2, $"2.2.0 : enregistrement ajouté à la piste vidéo ({p.Duree:0.00} s)");
        page.TestAller(3);
        page.TestCouper();
        Ok(p.Sequences.Count == 2 && Math.Abs(p.Sequences[0].Fin - 3) < 0.05 && Math.Abs(p.Duree - 8) < 0.2, "2.2.0 : couper à la tête de lecture (S)");
        page.TestAnnuler();
        p = page.TestProjet;
        Ok(p.Sequences.Count == 1, "2.2.0 : annuler (Ctrl Z) revient à une séquence");
        page.TestAller(3);
        page.TestCouper();
        p = page.TestProjet;
        p.Sequences[1].Debut = 5;
        p.Sequences[1].Transition = MyVerse.Montages.ProjetMontage.Fondu;
        p.Sequences[1].DureeTransition = 0.5;
        page.TestModifie();
        page.TestAller(1);
        page.TestTitre();
        page.TestGenerique();
        p = page.TestProjet;
        double attendu = 3 + 3 - 0.5 + p.Intro.Duree;
        Ok(p.Titres.Count == 1 && p.Intro.Actif && Math.Abs(p.Duree - attendu) < 0.05, $"2.2.0 : fondu, titre et générique ({p.Duree:0.00} s)");
        page.TestChoisir(null);
        page.TestAller(p.Intro.Duree + 1.5);
        await Attendre(1500);
        Capture(w, "70-v220-montage.png");
        var sortieExport = Path.Combine(dm, "export.mp4");
        var pub = MyVerse.Montages.Publications.Exporter(p, sortieExport, ffmpeg);
        var chrono = System.Diagnostics.Stopwatch.StartNew();
        while (pub.EnCours && chrono.Elapsed < TimeSpan.FromMinutes(4)) await Attendre(300);
        Ok(pub.Statut == "termine" && File.Exists(sortieExport), $"2.2.0 : export du montage ({chrono.Elapsed.TotalSeconds:0.0} s, {pub.Description}) : {pub.Message}");
        if (File.Exists(sortieExport))
        {
            var info = await MyVerse.Montages.AnalyseMedia.LireAsync(ffmpeg, sortieExport);
            Ok(info.Video && info.Audio && info.Largeur == 1280 && Math.Abs(info.Duree - attendu) < 0.2, $"2.2.0 : l'export se relit ({info.Resume}, {info.Duree:0.00} s)");
        }
        await Attendre(600);
        Capture(w, "71-v220-montage-export.png");
        await Fenetre(w, () => new Edition.PublicationWindow(p, () => { }), "72-v220-publier-youtube.png");
        // la recherche vocale : « Chant ou Bible ? »
        await Fenetre(w, () => new Voix.RechercheVocaleWindow(), "73-v220-recherche-vocale.png");
        // SILOE_TEST_MICRO=1 (un PC avec un micro) : le micro s'ouvre, une seconde est captée et mise en 16 kHz pour Whisper
        if (Environment.GetEnvironmentVariable("SILOE_TEST_MICRO") == "1")
        {
            try
            {
                using var micro = new Voix.EcouteMicro("");
                await Attendre(1200);
                var son = micro.Son16k();
                Ok(micro.Erreur == null && son.Length > 12000 && son.Length < 24000, $"2.2.0 : micro ouvert, {micro.Secondes:0.0} s captées → {son.Length} échantillons à 16 kHz");
            }
            catch (Exception x) { Echec("2.2.0 : micro : " + x.Message); }
        }
        // SILOE_TEST_VOIX_MODELE + SILOE_TEST_VOIX_WAV (WAV 16 kHz mono 16 bits) : Whisper dans l'application, puis la recherche
        if (Environment.GetEnvironmentVariable("SILOE_TEST_VOIX_MODELE") is { Length: > 0 } mod && Environment.GetEnvironmentVariable("SILOE_TEST_VOIX_WAV") is { Length: > 0 } wav && File.Exists(mod) && File.Exists(wav))
        {
            try
            {
                Directory.CreateDirectory(Voix.ModeleVoix.Dossier);
                if (!Voix.ModeleVoix.Present("base")) File.Copy(mod, Voix.ModeleVoix.Chemin("base"), true);
                var o = File.ReadAllBytes(wav);
                int d = 12;
                while (System.Text.Encoding.ASCII.GetString(o, d, 4) != "data") d += 8 + BitConverter.ToInt32(o, d + 4);
                var son = new float[BitConverter.ToInt32(o, d + 4) / 2];
                for (int i = 0; i < son.Length; i++) son[i] = BitConverter.ToInt16(o, d + 8 + 2 * i) / 32768f;
                var chronoVoix = System.Diagnostics.Stopwatch.StartNew();
                var texte = await Voix.ModeleVoix.ReconnaitreAsync(son, "base", "auto", RechercheVocale.Bible, CancellationToken.None);
                var res = await Task.Run(() => RechercheVocale.Chercher(App.Regie, texte, RechercheVocale.Bible, null));
                Ok(res.Count > 0 && res[0].Ref is { B: 42, C: 3, V1: 16 }, $"2.2.0 : Whisper dans Siloé Stream ({chronoVoix.Elapsed.TotalSeconds:0.0} s) : « {texte} » → {string.Join(", ", res.Take(3).Select(x => x.Titre))}");
            }
            catch (Exception x) { Echec("2.2.0 : Whisper : " + x); }
        }
    }

    public static async void Lancer(MainWindow w, string sortie)
    {
        dossier = sortie;
        Directory.CreateDirectory(sortie);
        var R = App.Regie;
        try
        {
            // 2.2.0 : SILOE_AUTOTEST_SEUL=montage : seulement le montage (essai rapide sur un PC de développement)
            if (Environment.GetEnvironmentVariable("SILOE_AUTOTEST_SEUL") == "montage")
            {
                await TestMontage(w);
                goto Fin;
            }

            Note($"Siloé Stream {App.Version} · données : {App.Donnees.Dossier} · {App.Donnees.Chants.Count} chants");
            // 4.2 : au premier lancement, Siloé Stream est vide ; seules les Bibles libres de droits LSG 1910 et King James sont livrées
            Ok(App.Donnees.Chants.Count == 0 && App.Audio.Config.Sons.Count == 1 && App.Audio.Config.Sons[0].Genre == GenreSon.Video && App.Donnees.Annonces.Count == 0,
                $"4.2 : premier lancement vide ({App.Donnees.Chants.Count} chants, {App.Audio.Config.Sons.Count} son : le son des vidéos)");
            Ok(App.Bibles.Toutes().Select(b => b.Id).SequenceEqual(new[] { "lsg", "kjv" }), "4.2 : Bibles livrées : " + string.Join(", ", App.Bibles.Toutes().Select(b => b.Nom)));
            // 1.6.6 : sans source principale, MyStream reste noir ; les essais d'avant (MyVirtualCam = ce que montre l'église) le font
            // suivre l'église (« Envoyer dans le stream ») ; « Aucune » choisie exprès : une caméra montrée ne devient pas sa source principale
            Ok(R.SourcePrincipaleStream == null && !R.StreamSuitEglise && R.EtatStream(R.Etat).Mode == "vide", "1.6.6 : premier lancement : MyStream noir (rien de choisi)");
            R.ChoisirSourceStream(null);
            R.EnvoyerDansStream();
            // des chants pour la suite du test (comme un recueil importé par l'utilisateur)
            R.AjouterChants(new[]
            {
                new Chant { Title = "12. Grâce infinie", Sections = { new Section { Label = "Couplet 1", Text = "Grâce infinie, ô quel doux son\nQui sauva un pécheur" }, new Section { Label = "Refrain", Text = "Gloire à Dieu\nGloire à Dieu" } } },
                new Chant { Title = "45. Mon Dieu est bon", Sections = { new Section { Label = "Couplet 1", Text = "Mon Dieu est bon\nIl est bon tout le temps" }, new Section { Label = "Couplet 2", Text = "Il est fidèle\nÀ toujours" } } },
                new Chant { Title = "154. À toi la gloire", Sections = { new Section { Label = "Couplet 1", Text = "À toi la gloire\nÔ Ressuscité" }, new Section { Label = "Refrain", Text = "À toi la gloire" } } },
            }, "Recueil de test");
            Note("police Serif : " + Police("Serif") + " · Sans : " + Police("Sans") + " · Titre : " + Police("Titre"));
            Note("police de l'interface : " + Police("Ui") + " · titres : " + Police("Grotesk") + " · chiffres : " + Police("Mono"));
            Ok(Police("Ui").Contains("Manrope") && Police("Grotesk").Contains("Manrope"), "police Manrope incluse (interface 3.2, design « Pupitre »)");
            int p5 = Poids("Ui", FontWeights.Medium), p6 = Poids("Ui", FontWeights.SemiBold), p7 = Poids("Ui", FontWeights.Bold), p8 = Poids("Ui", FontWeights.ExtraBold);
            Ok(p5 == 500 && p6 == 600 && p7 == 700 && p8 == 800, $"Manrope : les vrais poids 500, 600, 700, 800 ({p5}, {p6}, {p7}, {p8})");
            Ok(Police("Mono").Contains("IBM Plex Mono"), "police IBM Plex Mono incluse (heures et chronos)");
            Ok(EspaceFine("Ui"), "Manrope : espace très fine U+200A des étiquettes en capitales espacées");
            Ok(Police("Serif").Contains("Literata"), "police Literata incluse");
            Ok(Police("Sans").Contains("Atkinson"), "police Atkinson incluse");
            await Attendre(1500);
            Capture(w, "00-accueil-culte.png");
            // Les anciens réglages OBS ne doivent ni simuler un écran ouvert, ni polluer l'aperçu.
            App.Donnees.Reglages.Obs.Actif = true;
            App.Donnees.Reglages.Ecran = new EcranChoisi { Mode = "obs" };
            Ok(!R.EcranOuvert() && !w.TestStatutProjection.Contains("OBS"), "projection indépendante : anciens réglages OBS ignorés");
            App.Donnees.Reglages.Obs.Actif = false;
            w.TestOngletAnnonces();
            Ok(w.TestMenuSources(), "Display : menu déroulant d'ajout et commandes accessibles");
            // 2.1.0 : « Commencer le culte » : le projecteur, YouTube, le son et les caméras sont vérifiés (sans rien lancer ici)
            var verifs = w.TestVerifierCulte;
            Note("2.1.0 : vérifications : " + verifs);
            Ok(verifs.StartsWith("Projecteur:") && verifs.Contains("|YouTube:") && verifs.Contains("|Son:") && verifs.Contains("|Caméras:") && w.TestCommencerTexte == "Commencer le culte",
                "2.1.0 : Commencer le culte vérifie le projecteur, YouTube, le son et les caméras");
            Capture(w, "00b-display-menu.png");
            w.TestOnglet(true);
            await Attendre(400);
            Capture(w, "01-bible.png");

            w.TestRecherche("jn 3 16");
            await Attendre(300);
            w.TestAfficherSelection();
            await Attendre(500);
            Ok(R.Etat.Ref == "Jean 3:16 (LSG)", "Jean 3:16 à l'écran : " + R.Etat.Ref);
            Capture(w, "02-verset.png");

            var ecrans = Natif.Ecrans();
            Note("écrans : " + string.Join(" | ", ecrans.Select(e => $"{e.Nom} {e.W}×{e.H} @ {e.X},{e.Y}{(e.Principal ? " principal" : "")}")));
            w.OuvrirSur(ecrans[0], true);
            await Attendre(1500);
            Ok(w.Ecran != null, "fenêtre de projection ouverte");
            Capture(w.Ecran, "03-ecran.png");
            R.Bouger(1);
            await Attendre(1300);
            Ok(R.Etat.Ref == "Jean 3:17 (LSG)", "Suivant → " + R.Etat.Ref);
            Capture(w.Ecran, "04-ecran-suivant.png");
            R.Presentation("bandeau");
            R.Theme("vert");
            await Attendre(1300);
            Capture(w.Ecran, "05-ecran-bandeau-vert.png");
            R.Presentation("plein");
            R.Theme("aube");
            App.Bibles.BasculerSeconde("kjv");
            await Attendre(1300);
            Ok(R.Etat.Second != null && R.Etat.Ref.Contains("LSG · KJV"), "versets bilingues : " + R.Etat.Ref);
            Capture(w, "06-bilingue.png");
            Capture(w.Ecran, "07-ecran-bilingue.png");
            App.Bibles.BasculerSeconde("kjv");
            R.Theme("nuit");

            w.TestRecherche("charite pasiente");
            await Attendre(3000);
            Capture(w, "08-recherche.png");

            w.TestOnglet(false);
            await Attendre(400);
            var c = w.TestPremierChant();
            Ok(c != null, "liste des chants : " + (c?.Title ?? "vide"));
            if (c != null)
            {
                w.OuvrirChant(c.Id);
                R.AfficherSection(c.Id, 0);
            }
            await Attendre(1300);
            Ok(R.Etat.Mode == "chant", "chant à l'écran");
            Capture(w, "09-chants.png");
            Capture(w.Ecran, "10-ecran-chant.png");

            // recherche par numéro dans le recueil choisi (un numéro à 2 chiffres : pas de 116 ni de 160)
            var nums = w.TestNumero("").Select(x => Regie.Numero(x.Title)).ToList();
            var cible = nums.FirstOrDefault(x => x >= 10 && x <= 99) ?? nums.FirstOrDefault(x => x != null);
            if (cible is int nn)
            {
                var l = w.TestNumero(nn.ToString());
                Ok(l.Count >= 1 && l.All(x => Regie.Numero(x.Title) == nn), $"recherche par numéro {nn} : " + string.Join(" | ", l.Select(x => x.Title)));
                await Attendre(500);
                Capture(w, "09b-numero.png");
                var vide = w.TestNumero("99999");
                Ok(vide.Count == 0, "numéro absent : liste vide");
                w.TestNumero("");
            }

            // 5.0.1 : boutons « + » devant les chants (liste) et les versets (Bible)
            var agPlus = R.NouvelAgenda("Test des boutons +");
            int ajC = w.TestPlusPremierChant(), ajV = w.TestPlusPremierVerset();
            Ok(ajC == 1 && ajV == 1 && R.Agenda.Elements.Count == 2, $"boutons + devant les chants et les versets : {ajC} / {ajV}");
            R.SupprimerAgenda(agPlus.Id);

            // 5.2 (design « Aube ») : déroulé du culte, recherche Ctrl K, compte à rebours d'accueil, résumé, displays, écran de scène
            w.TestOngletAgenda();
            var agTest = R.NouvelAgenda("Culte de test");
            var compte = R.AjouterCompteAgenda(5);
            if (c != null) R.AjouterChantAgenda(c.Id);
            var lecture = R.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
            R.HeureAgenda(lecture.Id, "10h30");
            R.AjouterTexteAgenda("Annonces", "Bienvenue à tous !\n\nRepas fraternel après le culte");
            Ok(R.Agenda.Elements.Count == (c != null ? 4 : 3) && R.Agenda.Elements[0].Type == "compte", $"déroulé : {R.Agenda.Elements.Count} éléments, le compte à rebours en premier");
            Ok(lecture.Heure == "10:30", "heure prévue : " + lecture.Heure);
            R.OublierResume();
            R.AfficherElement(compte, 0);
            await Attendre(1300);
            var lc = R.Etat.Lignes;
            Ok(lc.Count == 2 && lc[0].StartsWith("Le culte commence") && (lc[1] == "05:00" || lc[1].StartsWith("04:5")), "compte à rebours : " + string.Join(" / ", lc));
            await Attendre(1200);
            Ok(R.Etat.Lignes.Count == 2 && R.Etat.Lignes[1].StartsWith("04:5"), "le compte à rebours descend : " + string.Join(" / ", R.Etat.Lignes));
            if (w.Ecran != null) Capture(w.Ecran, "19b-ecran-compte.png");
            var cm = w.TestCommande("jean 3 16");
            Ok(cm.Count > 0 && cm[0].Titre == "Jean 3:16", "recherche du culte (Ctrl K) : " + string.Join(" | ", cm.Select(x => x.Titre)));
            w.TestCommande("");
            // 1.5.1 : Entrée (ou un clic) sur un résultat emmène à sa place, sans rien montrer à l'écran
            w.TestAllerCommande("jean 3 16");
            await Attendre(300);
            Ok(w.TestPageOuverte == "bible" && w.TestSelection is Ref selAller && selAller.B == 42 && selAller.C == 3 && selAller.V1 == 16 && R.Cible is not CibleVerset,
                "1.5.1 : Entrée sur « jean 3 16 » : Bible › Jean 3, le verset 16 choisi, rien de montré à l'écran");
            Capture(w, "63-v151-recherche-aller-verset.png");
            if (c != null)
            {
                w.TestAllerCommande(c.Title);
                await Attendre(300);
                Ok(w.TestPageOuverte == "chants" && w.TestChantOuvert == c.Id && R.Cible is not CibleChant, "1.5.1 : Entrée sur un chant : Bibliothèque › Chants, le chant ouvert, rien de montré à l'écran");
            }
            w.TestPage("agenda");
            await Attendre(300);
            for (int i = 0; i < 15 && R.ElementCourant?.Type != "bible"; i++) R.Bouger(1);
            Ok(R.ElementCourant?.Type == "bible" && R.Etat.Ref.StartsWith("Jean 3:16"), "déroulé : Suivant passe à la lecture · " + R.Etat.Ref);
            App.Bibles.BasculerSeconde("kjv");
            await Attendre(900);
            int nbVignettes = R.Agenda.Elements.Sum(e => R.Diapos(e).Count);
            Ok(w.TestVignettes == nbVignettes && nbVignettes >= 5, $"page Culte : toutes les diapositives du culte en vignettes ({w.TestVignettes} / {nbVignettes})");
            Ok(w.TestVignetteDirecte.StartsWith("Jean 3:16") && w.TestVignetteDirecte.EndsWith("À l'écran"), "vignette à l'écran : " + w.TestVignetteDirecte);
            Ok(w.TestVignetteSuivante == "Jean 3:17" && w.TestSuivantLabel == "Jean 3:17", $"vignette suivante et aperçu « Suivant » : {w.TestVignetteSuivante} / {w.TestSuivantLabel}");
            Capture(w, "19-culte.png");
            App.Bibles.BasculerSeconde("kjv");
            Ok(R.EstFait(compte) && w.TestResume.Contains("Accueil") && w.TestResume.Contains("Jean 3"), "résumé du culte : " + w.TestResume);
            Ok(R.DeroulePartage().Contains("10:30") && R.ResumePartage().Length > 0, "textes à partager sur WhatsApp");
            for (int i = 0; i < 5 && R.ElementCourant?.Type != "texte"; i++) R.Bouger(1);
            Ok(R.ElementCourant?.Type == "texte" && R.Etat.Lignes.FirstOrDefault() == "Bienvenue à tous !", "déroulé : puis les displays");
            await Attendre(500);
            Capture(w, "20-culte-displays.png");
            if (w.Ecran != null) Capture(w.Ecran, "21-ecran-displays.png");
            w.TestNoir();
            await Attendre(600);
            Ok(w.TestEstNoir, "écran noir (touche B)");
            R.Bouger(1);
            await Attendre(300);
            Ok(!w.TestEstNoir, "la diapositive suivante rallume l'écran");

            // 3.3 : écran par défaut — un écran principal dans le déroulé ; Retirer de l'écran revient direct dessus
            Ok(!w.TestBarreLarge, "1.5.0 : barre de gauche étroite, trois pages (Culte, Bibliothèque, Son) (" + (int)w.ActualWidth + " px)");
            var principal = R.AjouterPrincipalAgenda(null);
            await Attendre(400);
            Ok(principal != null && w.TestEcranDefautChoix.Split(" | ").Count(x => x.StartsWith("Fond seul")) == 2,
                "écran principal ajouté au déroulé, proposé dans « Écran par défaut » : " + w.TestEcranDefautChoix);
            if (principal != null)
            {
                var avantRetrait = R.Etat.Lignes.FirstOrDefault();
                Ok(w.TestChoisirEcranDefaut(principal.Id) && R.EcranDefaut == principal.Id, "clic sur l'écran principal dans « Écran par défaut »");
                Ok(R.Etat.Lignes.FirstOrDefault() == avantRetrait && w.TestCulteRetour.EndsWith(principal.Titre),
                    "le choix ne touche pas aux paroles à l'écran · " + w.TestCulteRetour);
                R.Effacer();
                await Attendre(900);
                Ok(R.Cible is CibleDefaut && R.Etat.Mode == "fond" && R.EtatDiffusion(R.Etat).Vide, "Retirer de l'écran : retour direct sur l'écran par défaut (fond seul), OBS vide");
                Ok(w.TestVignetteDirecte.Contains("À l'écran") && w.TestCulteAEcran.Contains("repos"), "vignette et écran live : " + w.TestVignetteDirecte + " · " + w.TestCulteAEcran);
                Capture(w, "21b-culte-ecran-par-defaut.png");
                if (w.Ecran != null) Capture(w.Ecran, "21c-ecran-par-defaut.png");
                R.Bouger(-1);
                await Attendre(400);
                Ok(R.Etat.Lignes.FirstOrDefault() == avantRetrait, "Précédent remet ce qui a été retiré : " + string.Join(" / ", R.Etat.Lignes));
                w.TestChoisirEcranDefaut("noir");
                R.RetirerAgenda(principal.Id);
                await Attendre(300);
                Ok(R.EcranDefaut == "noir" && R.Etat.Lignes.FirstOrDefault() == avantRetrait, "écran par défaut remis à noir, écran principal retiré du déroulé");
            }

            // 4.0 : MyCam — l'image JPEG d'un téléphone (comme reçue par sa connexion) s'affiche dans Display, témoin rouge
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2A, 0x33, 0x46), Color.FromRgb(0xC8, 0x96, 0x2E), 30), null, new Rect(0, 0, 640, 360));
                    dc.DrawText(new FormattedText("MyCam · image du téléphone", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 34, Brushes.White, 1.0), new Point(40, 160));
                }
                var rtb = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new JpegBitmapEncoder { QualityLevel = 82 };
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                enc.Save(ms);
                var jpeg = ms.ToArray();
                var msg = new byte[jpeg.Length + 4];
                msg[0] = (byte)'M'; msg[1] = (byte)'C'; msg[2] = 1; msg[3] = 0;
                jpeg.CopyTo(msg, 4);
                var cams = App.Telephone.Cameras;
                cams.Infos("autotest-cam", "Téléphone test", System.Text.Json.Nodes.JsonNode.Parse("""{"nom":"Caméra · Chaire","taille":"640 × 360","diffuse":true,"batterie":88,"lien":"wifi"}""")!.AsObject());
                var cam = R.NouveauDisplay("Caméra · Chaire", new SourceDisplay { Type = "mycam", Peripherique = "autotest-cam" });
                R.GrilleAnnonce(cam.Id, true);
                R.AfficherAnnonce(cam);
                for (int k = 0; k < 12; k++)
                {
                    cams.Recevoir("autotest-cam", "Téléphone test", msg, msg.Length);
                    await Attendre(80);
                }
                await Attendre(600);
                Ok(App.Sources.Image is { PixelWidth: 640, PixelHeight: 360 }, "MyCam : l'image du téléphone est à l'écran (" + App.Sources.Statut + ")");
                Ok(R.TemoinMyCam("autotest-cam") == "live", "MyCam : témoin rouge pour le téléphone");
                Capture(w, "21d-mycam-direct.png");
                if (w.Ecran != null) Capture(w.Ecran, "21e-mycam-ecran.png");
                // 4.0.1 : OBS lit display.jpg pendant qu'on le remplace (Windows : « accès refusé ») ; la caméra ne doit ni s'arrêter ni passer au noir
                string jpgObs = Path.Combine(App.SortieObs.Dossier, "display.jpg");
                if (File.Exists(jpgObs))
                {
                    var avantVerrou = App.Sources.Image;
                    using (var verrou = new FileStream(jpgObs, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        for (int k = 0; k < 12; k++)
                        {
                            cams.Recevoir("autotest-cam", "Téléphone test", msg, msg.Length);
                            await Attendre(120);
                        }
                    }
                    await Attendre(300);
                    Ok(App.Sources.Image != null && !ReferenceEquals(App.Sources.Image, avantVerrou) && App.Sources.Statut == "En direct",
                        "4.0.1 : display.jpg tenu ouvert par OBS (accès refusé) : MyCam continue (" + App.Sources.Statut + ")");
                }
                else Note("display.jpg absent : essai du fichier tenu par OBS non fait");
                // téléphone tenu à la verticale, image entière : en hauteur, avec un fond flou sur l'écran
                var debout = (byte[])msg.Clone();
                debout[3] = 1;
                for (int k = 0; k < 6; k++)
                {
                    cams.Recevoir("autotest-cam", "Téléphone test", debout, debout.Length);
                    await Attendre(80);
                }
                await Attendre(500);
                Ok(App.Sources.Image is { PixelWidth: 360, PixelHeight: 640 }, "MyCam en hauteur : image tournée d'un quart de tour (" + App.Sources.Image?.PixelWidth + " × " + App.Sources.Image?.PixelHeight + ")");
                if (w.Ecran != null) Capture(w.Ecran, "21f-mycam-en-hauteur.png");
                // 4.1 : MyCam en H.264, comme DroidCam : le FFmpeg d'OBS livré avec Siloé Stream (obs\bin\64bit\avcodec-*.dll) décode la vidéo du téléphone
                Ok(MyCams.H264, "MyCam H.264 : décodeur prêt (" + DecodeurH264.Etat + ")");
                if (MyCams.H264)
                {
                    byte[] Au(byte[] au, bool cle, int rotation)
                    {
                        var b = new byte[au.Length + 4];
                        b[0] = (byte)'M'; b[1] = (byte)'C'; b[2] = 2; b[3] = (byte)(rotation | (cle ? 8 : 0));
                        au.CopyTo(b, 4);
                        return b;
                    }
                    for (int k = 0; k < DecodeurH264.Mire.Length; k++)
                    {
                        var au = Au(DecodeurH264.Mire[k], k == 0, 0);
                        cams.Recevoir("autotest-cam", "Téléphone test", au, au.Length);
                        await Attendre(120);
                    }
                    await Attendre(400);
                    var vue = App.Sources.Image;
                    var pixel = new byte[4];
                    if (vue is { PixelWidth: 64, PixelHeight: 48 }) vue.CopyPixels(new Int32Rect(32, 24, 1, 1), pixel, 4, 0);
                    var attendu = DecodeurH264.CouleursMire[^1];
                    Ok(vue is { PixelWidth: 64, PixelHeight: 48 } && DecodeurH264.Proche(pixel, 1, 0, 0, attendu),
                        $"MyCam H.264 : vidéo décodée à l'écran ({vue?.PixelWidth} × {vue?.PixelHeight}, pixel {pixel[0]},{pixel[1]},{pixel[2]})");
                    Ok(cams.InfoDe("autotest-cam") is { Codec: "H.264" }, "MyCam H.264 : format affiché (" + cams.InfoDe("autotest-cam")?.Resume + ")");
                    if (w.Ecran != null) Capture(w.Ecran, "21g-mycam-h264.png");
                }
                // 1.2 : un enregistrement vidéo de MyCam garde ses 30 images par seconde (la composition ne passe plus par l'interface)
                if (App.Audio.Running && App.Audio.TracksActive)
                {
                    // 12 gris en JPEG, envoyés à 30 i/s avec leur âge (100 ms), comme un téléphone en Siloé Stream 1.2
                    var gris = new List<byte[]>();
                    for (int k = 0; k < 12; k++)
                    {
                        var vg = new DrawingVisual();
                        byte niveau = (byte)(20 + k * 19);
                        using (var dc = vg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(niveau, niveau, niveau)), null, new Rect(0, 0, 640, 360));
                        var bg = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
                        bg.Render(vg);
                        var eg = new JpegBitmapEncoder { QualityLevel = 85 };
                        eg.Frames.Add(BitmapFrame.Create(bg));
                        using var mg = new MemoryStream();
                        eg.Save(mg);
                        var jg = mg.ToArray();
                        var image = new byte[jg.Length + 8];
                        const int age = 100_000;
                        image[0] = (byte)'M'; image[1] = (byte)'C'; image[2] = 1; image[3] = MyCams.DrapeauAge;
                        image[4] = age & 0xFF; image[5] = (age >> 8) & 0xFF; image[6] = (age >> 16) & 0xFF; image[7] = 0;
                        jg.CopyTo(image, 8);
                        gris.Add(image);
                    }
                    var cfgCam = App.Audio.Config;
                    var profilCam = new MyVerse.Audio.RecordingProfile { Name = "MyCam (test)", Track = 1, DisplayIds = { cam.Id }, Directory = Path.Combine(Path.GetTempPath(), "siloestream-autotest-mycam") };
                    cfgCam.Profiles.Add(profilCam);
                    App.Audio.Apply();
                    using var envoi = new CancellationTokenSource();
                    var telephone = Task.Run(() =>
                    {
                        long debut = Horloge.Ns();
                        int n = 0;
                        while (!envoi.IsCancellationRequested)
                        {
                            long quand = debut + n * Horloge.Seconde / 30;
                            while (Horloge.Ns() < quand) Thread.Sleep(1);
                            var g = gris[n % gris.Count];
                            cams.Recevoir("autotest-cam", "Téléphone test", g, g.Length);
                            n++;
                        }
                        return n;
                    });
                    await Attendre(800);
                    // 1.9.4 : la machine de GitHub est parfois très chargée (18 à 21 i/s mesurées, 29 d'habitude, même code) :
                    // un deuxième enregistrement si le premier est trop saccadé ; 26 i/s doivent être atteintes une fois
                    bool demarre = false;
                    string? fichierCam = null;
                    double ips = 0;
                    int total = 0;
                    for (int essai = 1; essai <= 2 && ips < 26; essai++)
                    {
                        if (essai > 1) await Attendre(500);
                        await App.Audio.Recorder.ToggleAsync(profilCam);
                        demarre = App.Audio.Recorder.Active(profilCam.Id);
                        Ok(demarre, "1.2 : enregistrement vidéo de MyCam démarré : " + App.Audio.Recorder.Message(profilCam.Id));
                        if (!demarre) break;
                        await Attendre(4500);
                        await App.Audio.Recorder.ToggleAsync(profilCam);
                        fichierCam = App.Audio.Recorder.LastFile(profilCam.Id);
                        if (fichierCam == null || !File.Exists(fichierCam)) break;
                        (ips, total) = await ImagesDifferentes(App.Audio.Ffmpeg, fichierCam);
                        if (ips < 26 && essai == 1) Note($"1.2 : MyCam à {ips:0.0} images différentes par seconde au premier enregistrement (machine chargée ?) : deuxième essai");
                    }
                    envoi.Cancel();
                    int envoyees = await telephone;
                    if (demarre && fichierCam != null && File.Exists(fichierCam))
                        Ok(ips >= 26, $"1.2 : MyCam enregistrée à {ips:0.0} images différentes par seconde (téléphone : 30 i/s, {envoyees} images envoyées, {total} dans le fichier)");
                    cfgCam.Profiles.Remove(profilCam);
                    App.Audio.Apply();
                }
                else Note("pistes d'OBS inactives : essai de l'enregistrement de MyCam non fait");
                R.Effacer();
                await Attendre(500);
                Ok(R.TemoinMyCam("autotest-cam") == "off", "MyCam retirée : témoin éteint");
                // la suite de l'autotest compte les tuiles de la grille : la source de test repart
                R.SupprimerAnnonce(cam.Id);
                cams.Deconnecte("autotest-cam");
                await Attendre(300);
            }

            // fonds clairs Lin et Ciel
            R.AfficherElement(lecture, 0);
            R.Theme("lin");
            await Attendre(1300);
            if (w.Ecran != null) Capture(w.Ecran, "22-ecran-lin.png");
            R.Theme("ciel");
            await Attendre(1300);
            if (w.Ecran != null) Capture(w.Ecran, "22b-ecran-ciel.png");
            R.Theme("aube");

            // écran de scène
            var scene = w.TestScene();
            await Attendre(1200);
            Ok(scene != null, "écran de scène ouvert");
            Capture(scene, "23-scene.png");
            try { scene?.Close(); } catch (Exception) { }

            // bibliothèque d'displays
            var an = R.NouvelleAnnonce("Repas fraternel", "Repas fraternel après le culte\nTout le monde est invité");
            w.TestOngletAnnonces();
            w.TestChoisirAnnonce(an);
            await Attendre(600);
            Capture(w, "24-displays.png");
            int avantAn = R.Agenda.Elements.Count;
            var elAn = R.AjouterAnnonceAgenda(an.Id);
            Ok(elAn != null && R.Agenda.Elements.Count == avantAn + 1 && elAn.Type == "texte", "display ajoutée au déroulé");
            R.AfficherAnnonce(an);
            await Attendre(300);
            Ok(R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "display à l'écran : " + string.Join(" / ", R.Etat.Lignes));
            // Siloé Stream 6: the same double-click handler sends the selected title live.
            R.Effacer();
            w.TestDisplayDoubleClick(an);
            await Attendre(300);
            Ok(R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "double-clic titre Display : en direct");
            var imagePath = Path.Combine(App.Donnees.Dossier, "test-display.png");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(200, 30, 40)), null, new Rect(0, 0, 1280, 720));
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var f = File.Create(imagePath)) png.Save(f);
            var img = R.NouveauDisplay("Image de test", new SourceDisplay { Type = "image", Chemin = imagePath });
            w.TestChoisirAnnonce(img); w.TestDisplayDoubleClick(img);
            await Attendre(1300);
            Ok(R.Etat.Mode == "media" && App.Sources.Image != null, "image Display chargée et projetée");
            Capture(w, "25-display-image.png");
            // 1.0.1 : grille de sources (un clic sur une tuile = en direct)
            R.GrilleAnnonce(an.Id, true);
            R.GrilleAnnonce(img.Id, true);
            await Attendre(400);
            Ok(w.TestTuiles == 2, "grille Display : " + w.TestTuiles + " tuiles (un texte, une image)");
            Ok(w.TestCliquerTuile(0) && R.Cible is CibleTexte { I: 0 } ct && ct.Element == an.Id && R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "clic sur une tuile de la grille : en direct");
            await Attendre(500);
            Capture(w, "27-display-grille.png");

            // 1.3 : le son simple (page Son), sur le vrai moteur d'OBS
            await TestSon(w, an, img);
            w.TestPage("annonces");
            w.TestCliquerTuile(1);
            await Attendre(1300);
            Ok(R.Etat.Mode == "media" && App.Sources.Image != null, "clic sur la tuile image : en direct");
            if (w.Ecran != null) Capture(w.Ecran, "26-ecran-image.png");
            Ok(File.Exists(Path.Combine(App.SortieObs.Dossier, "display.jpg")), "image Display publiée pour la source navigateur OBS");
            if (App.CameraVirtuelle != null)
            {
                using var mm = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
                using var view = mm.CreateViewAccessor();
                Ok(view.ReadInt32(0) == 0x4D43414D && view.ReadInt32(16) == 1280 && view.ReadInt64(8) > 0, "MyVirtualCam publie automatiquement en 720p");
                var pixel = 64 + (360 * 1280 + 640) * 3;
                Ok(view.ReadByte(pixel) == 40 && view.ReadByte(pixel + 1) == 30 && view.ReadByte(pixel + 2) == 200, "MyVirtualCam reçoit les pixels réels de la projection");
            }
            // 1.3.1 : où va une source. « Dans l'église » : l'écran de projection seulement, MyVirtualCam reste noire (Zoom projeté
            // ne revient pas dans Zoom) ; « En streaming » : MyVirtualCam seulement ; « Les deux » : partout, comme avant
            if (App.CameraVirtuelle != null && img.Source is { } sourceImage)
            {
                using var mmDif = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
                using var vueDif = mmDif.CreateViewAccessor();
                int milieu = 64 + (360 * 1280 + 640) * 3;
                bool Rouge() => vueDif.ReadByte(milieu) == 40 && vueDif.ReadByte(milieu + 1) == 30 && vueDif.ReadByte(milieu + 2) == 200;
                R.ModifierSource(img.Id, "", sourceImage with { Diffusion = SourceDisplay.Eglise });
                await Attendre(1300);
                Ok(R.Etat.Mode == "media" && R.EtatSalle(R.Etat).Media?.EgliseSeulement == true && App.Sources.Image != null && !Rouge(),
                    "1.3.1 : source « Dans l'église » : à l'écran de projection, MyVirtualCam ne la reçoit pas");
                Ok(w.TestStatutProjection is "Église seulement" or "Aperçu seulement", "1.3.1 : pastille d'une source « Dans l'église » : " + w.TestStatutProjection);
                // 1.3.2 : à la place, MyVirtualCam (Zoom) montre l'écran par défaut, ici une image verte, au lieu d'un écran noir
                var vertPath = Path.Combine(App.Donnees.Dossier, "test-ecran-defaut.png");
                var dessinVert = new DrawingVisual();
                using (var dcv = dessinVert.RenderOpen()) dcv.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 160, 60)), null, new Rect(0, 0, 1280, 720));
                var bitmapVert = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmapVert.Render(dessinVert);
                var pngVert = new PngBitmapEncoder(); pngVert.Frames.Add(BitmapFrame.Create(bitmapVert));
                using (var fv = File.Create(vertPath)) pngVert.Save(fv);
                bool Vert() => vueDif.ReadByte(milieu) == 60 && vueDif.ReadByte(milieu + 1) == 160 && vueDif.ReadByte(milieu + 2) == 20;
                var logoVert = R.NouveauDisplay("Écran par défaut de test", new SourceDisplay { Type = "image", Chemin = vertPath });
                var principalVert = R.AjouterPrincipalAgenda(logoVert.Id);
                if (principalVert != null) R.ChoisirEcranDefaut(principalVert.Id);
                Ok(principalVert != null && R.EcranDefaut == principalVert.Id, "1.3.2 : un écran principal choisi comme écran par défaut");
                await Attendre(1500);
                Ok(Vert() && R.Etat.Media?.Chemin == imagePath && R.EtatSalle(R.Etat).Media?.Chemin == imagePath,
                    "1.3.2 : source « Dans l'église » à l'écran : MyVirtualCam montre l'écran par défaut (pas un écran noir)");
                R.ModifierSource(img.Id, "", sourceImage with { Diffusion = SourceDisplay.Streaming });
                await Attendre(1300);
                Ok(Rouge() && R.EtatSalle(R.Etat).Media?.Chemin == vertPath, "1.3.1 : source « En streaming » : MyVirtualCam la reçoit, l'écran de projection garde l'écran par défaut");
                Ok(w.TestStatutProjection == "MyStream seulement", "1.3.1 : pastille « MyStream seulement » : " + w.TestStatutProjection);
                if (w.Ecran != null) Capture(w.Ecran, "26b-ecran-streaming-seulement.png");
                R.ModifierSource(img.Id, "", sourceImage with { Diffusion = null });
                await Attendre(1300);
                Ok(Rouge() && R.EtatSalle(R.Etat).Media?.Chemin == imagePath, "1.3.1 : les deux : MyVirtualCam et l'écran de projection la reçoivent de nouveau");
                R.Effacer();
                await Attendre(1500);
                Ok(R.Cible is CibleDefaut && Vert() && R.EtatDiffusion(R.Etat).Vide, "1.3.2 : l'écran par défaut part aussi sur MyVirtualCam (OBS reste vide)");
                R.ChoisirEcranDefaut("noir");
                if (principalVert != null) R.RetirerAgenda(principalVert.Id);
                R.SupprimerAnnonce(logoVert.Id);
                R.AfficherAnnonce(img);
                await Attendre(1300);
            }
            // 1.3.1 : la fenêtre « Capture de fenêtre » comme celle d'OBS (image en direct, liste « [programme] : titre », où elle va)
            await Fenetre(w, () => new CaptureFenetreWindow(null, ""), "25c-capture-fenetre.png");
            // Closed/unavailable windows report a failure and clear the picture rather than retaining a stale image.
            var absent = R.NouveauDisplay("Fenêtre fermée", new SourceDisplay { Type = "fenetre", Fenetre = 1, Processus = -1 });
            R.AfficherAnnonce(absent); await Attendre(500);
            Ok(App.Sources.Image == null && App.Sources.Statut.Contains("fermée"), "fenêtre fermée : image effacée et message utile");
            // 1.0.2 : Duo (deux contenus en même temps à l'écran)
            // 1.1 : onglet Duo — un dossier de 2 éléments, chacun à sa place, ajouté dans Display
            var dossier = R.NouveauDuo("Verset et annonce");
            dossier.A = new PartDuo { Type = "bible", Ref = new[] { 42, 3, 16, 16 }, Titre = "Jean 3:16", X = 0, Y = 0, L = 0.7 };
            dossier.B = new PartDuo { Type = "display", Id = an.Id, Titre = an.Titre, X = 0.55, Y = 0.55, L = 0.45 };
            R.EnregistrerDuo(dossier);
            w.TestOngletDuo();
            w.TestChoisirDossier(dossier);
            w.TestAfficherDossier();
            await Attendre(1600);
            Ok(R.Etat.Mode == "verset" && R.Etat.Duo is { Place: "libre" } dl0 && dl0.Dossier == dossier.Id, "onglet Duo : le dossier part en direct (verset + annonce)");
            Capture(w, "31-onglet-duo.png");
            if (w.Ecran != null) Capture(w.Ecran, "31-onglet-duo-ecran.png");
            var tuileDuo = R.AjouterDuoDisplay(dossier);
            R.Effacer();
            Ok(R.Etat.Duo == null, "Retirer de l'écran : le dossier part en entier");
            R.AfficherAnnonce(tuileDuo);
            await Attendre(400);
            Ok(R.Etat.Duo is { Place: "libre" } && R.Etat.Mode == "verset", "Display : la tuile du dossier Duo l'envoie en direct");
            // 3.0.1 : la vignette du dossier montre ses deux éléments comme sur l'écran principal
            w.TestOngletAnnonces();
            await Attendre(700);
            Ok(w.TestTuileDuoDessinee, "grille Display : la vignette du dossier Duo dessine ses deux éléments (comme à l'écran)");
            Capture(w, "31b-grille-duo.png");
            await Fenetre(w, () => new ChoixChantWindow(null), "31c-choix-chant.png");
            R.SupprimerDuo(dossier.Id);
            Ok(R.Etat.Vide && R.Annonces.All(x => x.Source?.Type != "duo"), "dossier supprimé : sa tuile Display aussi");
            w.TestOngletAnnonces();

            R.Effacer();
            R.AfficherAnnonce(an);
            await Attendre(400);
            w.TestBasculerDuo();
            await Attendre(300);
            Ok(R.Etat.Duo != null && R.Etat.Vide && R.Etat.Duo.Libelle == "Repas fraternel après le culte", "Duo : texte Display épinglé (touche P), écran principal libéré");
            R.AfficherVerset(42, 3, 16, 16);
            await Attendre(1600);
            Ok(R.Etat.Duo != null && R.Etat.Mode == "verset", "Duo : un verset + un texte épinglé");
            Ok(w.TestDuoLibelle == "Repas fraternel après le culte", "carte DUO : contenu épinglé affiché (" + w.TestDuoLibelle + ")");
            Capture(w, "28-duo.png");
            if (w.Ecran != null) Capture(w.Ecran, "28-duo-ecran.png");
            Ok(w.TestDuoPlace("cote", 35) && R.Etat.Duo?.Place == "cote", "carte DUO : côte à côte");
            await Attendre(1300);
            Capture(w, "29-duo-cote.png");
            if (w.Ecran != null) Capture(w.Ecran, "29-duo-cote-ecran.png");
            Ok(w.TestDuoPlace("bg", 50) && R.Etat.Duo is { Place: "bg", Part: 0.5 }, "carte DUO : bas gauche, 50 %");
            Ok(w.TestDuoPlace("hd", 35) && R.Etat.Duo is { Place: "hd", Part: 0.35 }, "carte DUO : haut droite, 35 %");
            // une image épinglée : la 2e capture fournit son image vivante pendant qu'un verset est à l'écran
            R.AfficherAnnonce(img);
            await Attendre(1300);
            w.TestEpingler();
            R.AfficherVerset(42, 3, 16, 16);
            await Attendre(1600);
            Ok(R.Etat.Duo?.Etat.Media == img.Source && R.Etat.Mode == "verset", "Duo : un verset + une image épinglée");
            Ok(App.SourcesDuo.Image != null && App.Sources.Image == null, "Duo : image vivante du contenu épinglé (2e capture)");
            var duoJpg = new FileInfo(Path.Combine(App.SortieObs.Dossier, "display-duo.jpg"));
            Ok(duoJpg.Exists && duoJpg.Length > 3000, "Duo : image épinglée publiée pour la page OBS (" + (duoJpg.Exists ? duoJpg.Length : 0) + " octets)");
            if (App.CameraVirtuelle != null)
            {
                // 1.5.0 : la caméra virtuelle dessine sur son propre fil (un dessin peut prendre 200 ms sur la machine du CI)
                await Attendre(900);
                using var mm = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
                using var view = mm.CreateViewAccessor();
                // milieu du coin haut droite (35 % de 1280 × 720, marge 3 %) ; l'image de la caméra virtuelle est rangée de bas en haut
                var pixel = 64 + ((719 - 147) * 1280 + 1034) * 3;
                Ok(view.ReadByte(pixel) == 40 && view.ReadByte(pixel + 1) == 30 && view.ReadByte(pixel + 2) == 200,
                    $"MyVirtualCam reçoit l'image du contenu épinglé ({view.ReadByte(pixel + 2)},{view.ReadByte(pixel + 1)},{view.ReadByte(pixel)})");
            }
            Capture(w, "30-duo-image.png");
            if (w.Ecran != null) Capture(w.Ecran, "30-duo-image-ecran.png");
            w.TestBasculerDuo();
            await Attendre(400);
            Ok(R.Etat.Duo == null && R.Etat.Mode == "verset" && App.SourcesDuo.Image == null, "Duo retiré (touche P) : le verset reste, la 2e capture s'arrête");
            R.Effacer();
            R.SupprimerAnnonce(img.Id); R.SupprimerAnnonce(absent.Id);
            R.SupprimerAnnonce(an.Id);
            R.SupprimerAgenda(agTest.Id);
            w.TestOnglet(false);
            R.Effacer();
            await Attendre(900);
            Capture(w, "11-vide.png");

            // 3.1 : les pages en clair puis en sombre, la page Réglages, le + de la bibliothèque Display
            var agC = R.NouvelAgenda("Culte du dimanche 15 novembre");
            R.AjouterCompteAgenda(5);
            if (c != null) R.AjouterChantAgenda(c.Id);
            var lect = R.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
            R.HeureAgenda(lect.Id, "10:30");
            R.AjouterTexteAgenda("Annonces", "Repas fraternel après le culte\n\nRéunion de prière mercredi à 19 h");
            R.AfficherElement(lect, 0);
            w.TestPage("agenda");
            await Attendre(600);
            int k17 = R.Agenda.Elements.TakeWhile(e => e != lect).Sum(e => R.Diapos(e).Count) + 1;
            Ok(w.TestCliquerVignette(k17) && R.Etat.Ref.StartsWith("Jean 3:17"), "clic sur une vignette : en direct (" + R.Etat.Ref + ")");
            await Attendre(300);
            Ok(w.TestVignetteDirecte.StartsWith("Jean 3:17"), "la vignette cliquée passe « À L'ÉCRAN »");
            Ok(w.TestDeroule.StartsWith("Culte du dimanche 15 novembre / 4 moments") && w.TestDeroule.Contains("à 10:30") || c == null, "en-tête du programme : " + w.TestDeroule);
            R.AfficherElement(lect, 0);
            await Attendre(400);
            Capture(w, "39-pupitre-culte.png");
            // 1.5.0 : la télécommande dit ce qui est à l'écran ; les outils de l'écran s'ouvrent d'un clic
            Ok(w.TestTelecommande.Contains("Jean 3:16") && w.TestTelecommande.Contains("1 sur 2"), "1.5.0 : télécommande : " + w.TestTelecommande);
            w.TestTiroir("style", true);
            await Attendre(300);
            Capture(w, "60-v150-outil-style.png");
            w.TestTiroir("repos", true);
            await Attendre(300);
            Capture(w, "61-v150-outil-repos.png");
            w.TestTiroir("repos", false);
            w.TestPage("chants");
            await Attendre(400);
            Capture(w, "62-v150-bibliotheque-chants.png");
            w.TestPage("agenda");
            await Attendre(300);
            w.TestBasculerTheme();
            Ok(Palette.Actuelle.Sombre && w.TestTheme == "Clair", "bouton de la barre de gauche : thème sombre");
            w.TestBasculerTheme();
            Ok(!Palette.Actuelle.Sombre && w.TestTheme == "Sombre", "bouton de la barre de gauche : retour au clair");
            var anG = R.NouvelleAnnonce("Annonce de test", "Réunion de prière\nmercredi à 19 h");
            w.TestPage("annonces");
            w.TestPlusGrille(anG);
            Ok(anG.Grille, "Display : le + ajoute la source à la grille");
            w.TestPlusGrille(anG);
            Ok(!anG.Grille, "Display : le ✓ la retire de la grille");
            w.TestPlusGrille(anG);
            w.TestPage("reglages");
            await Attendre(600);
            Capture(w, "40-reglages.png");
            App.AppliquerApparence("sombre");
            Ok(Palette.Actuelle.Sombre && App.Donnees.Reglages.Apparence == "sombre", "thème sombre appliqué et enregistré");
            Ok(((SolidColorBrush)MainWindow.Pinceau("Page")).Color == Palette.De("Page", true), "les pinceaux suivent le thème sombre");
            await Attendre(500);
            Capture(w, "41-sombre-reglages.png");
            foreach (var (page, nom) in new[] { ("agenda", "42-sombre-culte.png"), ("bible", "43-sombre-bible.png"), ("chants", "44-sombre-chants.png"), ("annonces", "45-sombre-display.png"), ("duo", "46-sombre-duo.png"), ("son", "46b-sombre-son.png"), ("montage", "46c-sombre-montage.png") })
            {
                w.TestPage(page);
                await Attendre(500);
                Capture(w, nom);
            }
            await Fenetre(w, () => new OutilsWindow(false), "47-sombre-fenetre.png");
            App.AppliquerApparence("clair");
            Ok(!Palette.Actuelle.Sombre && ((SolidColorBrush)MainWindow.Pinceau("Page")).Color == Palette.De("Page", false), "retour au thème clair");
            foreach (var (page, nom) in new[] { ("agenda", "48-clair-culte.png"), ("annonces", "49-clair-display.png"), ("bible", "49b-clair-bible.png"), ("chants", "49c-clair-chants.png"), ("duo", "49d-clair-duo.png"), ("son", "49e-clair-son.png"), ("montage", "49f-clair-montage.png") })
            {
                w.TestPage(page);
                await Attendre(500);
                Capture(w, nom);
            }
            // 1.5.0 : la vitesse d'un clic sur Suivant (toute l'interface mise à jour, page Culte), mesurée
            {
                w.TestPage("agenda");
                R.AfficherElement(lect, 0);
                await Attendre(600);
                Perf.Vider();
                var chrono = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    R.Bouger(i % 2 == 0 ? 1 : -1);
                    await w.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                chrono.Stop();
                double moy = chrono.Elapsed.TotalMilliseconds / 20;
                Note($"1.5.0 : un clic sur Suivant (page Culte) : {moy:0} ms en moyenne sur 20 clics");
                Note("1.5.0 : mesures :\n" + Perf.Resume());
                Ok(moy < 500, $"1.5.0 : un clic sur Suivant prend {moy:0} ms (moins de 500 ms)");
                var tCmd = System.Diagnostics.Stopwatch.StartNew();
                var resCmd = w.TestCommande("je lava vers dieu");
                tCmd.Stop();
                Note($"1.5.0 : recherche complète « je lava vers dieu » : {tCmd.Elapsed.TotalMilliseconds:0} ms ({resCmd.Count} résultats)");
                w.TestCommande("");

                // 1.5.1 : un vrai culte (une quarantaine de diapositives), puis le temps de chaque page et de chaque bouton,
                // jusqu'à l'image affichée (deux images de rendu après le changement)
                Perf.Vider();
                R.AjouterBibleAgenda(new Ref(42, 1, 1, 14));
                R.AjouterBibleAgenda(new Ref(18, 23, 1, 6));
                R.AjouterBibleAgenda(new Ref(44, 8, 28, 39));
                await Attendre(800);
                Note($"1.5.1 : programme de test : {R.Agenda.Elements.Count} moments, {w.TestVignettes} diapositives");
                async Task<double> Mesurer(Action action)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    action();
                    await w.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var images = new TaskCompletionSource();
                    int n = 0;
                    EventHandler? h = null;
                    h = (_, _) => { if (++n >= 2) { CompositionTarget.Rendering -= h; images.TrySetResult(); } };
                    CompositionTarget.Rendering += h;
                    await images.Task;
                    return sw.Elapsed.TotalMilliseconds;
                }
                var temps = new List<string>();
                foreach (var page in new[] { "bible", "chants", "annonces", "duo", "son", "reglages", "agenda", "bible", "agenda" })
                    temps.Add($"{page} {await Mesurer(() => w.TestPage(page)):0} ms");
                Note("1.5.1 : changements de page (jusqu'à l'image affichée) : " + string.Join(", ", temps));
                // 1.9.3 : les boutons de pages de chaque rôle (Culte, Bibliothèque, Médias, Son, Réglages), puis retour à l'état réel
                Ok(w.TestRole(Roles.Bible) == "01000" && w.TestRole(Roles.Affichage) == "10100" && w.TestRole(Roles.Camera) == "00000" && w.TestRole(Roles.Admin) == "11111",
                    "1.9.3 : pages par rôle (Versets : Bibliothèque ; Affichage : Culte et Médias ; Caméraman : aucune ; Admin : tout)");
                w.AppliquerRole();
                Ok(w.TestRole(ComptesPc.Role) == "11111", "1.9.3 : sans compte connecté ni connexion obligatoire, le logiciel est entier");
                w.AppliquerRole();
                var boutons = new List<string>();
                boutons.Add($"Suivant {await Mesurer(() => R.Bouger(1)):0} ms");
                boutons.Add($"Précédent {await Mesurer(() => R.Bouger(-1)):0} ms");
                boutons.Add($"Noir {await Mesurer(() => w.TestNoir()):0} ms");
                boutons.Add($"Noir (rallumer) {await Mesurer(() => w.TestNoir()):0} ms");
                boutons.Add($"outil Style {await Mesurer(() => w.TestTiroir("style", true)):0} ms");
                boutons.Add($"outil fermé {await Mesurer(() => w.TestTiroir("style", false)):0} ms");
                if (c != null) boutons.Add($"ouvrir un chant {await Mesurer(() => { w.TestPage("chants"); w.OuvrirChant(c.Id); }):0} ms");
                boutons.Add($"Bible : chapitre {await Mesurer(() => { w.TestPage("bible"); w.TestRecherche("ps 119"); }):0} ms");
                boutons.Add($"retour au Culte {await Mesurer(() => w.TestPage("agenda")):0} ms");
                Note("1.5.1 : boutons (jusqu'à l'image affichée) : " + string.Join(", ", boutons));
                Note("1.5.1 : mesures :\n" + Perf.Resume());
            }
            R.SupprimerAnnonce(anG.Id);
            R.Effacer();
            R.SupprimerAgenda(agC.Id);

            // 1.0 : Échap dans une recherche, anti-veille, projecteur débranché puis rebranché, réouverture au démarrage
            await TestEchapEtProjecteur(w);

            // 1.5.1 : l'éditeur enregistre tout seul ; « Retour en arrière » défait, étape par étape
            string? idEditeur = null;
            await Fenetre(w, () => new EditeurWindow(null), "12-editeur.png", d =>
            {
                var ed = (EditeurWindow)d;
                const string colle = "Mon chant collé\n\nC G Am F\nLigne une du chant\nLigne deux du chant\nLigne trois du chant\nLigne quatre du chant\nLigne cinq du chant\nLigne six du chant\nLigne sept\n\nRefrain :\nGloire à toi\nGloire à toi";
                ed.TestColler(colle);
                var idAuto = ed.TestSauver();
                Ok(idAuto != null && R.Trouver(idAuto) is { Sections.Count: > 0 }, "1.5.1 : éditeur : le chant collé s'enregistre tout seul, sans bouton Enregistrer");
                Ok(ed.TestRetourPossible, "1.5.1 : « Retour en arrière » est prêt");
                ed.TestRetour();
                Ok(idAuto != null && R.Trouver(idAuto) == null && !ed.TestRetourPossible, "1.5.1 : Retour en arrière : le nouveau chant revient à vide (il n'est plus dans la liste)");
                ed.TestColler(colle);
                idEditeur = ed.TestSauver();
            });
            Ok(idEditeur != null && R.Trouver(idEditeur) != null, "1.5.1 : fermer l'éditeur garde le chant enregistré");
            if (idEditeur != null) R.SupprimerChant(idEditeur);
            // 1.6.0 / 1.6.4 : MyStream (les deux écrans, la source principale, « Envoyer dans le stream », la touche M), le direct, le direct automatique
            try { await TestMyStream(w); }
            catch (Exception x) { Echec("1.6.0 : MyStream : " + x); }
            // 1.6.5 : une vraie caméra DirectShow (MyVirtualCam, si elle est installée) : réglage trouvé, images qui arrivent
            try { await TestCameraUsb(); }
            catch (Exception x) { Echec("1.6.5 : caméra USB : " + x); }
            // 1.7.0 : tuiles vivantes (caméras, fenêtres) et Réglages › À distance
            try { await TestTuilesEtDistance(w); }
            catch (Exception x) { Echec("1.7.0 : " + x); }
            await Fenetre(w, () => new DecoupeWindow(new CibleVerset(42, 3, 16, 16)), "13-decoupe.png");
            await Fenetre(w, () => new ImportWindow(), "14-import.png");
            await Fenetre(w, () => new BiblesWindow(), "15-bibles.png");
            await Fenetre(w, () => new TelephoneWindow(), "17-telephone.png");
            await Fenetre(w, () => new OutilsWindow(false), "18-outils.png");

            // 2.2.0 : le montage et l'export
            await TestMontage(w);

            // recueil PDF (lecture du PDF par PdfPig)
            var pdf = Path.Combine(Environment.CurrentDirectory, "windows", "MyVerse.Tests", "donnees", "recueil-test.pdf");
            if (File.Exists(pdf))
            {
                try
                {
                    var lignes = await Task.Run(() => ImportWindow.LignesPdf(pdf, new Progress<string>(_ => { })));
                    var chants = Recueils.ChantsDepuisLignes(lignes);
                    Note("PDF : " + string.Join(" | ", chants.Select(x => x.Title + " [" + string.Join(", ", x.Sections.Select(s => s.Label)) + "]")));
                    Ok(chants.Count == 3 && chants[0].Title == "1. GRACE INFINIE" && chants[0].Sections.Any(s => s.Label == "Refrain") && chants[2].Title == "3. YERA YERA",
                        "recueil PDF : 3 chants numérotés avec refrain");
                }
                catch (Exception x) { Echec("recueil PDF : " + x); }
            }
            else Note("PDF de test absent : " + pdf);

            // le serveur des téléphones répond
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var page = await http.GetStringAsync($"http://127.0.0.1:{App.Telephone.Port}/");
                Ok(page.Contains("Siloé Stream ✓"), $"serveur des téléphones (port {App.Telephone.Port})");
            }
            catch (Exception x) { Echec("serveur des téléphones : " + x.Message); }
            Ok(File.Exists(Path.Combine(App.SortieObs.Dossier, "etat.json")), "sortie OBS (etat.json)");
            Ok(File.Exists(App.SortieObs.Page), "page OBS (ecran.html)");
        }
        catch (Exception x) { Echec("exception : " + x); }
        Fin:
        lock (log)
        {
            log.Add("");
            log.Add("");
            log.Add("===== journal =====");
            log.AddRange(Journal.Lignes());
            log.Add("");
            log.Add(echecs.Count == 0 ? "TOUT EST BON" : echecs.Count + " ÉCHEC(S)");
            try { File.WriteAllLines(Path.Combine(dossier, "autotest.txt"), log); } catch (Exception) { }
        }
        try { w.Ecran?.Close(); } catch (Exception) { }
        Application.Current.Shutdown(echecs.Count == 0 ? 0 : 1);
    }
}
