using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyVerse;

// ======================================================================================================================
// Siloé Stream 1.6.0 : le direct sur YouTube, depuis Siloé Stream, comme dans OBS.
// - « Connecter le compte » (conseillé) : Siloé Stream crée le direct (titre, description, confidentialité, catégorie, miniature,
//   latence, DVR, 360°, programmation), le lie à sa clé de stream et le démarre ; « Arrêter le direct » le termine.
// - « Clé de stream » : la clé de YouTube Studio, rien d'autre à faire (le direct se règle dans YouTube Studio).
// - « Autre service » : une adresse RTMP(S) et une clé (Facebook, Vimeo…).
// L'image envoyée est celle de MyStream (la même que Zoom), le son celui de « Où va le son › YouTube ».
// ======================================================================================================================

/// <summary>Les réglages du direct (sans secret : la clé de stream et le jeton du compte sont dans <see cref="Coffre"/>).</summary>
public sealed class ReglagesDirect
{
    public const string Compte = "compte", Cle = "cle", Autre = "autre";
    /// <summary>« compte » (YouTube connecté), « cle » (clé de stream de YouTube Studio) ou « autre » (adresse RTMP et clé).</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = Compte;
    /// <summary>« Autre service » : l'adresse du serveur (rtmp:// ou rtmps://).</summary>
    [JsonPropertyName("serveur")] public string Serveur { get; set; } = "";
    /// <summary>« 720p » (conseillé, 4,5 Mb/s), « 1080p » (6 Mb/s) ou « 2160p » (4K, 30 Mb/s, carte graphique obligatoire).</summary>
    [JsonPropertyName("qualite")] public string Qualite { get; set; } = "720p";
    /// <summary>Le nom de la chaîne connectée (pour l'afficher).</summary>
    [JsonPropertyName("chaine")] public string Chaine { get; set; } = "";
    /// <summary>Le flux réutilisable créé sur la chaîne (sa clé ne change pas d'un direct à l'autre).</summary>
    [JsonPropertyName("flux")] public string Flux { get; set; } = "";
    /// <summary>Le direct en cours ou le dernier démarré (pour l'arrêter après un redémarrage de Siloé Stream).</summary>
    [JsonPropertyName("dernierDirect")] public string DernierDirect { get; set; } = "";
    /// <summary>« Retenir ces réglages » : le prochain direct reprend titre, description, confidentialité…</summary>
    [JsonPropertyName("retenir")] public bool Retenir { get; set; } = true;
    [JsonPropertyName("modele")] public ModeleDirect Modele { get; set; } = new();

    /// <summary>Les directs automatiques (1.6.4) : chaque semaine, les jours choisis à l'heure dite, avec leurs réglages.</summary>
    [JsonPropertyName("programmes")] public List<DirectProgramme> Programmes { get; set; } = new();

    public (int L, int H, int Kbps) Taille => TailleDe(Qualite);
    /// <summary>Le direct en 4K : YouTube ne le traite qu'en latence normale.</summary>
    public bool EnQuatreK => Qualite == "2160p";
    /// <summary>La taille et le débit d'une qualité (« 720p », « 1080p », « 2160p »).</summary>
    public static (int L, int H, int Kbps) TailleDe(string? qualite) => qualite switch { "2160p" => (3840, 2160, 30000), "1080p" => (1920, 1080, 6000), _ => (1280, 720, 4500) };

    public void Normaliser()
    {
        if (Mode is not (Compte or Cle or Autre)) Mode = Compte;
        if (Qualite is not ("720p" or "1080p" or "2160p")) Qualite = "720p";
        Serveur ??= ""; Chaine ??= ""; Flux ??= ""; DernierDirect ??= "";
        Modele ??= new ModeleDirect();
        Modele.Normaliser();
        Programmes = (Programmes ?? new()).Where(p => p != null).ToList();
        foreach (var p in Programmes) p.Normaliser();
        // identifiants en double (copie des réglages) : on les sépare
        var vus = new HashSet<string>();
        foreach (var p in Programmes) if (!vus.Add(p.Id)) { p.Id = DirectProgramme.NouvelId(); vus.Add(p.Id); }
    }
}

