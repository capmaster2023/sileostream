using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>
/// Siloé Stream 1.9.3 : se connecter au logiciel avec son compte Google, par le site siloestream.tech.
/// Le logiciel écoute sur 127.0.0.1 (un port au hasard) et ouvre le navigateur sur siloestream.tech/connexion ;
/// la page « Se connecter avec Google » renvoie à ce port le jeton d'identité donné par Google, avec le code « etat »
/// tiré ici : une autre page ne peut pas se faire passer pour elle. Le relais vérifie ensuite le jeton auprès de Google.
/// </summary>
public sealed class ConnexionGoogle : IDisposable
{
    public const string Site = "https://siloestream.tech/connexion/";
    const int MaxEntete = 16 * 1024, MaxCorps = 64 * 1024;

    readonly TcpListener ecoute = new(IPAddress.Loopback, 0);

    public string Etat { get; } = Telephone.B64(RandomNumberGenerator.GetBytes(18));
    public int Port { get; }
    /// <summary>La page à ouvrir dans le navigateur.</summary>
    public string Adresse => $"{Site}?port={Port}&etat={Etat}";

    public ConnexionGoogle()
    {
        ecoute.Start();
        Port = ((IPEndPoint)ecoute.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        try { ecoute.Stop(); } catch (SocketException) { }
    }

    public sealed record Requete(string Methode, string Chemin, Dictionary<string, string> Champs);

    /// <summary>Une requête HTTP simple : la ligne de requête et un corps « application/x-www-form-urlencoded ».</summary>
    public static Requete? Analyser(string entete, string corps)
    {
        var premiere = entete.Split("\r\n")[0].Split(' ');
        if (premiere.Length < 2) return null;
        var champs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var paire in corps.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int i = paire.IndexOf('=');
            var cle = WebUtility.UrlDecode(i < 0 ? paire : paire[..i]);
            champs[cle] = i < 0 ? "" : WebUtility.UrlDecode(paire[(i + 1)..]);
        }
        return new Requete(premiere[0].ToUpperInvariant(), premiere[1].Split('?')[0], champs);
    }

    /// <summary>Le jeton Google envoyé par la page de connexion (null : annulé ou délai dépassé).</summary>
    public async Task<string?> Attendre(CancellationToken t)
    {
        while (!t.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await ecoute.AcceptTcpClientAsync(t); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return null; }
            string? jeton = null;
            using (client)
            {
                try
                {
                    using var delai = CancellationTokenSource.CreateLinkedTokenSource(t);
                    delai.CancelAfter(TimeSpan.FromSeconds(10));
                    var flux = client.GetStream();
                    var (entete, corps) = await Lire(flux, delai.Token);
                    var r = entete == null ? null : Analyser(entete, corps);
                    if (r is { Methode: "POST", Chemin: "/connexion" }
                        && r.Champs.TryGetValue("etat", out var etat) && Egaux(etat, Etat)
                        && r.Champs.TryGetValue("idToken", out var id) && id.Length > 20)
                    {
                        jeton = id;
                        await Repondre(flux, 200, PageConnecte, delai.Token);
                    }
                    else await Repondre(flux, 404, PageInconnue, delai.Token);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
            }
            if (jeton != null) return jeton;
        }
        return null;
    }

    static bool Egaux(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>L'en-tête (sans la ligne vide) et le corps d'une requête ; en-tête null si elle est illisible ou trop longue.</summary>
    static async Task<(string? Entete, string Corps)> Lire(NetworkStream flux, CancellationToken t)
    {
        var tampon = new byte[MaxEntete + MaxCorps];
        int n = 0, fin = -1;
        while (fin < 0)
        {
            if (n >= MaxEntete) return (null, "");
            int lu = await flux.ReadAsync(tampon.AsMemory(n, MaxEntete - n), t);
            if (lu == 0) return (null, "");
            n += lu;
            fin = FinEntete(tampon, n);
        }
        var entete = Encoding.ASCII.GetString(tampon, 0, fin);
        int longueur = 0;
        foreach (var l in entete.Split("\r\n"))
            if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l[15..].Trim(), out longueur);
        longueur = Math.Clamp(longueur, 0, MaxCorps);
        int debut = fin + 4;
        while (n - debut < longueur)
        {
            int lu = await flux.ReadAsync(tampon.AsMemory(n, tampon.Length - n), t);
            if (lu == 0) break;
            n += lu;
        }
        return (entete, Encoding.UTF8.GetString(tampon, debut, Math.Min(longueur, n - debut)));
    }

