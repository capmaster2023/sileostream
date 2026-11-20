namespace MyVerse;

/// <summary>
/// Écran par défaut (Siloé Stream 3.3).
/// Le déroulé peut contenir des « écrans principaux » (logo, image, caméra, fenêtre, texte de bienvenue, fond seul) : quand l'un
/// d'eux passe à l'écran, il y reste et devient l'écran par défaut. Dans la colonne de l'écran en direct, on choisit l'écran par
/// défaut parmi eux, le fond seul ou noir. « Retirer de l'écran » (Échap) revient alors direct dessus au lieu de vider l'écran.
/// L'écran par défaut est pour la salle : OBS, la caméra virtuelle et la page publique restent vides, comme avant.
/// </summary>
public sealed partial class Regie
{
    /// <summary>Ce qui était à l'écran avant « Retirer » : Suivant reprend après, Précédent le remet.</summary>
    Cible? cibleRetiree;
    ElementAgenda? elementRetire;
    /// <summary>L'écran par défaut est arrivé par « Retirer » (et non affiché depuis le déroulé).</summary>
    bool defautApresRetrait;

    bool SurDefautApresRetrait => defautApresRetrait && Cible is CibleDefaut;

    void OublierReprise()
    {
        cibleRetiree = null;
        elementRetire = null;
    }

    // ------------------------------------------------------------ le choix
    /// <summary>L'écran par défaut en vigueur : « noir », « fond » ou l'id d'un écran principal du déroulé (noir s'il n'y est plus).</summary>
    public string EcranDefaut
    {
        get
        {
            var d = donnees.Reglages.EcranDefaut;
            if (d is "noir" or "fond") return d;
            return !string.IsNullOrEmpty(d) && ElementDuDeroule(d) is { Type: "principal" } ? d : "noir";
        }
    }

    /// <summary>« Noir », « Fond seul » ou le titre de l'écran principal choisi.</summary>
    public string LibelleEcranDefaut => EcranDefaut switch
    {
        "noir" => "Noir",
        "fond" => "Fond seul",
        var id => ElementDuDeroule(id)?.Titre ?? "Noir",
    };

    /// <summary>Les écrans principaux du déroulé choisi, dans l'ordre du culte.</summary>
    public IReadOnlyList<ElementAgenda> EcransPrincipaux => Agenda.Elements.Where(e => e.Type == "principal").ToList();

    /// <summary>
    /// Choisit l'écran par défaut (colonne de l'écran en direct) : « noir », « fond » ou un écran principal du déroulé.
    /// Si l'écran par défaut est déjà à l'écran (après « Retirer »), ou si l'écran est vide, le nouveau s'affiche tout de suite.
    /// </summary>
    public void ChoisirEcranDefaut(string id)
    {
        if (id is not ("noir" or "fond") && ElementDuDeroule(id) is not { Type: "principal" }) return;
        donnees.Reglages.EcranDefaut = id;
        donnees.SauverReglages();
        if (SurDefautApresRetrait || (Cible == null && Etat.Vide))
        {
            Vider(false);
            if (MontrerDefaut()) defautApresRetrait = true;
            else OublierReprise();
        }
        Signaler();
    }

    /// <summary>
    /// 1.8.0 : un média devient la source principale de l'église (son écran par défaut) : l'écran principal du déroulé qui le
    /// montre, ajouté au besoin. Faux pour un texte.
    /// </summary>
    public bool ChoisirPrincipaleEglise(string annonceId)
    {
        if (donnees.Annonces.FirstOrDefault(x => x.Id == annonceId) is not { Source: { Type: "image" or "camera" or "mycam" or "fenetre" or "video" } s }) return false;
        var e = Agenda.Elements.FirstOrDefault(x => x.Type == "principal" && x.Source is { } xs && MemeSource(xs, s)) ?? AjouterPrincipalAgenda(annonceId);
        if (e == null) return false;
        ChoisirEcranDefaut(e.Id);
        return true;
    }

    /// <summary>1.8.0 : le média (id) qui est la source principale de l'église, ou null (noir, fond seul, texte).</summary>
    public string? AnnoncePrincipaleEglise =>
        EcranDefaut is "noir" or "fond" || ElementDuDeroule(EcranDefaut)?.Source is not { } s ? null
            : donnees.Annonces.FirstOrDefault(a => a.Source is { } x && MemeSource(x, s))?.Id;

    static bool MemeSource(SourceDisplay a, SourceDisplay b) =>
        a.Type == b.Type && a.Peripherique == b.Peripherique && a.Chemin == b.Chemin && a.Fenetre == b.Fenetre && a.Processus == b.Processus;