/// <summary>Les réglages d'un direct YouTube (« Créer le direct ») : comme la fenêtre d'OBS.</summary>
public sealed class ModeleDirect
{
    [JsonPropertyName("titre")] public string Titre { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>« public », « unlisted » (non répertorié) ou « private ».</summary>
    [JsonPropertyName("confidentialite")] public string Confidentialite { get; set; } = "public";
    /// <summary>Catégorie YouTube (29 : organisations à but non lucratif).</summary>
    [JsonPropertyName("categorie")] public string Categorie { get; set; } = "29";
    /// <summary>« Conçue pour les enfants ? » : YouTube exige une réponse. null : pas encore répondu.</summary>
    [JsonPropertyName("enfants")] public bool? Enfants { get; set; } = false;
    /// <summary>« normal », « low » (faible) ou « ultraLow » (très faible).</summary>
    [JsonPropertyName("latence")] public string Latence { get; set; } = "normal";
    [JsonPropertyName("dvr")] public bool Dvr { get; set; } = true;
    [JsonPropertyName("v360")] public bool Video360 { get; set; }
    /// <summary>Chemin de la miniature (JPEG ou PNG, 2 Mo au plus) ; vide : celle de YouTube.</summary>
    [JsonPropertyName("miniature")] public string Miniature { get; set; } = "";

    public void Normaliser()
    {
        Titre ??= ""; Description ??= ""; Miniature ??= "";
        if (Confidentialite is not ("public" or "unlisted" or "private")) Confidentialite = "public";
        if (string.IsNullOrWhiteSpace(Categorie) || !Categorie.All(char.IsDigit)) Categorie = "29";
        if (Latence is not ("normal" or "low" or "ultraLow")) Latence = "normal";
    }

    /// <summary>Les catégories de YouTube, en français (identifiant, nom).</summary>
    public static readonly (string Id, string Nom)[] Categories =
    {
        ("29", "Organisations à but non lucratif"), ("22", "Personnes et blogs"), ("27", "Éducation"), ("10", "Musique"),
        ("24", "Divertissement"), ("25", "Actualités et politique"), ("1", "Films et animations"), ("19", "Voyages et événements"),
        ("26", "Vie pratique et style"), ("28", "Science et technologie"), ("17", "Sport"), ("23", "Humour"), ("20", "Jeux vidéo"),
        ("2", "Auto et véhicules"), ("15", "Animaux"),
    };
}

/// <summary>
/// Les secrets du direct (clé de stream, jeton du compte YouTube, identifiant Google), chiffrés pour cet utilisateur de Windows (DPAPI) :
/// un autre compte de l'ordinateur ou une copie du fichier ailleurs ne peut pas les lire.
/// </summary>
public sealed class Coffre
{
    readonly string fichier;
    Dictionary<string, string> valeurs = new();
    readonly object verrou = new();
    public Coffre(string dossier)
    {
        fichier = Path.Combine(dossier, "direct.secret");
        try
        {
            if (File.Exists(fichier))
            {
                var brut = Dechiffrer(File.ReadAllBytes(fichier));
                valeurs = JsonSerializer.Deserialize<Dictionary<string, string>>(brut) ?? new();
            }
        }
        catch (Exception e) { Journal.Erreur("coffre du direct (illisible : à refaire)", e); valeurs = new(); }
    }

    public string Lire(string cle) { lock (verrou) return valeurs.TryGetValue(cle, out var v) ? v : ""; }
    public void Ecrire(string cle, string? valeur)
    {
        lock (verrou)
        {
            if (string.IsNullOrEmpty(valeur)) valeurs.Remove(cle); else valeurs[cle] = valeur;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fichier)!);
                var tmp = fichier + ".tmp";
                File.WriteAllBytes(tmp, Chiffrer(JsonSerializer.SerializeToUtf8Bytes(valeurs)));
                File.Move(tmp, fichier, true);
            }
            catch (Exception e) { Journal.Erreur("coffre du direct", e); }
        }
    }

    // ------------------------------------------------------------ DPAPI (Windows) ; ailleurs (essais sous Linux), en clair
    static readonly byte[] Sel = Encoding.UTF8.GetBytes("MyChurch.Direct.v1");
    static byte[] Chiffrer(byte[] donnees) => OperatingSystem.IsWindows() ? Dpapi(donnees, true) : donnees;
    static byte[] Dechiffrer(byte[] donnees) => OperatingSystem.IsWindows() ? Dpapi(donnees, false) : donnees;

    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Taille; public IntPtr Donnees; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob entree, string? description, ref Blob sel, IntPtr reserve, IntPtr invite, int options, out Blob sortie);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob entree, IntPtr description, ref Blob sel, IntPtr reserve, IntPtr invite, int options, out Blob sortie);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    static byte[] Dpapi(byte[] donnees, bool chiffrer)
    {
        var hd = GCHandle.Alloc(donnees, GCHandleType.Pinned);
        var hs = GCHandle.Alloc(Sel, GCHandleType.Pinned);
        try
        {
            var e = new Blob { Taille = donnees.Length, Donnees = hd.AddrOfPinnedObject() };
            var s = new Blob { Taille = Sel.Length, Donnees = hs.AddrOfPinnedObject() };
            const int SansInterface = 1;
            Blob sortie;
            bool ok = chiffrer
                ? CryptProtectData(ref e, "Siloé Stream", ref s, IntPtr.Zero, IntPtr.Zero, SansInterface, out sortie)
                : CryptUnprotectData(ref e, IntPtr.Zero, ref s, IntPtr.Zero, IntPtr.Zero, SansInterface, out sortie);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var r = new byte[sortie.Taille];
                Marshal.Copy(sortie.Donnees, r, 0, sortie.Taille);
                return r;
            }
            finally { LocalFree(sortie.Donnees); }
        }
        finally { hd.Free(); hs.Free(); }
    }
}