    static int FinEntete(byte[] b, int n)
    {
        for (int i = 3; i < n; i++)
            if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') return i - 3;
        return -1;
    }

    static async Task Repondre(NetworkStream flux, int code, string html, CancellationToken t)
    {
        var corps = Encoding.UTF8.GetBytes(html);
        var tete = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code == 200 ? "OK" : "Not Found")}\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {corps.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await flux.WriteAsync(tete, t);
        await flux.WriteAsync(corps, t);
        await flux.FlushAsync(t);
    }

    const string Style = "<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        + "<style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#fbf7f1;color:#1e1a17;"
        + "font:17px/1.5 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:30rem;padding:2rem;text-align:center}"
        + "h1{font-size:1.6rem;margin:0 0 .5rem}p{color:#4a433d;margin:0}</style>";

    const string PageConnecte = "<!doctype html><html lang=\"fr\"><title>Siloé Stream : connecté</title>" + Style
        + "<main><h1>Vous êtes connecté</h1><p>Retournez dans Siloé Stream : il vous attend. Vous pouvez fermer cet onglet.</p></main>"
        + "<script>setTimeout(function(){window.close()},1500)</script></html>";

    const string PageInconnue = "<!doctype html><html lang=\"fr\"><title>Siloé Stream</title>" + Style
        + "<main><h1>Connexion expirée</h1><p>Relancez « Se connecter avec Google » depuis Siloé Stream.</p></main></html>";
}

/// <summary>
/// Siloé Stream 1.9.3 : le compte Google connecté sur ce PC (compte-pc.json). Le rôle vient de l'église à laquelle le PC
/// est relié ; le jeton de session du relais permet de le relire (l'admin a pu le changer). Sans Internet, le rôle gardé sert.
/// </summary>
public sealed class SessionPc
{
    public const string Fichier = "compte-pc.json";

    [JsonPropertyName("jeton")] public string Jeton { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("eglise")] public string Eglise { get; set; } = "";
    [JsonPropertyName("egliseNom")] public string EgliseNom { get; set; } = "";
    [JsonPropertyName("relais")] public string Relais { get; set; } = ReglagesDistance.RelaisDefaut;

