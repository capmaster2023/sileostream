using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Siloé Stream 3.0.0 : la traduction française des chants swahili (le recueil Wokovu). Elle se fait toute seule, en arrière-plan,
/// et reste dans le chant : ensuite, plus besoin d'Internet. Elle ne s'affiche sous les paroles que si on le veut (touche T).
/// </summary>
public sealed class ReglagesTraduction
{
    /// <summary>Traduire tout seul les chants des recueils swahili (en arrière-plan).</summary>
    [JsonPropertyName("actif")] public bool Actif { get; set; } = true;
    /// <summary>Montrer la traduction sous les paroles (à l'écran, sur Zoom, YouTube, OBS). Non par défaut : seulement si on le veut.</summary>
    [JsonPropertyName("afficher")] public bool Afficher { get; set; }
    /// <summary>Les recueils en swahili : un morceau de leur nom (« wokovu » trouve « Nyimbo za Wokovu »).</summary>
    [JsonPropertyName("recueils")] public List<string> Recueils { get; set; } = new() { "wokovu" };
    /// <summary>Langue des chants et langue de la traduction (codes ISO).</summary>
    [JsonPropertyName("de")] public string De { get; set; } = "sw";
    [JsonPropertyName("vers")] public string Vers { get; set; } = "fr";
    /// <summary>Une adresse courriel (facultative) : le service gratuit traduit alors 50 000 caractères par jour au lieu de 5 000.</summary>
    [JsonPropertyName("courriel")] public string Courriel { get; set; } = "";
    /// <summary>La limite gratuite du jour est atteinte : la traduction reprend à cette heure-là (vide : pas de pause).</summary>
    [JsonPropertyName("reprise"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? Reprise { get; set; }
}

/// <summary>Ce qui sert à traduire les chants : quelles lignes, quels chants, la traduction d'une diapositive.</summary>
public static class TraductionChants
{
    /// <summary>La clé d'une ligne dans <see cref="Chant.Traductions"/> : sans espaces en trop.</summary>
    public static string Cle(string ligne) => Regex.Replace(ligne.Trim(), @"\s+", " ");

    /// <summary>Les lignes d'une diapositive (sans les lignes vides).</summary>
    public static IEnumerable<string> Lignes(string texte) => (texte ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    static string Simple(string s) => Texte.SansAccents((s ?? "").ToLowerInvariant()).Trim();

    /// <summary>Le recueil est en swahili (Wokovu…) : son nom contient un des noms des réglages.</summary>
    public static bool RecueilSwahili(string recueil, ReglagesTraduction r) =>
        (recueil ?? "").Trim().Length > 0 && r.Recueils.Any(m => Simple(m).Length > 0 && Simple(recueil!).Contains(Simple(m)));

    /// <summary>Le chant est d'un recueil en swahili (sa traduction peut être montrée, ou corrigée à la main).</summary>
    public static bool EstSwahili(Chant c, ReglagesTraduction r) => RecueilSwahili(c.Author, r);

    /// <summary>Le chant est en swahili, et la traduction automatique (par Internet) est en marche.</summary>
    public static bool ATraduire(Chant c, ReglagesTraduction r) => r.Actif && EstSwahili(c, r);

    /// <summary>Les lignes du chant pas encore traduites, sans doublons, dans l'ordre (celles de <paramref name="dabord"/> en premier).</summary>
    public static List<string> Manquantes(Chant c, string? dabord = null)
    {
        var lignes = (dabord != null ? Lignes(dabord) : Enumerable.Empty<string>()).Concat(c.Sections.SelectMany(s => Lignes(s.Text)));
        return lignes.Select(Cle).Distinct().Where(k => !(c.Traductions != null && c.Traductions.TryGetValue(k, out var t) && t.Trim().Length > 0)).ToList();
    }

    /// <summary>La traduction d'une diapositive, ligne à ligne ; null tant qu'une de ses lignes n'est pas traduite.</summary>
    public static string? Traduire(Chant c, string texte)
    {
        if (c.Traductions is not { Count: > 0 } t) return null;
        var o = new List<string>();
        foreach (var l in Lignes(texte))
        {
            if (!t.TryGetValue(Cle(l), out var f) || f.Trim().Length == 0) return null;
            o.Add(f.Trim());
        }
        return o.Count > 0 ? string.Join("\n", o) : null;
    }

    /// <summary>Combien de chants à traduire, et combien le sont entièrement.</summary>
    public static (int Traduits, int Total) Compte(IEnumerable<Chant> chants, ReglagesTraduction r)
    {
        int total = 0, faits = 0;
        foreach (var c in chants)
        {
            if (!EstSwahili(c, r)) continue;
            total++;
            if (Manquantes(c).Count == 0) faits++;
        }
        return (faits, total);
    }
}

/// <summary>Un service de traduction : rend autant de lignes qu'il en reçoit, dans le même ordre.</summary>
public interface ITraducteur
{
    Task<List<string>> TraduireAsync(IReadOnlyList<string> lignes, string de, string vers, CancellationToken annuler);
}

/// <summary>La limite gratuite du service est atteinte ; <see cref="Reprise"/> : quand il en redonne (si le service le dit).</summary>
public sealed class QuotaTraduction : Exception
{
    public DateTime? Reprise { get; }
    public QuotaTraduction(string message, DateTime? reprise = null) : base(message) { Reprise = reprise; }
}

/// <summary>
/// MyMemory (translated.net) : gratuit, sans compte. 5 000 caractères par jour, 50 000 avec une adresse courriel ; 500 octets par demande.
/// Chaque ligne est traduite à part : la traduction garde exactement les lignes du chant.
/// </summary>
public sealed class TraducteurMyMemory : ITraducteur
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    public string Adresse { get; set; } = "https://api.mymemory.translated.net/get";
    /// <summary>L'adresse courriel à donner au service (vide : anonyme).</summary>
    public Func<string> Courriel { get; set; } = () => "";
    /// <summary>Le temps entre deux demandes (pour rester poli avec un service gratuit).</summary>
    public TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(150);

    public async Task<List<string>> TraduireAsync(IReadOnlyList<string> lignes, string de, string vers, CancellationToken annuler)
    {
        var o = new List<string>();
        foreach (var l in lignes)
        {
            string t;
            // une ligne que le service refuse (pas le réseau, pas la limite du jour) : vide, la file passe à la suite
            try { t = await UneAsync(l, de, vers, annuler); }
            catch (IOException e) { Journal.Info($"traduction refusée pour « {l} » : {e.Message}"); t = ""; }
            o.Add(t);
            if (Pause > TimeSpan.Zero) await Task.Delay(Pause, annuler);
        }
        return o;
    }

    async Task<string> UneAsync(string texte, string de, string vers, CancellationToken annuler)
    {
        // 500 octets au plus par demande : une ligne de chant plus longue est coupée (rare)
        var q = texte.Length > 240 ? texte[..240] : texte;
        var courriel = (Courriel() ?? "").Trim();
        var url = $"{Adresse}?q={Uri.EscapeDataString(q)}&langpair={Uri.EscapeDataString(de + "|" + vers)}"
            + (courriel.Contains('@') ? "&de=" + Uri.EscapeDataString(courriel) : "");
        using var r = await http.GetAsync(url, annuler);
        var corps = await r.Content.ReadAsStringAsync(annuler);
        return Lire(corps, (int)r.StatusCode, DateTime.Now);
    }

    static readonly Regex PROCHAINE = new(@"NEXT AVAILABLE IN\s+(?:(\d+)\s+HOURS?)?\s*(?:(\d+)\s+MINUTES?)?\s*(?:(\d+)\s+SECONDS?)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// La réponse du service : le texte traduit. <see cref="QuotaTraduction"/> quand la limite gratuite du jour est atteinte (avec l'heure
    /// de reprise si le service la donne), <see cref="IOException"/> pour une autre erreur.
    /// </summary>
    public static string Lire(string json, int statutHttp, DateTime maintenant)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            if (statutHttp == 429) throw new QuotaTraduction("La limite gratuite de traductions du jour est atteinte.", maintenant.AddMinutes(15));
            throw new IOException($"réponse illisible du service de traduction (code {statutHttp})");
        }
        using (doc)
        {
            var racine = doc.RootElement;
            if (racine.ValueKind != JsonValueKind.Object) throw new IOException("réponse inattendue du service de traduction");
            int statut = statutHttp;
            if (racine.TryGetProperty("responseStatus", out var s))
            {
                if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n)) statut = n;
                else if (s.ValueKind == JsonValueKind.String && int.TryParse(s.GetString(), out var n2)) statut = n2;
            }
            bool quota = racine.TryGetProperty("quotaFinished", out var qf) && qf.ValueKind == JsonValueKind.True;
            string texte = racine.TryGetProperty("responseData", out var d) && d.ValueKind == JsonValueKind.Object
                && d.TryGetProperty("translatedText", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            string details = racine.TryGetProperty("responseDetails", out var dd) && dd.ValueKind == JsonValueKind.String ? dd.GetString() ?? "" : "";
            if (quota || statut == 429 || statutHttp == 429 || texte.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)
                || details.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            {
                var m = PROCHAINE.Match(texte + " " + details);
                DateTime? reprise = m.Success && (m.Groups[1].Success || m.Groups[2].Success || m.Groups[3].Success)
                    ? maintenant.AddHours(Nombre(m.Groups[1])).AddMinutes(Nombre(m.Groups[2])).AddSeconds(Nombre(m.Groups[3]) + 30)
                    : maintenant.AddMinutes(15);
                throw new QuotaTraduction("La limite gratuite de traductions du jour est atteinte.", reprise);
            }
            if (statut != 200 || texte.Trim().Length == 0)
                throw new IOException("traduction refusée : " + (details.Length > 0 ? details : texte.Length > 0 ? texte : $"code {statut}"));
            return WebUtility.HtmlDecode(texte).Trim();
        }
    }