    /// <summary>
    /// Un écran principal dans le déroulé : une source Display (image, caméra, fenêtre, vidéo), un display texte (sa première diapositive),
    /// ou rien (le fond seul : l'habillage sans texte). Null si la source n'existe pas ou est un texte vide.
    /// </summary>
    public ElementAgenda? AjouterPrincipalAgenda(string? annonceId, int position = -1)
    {
        if (annonceId == null) return Ajouter(new ElementAgenda { Type = "principal", Titre = "Fond seul" }, position);
        if (donnees.Annonces.FirstOrDefault(x => x.Id == annonceId) is not { } a) return null;
        if (a.Source == null && BlocsTexte(a.Texte).Count == 0) { Dire($"« {a.Titre} » est vide."); return null; }
        return Ajouter(new ElementAgenda
        {
            Type = "principal", Titre = a.Titre.Length > 0 ? a.Titre : "Écran principal",
            Source = a.Source, Texte = a.Source == null ? a.Texte : null,
        }, position);
    }

    /// <summary>« Image », « Caméra », « Texte », « Fond seul »… (déroulé).</summary>
    static string SortePrincipal(ElementAgenda e) => e.Source?.Type switch
    {
        "image" => "Image",
        "camera" => "Caméra",
        "mycam" => "MyCam",
        "fenetre" => "Capture de fenêtre",
        "video" => "Vidéo",
        _ => BlocsTexte(e.Texte).Count > 0 ? "Texte" : "Fond seul",
    };

    // ------------------------------------------------------------ ce qui s'affiche
    /// <summary>L'écran d'un écran principal : son média, la première diapositive de son texte, ou le fond seul.</summary>
    EtatEcran EtatPrincipal(ElementAgenda e, EtatEcran b)
    {
        if (e.Source is { Type: "image" or "camera" or "mycam" or "fenetre" or "video" } s)
            return b with { Mode = "media", Media = s, Lignes = Array.Empty<string>(), Ref = e.Titre, Second = null };
        var blocs = BlocsTexte(e.Texte);
        if (blocs.Count > 0)
            return b with { Mode = "chant", Media = null, Lignes = blocs[0].Split('\n').Where(l => l.Trim().Length > 0).ToArray(), Ref = "", Second = null };
        return EtatFond(b);
    }

    /// <summary>Le fond seul : l'habillage choisi, sans texte.</summary>
    static EtatEcran EtatFond(EtatEcran b) => b with { Mode = "fond", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null };

