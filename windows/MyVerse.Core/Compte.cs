using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>
/// Siloé Stream 1.9.1 : le PC relié à une église (comptes Google). Le serveur (relais du VPS) garde les comptes, les églises
/// et les rôles ; il remet aux membres un billet signé avec le secret de ce PC. Le PC vérifie le billet sans appeler le serveur :
/// la clé du téléphone se déduit du secret, et le rôle vient du billet (l'admin de l'église le change depuis son téléphone).
/// </summary>
public sealed class CompteEglise
{
    public const string Fichier = "eglise.json";

    [JsonPropertyName("eglise")] public string Eglise { get; set; } = "";
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("secret")] public string Secret { get; set; } = "";
    [JsonPropertyName("relais")] public string Relais { get; set; } = ReglagesDistance.RelaisDefaut;
    /// <summary>1.9.3 : chacun se connecte avec Google pour utiliser le logiciel sur ce PC (l'admin l'active dans Réglages).</summary>
    [JsonPropertyName("obligatoire")] public bool ConnexionObligatoire { get; set; }

    public static CompteEglise? Lire(string dossier)
    {
        try
        {
            var f = Path.Combine(dossier, Fichier);
            if (File.Exists(f) && JsonSerializer.Deserialize<CompteEglise>(File.ReadAllText(f)) is { Secret.Length: > 0 } c) return c;
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

    /// <summary>La clé du téléphone <paramref name="deviceId"/> (le serveur la calcule de la même façon).</summary>
    public static byte[] CleTelephone(byte[] secret, string pcId, string deviceId) =>
        HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"siloe-compte|{pcId}|{deviceId}"));

    public sealed record Billet(string Email, string Role, string Nom);

    /// <summary>« charge.signature » : charge = JSON base64url {pc, dev, email, role, nom, exp} ; signature = HMAC(secret, "siloe-billet|" + charge).</summary>
    public static Billet? Verifier(string billet, byte[] secret, string pcId, string deviceId, DateTimeOffset maintenant)
    {
        var i = billet.IndexOf('.');
        if (i <= 0) return null;
        var charge = billet[..i];
        var attendu = Encoding.ASCII.GetBytes(Telephone.HmacB64(secret, "siloe-billet|" + charge));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(billet[(i + 1)..]), attendu)) return null;
        try
        {
            var j = JsonNode.Parse(Telephone.B64Dec(charge)) as JsonObject;
            if (j == null) return null;
            string S(string k) => j[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
            long exp = j["exp"] is JsonValue e && e.TryGetValue<long>(out var x) ? x : 0;
            if (S("pc") != pcId || S("dev") != deviceId || exp < maintenant.ToUnixTimeSeconds()) return null;
            return new Billet(S("email"), Roles.Nettoyer(S("role")), S("nom"));
        }
        catch (Exception e) when (e is JsonException or FormatException) { return null; }
    }

    /// <summary>Adresse HTTPS de l'API du relais (« nom » → https://nom/api/…).</summary>
    public static Uri Api(string relais, string chemin)
    {
        var r = ReglagesDistance.Nettoyer(relais);
        if (r.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) return new Uri("http://" + r[5..] + "/api/" + chemin);
        if (r.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) return new Uri("https://" + r[6..] + "/api/" + chemin);
        return new Uri("https://" + r + "/api/" + chemin);
    }

    /// <summary>
    /// Relie ce PC à l'église avec le code à usage unique que l'admin a reçu du serveur. Le secret, tiré ici, part au serveur
    /// (HTTPS) puis reste dans eglise.json. Renvoie le nom de l'église, ou lève une exception avec le message à afficher.
    /// </summary>
    public static async Task<CompteEglise> Lier(string dossier, string relais, string code, string pcId, string nomPc, CancellationToken t = default)
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var rep = await http.PostAsJsonAsync(Api(relais, "pc/lier"), new { code, pcId, nom = nomPc, secret = Telephone.B64(secret) }, t);
        var corps = await rep.Content.ReadAsStringAsync(t);
        JsonObject? j = null;
        try { j = JsonNode.Parse(corps) as JsonObject; } catch (JsonException) { }
        if (!rep.IsSuccessStatusCode)
            throw new InvalidOperationException(j?["erreur"]?.GetValue<string>() ?? $"Le serveur a refusé ({(int)rep.StatusCode}).");
        var c = new CompteEglise
        {
            Eglise = j?["eglise"]?.GetValue<string>() ?? "",
            Nom = j?["nom"]?.GetValue<string>() ?? "",
            Secret = Telephone.B64(secret),
            Relais = ReglagesDistance.Nettoyer(relais),
        };
        c.Ecrire(dossier);
        // les membres arrivent par Internet : le contrôle à distance s'active
        var r = ReglagesDistance.Lire(dossier);
        if (!r.Actif || ReglagesDistance.Nettoyer(r.Relais) != c.Relais)
        {
            r.Actif = true;
            r.Relais = c.Relais;
            r.Ecrire(dossier);
        }
        return c;
    }
}
