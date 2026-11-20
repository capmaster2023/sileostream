using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.9.3 : le compte Google connecté sur ce PC. Admin : tout ; Versets : la Bible ; Chants : les chants ;
/// Affichage : le culte et les médias (ce qui est montré dans l'église et sur Zoom). Sans compte connecté, et tant que
/// l'admin n'a pas rendu la connexion obligatoire (Réglages › Église et comptes), le logiciel reste entier, comme avant.
/// </summary>
public static class ComptesPc
{
    public static SessionPc? Session { get; private set; }
    /// <summary>Le compte ou le rôle a changé (toujours sur l'interface).</summary>
    public static event Action? Change;

    static string Dossier => App.Donnees.Dossier;
    static CancellationTokenSource? enCours;

    public static CompteEglise? Eglise => CompteEglise.Lire(Dossier);
    public static bool Obligatoire => Eglise is { ConnexionObligatoire: true };

    /// <summary>Le rôle qui décide des pages : celui du compte connecté ; sans compte, Admin si la connexion est facultative.</summary>
    public static string? Role => Session != null ? Roles.Connu(Session.Role) : Obligatoire ? null : Roles.Admin;
    public static bool EstAdmin => Role == Roles.Admin;
    /// <summary>Le relais (VPS) qui garde les comptes : celui de l'église du PC, sinon celui de Réglages › À distance.</summary>
    public static string Relais => Eglise?.Relais is { Length: > 0 } r ? r : ReglagesDistance.Lire(Dossier).Relais;

    public static void Charger() => Session = SessionPc.Lire(Dossier);

    public static void Annuler() => enCours?.Cancel();

    /// <summary>
    /// Ouvre siloestream.tech/connexion dans le navigateur, attend le compte Google (5 minutes au plus), puis demande au relais
    /// la session et le rôle. Renvoie null, ou le message à montrer.
    /// </summary>
    public static async Task<string?> Connecter()
    {
        enCours?.Cancel();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        enCours = cts;
        try
        {
            using var page = new ConnexionGoogle();
            try { Process.Start(new ProcessStartInfo(page.Adresse) { UseShellExecute = true }); }
            catch (Exception x) { Journal.Erreur("connexion : navigateur", x); return "Le navigateur ne s'ouvre pas : " + x.Message; }
            var idToken = await page.Attendre(cts.Token);
            if (idToken == null) return "Connexion annulée.";
            return await Ouvrir(idToken);
        }
        catch (SocketException x) { return "Connexion impossible sur ce PC : " + x.Message; }
        finally { if (enCours == cts) enCours = null; }
    }

    static async Task<string?> Ouvrir(string idToken)
    {
        var relais = Relais;
        ComptesApi.Moi moi;
        try { moi = await ComptesApi.Connecter(relais, idToken); }
        catch (ComptesApi.Refus r) { return r.Message; }
        catch (Exception x) when (x is HttpRequestException or TaskCanceledException) { return $"Le serveur des comptes (« {relais} ») ne répond pas : vérifiez Internet."; }
        string role = Roles.Admin, id = "", nom = "";
        if (Eglise is { } lien)
        {
            var e = moi.Eglises.FirstOrDefault(x => x.Id == lien.Eglise);
            if (e == null || Roles.Connu(e.Role) == null)
            {
                try { await ComptesApi.Deconnecter(relais, moi.Jeton); } catch (Exception) { }
                return $"{moi.Email} n'est pas membre de l'église « {lien.Nom} » : demandez à son admin de vous ajouter (Réglages › Église et comptes).";
            }
            (role, id, nom) = (e.Role, e.Id, e.Nom);
        }
        // PC pas encore relié à une église : celui qui le prépare a tout (il le reliera dans Réglages)
        Session = new SessionPc { Jeton = moi.Jeton, Email = moi.Email, Nom = moi.Nom, Role = role, Eglise = id, EgliseNom = nom, Relais = relais };
        try { Session.Ecrire(Dossier); } catch (Exception x) { Journal.Erreur("compte : enregistrement", x); }
        Journal.Info($"compte : {moi.Email} connecté ({Roles.Nom(role)})");
        Change?.Invoke();
        return null;
    }

    /// <summary>Relit le rôle auprès du relais (l'admin a pu le changer ou retirer ce membre). Sans Internet, le rôle gardé sert.</summary>
    public static async Task Rafraichir()
    {
        var s = Session;
        if (s == null) return;
        try
        {
            var moi = await ComptesApi.MoiActuel(s.Relais, s.Jeton);
            if (Session != s) return;
            string? role = Roles.Admin;
            string id = "", nom = "";
            if (Eglise is { } lien)
            {
                var e = moi.Eglises.FirstOrDefault(x => x.Id == lien.Eglise);
                role = e == null ? null : Roles.Connu(e.Role);
                if (e != null) (id, nom) = (e.Id, e.Nom);
            }
            if (role == null)
            {
                Journal.Info($"compte : {s.Email} n'est plus membre de l'église du PC");
                Fermer();
                return;
            }
            if (role != s.Role || id != s.Eglise || nom != s.EgliseNom || (moi.Nom.Length > 0 && moi.Nom != s.Nom))
            {
                s.Role = role;
                s.Eglise = id;
                s.EgliseNom = nom;
                if (moi.Nom.Length > 0) s.Nom = moi.Nom;
                try { s.Ecrire(Dossier); } catch (Exception) { }
                Journal.Info($"compte : {s.Email} est maintenant {Roles.Nom(role)}");
                Change?.Invoke();
            }
        }
        catch (ComptesApi.Refus r) when (r.Code == 401)
        {
            if (Session == s) Fermer();
        }
        catch (Exception x) when (x is ComptesApi.Refus or HttpRequestException or TaskCanceledException)
        {
            // hors ligne ou serveur en panne : le rôle gardé sert
        }
    }

