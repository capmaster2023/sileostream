using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace MyVerse;

public sealed class BibleImportee
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("short")] public string Short { get; set; } = "";
    [JsonPropertyName("versets")] public int Versets { get; set; }
}

public sealed class ReglagesBibles
{
    [JsonPropertyName("principale")] public string Principale { get; set; } = "lsg";
    [JsonPropertyName("seconde")] public string Seconde { get; set; } = "";
    [JsonPropertyName("importees")] public List<BibleImportee> Importees { get; set; } = new();
}

public sealed class ReglagesObs
{
    [JsonPropertyName("actif")] public bool Actif { get; set; }
    [JsonPropertyName("port")] public int Port { get; set; } = 4455;
    [JsonPropertyName("mdp")] public string Mdp { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "BIBLE";
}

/// <summary>Écran de projection choisi : plein écran sur un moniteur, fenêtre, ou OBS seulement.</summary>
public sealed class EcranChoisi
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "plein"; // plein, fenetre, obs
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("w")] public int W { get; set; }
    [JsonPropertyName("h")] public int H { get; set; }
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("p")] public bool Principal { get; set; }
    /// <summary>Sortie de la carte graphique (\\.\DISPLAY2) : retrouver le projecteur rebranché (Siloé Stream 1.0).</summary>
    [JsonPropertyName("periph")] public string Peripherique { get; set; } = "";
    /// <summary>
    /// Projection ouverte quand Siloé Stream s'est fermé : elle se rouvre au démarrage. Faux : arrêtée par l'opérateur.
    /// Absent (réglage de Siloé Stream 4) : elle se rouvre aussi.
    /// </summary>
    [JsonPropertyName("ouvert")] public bool? Ouvert { get; set; }
}

public sealed class PositionFenetre
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
    [JsonPropertyName("max")] public bool Max { get; set; } = true;
}

public sealed class Reglages
{
    [JsonPropertyName("audio")] public MyVerse.Audio.AudioConfiguration Audio { get; set; } = new();
    [JsonPropertyName("duoPlace")] public string DuoPlace { get; set; } = "hd";
    [JsonPropertyName("duoPart")] public double DuoPart { get; set; } = 0.35;
    [JsonPropertyName("version")] public int Version { get; set; } = 4;
    [JsonPropertyName("theme")] public string Theme { get; set; } = "nuit";
    [JsonPropertyName("taille")] public double Taille { get; set; } = 1;
    [JsonPropertyName("presentation")] public string Presentation { get; set; } = "plein";
    [JsonPropertyName("bibles")] public ReglagesBibles Bibles { get; set; } = new();
    [JsonPropertyName("obs")] public ReglagesObs Obs { get; set; } = new();
    [JsonPropertyName("ecran")] public EcranChoisi? Ecran { get; set; }
    [JsonPropertyName("lignesDiapo")] public int LignesDiapo { get; set; }
    /// <summary>Avant la 4.2, les recueils de l'église étaient ajoutés au premier démarrage. Depuis, Siloé Stream démarre vide (seulement les Bibles LSG et KJV).</summary>
    [JsonPropertyName("inclus")] public bool Inclus { get; set; }
    [JsonPropertyName("fenetre")] public PositionFenetre? Fenetre { get; set; }
    [JsonPropertyName("recueil")] public string Recueil { get; set; } = "";
    [JsonPropertyName("agenda")] public string Agenda { get; set; } = "";
    /// <summary>Thème de l'interface (Siloé Stream 3.1) : « clair » (par défaut) ou « sombre ». À ne pas confondre avec <see cref="Theme"/>, le fond de la projection.</summary>
    [JsonPropertyName("apparence")] public string Apparence { get; set; } = "clair";
    /// <summary>Siloé Stream 2.0 : « simple » (les écrans du Grand Bouton, par défaut) ou « complet » (toute la régie, comme en 1.x).</summary>
    [JsonPropertyName("interface")] public string Interface { get; set; } = "simple";
    /// <summary>
    /// Écran par défaut (Siloé Stream 3.3) : ce qui revient à l'écran quand on retire les paroles. « noir » (l'écran se vide, comme avant),
    /// « fond » (l'habillage sans texte) ou l'id d'un écran principal du déroulé.
    /// </summary>
    [JsonPropertyName("ecranDefaut")] public string EcranDefaut { get; set; } = "noir";
    /// <summary>MyStream (1.6.0) : la source principale, sur Zoom et YouTube quoi que montre l'église (id d'une source Display). Vide : MyStream reste noir (1.6.6).</summary>
    [JsonPropertyName("myStreamSource")] public string MyStreamSource { get; set; } = "";
    /// <summary>MyStream (1.6.6) : « Aucune » choisie exprès : une caméra montrée ne devient pas la source principale toute seule.</summary>
    [JsonPropertyName("myStreamAucune")] public bool MyStreamAucune { get; set; }
    /// <summary>Le direct (1.6.0) : YouTube (compte connecté ou clé de stream) ou un autre service.</summary>
    [JsonPropertyName("direct")] public ReglagesDirect Direct { get; set; } = new();
}