    static int Nombre(Group g) => g.Success && int.TryParse(g.Value, out var n) ? n : 0;
}

/// <summary>
/// La file des chants à traduire, en arrière-plan : le chant montré, ouvert ou dans le programme d'abord, puis le reste du recueil,
/// petit à petit. Les traductions arrivent par paquets de quelques lignes (la diapositive à l'écran se complète vite) et sont gardées
/// dans le chant. La limite gratuite atteinte, la file attend l'heure de reprise ; sans Internet, elle réessaie toute seule.
/// Les chants ne sont lus et modifiés que sur le fil de l'interface (celui qui a créé la file) ; le réseau, lui, travaille à côté.
/// </summary>
public sealed class FileTraduction : IDisposable
{
    readonly Donnees donnees;
    readonly ITraducteur traducteur;
    readonly SynchronizationContext? ui;
    readonly object verrou = new();
    readonly List<(string Id, string? Dabord)> file = new();
    readonly SemaphoreSlim signal = new(0);
    readonly CancellationTokenSource fin = new();
    Task? travail;
    string dernierMessage = "";
    /// <summary>Les lignes que le service n'a pas su traduire (« chant|ligne ») : pas redemandées pendant cette séance (à corriger à la main).</summary>
    readonly HashSet<string> abandonnees = new();

