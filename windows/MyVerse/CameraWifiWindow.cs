using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.9.5 : connecter la caméra Sony par son Wi-Fi, comme Imaging Edge Mobile, sans câble.
/// Les étapes sur la caméra, le Wi-Fi de la caméra (le PC s'y connecte avec netsh), puis l'état en direct.
/// </summary>
public sealed class CameraWifiWindow : FenetreDialogue
{
    readonly TextBox ssid, nom, adresse;
    readonly PasswordBox motDePasse;
    readonly TextBlock etat;
    readonly DispatcherTimer minuteur = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>Vrai si l'utilisateur a cliqué Connecter (la source est à ajouter à Display).</summary>
    public bool Connectee { get; private set; }

    public CameraWifiWindow()
    {
        Title = "Caméra Sony par Wi-Fi";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = Pinceau("Page");
        var r = ReglagesCameraWifi.Lire(App.Donnees.Dossier);

        var p = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        p.Children.Add(new TextBlock { Text = "Caméra Sony par Wi-Fi", Style = Style_("Titre2") });
        p.Children.Add(new TextBlock
        {
            Text = "L'image en direct de la caméra arrive sans câble, comme dans Imaging Edge Mobile, et devient une source de Display " +
                   "(église, Zoom, YouTube). C'est l'image de contrôle de la caméra : environ 1024 × 576, moins nette qu'un câble HDMI.",
            Style = Style_("Muet"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14),
        });
        p.Children.Add(new TextBlock { Text = "SUR LA CAMÉRA", Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 0, 6) });
        p.Children.Add(new TextBlock
        {
            Text = "1. Fermez Imaging Edge Mobile sur les téléphones (une seule connexion à la fois).\n" +
                   "2. MENU › Réseau › Ctrl w/ Smartphone › Ctrl w/ Smartphone : Oui.\n" +
                   "3. MENU › Réseau › Ctrl w/ Smartphone › Connexion : la caméra montre son Wi-Fi (DIRECT-xxxx) et son mot de passe.\n" +
                   "4. Recopiez-les ci-dessous, puis Connecter.",
            TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 14),
        });

        p.Children.Add(new TextBlock { Text = "WI-FI DE LA CAMÉRA (SSID)", Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 0, 6) });
        ssid = new TextBox { Style = Style_("Champ"), Text = r.Ssid, ToolTip = "DIRECT-xxxx:ILCE-6100" };
        p.Children.Add(ssid);
        p.Children.Add(new TextBlock { Text = "MOT DE PASSE", Style = Style_("CarteTitre"), Margin = new Thickness(0, 12, 0, 6) });
        motDePasse = new PasswordBox { FontSize = 14, Height = 40, Padding = new Thickness(10, 8, 10, 8), Password = r.MotDePasse };
        p.Children.Add(motDePasse);
        p.Children.Add(new TextBlock
        {
            Text = "Le PC quitte alors le Wi-Fi de l'église sur cette carte Wi-Fi. Pour garder Internet (YouTube, Zoom) : un câble réseau, " +
                   "ou une 2e clé Wi-Fi USB (Siloé Stream prend celle qui n'est pas connectée). Laissez vide si le PC est déjà sur le Wi-Fi de la caméra.",
            Style = Style_("Muet"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });

        var ligne = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        ligne.ColumnDefinitions.Add(new ColumnDefinition());
        ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        ligne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        var colNom = new StackPanel();
        colNom.Children.Add(new TextBlock { Text = "NOM DANS DISPLAY", Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 0, 6) });
        nom = new TextBox { Style = Style_("Champ"), Text = r.Nom };
        colNom.Children.Add(nom);
        ligne.Children.Add(colNom);
        var colAdr = new StackPanel();
        colAdr.Children.Add(new TextBlock { Text = "ADRESSE (AVANCÉ)", Style = Style_("CarteTitre"), Margin = new Thickness(0, 0, 0, 6) });
        adresse = new TextBox { Style = Style_("Champ"), Text = r.Adresse, ToolTip = "Vide : recherche automatique, puis 192.168.122.1" };
        colAdr.Children.Add(adresse);
        Grid.SetColumn(colAdr, 2);
        ligne.Children.Add(colAdr);
        p.Children.Add(ligne);

        etat = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
        p.Children.Add(etat);

        var bas = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        var oui = new Button { Style = Style_("BtnOr"), Content = "Connecter", MinWidth = 130, Height = 40, Margin = new Thickness(10, 0, 0, 0) };
        oui.Click += (_, _) => Connecter();
        DockPanel.SetDock(oui, Dock.Right);
        bas.Children.Add(oui);
        var fermer = new Button { Style = Style_("Btn"), Content = "Fermer", IsCancel = true, MinWidth = 110, Height = 40 };
        fermer.Click += (_, _) => Close();
        DockPanel.SetDock(fermer, Dock.Right);
        bas.Children.Add(fermer);
        var stop = new Button { Style = Style_("Btn"), Content = "Arrêter la caméra", MinWidth = 140, Height = 40 };
        stop.Click += (_, _) => { App.CameraWifi.Arreter(); MajEtat(); };
        DockPanel.SetDock(stop, Dock.Left);
        bas.Children.Add(stop);
        p.Children.Add(bas);
        Content = p;

        minuteur.Tick += (_, _) => MajEtat();
        minuteur.Start();
        Closed += (_, _) => minuteur.Stop();
        MajEtat();
    }

    void MajEtat()
    {
        var c = App.CameraWifi;
        etat.Text = (c.Actif ? "● " : "○ ") + c.Etat;
        etat.Foreground = c.EnDirect ? Pinceau("Or") : Pinceau("Brume");
    }

    async void Connecter()
    {
        var r = new ReglagesCameraWifi
        {
            Ssid = ssid.Text.Trim(),
            MotDePasse = motDePasse.Password,
            Adresse = adresse.Text.Trim(),
            Nom = nom.Text.Trim().Length > 0 ? nom.Text.Trim() : "Caméra Sony (Wi-Fi)",
        };
        r.Sauver(App.Donnees.Dossier);
        var c = App.CameraWifi;
        c.Arreter();
        c.Nom = r.Nom;
        c.Adresse = r.Adresse;
        if (r.Ssid.Length > 0)
        {
            etat.Text = "Connexion de ce PC au Wi-Fi « " + r.Ssid + " »…";
            var erreur = await Task.Run(() => WifiWindows.Connecter(r.Ssid, r.MotDePasse));
            if (erreur != null)
            {
                etat.Text = erreur;
                etat.Foreground = Pinceau("Danger");
                return;
            }
            // le temps que Windows obtienne son adresse sur le Wi-Fi de la caméra
            await Task.Delay(3000);
        }
        c.Demarrer();
        Connectee = true;
        MajEtat();
    }
}