    /// <summary>L'écran sans son contenu principal (ni texte ni média) : les calques restent.</summary>
    static EtatEcran SansContenu(EtatEcran b) => b with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null };

    /// <summary>
    /// Ce qui part vers OBS et la page publique : l'écran par défaut est pour la salle, eux restent vides (les calques restent),
    /// comme quand on retirait les paroles avant la 3.3 ; la page OBS reste transparente par-dessus la caméra d'OBS.
    /// Une source « Dans l'église » (1.3.1) ne part pas non plus, ni en grand ni en calque. MyVirtualCam, elle, suit <see cref="EtatCamera"/>.
    /// </summary>
    public EtatEcran EtatDiffusion(EtatEcran e)
    {
        var r = CalquesPermis(e, s => s.EgliseSeulement);
        return Cible is CibleDefaut || r is { Mode: "media", Media.EgliseSeulement: true } ? SansContenu(r) : r;
    }

    /// <summary>
    /// Ce que montre MyVirtualCam, la caméra de Zoom (1.3.2) : comme l'écran de projection, sauf une source « Dans l'église »
    /// (ni en grand, ni en calque) : à sa place, l'écran par défaut (le logo, une image, le fond seul…). Une fenêtre Zoom projetée
    /// dans l'église ne revient pas dans Zoom (pas d'effet miroir), et Zoom voit l'écran principal au lieu d'un écran noir.
    /// L'écran par défaut lui-même part aussi vers MyVirtualCam (jusqu'à la 1.3.1, elle restait noire).
    /// </summary>
    public EtatEcran EtatCamera(EtatEcran e)
    {
        var r = CalquesPermis(e, s => s.EgliseSeulement);
        if (r is not { Mode: "media", Media.EgliseSeulement: true }) return r;
        return EcranDefautSur(SansContenu(r), s => !s.EgliseSeulement);
    }

    /// <summary>
    /// Ce qui part sur l'écran de projection, dans l'église (1.3.1) : tout, sauf une source « En streaming ». À sa place, la salle garde
    /// l'écran par défaut (noir, le fond seul ou un écran principal du déroulé), comme après « Retirer de l'écran ».
    /// Un calque « MyStream seulement » n'y est pas non plus.
    /// </summary>
    public EtatEcran EtatSalle(EtatEcran e)
    {
        var r = CalquesPermis(e, s => s.StreamingSeulement);
        if (r is not { Mode: "media", Media.StreamingSeulement: true }) return r;
        return EcranDefautSur(SansContenu(r), s => !s.StreamingSeulement);
    }

    /// <summary>
    /// L'écran par défaut posé sur <paramref name="vide"/> (même habillage, mêmes calques) : rien pour « noir », le fond seul, ou l'écran
    /// principal choisi. Un écran principal dont la source ne peut pas aller là (<paramref name="permis"/>) laisse l'écran vide.
    /// </summary>
    EtatEcran EcranDefautSur(EtatEcran vide, Func<SourceDisplay, bool> permis) => EcranDefaut switch
    {
        "noir" => vide,
        "fond" => EtatFond(vide),
        var id => ElementDuDeroule(id) is { } el && (el.Source == null || permis(el.Source)) ? EtatPrincipal(el, vide) : vide,
    };

    /// <summary>Un écran principal affiché depuis le déroulé (clic, Suivant) : il reste à l'écran et devient l'écran par défaut.</summary>
    void AfficherPrincipal(ElementAgenda e)
    {
        donnees.Reglages.EcranDefaut = e.Id;
        donnees.SauverReglages();
        Cible = new CibleDefaut(e.Id);
        defautApresRetrait = false;
        OublierReprise();
        // pour la salle : la source OBS reste cachée (sauf calques montrés)
        Oeil?.Invoke(Etat.AvecCalques);
        Etat = EtatPrincipal(e, Etat);
        Noter("e:" + e.Id, e.Titre, "principal");
        Signaler();
    }

    /// <summary>Met l'écran par défaut sur l'écran vidé (sans signaler). Faux pour « noir » : l'écran reste vide.</summary>
    bool MontrerDefaut()
    {
        var id = EcranDefaut;
        if (id == "noir") return false;
        if (id == "fond") Etat = EtatFond(Etat);
        else if (ElementDuDeroule(id) is { } e) Etat = EtatPrincipal(e, Etat);
        else return false;
        Cible = new CibleDefaut(id);
        ElementCourant = null;
        defautApresRetrait = true;
        return true;
    }

    // ------------------------------------------------------------ reprendre après « Retirer »
    bool EnReprise(out Cible c, out ElementAgenda? el)
    {
        c = cibleRetiree!;
        el = elementRetire;
        return SurDefautApresRetrait && cibleRetiree != null;
    }

    /// <summary>Exécute <paramref name="f"/> comme si ce qui a été retiré était encore à l'écran.</summary>
    T AvecPosition<T>(Cible c, ElementAgenda? el, Func<T> f)
    {
        var (cible, element) = (Cible, ElementCourant);
        Cible = c;
        ElementCourant = el;
        try { return f(); }
        finally { Cible = cible; ElementCourant = element; }
    }

    /// <summary>Sur l'écran par défaut après « Retirer » : Suivant affiche ce qui vient après ce qui a été retiré, Précédent le remet.</summary>
    bool Reprise(int d)
    {
        if (!EnReprise(out var c, out var el)) return false;
        if (d < 0)
        {
            OublierReprise();
            Remettre(c, el);
            return true;
        }
        // ni chant, ni verset, ni texte, ni élément du déroulé (source Display seule) : rien après, l'écran par défaut reste
        if (el == null && c is not (CibleChant or CibleVerset or CibleTexte)) return true;
        var avant = Etat;
        var (cible, element) = (Cible, ElementCourant);
        Cible = c;
        ElementCourant = el;
        BougerDepuis(d);
        if (ReferenceEquals(Etat, avant))
        {
            // fin du déroulé, du chant ou du livre : l'écran par défaut reste
            Cible = cible;
            ElementCourant = element;
        }
        else OublierReprise();
        return true;
    }

    /// <summary>Réaffiche ce qui a été retiré.</summary>
    void Remettre(Cible c, ElementAgenda? el)
    {
        if (el != null && Diapos(el).FirstOrDefault(x => MemeCible(x.Cible, c)) is { } dp)
        {
            AfficherDiapo(el, dp);
            return;
        }
        switch (c)
        {
            case CibleChant cc: AfficherSection(cc.Id, cc.I); break;
            case CibleVerset cv: AfficherVerset(cv.B, cv.C, cv.V1, cv.V2, cv.Part); break;
            case CibleTexte ct when donnees.Annonces.FirstOrDefault(a => a.Id == ct.Element) is { } an: AfficherAnnonce(an, ct.I); break;
            case CibleDisplay cd when donnees.Annonces.FirstOrDefault(a => a.Id == cd.Id) is { } ad: AfficherAnnonce(ad); break;
        }
    }

    /// <summary>
    /// La diapositive du déroulé qui viendra avec Suivant (élément et position), y compris sur l'écran par défaut après « Retirer ».
    /// Null hors du déroulé ou à la fin.
    /// </summary>
    public (ElementAgenda E, int I)? SuivanteDeroule()
    {
        var (c, el) = EnReprise(out var cr, out var er) ? (cr, er) : (Cible, ElementCourant);
        if (c == null || el == null) return null;
        var d = Diapos(el);
        int pos = d.FindIndex(x => MemeCible(x.Cible, c));
        if (pos < 0) return null;
        if (pos + 1 < d.Count) return (el, pos + 1);
        var l = Agenda.Elements;
        for (int k = l.IndexOf(el) + 1; k >= 1 && k < l.Count; k++)
            if (Diapos(l[k]).Count > 0) return (l[k], 0);
        return null;
    }
}