    /// <summary>Les lignes d'un chant encore à demander au service (sans celles qu'il n'a pas su traduire).</summary>
    List<string> ADemander(Chant c, string? dabord = null) => TraductionChants.Manquantes(c, dabord).Where(k => !abandonnees.Contains(c.Id + "|" + k)).ToList();

    /// <summary>Des lignes d'un chant viennent d'être traduites (sur le fil de l'interface).</summary>
    public event Action<string>? Traduit;
    /// <summary>Ce qui se passe, en clair (« la suite demain », « pas d'Internet »), sur le fil de l'interface.</summary>
    public event Action<string>? Message;
    /// <summary>L'état de la file, en clair (vide : rien en cours).</summary>
    public string Etat { get; private set; } = "";
    /// <summary>Lignes par paquet (la diapositive à l'écran est d'habitude complète au premier paquet).</summary>
    public int Paquet { get; set; } = 5;
    /// <summary>Attente après une erreur de réseau.</summary>
    public TimeSpan AttenteReseau { get; set; } = TimeSpan.FromMinutes(2);
    public Func<DateTime> Maintenant { get; set; } = () => DateTime.Now;

    public FileTraduction(Donnees donnees, ITraducteur traducteur)
    {
        this.donnees = donnees;
        this.traducteur = traducteur;
        ui = SynchronizationContext.Current;
    }

    ReglagesTraduction R => donnees.Reglages.Traduction;

    /// <summary>Démarre la file (une seule fois).</summary>
    public void Demarrer() => travail ??= Task.Run(BoucleAsync);

    /// <summary>
    /// Un chant à traduire (rien s'il n'est pas d'un recueil swahili ou s'il est déjà traduit). <paramref name="dabord"/> : le texte de la
    /// diapositive à traduire en premier ; un chant demandé ainsi passe devant les autres.
    /// </summary>
    public void Demander(Chant c, string? dabord = null, bool devant = true)
    {
        if (!TraductionChants.ATraduire(c, R) || ADemander(c).Count == 0) return;
        lock (verrou)
        {
            int i = file.FindIndex(x => x.Id == c.Id);
            if (i >= 0)
            {
                if (!devant) return;
                file.RemoveAt(i);
            }
            if (devant) file.Insert(0, (c.Id, dabord)); else file.Add((c.Id, dabord));
        }
        Reveiller();
    }