/// <summary>Une erreur de YouTube, dite simplement (et la raison technique de Google, pour les tests et le journal).</summary>
public sealed class ErreurYouTube : Exception
{
    public string Raison { get; }
    public int Code { get; }
    public ErreurYouTube(string message, string raison = "", int code = 0) : base(message) { Raison = raison; Code = code; }
}

public sealed record ChaineYouTube(string Id, string Titre);
/// <summary>Un direct de la chaîne : à venir (« Direct déjà prévu ») ou en cours.</summary>
public sealed record DirectPrevu(string Id, string Titre, DateTimeOffset? Debut, string Confidentialite, string Statut, string FluxLie, bool DemarrageAuto);
/// <summary>Où envoyer l'image : l'adresse RTMPS de YouTube et la clé du flux.</summary>
public sealed record FluxYouTube(string Id, string Adresse, string Cle);

/// <summary>
/// Le compte YouTube de l'église (OAuth 2.0 de Google pour un programme installé : le navigateur s'ouvre, l'église choisit son compte,
/// Google renvoie un code à Siloé Stream par une adresse locale 127.0.0.1). Siloé Stream garde le jeton de rafraîchissement dans le
/// <see cref="Coffre"/> ; le jeton d'accès (1 h) est refait tout seul.
/// </summary>
public sealed class YouTubeCompte
{
    public const string Portee = "https://www.googleapis.com/auth/youtube";
    // adresses de Google (changées par les essais pour un faux serveur)
    public string AdresseAutorisation { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string AdresseJeton { get; set; } = "https://oauth2.googleapis.com/token";
    public string AdresseRevocation { get; set; } = "https://oauth2.googleapis.com/revoke";
    public string AdresseApi { get; set; } = "https://www.googleapis.com/youtube/v3";
    public string AdresseEnvoi { get; set; } = "https://www.googleapis.com/upload/youtube/v3";

    readonly Coffre coffre;
    readonly HttpClient http;
    readonly Func<(string Id, string Secret)> identifiants;
    string jeton = "";
    DateTimeOffset expiration;
    readonly SemaphoreSlim verrouJeton = new(1, 1);

    /// <param name="identifiants">L'identifiant client de Google (programme installé) et son secret : ceux de Siloé Stream, ou ceux donnés dans les réglages.</param>
    public YouTubeCompte(Coffre coffre, Func<(string Id, string Secret)> identifiants, HttpClient? http = null)
    {
        this.coffre = coffre; this.identifiants = identifiants;
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public const string CleJeton = "youtube.refresh";
    /// <summary>Un compte est connecté (jeton de rafraîchissement gardé).</summary>
    public bool Connecte => coffre.Lire(CleJeton).Length > 0;
    /// <summary>Siloé Stream a un identifiant Google pour « Connecter le compte ».</summary>
    public bool Disponible => identifiants().Id.Length > 0;

    // ------------------------------------------------------------ connexion
    /// <summary>
    /// Connecte le compte : ouvre <paramref name="ouvrir"/> (le navigateur) sur la page de Google, attend la réponse sur 127.0.0.1
    /// (5 minutes au plus), échange le code contre les jetons, puis lit le nom de la chaîne.
    /// </summary>
    public async Task<ChaineYouTube> ConnecterAsync(Action<string> ouvrir, CancellationToken annuler = default)
    {
        var (id, secret) = identifiants();
        if (id.Length == 0) throw new ErreurYouTube("Siloé Stream n'a pas d'identifiant Google pour se connecter à YouTube : utilisez la clé de stream (voir les notes de la 1.6.0).");
        var ecoute = new TcpListener(IPAddress.Loopback, 0);
        ecoute.Start();
        try
        {
            int port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
            string retour = $"http://127.0.0.1:{port}/";
            string verificateur = Aleatoire(48), etat = Aleatoire(24);
            string defi = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificateur)));
            string url = AdresseAutorisation + "?" + Requete(new()
            {
                ["client_id"] = id, ["redirect_uri"] = retour, ["response_type"] = "code", ["scope"] = Portee,
                ["code_challenge"] = defi, ["code_challenge_method"] = "S256", ["state"] = etat,
                ["access_type"] = "offline", ["prompt"] = "consent select_account",
            });
            ouvrir(url);
            using var delai = CancellationTokenSource.CreateLinkedTokenSource(annuler);
            delai.CancelAfter(TimeSpan.FromMinutes(5));
            string code;
            try { code = await AttendreCodeAsync(ecoute, etat, delai.Token); }
            catch (OperationCanceledException) when (!annuler.IsCancellationRequested) { throw new ErreurYouTube("Pas de réponse de Google après 5 minutes : recommencez « Connecter le compte »."); }
            var r = await PosterAsync(AdresseJeton, new()
            {
                ["code"] = code, ["client_id"] = id, ["client_secret"] = secret, ["redirect_uri"] = retour,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verificateur,
            }, annuler);
            var rafraichir = r["refresh_token"]?.GetValue<string>() ?? "";
            if (rafraichir.Length == 0) throw new ErreurYouTube("Google n'a pas donné d'accès durable : recommencez « Connecter le compte ».");
            coffre.Ecrire(CleJeton, rafraichir);
            Retenir(r);
            return await ChaineAsync(annuler);
        }
        finally { ecoute.Stop(); }
    }

    /// <summary>
    /// Attend la réponse de Google : « GET /?code=…&amp;state=… » sur l'adresse locale. Les autres demandes du navigateur
    /// (icône, connexions ouvertes d'avance) sont ignorées.
    /// </summary>
    public static async Task<string> AttendreCodeAsync(TcpListener ecoute, string etat, CancellationToken annuler)
    {
        while (true)
        {
            using var client = await ecoute.AcceptTcpClientAsync(annuler);
            using var flux = client.GetStream();
            using var lecteur = new StreamReader(flux, Encoding.ASCII, false, 4096, true);
            string? ligne;
            using (var court = CancellationTokenSource.CreateLinkedTokenSource(annuler))
            {
                court.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    ligne = await lecteur.ReadLineAsync(court.Token);
                    string? entete;
                    while (!string.IsNullOrEmpty(entete = await lecteur.ReadLineAsync(court.Token))) { }
                }
                catch (OperationCanceledException) when (!annuler.IsCancellationRequested) { continue; }
                catch (IOException) { continue; }
            }
            if (ligne == null) continue;
            var parties = ligne.Split(' ');
            if (parties.Length < 2 || parties[0] != "GET") { await RepondreAsync(flux, 400, "Demande inattendue."); continue; }
            var chemin = parties[1];
            int q = chemin.IndexOf('?');
            if (q < 0) { await RepondreAsync(flux, 404, ""); continue; }
            var p = LireRequete(chemin[(q + 1)..]);
            if (p.TryGetValue("error", out var erreur))
            {
                await RepondreAsync(flux, 200, "La connexion à YouTube a été annulée. Vous pouvez fermer cet onglet et revenir à Siloé Stream.");
                throw new ErreurYouTube(erreur == "access_denied" ? "Connexion refusée dans la page de Google." : "Google a refusé la connexion : " + erreur, erreur);
            }
            if (!p.TryGetValue("code", out var code) || !p.TryGetValue("state", out var s) || s != etat) { await RepondreAsync(flux, 400, "Réponse inattendue."); continue; }
            await RepondreAsync(flux, 200, "Siloé Stream est connecté à YouTube. Vous pouvez fermer cet onglet et revenir à Siloé Stream.");
            return code;
        }
    }

    static async Task RepondreAsync(Stream flux, int statut, string message)
    {
        string page = message.Length == 0 ? "" :
            "<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Siloé Stream</title></head>" +
            "<body style=\"font-family:Segoe UI,sans-serif;background:#FAF7F7;color:#1E1818;display:grid;place-items:center;height:100vh;margin:0\">" +
            "<div style=\"text-align:center;max-width:520px\"><div style=\"width:56px;height:56px;border-radius:16px;background:#930000;margin:0 auto 18px\"></div>" +
            $"<h1 style=\"font-size:22px\">{message.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")}</h1></div></body></html>";
        var corps = Encoding.UTF8.GetBytes(page);
        var texte = statut switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var tete = Encoding.ASCII.GetBytes($"HTTP/1.1 {statut} {texte}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corps.Length}\r\nConnection: close\r\n\r\n");
        try { await flux.WriteAsync(tete); await flux.WriteAsync(corps); await flux.FlushAsync(); } catch (IOException) { }
    }

    /// <summary>Oublie le compte (et demande à Google de retirer l'accès).</summary>
    public async Task DeconnecterAsync()
    {
        var r = coffre.Lire(CleJeton);
        coffre.Ecrire(CleJeton, null);
        jeton = "";
        if (r.Length == 0) return;
        try { using var _ = await http.PostAsync(AdresseRevocation + "?token=" + Uri.EscapeDataString(r), null); } catch (Exception) { }
    }

    /// <summary>Un jeton d'accès valable (refait avec le jeton de rafraîchissement quand il expire).</summary>
    public async Task<string> JetonAsync(CancellationToken annuler = default)
    {
        await verrouJeton.WaitAsync(annuler);
        try
        {
            if (jeton.Length > 0 && DateTimeOffset.UtcNow < expiration - TimeSpan.FromMinutes(2)) return jeton;
            var rafraichir = coffre.Lire(CleJeton);
            if (rafraichir.Length == 0) throw new ErreurYouTube("Le compte YouTube n'est pas connecté.", "nonConnecte");
            var (id, secret) = identifiants();
            JsonNode r;
            try
            {
                r = await PosterAsync(AdresseJeton, new()
                {
                    ["client_id"] = id, ["client_secret"] = secret, ["refresh_token"] = rafraichir, ["grant_type"] = "refresh_token",
                }, annuler);
            }
            catch (ErreurYouTube e) when (e.Raison == "invalid_grant")
            {
                coffre.Ecrire(CleJeton, null);
                throw new ErreurYouTube("L'accès à YouTube a expiré ou a été retiré : reconnectez le compte.", "invalid_grant");
            }
            Retenir(r);
            return jeton;
        }
        finally { verrouJeton.Release(); }
    }

    void Retenir(JsonNode r)
    {
        jeton = r["access_token"]?.GetValue<string>() ?? throw new ErreurYouTube("Réponse de Google sans jeton d'accès.");
        int duree = r["expires_in"]?.GetValue<int>() ?? 3600;
        expiration = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(duree);
    }

    async Task<JsonNode> PosterAsync(string adresse, Dictionary<string, string> champs, CancellationToken annuler)
    {
        using var r = await http.PostAsync(adresse, new FormUrlEncodedContent(champs), annuler);
        var texte = await r.Content.ReadAsStringAsync(annuler);
        JsonNode? n = null;
        try { n = JsonNode.Parse(texte); } catch (JsonException) { }
        if (!r.IsSuccessStatusCode)
        {
            var raison = n?["error"] is JsonValue v ? v.GetValue<string>() : "";
            var detail = n?["error_description"]?.GetValue<string>() ?? texte;
            throw new ErreurYouTube(raison == "invalid_client" ? "L'identifiant Google de Siloé Stream est refusé (identifiant client ou secret)." : "Google : " + Court(detail), raison, (int)r.StatusCode);
        }
        return n ?? throw new ErreurYouTube("Réponse de Google illisible.");
    }

    // ------------------------------------------------------------ API YouTube
    async Task<JsonNode> AppelerAsync(HttpMethod methode, string chemin, JsonNode? corps = null, CancellationToken annuler = default, HttpContent? brut = null, bool envoi = false)
    {
        for (int essai = 0; ; essai++)
        {
            var t = await JetonAsync(annuler);
            using var m = new HttpRequestMessage(methode, (envoi ? AdresseEnvoi : AdresseApi) + chemin);
            m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
            if (brut != null) m.Content = brut;
            else if (corps != null) m.Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json");
            using var r = await http.SendAsync(m, annuler);
            var texte = await r.Content.ReadAsStringAsync(annuler);
            // un jeton refusé (horloge du PC décalée, accès retiré entre-temps) : on le refait une fois
            if (r.StatusCode == HttpStatusCode.Unauthorized && essai == 0) { jeton = ""; continue; }
            JsonNode? n = null;
            try { n = texte.Length > 0 ? JsonNode.Parse(texte) : new JsonObject(); } catch (JsonException) { }
            if (!r.IsSuccessStatusCode) throw Erreur(n, texte, (int)r.StatusCode);
            return n ?? new JsonObject();
        }
    }

    /// <summary>Une erreur de l'API, en mots simples.</summary>
    public static ErreurYouTube Erreur(JsonNode? n, string texte, int code)
    {
        var e = n?["error"];
        string raison = e?["errors"]?[0]?["reason"]?.GetValue<string>() ?? "";
        string message = e?["message"]?.GetValue<string>() ?? texte;
        string dit = raison switch
        {
            "liveStreamingNotEnabled" => "Le direct n'est pas activé sur cette chaîne : YouTube Studio › Créer › Passer au direct (YouTube demande parfois 24 h).",
            "insufficientPermissions" or "forbidden" when code == 403 => "Ce compte n'a pas le droit de diffuser sur cette chaîne YouTube.",
            "quotaExceeded" or "rateLimitExceeded" or "userRateLimitExceeded" => "YouTube refuse pour aujourd'hui (quota atteint) : démarrez avec la clé de stream de YouTube Studio.",
            "invalidTitle" or "titleRequired" => "Titre refusé par YouTube (100 caractères au plus, sans < ni >).",
            "invalidDescription" => "Description refusée par YouTube (5000 caractères au plus, sans < ni >).",
            "invalidScheduledStartTime" => "Heure de début refusée par YouTube : elle doit être dans le futur.",
            "invalidLatencyPreferenceOptions" => "Cette latence ne va pas avec ces options (la très faible latence n'a ni DVR ni 360°).",
            "liveBroadcastNotFound" or "liveStreamNotFound" or "videoNotFound" => "Ce direct n'existe plus sur YouTube.",
            "mediaBodyRequired" or "invalidImage" or "mediaTypeNotSupported" => "Miniature refusée : une image JPEG ou PNG de 2 Mo au plus.",
            _ => "YouTube : " + Court(message),
        };
        return new ErreurYouTube(dit, raison, code);
    }

    static string Court(string s) { s = s.Replace('\n', ' ').Trim(); return s.Length > 300 ? s[..300] + "…" : s; }

    /// <summary>La chaîne du compte connecté.</summary>
    public async Task<ChaineYouTube> ChaineAsync(CancellationToken annuler = default)
    {
        var r = await AppelerAsync(HttpMethod.Get, "/channels?part=snippet&mine=true", annuler: annuler);
        var c = r["items"]?[0] ?? throw new ErreurYouTube("Ce compte Google n'a pas de chaîne YouTube : choisissez le compte de la chaîne de l'église.", "pasDeChaine");
        return new ChaineYouTube(c["id"]?.GetValue<string>() ?? "", c["snippet"]?["title"]?.GetValue<string>() ?? "");
    }

    /// <summary>Crée le direct (« Créer le direct ») ; <paramref name="debut"/> : maintenant, ou plus tard (« Programmer »). Rend son identifiant.</summary>
    public async Task<string> CreerDirectAsync(ModeleDirect m, DateTimeOffset debut, CancellationToken annuler = default)
    {
        bool tresFaible = m.Latence == "ultraLow";
        var corps = new JsonObject
        {
            ["snippet"] = new JsonObject
            {
                ["title"] = m.Titre.Trim(), ["description"] = m.Description,
                ["scheduledStartTime"] = debut.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            },
            ["status"] = new JsonObject { ["privacyStatus"] = m.Confidentialite, ["selfDeclaredMadeForKids"] = m.Enfants == true },
            ["contentDetails"] = new JsonObject
            {
                // le direct commence quand l'image arrive ; Siloé Stream le termine (« Arrêter le direct ») : une coupure d'Internet ne l'arrête pas
                ["enableAutoStart"] = true, ["enableAutoStop"] = false,
                ["enableDvr"] = m.Dvr && !tresFaible, ["latencyPreference"] = m.Latence,
                ["projection"] = m.Video360 && !tresFaible ? "360" : "rectangular",
                ["monitorStream"] = new JsonObject { ["enableMonitorStream"] = false },
            },
        };
        var r = await AppelerAsync(HttpMethod.Post, "/liveBroadcasts?part=snippet,status,contentDetails", corps, annuler);
        return r["id"]?.GetValue<string>() ?? throw new ErreurYouTube("YouTube n'a pas créé le direct.");
    }

    /// <summary>La catégorie (et le titre, la description) de la vidéo du direct.</summary>
    public async Task CategorieAsync(string video, ModeleDirect m, CancellationToken annuler = default)
    {
        var corps = new JsonObject
        {
            ["id"] = video,
            ["snippet"] = new JsonObject { ["title"] = m.Titre.Trim(), ["description"] = m.Description, ["categoryId"] = m.Categorie },
        };
        await AppelerAsync(HttpMethod.Put, "/videos?part=snippet", corps, annuler);
    }

    /// <summary>La miniature du direct (JPEG ou PNG, 2 Mo au plus).</summary>
    public async Task MiniatureAsync(string video, string chemin, CancellationToken annuler = default)
    {
        var octets = await File.ReadAllBytesAsync(chemin, annuler);
        if (octets.Length > 2 * 1024 * 1024) throw new ErreurYouTube("Miniature trop grande : 2 Mo au plus.", "trop grande");
        var type = Path.GetExtension(chemin).ToLowerInvariant() is ".png" ? "image/png" : "image/jpeg";
        var contenu = new ByteArrayContent(octets);
        contenu.Headers.ContentType = new MediaTypeHeaderValue(type);
        try { await AppelerAsync(HttpMethod.Post, "/thumbnails/set?videoId=" + Uri.EscapeDataString(video) + "&uploadType=media", null, annuler, contenu, true); }
        catch (ErreurYouTube e) when (e.Code == 403 && e.Raison is "forbidden" or "insufficientPermissions")
        {
            throw new ErreurYouTube("Miniature refusée : la chaîne doit être vérifiée pour les miniatures (YouTube Studio › Paramètres › Chaîne › Fonctionnalités).", e.Raison, e.Code);
        }
    }

    /// <summary>Le flux réutilisable de la chaîne (le même à chaque direct) : celui gardé s'il existe encore, sinon un nouveau.</summary>
    public async Task<FluxYouTube> FluxAsync(string? garde, CancellationToken annuler = default)
    {
        if (!string.IsNullOrEmpty(garde))
        {
            var r = await AppelerAsync(HttpMethod.Get, "/liveStreams?part=id,cdn,status&id=" + Uri.EscapeDataString(garde), annuler: annuler);
            if (r["items"]?[0] is { } f && LireFlux(f) is { } lu) return lu;
        }
        var corps = new JsonObject
        {
            ["snippet"] = new JsonObject { ["title"] = "Siloé Stream" },
            ["cdn"] = new JsonObject { ["ingestionType"] = "rtmp", ["resolution"] = "variable", ["frameRate"] = "variable" },
            ["contentDetails"] = new JsonObject { ["isReusable"] = true },
        };
        var n = await AppelerAsync(HttpMethod.Post, "/liveStreams?part=snippet,cdn,contentDetails", corps, annuler);
        return LireFlux(n) ?? throw new ErreurYouTube("YouTube n'a pas donné de clé de stream.");
    }

    static FluxYouTube? LireFlux(JsonNode f)
    {
        var info = f["cdn"]?["ingestionInfo"];
        string id = f["id"]?.GetValue<string>() ?? "", cle = info?["streamName"]?.GetValue<string>() ?? "";
        string adresse = info?["rtmpsIngestionAddress"]?.GetValue<string>() ?? info?["ingestionAddress"]?.GetValue<string>() ?? "";
        return id.Length > 0 && cle.Length > 0 && adresse.Length > 0 ? new FluxYouTube(id, adresse, cle) : null;
    }

    /// <summary>Lie le direct au flux (sa clé de stream).</summary>
    public Task LierAsync(string direct, string flux, CancellationToken annuler = default) =>
        AppelerAsync(HttpMethod.Post, "/liveBroadcasts/bind?part=id,contentDetails&id=" + Uri.EscapeDataString(direct) + "&streamId=" + Uri.EscapeDataString(flux), new JsonObject(), annuler);

    /// <summary>Les directs à venir et en cours de la chaîne (« Direct déjà prévu »), les plus proches d'abord.</summary>
    public async Task<List<DirectPrevu>> DirectsAsync(CancellationToken annuler = default)
    {
        var liste = new List<DirectPrevu>();
        foreach (var statut in new[] { "active", "upcoming" })
        {
            var r = await AppelerAsync(HttpMethod.Get, "/liveBroadcasts?part=id,snippet,status,contentDetails&broadcastType=all&maxResults=50&broadcastStatus=" + statut, annuler: annuler);
            foreach (var i in r["items"]?.AsArray() ?? new JsonArray())
                if (i != null && LireDirect(i) is { } d && liste.All(x => x.Id != d.Id)) liste.Add(d);
        }
        return liste.OrderBy(d => d.Statut == "live" ? 0 : 1).ThenBy(d => d.Debut ?? DateTimeOffset.MaxValue).ToList();
    }

    static DirectPrevu? LireDirect(JsonNode i)
    {
        string id = i["id"]?.GetValue<string>() ?? "";
        if (id.Length == 0) return null;
        DateTimeOffset? debut = DateTimeOffset.TryParse(i["snippet"]?["scheduledStartTime"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        return new DirectPrevu(id, i["snippet"]?["title"]?.GetValue<string>() ?? "", debut,
            i["status"]?["privacyStatus"]?.GetValue<string>() ?? "", i["status"]?["lifeCycleStatus"]?.GetValue<string>() ?? "",
            i["contentDetails"]?["boundStreamId"]?.GetValue<string>() ?? "", i["contentDetails"]?["enableAutoStart"]?.GetValue<bool>() ?? false);
    }

    /// <summary>Un direct (son état : created, ready, testing, live, complete…).</summary>
    public async Task<DirectPrevu?> DirectAsync(string id, CancellationToken annuler = default)
    {
        var r = await AppelerAsync(HttpMethod.Get, "/liveBroadcasts?part=id,snippet,status,contentDetails&id=" + Uri.EscapeDataString(id), annuler: annuler);
        return r["items"]?[0] is { } i ? LireDirect(i) : null;
    }

    /// <summary>L'état du flux : « active » quand YouTube reçoit l'image, et sa santé (good, ok, bad, noData).</summary>
    public async Task<(string Statut, string Sante)> EtatFluxAsync(string flux, CancellationToken annuler = default)
    {
        var r = await AppelerAsync(HttpMethod.Get, "/liveStreams?part=id,status&id=" + Uri.EscapeDataString(flux), annuler: annuler);
        var s = r["items"]?[0]?["status"];
        return (s?["streamStatus"]?.GetValue<string>() ?? "", s?["healthStatus"]?["status"]?.GetValue<string>() ?? "");
    }

    /// <summary>Passe le direct en « live » ou « complete » (un changement déjà fait n'est pas une erreur).</summary>
    public async Task TransitionAsync(string direct, string statut, CancellationToken annuler = default)
    {
        try { await AppelerAsync(HttpMethod.Post, "/liveBroadcasts/transition?part=status&broadcastStatus=" + statut + "&id=" + Uri.EscapeDataString(direct), new JsonObject(), annuler); }
        catch (ErreurYouTube e) when (e.Raison is "redundantTransition") { }
    }

    // ------------------------------------------------------------ 2.2.0 : les vidéos publiées depuis le montage
    /// <summary>Oublie le jeton d'accès (Google l'a refusé) : le prochain appel en refait un avec le jeton de rafraîchissement.</summary>
    public void OublierJeton() => jeton = "";

    /// <summary>Les playlists de la chaîne (identifiant, titre).</summary>
    public async Task<List<(string Id, string Titre)>> PlaylistsAsync(CancellationToken annuler = default)
    {
        var l = new List<(string Id, string Titre)>();
        string page = "";
        for (int i = 0; i < 20; i++)
        {
            var r = await AppelerAsync(HttpMethod.Get, "/playlists?part=snippet&mine=true&maxResults=50" + (page.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(page) : ""), annuler: annuler);
            foreach (var it in r["items"]?.AsArray() ?? new JsonArray())
                if (it?["id"]?.GetValue<string>() is { Length: > 0 } id) l.Add((id, it["snippet"]?["title"]?.GetValue<string>() ?? id));
            page = r["nextPageToken"]?.GetValue<string>() ?? "";
            if (page.Length == 0) break;
        }
        return l;
    }

    /// <summary>Ajoute la vidéo à la fin d'une playlist de la chaîne.</summary>
    public Task AjouterAPlaylistAsync(string playlist, string video, CancellationToken annuler = default) =>
        AppelerAsync(HttpMethod.Post, "/playlistItems?part=snippet", new JsonObject
        {
            ["snippet"] = new JsonObject { ["playlistId"] = playlist, ["resourceId"] = new JsonObject { ["kind"] = "youtube#video", ["videoId"] = video } },
        }, annuler);

    // ------------------------------------------------------------ outils
    public static string Aleatoire(int octets) => Base64Url(RandomNumberGenerator.GetBytes(octets));
    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string Requete(Dictionary<string, string> p) => string.Join("&", p.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
    public static Dictionary<string, string> LireRequete(string q)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int i = part.IndexOf('=');
            string k = Uri.UnescapeDataString((i < 0 ? part : part[..i]).Replace('+', ' '));
            string v = i < 0 ? "" : Uri.UnescapeDataString(part[(i + 1)..].Replace('+', ' '));
            d[k] = v;
        }
        return d;
    }
}