    public static SessionPc? Lire(string dossier)
    {
        try
        {
            var f = Path.Combine(dossier, Fichier);
            if (File.Exists(f) && JsonSerializer.Deserialize<SessionPc>(File.ReadAllText(f)) is { Jeton.Length: > 0, Email.Length: > 0 } s) return s;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    public void Ecrire(string dossier)
    {
        Directory.CreateDirectory(dossier);
        var f = Path.Combine(dossier, Fichier);
        File.WriteAllText(f + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(f + ".tmp", f, true);
    }

    public static void Effacer(string dossier)
    {
        try { File.Delete(Path.Combine(dossier, Fichier)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Siloé Stream 1.9.3 : l'API des comptes du relais (comptes Google, églises, membres et rôles), vue du logiciel.
/// Les membres se gèrent ici comme dans l'application Android : c'est le même serveur.
/// </summary>
public static class ComptesApi
{
    public sealed record EgliseDe(string Id, string Nom, string Role, string? PcId);
    public sealed record Moi(string Email, string Nom, List<EgliseDe> Eglises, string Jeton);
    public sealed record Membre(string Email, string Role, string Nom);

    /// <summary>Un refus du serveur, avec son message (« Session expirée… », « Adresse Gmail invalide »…).</summary>
    public sealed class Refus(string message, int code) : Exception(message)
    {
        public int Code { get; } = code;
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static async Task<JsonObject> Appel(HttpMethod methode, string relais, string chemin, string? jeton, object? corps, CancellationToken t)
    {
        using var req = new HttpRequestMessage(methode, CompteEglise.Api(relais, chemin));
        if (jeton != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton);
        if (corps != null) req.Content = JsonContent.Create(corps);
        using var rep = await Http.SendAsync(req, t);
        var texte = await rep.Content.ReadAsStringAsync(t);
        JsonObject? j = null;
        try { j = JsonNode.Parse(texte) as JsonObject; } catch (JsonException) { }
        if (!rep.IsSuccessStatusCode)
            throw new Refus(S(j?["erreur"]) is { Length: > 0 } m ? m : $"Le serveur a refusé ({(int)rep.StatusCode}).", (int)rep.StatusCode);
        return j ?? new JsonObject();
    }

    static Moi LireMoi(JsonObject j, string jeton)
    {
        var eglises = new List<EgliseDe>();
        if (j["eglises"] is JsonArray a)
            foreach (var e in a.OfType<JsonObject>())
                eglises.Add(new EgliseDe(S(e["id"]), S(e["nom"]), S(e["role"]), e["pc"] is JsonObject pc ? S(pc["pcId"]) : null));
        return new Moi(S(j["email"]), S(j["nom"]), eglises, jeton);
    }

    static string Eg(string eglise) => "eglises/" + Uri.EscapeDataString(eglise);

    /// <summary>Le jeton d'identité Google (page siloestream.tech/connexion) contre une session du relais.</summary>
    public static async Task<Moi> Connecter(string relais, string idToken, CancellationToken t = default)
    {
        var j = await Appel(HttpMethod.Post, relais, "connexion", null, new { idToken }, t);
        return LireMoi(j, S(j["jeton"]));
    }

    public static async Task<Moi> MoiActuel(string relais, string jeton, CancellationToken t = default) =>
        LireMoi(await Appel(HttpMethod.Get, relais, "moi", jeton, null, t), jeton);

    public static Task Deconnecter(string relais, string jeton) => Appel(HttpMethod.Post, relais, "deconnexion", jeton, new { }, default);

    public static async Task<List<Membre>> Membres(string relais, string jeton, string eglise, CancellationToken t = default)
    {
        var j = await Appel(HttpMethod.Get, relais, Eg(eglise) + "/membres", jeton, null, t);
        var l = new List<Membre>();
        if (j["membres"] is JsonArray a)
            foreach (var m in a.OfType<JsonObject>()) l.Add(new Membre(S(m["email"]), S(m["role"]), S(m["nom"])));
        return l;
    }

    /// <summary>Ajoute un membre par son Gmail, ou change son rôle (admin de l'église).</summary>
    public static Task AjouterMembre(string relais, string jeton, string eglise, string email, string role) =>
        Appel(HttpMethod.Post, relais, Eg(eglise) + "/membres", jeton, new { email, role }, default);

    public static Task RetirerMembre(string relais, string jeton, string eglise, string email) =>
        Appel(HttpMethod.Delete, relais, Eg(eglise) + "/membres/" + Uri.EscapeDataString(email), jeton, null, default);

    /// <summary>Le code à usage unique qui relie un PC à l'église (admin).</summary>
    public static async Task<string> CodePc(string relais, string jeton, string eglise) =>
        S((await Appel(HttpMethod.Post, relais, Eg(eglise) + "/code-pc", jeton, new { }, default))["code"]);

    /// <summary>Crée une église dont ce compte est l'admin ; renvoie son identifiant.</summary>
    public static async Task<string> CreerEglise(string relais, string jeton, string nom) =>
        S((await Appel(HttpMethod.Post, relais, "eglises", jeton, new { nom }, default))["id"]);
}