    /// <summary>Tous les chants à traduire : ceux des programmes d'abord, puis les autres (au démarrage, et quand le réglage change).</summary>
    public void DemanderTout()
    {
        var programme = donnees.Agendas.SelectMany(a => a.Elements).Where(e => e.Type == "chant" && e.ChantId != null).Select(e => e.ChantId!).ToHashSet();
        foreach (var c in donnees.Chants.Where(c => programme.Contains(c.Id)).Concat(donnees.Chants.Where(c => !programme.Contains(c.Id))))
            Demander(c, null, false);
    }

    /// <summary>Combien de chants attendent.</summary>
    public int EnAttente { get { lock (verrou) return file.Count; } }

    void Reveiller()
    {
        try { if (signal.CurrentCount == 0) signal.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }

    T Interface<T>(Func<T> f)
    {
        if (ui == null) return f();
        T r = default!;
        ui.Send(_ => r = f(), null);
        return r;
    }

    void PourInterface(Action a)
    {
        if (ui == null) a(); else ui.Post(_ => a(), null);
    }

    void Dire(string message, bool aussiEtat = true)
    {
        if (aussiEtat) Etat = message;
        if (message == dernierMessage) return;
        dernierMessage = message;
        PourInterface(() => Message?.Invoke(message));
    }

    /// <summary>La limite gratuite atteinte : l'heure de reprise (null : pas de pause).</summary>
    public DateTime? Pause => R.Reprise is { } quand && quand > Maintenant() ? quand : null;

    /// <summary>
    /// Un tour de la file : un paquet de lignes du premier chant traduit et rangé dans le chant (vrai), ou rien à faire (faux : file vide,
    /// ou limite gratuite atteinte, la file attend alors l'heure de reprise). Les erreurs de réseau remontent.
    /// </summary>
    public async Task<bool> TourAsync(CancellationToken annuler = default)
    {
        if (Pause is { } reprise)
        {
            Dire($"Traduction en pause : la limite gratuite du jour est atteinte (reprise vers {reprise:HH:mm}). Les chants déjà traduits restent.");
            return false;
        }
        (string Id, string? Dabord) suivant;
        lock (verrou) suivant = file.Count > 0 ? file[0] : default;
        if (suivant.Id == null) { Etat = ""; return false; }
        // les lignes à traduire, lues sur le fil de l'interface (le chant peut changer pendant ce temps)
        var travailChant = Interface(() =>
        {
            var c = donnees.Chants.FirstOrDefault(x => x.Id == suivant.Id);
            if (c == null || !TraductionChants.ATraduire(c, R)) return (Titre: "", Lignes: new List<string>());
            return (Titre: c.Title, Lignes: ADemander(c, suivant.Dabord));
        });
        if (travailChant.Lignes.Count == 0)
        {
            lock (verrou) file.RemoveAll(x => x.Id == suivant.Id);
            return true;
        }
        Etat = $"Traduction de « {travailChant.Titre} »…";
        var paquet = travailChant.Lignes.Take(Math.Max(1, Paquet)).ToList();
        List<string> traduites;
        try { traduites = await traducteur.TraduireAsync(paquet, R.De, R.Vers, annuler); }
        catch (QuotaTraduction q)
        {
            var quand = q.Reprise ?? Maintenant().AddMinutes(15);
            Interface(() => { R.Reprise = quand; donnees.SauverReglages(); return true; });
            Dire($"Traduction en pause : la limite gratuite du jour est atteinte (reprise vers {quand:HH:mm}). Les chants déjà traduits restent.");
            return false;
        }
        if (traduites.Count != paquet.Count) throw new IOException("le service de traduction n'a pas rendu toutes les lignes");
        var id = suivant.Id;
        // tout de suite (pas « plus tard ») : le tour suivant doit voir ces lignes traduites
        Interface(() => { Appliquer(id, paquet, traduites); return true; });
        dernierMessage = "";
        return true;
    }

    async Task BoucleAsync()
    {
        var annuler = fin.Token;
        while (!annuler.IsCancellationRequested)
        {
            try
            {
                if (await TourAsync(annuler)) continue;
                // rien à faire : la limite gratuite (on attend l'heure de reprise, sans rien demander au service), ou la file vide
                if (Pause is { } quand)
                {
                    var attente = quand - Maintenant();
                    await Task.Delay(attente > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : attente < TimeSpan.Zero ? TimeSpan.Zero : attente, annuler);
                }
                else await signal.WaitAsync(TimeSpan.FromMinutes(10), annuler);
            }
            catch (OperationCanceledException) when (annuler.IsCancellationRequested) { return; }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or JsonException)
            {
                Dire("Traduction en attente : pas d'accès au service de traduction (Internet ?). Elle reprendra toute seule.");
                Journal.Info("traduction des chants : " + e.Message);
                try { await Task.Delay(AttenteReseau, annuler); } catch (OperationCanceledException) { return; }
            }
            catch (Exception e)
            {
                Journal.Erreur("traduction des chants", e);
                try { await Task.Delay(AttenteReseau, annuler); } catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Range les lignes traduites dans le chant (fil de l'interface) et prévient (l'écran se met à jour).</summary>
    void Appliquer(string id, List<string> sources, List<string> traductions)
    {
        if (R.Reprise != null) { R.Reprise = null; donnees.SauverReglages(); }
        var c = donnees.Chants.FirstOrDefault(x => x.Id == id);
        if (c == null) return;
        c.Traductions ??= new Dictionary<string, string>();
        for (int i = 0; i < sources.Count; i++)
        {
            var cle = TraductionChants.Cle(sources[i]);
            if (traductions[i].Trim().Length > 0) c.Traductions[cle] = traductions[i].Trim();
            else abandonnees.Add(id + "|" + cle);
        }
        donnees.SauverChantsPlusTard();
        if (ADemander(c).Count == 0) lock (verrou) file.RemoveAll(x => x.Id == id);
        Traduit?.Invoke(id);
    }

    public void Dispose()
    {
        try { fin.Cancel(); } catch (ObjectDisposedException) { }
    }
}

public sealed partial class Regie
{
    /// <summary>3.0.0 : une diapositive d'un chant swahili n'est pas encore traduite (le chant, le texte de la diapositive).</summary>
    public event Action<Chant, string>? TraductionDemandee;
    /// <summary>3.0.0 : la traduction a changé (montrée ou cachée, nouvelles lignes traduites).</summary>
    public event Action? TraductionChange;

    /// <summary>3.0.0 : la traduction est montrée sous les paroles des chants swahili (seulement si on le veut).</summary>
    public bool TraductionAffichee => donnees.Reglages.Traduction.Afficher;

    /// <summary>
    /// La traduction d'une diapositive, à montrer sous les paroles ; null s'il n'y en a pas (pas un chant swahili, pas encore traduite)
    /// ou si on ne veut pas la montrer. Une diapositive pas encore traduite est demandée (elle sera prête la prochaine fois).
    /// </summary>
    EtatEcran.TexteSecond? SecondChant(Chant c, int i)
    {
        var r = donnees.Reglages.Traduction;
        if (i < 0 || i >= c.Sections.Count || !TraductionChants.EstSwahili(c, r)) return null;
        var texte = c.Sections[i].Text;
        var t = TraductionChants.Traduire(c, texte);
        if (t == null && r.Actif) TraductionDemandee?.Invoke(c, texte);
        return r.Afficher && t != null ? new EtatEcran.TexteSecond(t) : null;
    }

    /// <summary>3.0.0 : montre (ou cache) la traduction sous les paroles, tout de suite, sur la diapositive à l'écran aussi.</summary>
    public void AfficherTraduction(bool afficher)
    {
        var r = donnees.Reglages.Traduction;
        if (r.Afficher == afficher) return;
        r.Afficher = afficher;
        donnees.SauverReglages();
        RafraichirTraduction();
        TraductionChange?.Invoke();
    }

    /// <summary>3.0.0 : des lignes d'un chant viennent d'être traduites (ou corrigées) : la diapositive à l'écran se complète.</summary>
    public void ChantTraduit(string id)
    {
        if (Cible is CibleChant cc && cc.Id == id) RafraichirTraduction();
        TraductionChange?.Invoke();
    }

    void RafraichirTraduction()
    {
        if (Etat.Mode != "chant" || Cible is not CibleChant cc || Trouver(cc.Id) is not { } c || cc.I < 0 || cc.I >= c.Sections.Count) return;
        var second = SecondChant(c, cc.I);
        if (second == Etat.Second) return;
        Etat = Etat with { Second = second };
        Signaler();
    }
}