/// <summary>
/// Données de Siloé Stream dans %APPDATA%\SiloeStream (copiées une fois de l'ancien dossier) : réglages, chants, versets découpés.
/// Au premier démarrage : Siloé Stream est vide (aucun chant, aucune source), seules les Bibles libres de droits Louis Segond 1910 et King James
/// sont livrées. Les données de la 3.x (myverse-donnees.json) sont reprises s'il y en a.
/// </summary>
public sealed class Donnees
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public string Dossier { get; }
    public string DossierRessources { get; }
    public Reglages Reglages { get; private set; } = new();
    public List<Chant> Chants { get; private set; } = new();
    public Dictionary<string, List<string>> Decoupes { get; private set; } = new();
    public List<Agenda> Agendas { get; private set; } = new();
    public List<Annonce> Annonces { get; private set; } = new();
    public List<DossierDuo> Duos { get; private set; } = new();
    string FDuos => Path.Combine(Dossier, "duos.json");
    public void SauverDuos() => Sauver(FDuos, Duos, "duos");
    /// <summary>Ce qui a été repris de la 3.x au premier démarrage (vide sinon).</summary>
    public string Migration { get; private set; } = "";

    string FReglages => Path.Combine(Dossier, "reglages.json");
    string FChants => Path.Combine(Dossier, "chants.json");
    string FDecoupes => Path.Combine(Dossier, "decoupes.json");
    string FAgendas => Path.Combine(Dossier, "agendas.json");
    string FAnnonces => Path.Combine(Dossier, "annonces.json");
    public string DossierBibles => Path.Combine(Dossier, "bibles");

    Donnees(string dossier, string ressources) { Dossier = dossier; DossierRessources = ressources; }

    public static string DossierParDefaut()
    {
        var d = Environment.GetEnvironmentVariable("SILOE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(d)) return d;
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nouveau = Path.Combine(appdata, "SiloeStream");
        // 1.9.0 : nouveau nom : le dossier des données est copié une seule fois depuis l'ancien,
        // qui reste en place comme sauvegarde (chants, Bibles, réglages, téléphones jumelés)
        var ancien = Path.Combine(appdata, "VerseLight");
        if (!Directory.Exists(nouveau) && Directory.Exists(ancien))
        {
            try
            {
                var temp = nouveau + ".copie-" + Environment.ProcessId;
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                Copier(ancien, temp);
                Directory.Move(temp, nouveau);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Directory.Exists(nouveau) ? nouveau : ancien; }
        }
        return nouveau;
    }

    static void Copier(string de, string vers)
    {
        Directory.CreateDirectory(vers);
        foreach (var f in Directory.GetFiles(de)) File.Copy(f, Path.Combine(vers, Path.GetFileName(f)), true);
        foreach (var r in Directory.GetDirectories(de)) Copier(r, Path.Combine(vers, Path.GetFileName(r)));
    }

    public static Donnees Charger(string dossier, string ressources)
    {
        Directory.CreateDirectory(dossier);
        var d = new Donnees(dossier, ressources);
        bool nouveau = !File.Exists(d.FReglages);
        d.Reglages = Lire<Reglages>(d.FReglages) ?? new Reglages();
        d.Chants = Lire<List<Chant>>(d.FChants) ?? new List<Chant>();
        d.Decoupes = Lire<Dictionary<string, List<string>>>(d.FDecoupes) ?? new Dictionary<string, List<string>>();
        d.Agendas = Lire<List<Agenda>>(d.FAgendas) ?? new List<Agenda>();
        foreach (var a in d.Agendas) { a.Elements ??= new(); a.Nom ??= ""; }
        d.Annonces = Lire<List<Annonce>>(d.FAnnonces) ?? new List<Annonce>();
        d.Duos = Lire<List<DossierDuo>>(d.FDuos) ?? new List<DossierDuo>();
        foreach (var x in d.Duos) { x.A ??= new(); x.B ??= new(); x.Nom ??= ""; }
        if (nouveau)
        {
            try { d.Migrer(); }
            catch (Exception e) { Journal.Erreur("reprise des données de la 3.x", e); }
        }
        // 4.2 : plus aucun chant ajouté au premier démarrage, le logiciel démarre vide
        d.Reglages.Inclus = true;
        d.Corriger();
        if (nouveau || !File.Exists(d.FReglages)) d.SauverReglages();
        return d;
    }

    void Corriger()
    {
        var r = Reglages;
        r.Audio ??= new MyVerse.Audio.AudioConfiguration();
        r.Bibles ??= new ReglagesBibles();
        r.Bibles.Importees ??= new List<BibleImportee>();
        r.Obs ??= new ReglagesObs();
        r.MyStreamSource ??= "";
        r.Direct ??= new ReglagesDirect();
        r.Direct.Normaliser();
        if (string.IsNullOrEmpty(r.Bibles.Principale)) r.Bibles.Principale = "lsg";
        r.Bibles.Seconde ??= "";
        if (r.Bibles.Seconde == r.Bibles.Principale) r.Bibles.Seconde = "";
        if (r.Theme is not ("nuit" or "aube" or "lin" or "ciel" or "noir" or "vert" or "transparent")) r.Theme = "nuit";
        if (r.Presentation is not ("plein" or "bandeau")) r.Presentation = "plein";
        if (!(r.Taille >= 0.5 && r.Taille <= 1.6)) r.Taille = 1;
        if (r.Obs.Port <= 0 || r.Obs.Port > 65535) r.Obs.Port = 4455;
        foreach (var c in Chants)
        {
            if (string.IsNullOrEmpty(c.Id)) c.Id = Chant.NouvelId();
            c.Title = Texte.Espaces.Replace(c.Title ?? "", " ").Trim();
            if (c.Title.Length == 0) c.Title = "Sans titre";
            c.Author ??= "";
            c.Sections ??= new List<Section>();
            foreach (var s in c.Sections) { s.Label ??= ""; s.Text ??= ""; }
        }
        // identifiants en double (copie de chants) : on les sépare
        var vus = new HashSet<string>();
        foreach (var c in Chants) if (!vus.Add(c.Id)) { c.Id = Chant.NouvelId(); vus.Add(c.Id); }
    }

    // ------------------------------------------------------------ lecture / écriture
    public static T? Lire<T>(string chemin) where T : class
    {
        foreach (var f in new[] { chemin, chemin + ".bak" })
        {
            try
            {
                if (!File.Exists(f)) continue;
                var t = Texte.Decoder(File.ReadAllBytes(f));
                if (t.Trim().Length == 0) continue;
                return JsonSerializer.Deserialize<T>(t, Json);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Journal.Erreur($"fichier illisible : {Path.GetFileName(f)} ({e.Message})");
            }
        }
        return null;
    }

    /// <summary>Écriture sûre : fichier temporaire puis remplacement (l'ancien devient .bak).</summary>
    public static void Ecrire(string chemin, string contenu)
    {
        var tmp = chemin + ".tmp";
        File.WriteAllText(tmp, contenu, new System.Text.UTF8Encoding(false));
        if (File.Exists(chemin)) File.Replace(tmp, chemin, chemin + ".bak", true);
        else File.Move(tmp, chemin);
    }

    void Sauver(string chemin, object valeur, string quoi)
    {
        try { Ecrire(chemin, JsonSerializer.Serialize(valeur, Json)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Journal.Erreur($"enregistrement des {quoi}", e); }
    }

    public void SauverReglages() => Sauver(FReglages, Reglages, "réglages");
    public void SauverAgendas() => Sauver(FAgendas, Agendas, "agendas");
    public void SauverAnnonces() => Sauver(FAnnonces, Annonces, "annonces");
    public void SauverChants() { lock (verrouChants) Sauver(FChants, Chants, "chants"); }

    // ------------------------------------------------------------ 1.5.1 : l'éditeur enregistre tout seul, à côté
    readonly object verrouChants = new();
    int chantsAEcrire;
    /// <summary>
    /// Enregistre les chants un instant plus tard, sans bloquer l'interface (l'éditeur enregistre à chaque pause de frappe) :
    /// plusieurs demandes rapprochées ne font qu'une écriture. Le fichier reste toujours entier (écrit à côté, puis remplacé).
    /// </summary>
    public void SauverChantsPlusTard()
    {
        if (Interlocked.Exchange(ref chantsAEcrire, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            Interlocked.Exchange(ref chantsAEcrire, 0);
            for (int essai = 0; essai < 4; essai++)
            {
                try
                {
                    lock (verrouChants) Ecrire(FChants, JsonSerializer.Serialize(Chants.ToList(), Json));
                    return;
                }
                // la liste a changé pendant la copie : on recommence un peu plus tard
                catch (InvalidOperationException) { await Task.Delay(120); }
                catch (ArgumentException) { await Task.Delay(120); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Journal.Erreur("enregistrement des chants", e); return; }
            }
        });
    }
    public void SauverDecoupes() => Sauver(FDecoupes, Decoupes, "versets découpés");

    // ------------------------------------------------------------ reprise des données de MyVerse 3.x
    /// <summary>
    /// La 3.0.4 copie ses données (chants, réglages, Bibles choisies, versets découpés) dans myverse-donnees.json.
    /// Sinon, on les lit directement dans le profil WebView2 de la 3.x (stockage local).
    /// </summary>
    void Migrer()
    {
        Dictionary<string, string>? cles = null;
        string source = "";
        var fichier = Path.Combine(Dossier, "myverse-donnees.json");
        if (File.Exists(fichier))
        {
            using var doc = JsonDocument.Parse(Texte.Decoder(File.ReadAllBytes(fichier)));
            if (doc.RootElement.TryGetProperty("donnees", out var dd) && dd.ValueKind == JsonValueKind.Object)
            {
                cles = new Dictionary<string, string>();
                foreach (var p in dd.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) cles[p.Name] = p.Value.GetString() ?? "";
                source = "copie de la 3.x";
            }
        }
        if (cles == null || cles.Count == 0)
        {
            cles = Ancien3x.LireStockageLocal();
            source = "profil de la 3.x";
        }
        if (cles == null || cles.Count == 0) return;
        Migration = AppliquerCles(cles, source);
        if (Migration.Length > 0) Journal.Info("reprise des données de MyVerse 3.x (" + source + ") : " + Migration);
    }

    /// <summary>Applique les clés du stockage local de la 3.x (valeurs JSON en texte). Renvoie un résumé.</summary>
    public string AppliquerCles(IReadOnlyDictionary<string, string> cles, string source = "")
    {
        var faits = new List<string>();
        JsonElement? Val(string k)
        {
            if (!cles.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)) return null;
            try { using var d = JsonDocument.Parse(v); return d.RootElement.Clone(); }
            catch (JsonException) { return null; }
        }
        if (Val("verselight-chants") is { ValueKind: JsonValueKind.Array } ch)
        {
            var liste = ch.Deserialize<List<Chant>>(Json) ?? new List<Chant>();
            liste = liste.Where(c => c != null && c.Sections != null).ToList();
            if (liste.Count > 0)
            {
                Chants = liste;
                Reglages.Inclus = true; // la 3.x avait déjà ajouté les recueils de l'église
                SauverChants();
                faits.Add($"{liste.Count} chants");
            }
        }
        if (Val("verselight-inclus-v1") is { ValueKind: JsonValueKind.True }) Reglages.Inclus = true;
        if (Val("verselight-theme") is { ValueKind: JsonValueKind.String } th) Reglages.Theme = th.GetString() ?? "nuit";
        if (Val("verselight-taille") is { ValueKind: JsonValueKind.Number } ta) Reglages.Taille = ta.GetDouble();
        if (Val("verselight-presentation") is { ValueKind: JsonValueKind.String } pr) Reglages.Presentation = pr.GetString() ?? "plein";
        if (Val("myverse-decoupes") is { ValueKind: JsonValueKind.Object } de)
        {
            var dec = new Dictionary<string, List<string>>();
            foreach (var p in de.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array)
                    dec[p.Name] = p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToList();
            if (dec.Count > 0) { Decoupes = dec; SauverDecoupes(); faits.Add($"{dec.Count} versets découpés"); }
        }
        if (Val("myverse-bibles") is { ValueKind: JsonValueKind.Object } bi)
        {
            var rb = bi.Deserialize<ReglagesBibles>(Json);
            if (rb != null)
            {
                rb.Importees ??= new List<BibleImportee>();
                // seulement les Bibles importées dont le fichier existe encore
                rb.Importees = rb.Importees.Where(b => File.Exists(Path.Combine(DossierBibles, b.Id + ".json"))).ToList();
                bool connue(string id) => id is "lsg" or "kjv" || rb.Importees.Any(b => b.Id == id);
                if (!connue(rb.Principale)) rb.Principale = "lsg";
                if (!string.IsNullOrEmpty(rb.Seconde) && !connue(rb.Seconde)) rb.Seconde = "";
                Reglages.Bibles = rb;
                if (rb.Importees.Count > 0) faits.Add($"{rb.Importees.Count} Bible(s) importée(s)");
            }
        }
        if (Val("verselight-obs") is { ValueKind: JsonValueKind.Object } ob)
        {
            var o = ob.Deserialize<ReglagesObs>(Json);
            if (o != null) { Reglages.Obs = o; if (o.Actif) faits.Add("réglages OBS"); }
        }
        if (Val("verselight-ecran-choisi") is { ValueKind: JsonValueKind.Object } ec)
        {
            var e = ec.Deserialize<EcranChoisi>(Json);
            if (e != null && e.Mode is "plein" or "fenetre" or "obs") Reglages.Ecran = e;
        }
        Corriger();
        SauverReglages();
        return string.Join(", ", faits);
    }
}
