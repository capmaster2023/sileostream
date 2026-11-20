namespace MyVerse;

/// <summary>
/// MyStream (Siloé Stream 1.6.0) : l'écran envoyé sur Internet. Zoom (par MyVirtualCam) et YouTube (direct de Siloé Stream) montrent la même image.
/// <para>
/// 1.6.4 : l'église et MyStream sont deux écrans indépendants. MyStream montre sa source principale (une caméra, MyCam, une fenêtre,
/// une vidéo, une image), quoi que montre l'église : les paroles, les versets, les images de l'église n'y vont pas.
/// « Envoyer dans le stream » (touche S, sous l'écran de l'église) : MyStream montre aussi ce que montre l'église, et le suit
/// (le verset suivant, la strophe suivante…) jusqu'à « Source principale » (touche M), qui le ramène sur sa source principale.
/// Une source « MyStream seulement » montrée part toujours sur MyStream (elle est faite pour Internet).
/// </para>
/// <para>
/// 1.6.6 : MyStream ne montre que ce qu'on choisit. Sans source principale, il reste noir (jamais les paroles toutes seules) ;
/// la première caméra (USB ou MyCam) montrée devient sa source principale, sauf si « Aucune » a été choisie exprès.
/// « Envoyer dans le stream » marche aussi sans source principale.
/// </para>
/// </summary>
public sealed partial class Regie
{
    /// <summary>Les sources qui peuvent être la source principale de MyStream (une image vivante ou fixe, pas un texte).</summary>
    /// <remarks>1.6.8 : pas une source « Dans l'église seulement » (MyVirtualCam choisie comme média, un Zoom projeté…).</remarks>
    public static bool SourceStreamPossible(SourceDisplay? s) => s?.Type is "camera" or "mycam" or "fenetre" or "video" or "image" && !s.EgliseSeulement;

    /// <summary>La source principale de MyStream (une source Display), ou null : MyStream reste noir (sauf « Envoyer dans le stream »).</summary>
    public Annonce? SourcePrincipaleStream =>
        donnees.Reglages.MyStreamSource is { Length: > 0 } id && donnees.Annonces.FirstOrDefault(a => a.Id == id) is { } a && SourceStreamPossible(a.Source) ? a : null;

    /// <summary>« Aucune » a été choisie exprès dans le menu « Source » (une caméra montrée ne devient pas la source principale).</summary>
    public bool AucuneSourceStream => donnees.Reglages.MyStreamAucune && SourcePrincipaleStream == null;