    static void Fermer()
    {
        SessionPc.Effacer(Dossier);
        Session = null;
        Change?.Invoke();
    }

    public static async Task Deconnecter()
    {
        var s = Session;
        if (s != null) Journal.Info($"compte : {s.Email} déconnecté");
        Fermer();
        if (s != null) try { await ComptesApi.Deconnecter(s.Relais, s.Jeton); } catch (Exception) { }
    }

    /// <summary>Relie ce PC à une église dont le compte connecté est l'admin. Renvoie null, ou le message à montrer.</summary>
    public static async Task<string?> RelierPc(string eglise)
    {
        var s = Session;
        if (s == null) return "Connectez-vous d'abord avec Google.";
        try
        {
            var code = await ComptesApi.CodePc(s.Relais, s.Jeton, eglise);
            var c = await CompteEglise.Lier(Dossier, s.Relais, code, App.Telephone.PcId, App.Telephone.NomPc);
            Journal.Info($"PC relié à l'église « {c.Nom} » (depuis le logiciel)");
            App.MajAgent();
            await Rafraichir();
            Change?.Invoke();
            return null;
        }
        catch (Exception x) when (x is ComptesApi.Refus or InvalidOperationException or HttpRequestException or TaskCanceledException or IOException)
        {
            return "Liaison impossible : " + x.Message;
        }
    }

    /// <summary>Crée une église (le compte connecté en est l'admin) et y relie ce PC.</summary>
    public static async Task<string?> CreerEglise(string nom)
    {
        var s = Session;
        if (s == null) return "Connectez-vous d'abord avec Google.";
        try { return await RelierPc(await ComptesApi.CreerEglise(s.Relais, s.Jeton, nom)); }
        catch (Exception x) when (x is ComptesApi.Refus or HttpRequestException or TaskCanceledException) { return x.Message; }
    }
}

/// <summary>
/// Siloé Stream 1.9.3 : l'écran de connexion, par-dessus toute la fenêtre, quand la connexion Google est obligatoire sur ce PC
/// (ou quand le rôle du compte, Caméraman, ne donne aucune page du logiciel).
/// </summary>
public sealed class ConnexionPanel : Grid
{
    readonly TextBlock texte, erreur;
    readonly Button connecter, annuler, changer;

    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static Style S(string cle) => MainWindow.Style_(cle);

    public ConnexionPanel()
    {
        Background = P("Page");
        var carte = new StackPanel { MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        carte.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/logo-icone.png")), Width = 104, Height = 104,
            Margin = new Thickness(0, 0, 0, 18), HorizontalAlignment = HorizontalAlignment.Center,
        });
        carte.Children.Add(new TextBlock { Text = "Siloé Stream", FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        texte = new TextBlock { FontSize = 15.5, LineHeight = 23, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = P("Brume"), Margin = new Thickness(0, 10, 0, 24) };
        carte.Children.Add(texte);

        connecter = new Button { Style = S("BtnRouge"), Content = "Se connecter avec Google", Height = 48, Padding = new Thickness(26, 0, 26, 0), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center };
        annuler = new Button { Style = S("BtnPetit"), Content = "Annuler", Height = 36, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        changer = new Button { Style = S("BtnPetit"), Content = "Se déconnecter", Height = 40, Padding = new Thickness(18, 0, 18, 0), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        carte.Children.Add(connecter);
        carte.Children.Add(changer);
        carte.Children.Add(annuler);
        erreur = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = P("Danger"), Margin = new Thickness(0, 18, 0, 0) };
        carte.Children.Add(erreur);
        Children.Add(carte);

        connecter.Click += async (_, _) =>
        {
            connecter.IsEnabled = false;
            annuler.Visibility = Visibility.Visible;
            erreur.Text = "";
            texte.Text = "Terminez la connexion dans votre navigateur (siloestream.tech), puis revenez ici.";
            var e = await ComptesPc.Connecter();
            connecter.IsEnabled = true;
            annuler.Visibility = Visibility.Collapsed;
            Maj();
            if (e != null) erreur.Text = e;
        };
        annuler.Click += (_, _) => ComptesPc.Annuler();
        changer.Click += async (_, _) => { await ComptesPc.Deconnecter(); Maj(); };
    }

    public void Maj()
    {
        erreur.Text = "";
        if (ComptesPc.Session is { } s)
        {
            texte.Text = $"{(s.Nom.Length > 0 ? s.Nom : s.Email)} : votre rôle, {Roles.Nom(s.Role)}, se sert de l'application Siloé Stream du téléphone "
                + "et ne donne aucune page du logiciel. Demandez à l'admin un autre rôle, ou connectez-vous avec un autre compte.";
            connecter.Visibility = Visibility.Collapsed;
            changer.Visibility = Visibility.Visible;
            return;
        }
        var eg = ComptesPc.Eglise;
        texte.Text = (eg != null ? $"Pour utiliser Siloé Stream à « {eg.Nom} », connectez-vous avec votre compte Google. " : "Connectez-vous avec votre compte Google. ")
            + "Chacun voit les pages de son rôle : la Bible, les chants, ou ce qui est affiché dans l'église et sur Zoom.";
        connecter.Visibility = Visibility.Visible;
        changer.Visibility = Visibility.Collapsed;
    }
}
