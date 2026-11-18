using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MyVerse.Ui;

/// <summary>
/// Siloé Stream 1.9.3 : le compte Google connecté sur le PC décide des pages. Admin : tout ; Versets : la Bible ;
/// Chants : les chants ; Affichage : Culte, Médias et Duo (l'église et Zoom). Le bouton « Compte » de la barre de gauche
/// montre qui est connecté et permet de se déconnecter.
/// </summary>
public partial class MainWindow
{
    ConnexionPanel? connexion;
    Button? compteBtn;
    bool comptesPrets;
    /// <summary>L'écran de scène se montre à l'admin et à l'affichage (Placement le cache aussi sur un petit écran).</summary>
    bool sceneRole = true;

    void BrancherComptes()
    {
        ComptesPc.Charger();
        comptesPrets = true;
        ComptesPc.Change += AppliquerRole;
        AjouterBoutonCompte();
        AppliquerRole();
        _ = ComptesPc.Rafraichir();
    }

    /// <summary>La page fait partie de celles du rôle (toutes tant que les comptes ne sont pas chargés).</summary>
    bool PagePermise(string o) => !comptesPrets || Roles.Pages(ComptesPc.Role).Contains(o);

    /// <summary>Les pages, les boutons et l'écran de connexion selon le compte connecté (ou l'absence de compte).</summary>
    public void AppliquerRole()
    {
        var role = ComptesPc.Role;
        var pages = Roles.Pages(role);
        static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        OngletAgenda.Visibility = V(pages.Contains("agenda"));
        OngletBiblio.Visibility = V(pages.Contains("bible") || pages.Contains("chants"));
        OngletMediasRail.Visibility = V(pages.Contains("annonces") || pages.Contains("duo"));
        OngletSon.Visibility = V(pages.Contains("son"));
        OngletMontage.Visibility = V(pages.Contains("montage"));
        OngletMiniature.Visibility = V(pages.Contains("miniature"));
        OngletReglages.Visibility = V(pages.Contains("reglages"));
        SegBible.Visibility = V(pages.Contains("bible"));
        SegChants.Visibility = V(pages.Contains("chants"));
        SegMedias.Visibility = V(pages.Contains("annonces"));
        SegDuo.Visibility = V(pages.Contains("duo"));

        // en haut : le téléphone, le direct YouTube et la mise à jour pour l'admin ; la projection et l'écran de scène pour l'affichage aussi
        bool admin = role == Roles.Admin;
        TelBtn.Visibility = DirectBtn.Visibility = V(admin);
        ProjectionBtn.Visibility = V(admin || role == Roles.Affichage);
        sceneRole = admin || role == Roles.Affichage;
        MajBtn.Visibility = V(admin && maj is { Disponible: true });
        Placement();

        // la page ouverte n'est pas permise : la première qui l'est
        if (pages.Count > 0 && !pages.Contains(onglet)) MontrerOnglet(pages[0]);

        // l'écran de connexion : par-dessus tout, tant qu'aucune page n'est permise
        if (pages.Count == 0)
        {
            if (connexion == null && Content is Grid racine)
            {
                connexion = new ConnexionPanel();
                Grid.SetColumnSpan(connexion, 4);
                Grid.SetRowSpan(connexion, 3);
                Panel.SetZIndex(connexion, 1000);
                racine.Children.Add(connexion);
            }
            if (connexion != null)
            {
                connexion.Maj();
                connexion.Visibility = Visibility.Visible;
            }
        }
        else if (connexion != null) connexion.Visibility = Visibility.Collapsed;
        MajBoutonCompte();
    }

    /// <summary>L'écran de connexion est affiché : le clavier ne touche pas à ce qui est à l'écran.</summary>
    bool ConnexionAffichee => connexion is { Visibility: Visibility.Visible };