/// <summary>Où part l'image du direct, selon le réglage (et la clé gardée) ; la clé n'apparaît jamais dans un message.</summary>
public static class AdresseDirect
{
    public const string YouTubeRtmps = "rtmps://a.rtmps.youtube.com:443/live2";
    public const string CleStream = "direct.cle", CleAutre = "direct.autre";

    /// <summary>L'adresse complète (serveur/clé) pour la clé de stream de YouTube ou un autre service ; null s'il manque quelque chose.</summary>
    public static string? Construire(ReglagesDirect r, Coffre coffre)
    {
        if (r.Mode == ReglagesDirect.Cle)
        {
            var cle = coffre.Lire(CleStream).Trim();
            return cle.Length > 0 ? YouTubeRtmps + "/" + cle : null;
        }
        if (r.Mode == ReglagesDirect.Autre)
        {
            var serveur = r.Serveur.Trim();
            if (!(serveur.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) || serveur.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))) return null;
            var cle = coffre.Lire(CleAutre).Trim();
            return cle.Length > 0 ? serveur.TrimEnd('/') + "/" + cle : serveur;
        }
        return null;
    }

    public static string Pour(FluxYouTube f) => f.Adresse.TrimEnd('/') + "/" + f.Cle;

    /// <summary>Cache la clé (tout ce qui suit le dernier « / ») dans un texte destiné au journal ou à l'écran.</summary>
    public static string Masquer(string texte, string? adresse)
    {
        if (string.IsNullOrEmpty(adresse)) return texte;
        int i = adresse.LastIndexOf('/');
        var cle = i >= 0 ? adresse[(i + 1)..] : "";
        return cle.Length >= 4 ? texte.Replace(cle, "••••") : texte;
    }
}