    /// <summary>Les sources Display qui peuvent être la source principale, caméras d'abord.</summary>
    public IReadOnlyList<Annonce> SourcesStreamPossibles =>
        donnees.Annonces.Where(a => SourceStreamPossible(a.Source))
            .OrderBy(a => a.Source!.Type switch { "mycam" => 0, "camera" => 1, "fenetre" => 2, "video" => 3, _ => 4 })
            .ThenBy(a => a.Titre, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Choisit la source principale de MyStream (null ou vide : « Aucune », MyStream reste noir). MyStream la montre aussitôt
    /// (s'il suivait l'église, il revient sur la nouvelle source).
    /// </summary>
    public void ChoisirSourceStream(string? id)
    {
        id ??= "";
        if (id.Length > 0 && !SourceStreamPossible(donnees.Annonces.FirstOrDefault(a => a.Id == id)?.Source)) return;
        bool suivait = StreamSuitEglise;
        StreamSuitEglise = false;
        bool aucune = id.Length == 0;
        if (donnees.Reglages.MyStreamSource == id && donnees.Reglages.MyStreamAucune == aucune) { if (suivait) Signaler(); return; }
        donnees.Reglages.MyStreamSource = id;
        donnees.Reglages.MyStreamAucune = aucune;
        donnees.SauverReglages();
        Signaler();
    }

    /// <summary>
    /// 1.6.6 : une caméra (USB ou MyCam) montrée à l'église alors que MyStream n'a pas de source principale devient sa source
    /// principale : Zoom et YouTube la gardent, les paroles et les versets restent dans l'église. Pas si « Aucune » a été choisie
    /// exprès, ni pour une caméra « Dans l'église seulement ».
    /// </summary>
    void AdopterCameraStream(string id, string titre, SourceDisplay source)
    {
        if (source.Type is not ("camera" or "mycam") || source.EgliseSeulement || SourcePrincipaleStream != null || donnees.Reglages.MyStreamAucune) return;
        donnees.Reglages.MyStreamSource = id;
        donnees.SauverReglages();
        Dire($"« {titre} » devient la source principale de MyStream : Zoom et YouTube la gardent, les paroles et les versets restent dans l'église.");
    }

    /// <summary>
    /// « Envoyer dans le stream » (touche S) : MyStream montre aussi ce que montre l'église, et la suit jusqu'à « Source principale ».
    /// </summary>
    public bool StreamSuitEglise { get; private set; }

    /// <summary>MyStream montre sa source principale (une source principale est choisie et MyStream ne suit pas l'église).</summary>
    public bool StreamSurPrincipale => SourcePrincipaleStream != null && !StreamSuitEglise;

    /// <summary>« Envoyer dans le stream » : MyStream montre ce que montre l'église et la suit (avec ou sans source principale).</summary>
    public bool EnvoyerDansStream()
    {
        if (StreamSuitEglise) return true;
        StreamSuitEglise = true;
        Signaler();
        return true;
    }

    /// <summary>
    /// « Source principale » (touche M) : MyStream ne suit plus l'église et revient sur sa source principale (noir sans source
    /// principale) ; l'église garde ce qu'elle montre. Faux sans source principale quand MyStream ne suivait pas l'église.
    /// </summary>
    public bool StreamPrincipale()
    {
        if (StreamSuitEglise)
        {
            StreamSuitEglise = false;
            Signaler();
            return true;
        }
        if (SourcePrincipaleStream == null) { Dire("Choisissez d'abord la source principale de MyStream (à droite, sous MyStream : « Source »)."); return false; }
        return true;
    }

    /// <summary>
    /// Ce que montre MyStream (Zoom et YouTube) pour l'état <paramref name="e"/> de l'église (écran noir compris).
    /// Sa source principale, quoi que montre l'église (noir sans source principale) ; après « Envoyer dans le stream », ce que montre
    /// l'église, et la source principale quand l'église n'a rien pour Internet (vide, noir, fond seul, écran de repos, source
    /// « Dans l'église »). Sans source principale, « Envoyer dans le stream » montre ce que voyait Zoom avant la 1.6 (<see cref="EtatCamera"/>).
    /// </summary>
    public EtatEcran EtatStream(EtatEcran e)
    {
        var p = SourcePrincipaleStream?.Source;
        // un calque « Dans l'église » ne part pas sur Internet
        var r = CalquesPermis(e, s => s.EgliseSeulement);
        // une source « MyStream seulement » est faite pour Internet : elle y part toujours (sans les calques de l'église)
        if (r is { Mode: "media", Media.StreamingSeulement: true }) return StreamSuitEglise ? r : r with { Calques = null, Rang = 0 };
        if (p == null) return StreamSuitEglise ? EtatCamera(e) : Noir(r);
        var seule = r with { Mode = "media", Media = p, Lignes = Array.Empty<string>(), Ref = "", Second = null, Calques = null, Rang = 0, Presentation = "plein" };
        if (!StreamSuitEglise) return seule;
        // MyStream suit l'église ; quand elle n'a rien pour Internet, la source principale (avec les calques de l'église)
        if (Cible is CibleDefaut || r.Mode is "vide" or "fond" || r is { Mode: "media", Media.EgliseSeulement: true })
            return seule with { Calques = r.Calques, Rang = r.Calques == null ? 0 : r.Rang };
        return r;
    }

    /// <summary>MyStream noir : rien n'a été choisi pour Internet.</summary>
    static EtatEcran Noir(EtatEcran e) => e with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null, Calques = null, Rang = 0, Presentation = "plein" };

    /// <summary>Le texte posé sur une image (le dessin le met en bandeau par-dessus l'image).</summary>
    public static bool TexteSurImage(EtatEcran e) => e.Media != null && e.Mode is "verset" or "chant";
}