    void AjouterBoutonCompte()
    {
        if (compteBtn != null || ThemeBtn.Parent is not Panel bas) return;
        compteBtn = new Button
        {
            Style = Style_("BtnRail"), Content = "Compte",
            Tag = Geometry.Parse("M12,12 A4.5,4.5 0 1 0 12,3 A4.5,4.5 0 1 0 12,12 Z M4,21 C4,17 7.6,14.5 12,14.5 C16.4,14.5 20,17 20,21"),
        };
        compteBtn.Click += (_, _) => MenuCompte();
        bas.Children.Insert(bas.Children.IndexOf(ThemeBtn), compteBtn);
    }

    void MajBoutonCompte()
    {
        if (compteBtn == null) return;
        var s = ComptesPc.Session;
        var prenom = s == null ? "" : (s.Nom.Length > 0 ? s.Nom : s.Email).Split(' ', '@')[0];
        compteBtn.Content = s == null ? "Compte" : prenom.Length > 11 ? prenom[..10] + "…" : prenom;
        compteBtn.ToolTip = s == null
            ? "Se connecter avec Google (chacun voit les pages de son rôle)"
            : $"{s.Nom} ({s.Email}) · {Roles.Nom(s.Role)}" + (s.EgliseNom.Length > 0 ? $" · {s.EgliseNom}" : "");
    }

    void MenuCompte()
    {
        var m = new ContextMenu { PlacementTarget = compteBtn, Placement = PlacementMode.Right };
        if (ComptesPc.Session is { } s)
        {
            m.Items.Add(new MenuItem { Header = $"{(s.Nom.Length > 0 ? s.Nom : s.Email)} · {s.Email}", IsEnabled = false });
            m.Items.Add(new MenuItem { Header = $"Rôle : {Roles.Nom(s.Role)} ({Roles.Description(s.Role)})" + (s.EgliseNom.Length > 0 ? $" · {s.EgliseNom}" : ""), IsEnabled = false });
            m.Items.Add(new Separator());
            var sortir = new MenuItem { Header = "Se déconnecter" };
            sortir.Click += async (_, _) => { await ComptesPc.Deconnecter(); Toast("Déconnecté de Siloé Stream."); };
            m.Items.Add(sortir);
        }
        else
        {
            var entrer = new MenuItem { Header = "Se connecter avec Google…" };
            entrer.Click += async (_, _) =>
            {
                Toast("Terminez la connexion dans votre navigateur (siloestream.tech).");
                var e = await ComptesPc.Connecter();
                Toast(e ?? $"Connecté : {ComptesPc.Session?.Nom} ({Roles.Nom(ComptesPc.Session?.Role)}).");
            };
            m.Items.Add(entrer);
        }
        m.IsOpen = true;
    }

    /// <summary>La recherche Ctrl K ne propose que ce que le rôle peut montrer (versets, chants).</summary>
    List<ResultatCommande> FiltrerRole(List<ResultatCommande> l)
    {
        if (!comptesPrets || ComptesPc.EstAdmin) return l;
        var pages = Roles.Pages(ComptesPc.Role);
        return l.Where(r => r.Action != null
            || (r.ChantId != null ? pages.Contains("chants") : r.Ref != null ? pages.Contains("bible") : true)).ToList();
    }

    // ------------------------------------------------------------ pour le test automatique
    public string TestRole(string? role)
    {
        // un compte imaginaire, sans fichier : seules les pages et les boutons changent
        var pages = Roles.Pages(role);
        static string V(UIElement e) => e.Visibility == Visibility.Visible ? "1" : "0";
        OngletAgenda.Visibility = pages.Contains("agenda") ? Visibility.Visible : Visibility.Collapsed;
        OngletBiblio.Visibility = pages.Contains("bible") || pages.Contains("chants") ? Visibility.Visible : Visibility.Collapsed;
        OngletMediasRail.Visibility = pages.Contains("annonces") || pages.Contains("duo") ? Visibility.Visible : Visibility.Collapsed;
        OngletSon.Visibility = pages.Contains("son") ? Visibility.Visible : Visibility.Collapsed;
        OngletReglages.Visibility = pages.Contains("reglages") ? Visibility.Visible : Visibility.Collapsed;
        return $"{V(OngletAgenda)}{V(OngletBiblio)}{V(OngletMediasRail)}{V(OngletSon)}{V(OngletReglages)}";
    }
}
