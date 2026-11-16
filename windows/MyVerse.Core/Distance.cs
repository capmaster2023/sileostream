using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>
/// Siloé Stream 1.7.0 : contrôle à distance par Internet (%APPDATA%\SiloeStream\distance.json, à côté de devices.json).
/// Réglé une fois dans Réglages › À distance ; l'agent le relit tout seul.
/// </summary>
public sealed class ReglagesDistance
{
    /// <summary>Le relais Siloé Stream du VPS de l'église (installé avec relais/installer.sh).</summary>
    public const string RelaisDefaut = "2-25-216-99.sslip.io";
    public const string Fichier = "distance.json";

    [JsonPropertyName("actif")] public bool Actif { get; set; }
    [JsonPropertyName("relais")] public string Relais { get; set; } = RelaisDefaut;

    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static ReglagesDistance Lire(string dossier)
    {
        try
        {
            var f = Path.Combine(dossier, Fichier);
            if (File.Exists(f) && JsonSerializer.Deserialize<ReglagesDistance>(File.ReadAllText(f)) is { } r)
            {
                if (string.IsNullOrWhiteSpace(r.Relais)) r.Relais = RelaisDefaut;
                return r;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new ReglagesDistance();
    }

    public void Ecrire(string dossier)
    {
        Directory.CreateDirectory(dossier);
        var f = Path.Combine(dossier, Fichier);
        File.WriteAllText(f + ".tmp", JsonSerializer.Serialize(this, Opt));
        File.Move(f + ".tmp", f, true);
    }

    /// <summary>Nom du relais tel que tapé (« https://… », espaces, barre finale) ramené à « nom » ; « ws://… » gardé (essais).</summary>
    public static string Nettoyer(string relais)
    {
        var r = (relais ?? "").Trim().TrimEnd('/');
        if (r.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || r.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) return r;
        foreach (var prefixe in new[] { "https://", "http://" })
            if (r.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)) r = r[prefixe.Length..];
        return r.Length == 0 ? RelaisDefaut : r;
    }

    /// <summary>« nom » → wss://nom/chemin (HTTPS) ; « ws://hôte:port » (essais) gardé tel quel.</summary>
    public static Uri Adresse(string relais, string chemin)
    {
        var r = Nettoyer(relais);
        return r.Contains("://") ? new Uri(r + chemin) : new Uri("wss://" + r + chemin);
    }

    /// <summary>Page d'état du relais : le PC est-il joignable de l'extérieur ?</summary>
    public static Uri AdresseEtat(string relais, string pcId)
    {
        var r = Nettoyer(relais);
        if (r.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) return new Uri("http://" + r[5..] + "/etat/" + pcId);
        if (r.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) return new Uri("https://" + r[6..] + "/etat/" + pcId);
        return new Uri("https://" + r + "/etat/" + pcId);
    }

    /// <summary>Le relais dit-il que ce PC est connecté ? null : relais injoignable.</summary>
    public static async Task<bool?> PcJoignable(string relais, string pcId, CancellationToken t = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await http.GetStringAsync(AdresseEtat(relais, pcId), t);
            return JsonNode.Parse(json)?["pc"]?.GetValue<bool>();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException) { return null; }
    }
}

/// <summary>
/// 1.7.0 : Siloé Stream ouvert écrit ici le port de son module téléphone (et son numéro de processus) ; l'agent le lit pour
/// savoir si Siloé Stream tourne et où lui passer un téléphone venu par Internet.
/// </summary>
public static class PortSiloe
{
    public const string Fichier = "telephone-port.json";

    public static void Ecrire(string dossier, int port)
    {
        try { File.WriteAllText(Path.Combine(dossier, Fichier), new JsonObject { ["port"] = port, ["pid"] = Environment.ProcessId }.ToJsonString()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void Effacer(string dossier)
    {
        try
        {
            var f = Path.Combine(dossier, Fichier);
            if (File.Exists(f) && JsonNode.Parse(File.ReadAllText(f))?["pid"]?.GetValue<int>() == Environment.ProcessId) File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException) { }
    }

    /// <summary>Le port de Siloé Stream ouvert sur ce PC, ou null (fermé, ou planté sans effacer le fichier).</summary>
    public static int? Lire(string dossier)
    {
        try
        {
            var f = Path.Combine(dossier, Fichier);
            if (!File.Exists(f) || JsonNode.Parse(File.ReadAllText(f)) is not JsonObject o) return null;
            int port = o["port"]!.GetValue<int>(), pid = o["pid"]!.GetValue<int>();
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return null;
            }
            catch (ArgumentException) { return null; }
            return port;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or NullReferenceException) { return null; }
    }
}

/// <summary>
/// Siloé Stream 1.7.0, l'agent « à distance » : un petit processus sans fenêtre (SiloeStream.exe --agent), lancé à l'ouverture de
/// session Windows. Il garde une connexion avec le relais Siloé Stream (VPS). Un téléphone qui arrive par Internet :
/// <list type="bullet">
/// <item>Siloé Stream est ouvert : l'agent le relie au module téléphone de Siloé Stream (127.0.0.1), comme sur le Wi-Fi de l'église ;</item>
/// <item>Siloé Stream est fermé : l'agent vérifie le téléphone (même clé de jumelage) et peut démarrer Siloé Stream (« app.start »).</item>
/// </list>
/// Le relais ne fait que passer les messages ; le jumelage et la clé restent entre le téléphone et ce PC.
/// </summary>
public sealed class AgentDistance : IDisposable
{
    readonly string dossier;
    readonly Func<bool> demarrerSiloe;
    readonly string nomPc;
    readonly CancellationTokenSource fin = new();
    /// <summary>Attente maximale du démarrage de Siloé Stream (module téléphone ouvert).</summary>
    public TimeSpan AttenteDemarrage { get; init; } = TimeSpan.FromSeconds(90);
    /// <summary>Pause entre deux essais de connexion au relais.</summary>
    public TimeSpan PauseReconnexion { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Relié au relais en ce moment.</summary>
    public bool Connecte { get; private set; }
    /// <summary>Téléphones reçus depuis le démarrage (journal, essais).</summary>
    public int Canaux;

    public AgentDistance(string dossier, Func<bool> demarrerSiloe, string? nomPc = null)
    {
        this.dossier = dossier;
        this.demarrerSiloe = demarrerSiloe;
        this.nomPc = nomPc ?? Environment.GetEnvironmentVariable("COMPUTERNAME") ?? System.Net.Dns.GetHostName();
    }

    public void Dispose() { try { fin.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>Tourne tant que le contrôle à distance est actif (Réglages) ; se termine quand on le coupe.</summary>
    public async Task Executer()
    {
        string derniereErreur = "";
        while (!fin.IsCancellationRequested)
        {
            var r = ReglagesDistance.Lire(dossier);
            if (!r.Actif) { Journal.Info("à distance : coupé dans les Réglages, l'agent s'arrête"); return; }
            var pcId = new AppareilsJumeles(dossier).PcId;
            try
            {
                await Controle(r, pcId);
                derniereErreur = "";
            }
            catch (Exception e)
            {
                if (fin.IsCancellationRequested) return;
                var m = e.InnerException?.Message ?? e.Message;
                if (m != derniereErreur) Journal.Info($"à distance : relais « {r.Relais} » injoignable ({m}), nouvel essai toutes les {PauseReconnexion.TotalSeconds:0} s");
                derniereErreur = m;
            }
            Connecte = false;
            try { await Task.Delay(PauseReconnexion, fin.Token); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Une socket vers le relais qui se rend compte d'une coupure (Internet de l'église tombé, box redémarrée) en moins
    /// d'une minute : TCP keepalive, sinon une connexion morte resterait « ouverte » des heures.
    /// </summary>
    static async Task<ClientWebSocket> Ouvrir(Uri adresse, CancellationToken t)
    {
        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var h = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            // Siloé Stream lui-même (127.0.0.1) : jamais par un proxy
            UseProxy = !(adresse.IsLoopback),
            ConnectCallback = async (ctx, ct) =>
            {
                // une socket neuve par adresse (IPv4 d'abord : l'IPv6 des box est parfois cassé) ; une socket ratée ne se réutilise pas
                var adresses = IPAddress.TryParse(ctx.DnsEndPoint.Host, out var ip) ? new[] { ip } : await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                Exception? derniere = null;
                foreach (var a in adresses.OrderBy(x => x.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
                {
                    var s = new Socket(a.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                        s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 20);
                        s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
                        s.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 4);
                    }
                    catch (Exception e) when (e is SocketException or PlatformNotSupportedException) { }
                    try
                    {
                        await s.ConnectAsync(new IPEndPoint(a, ctx.DnsEndPoint.Port), ct);
                        return new NetworkStream(s, true);
                    }
                    catch (SocketException e)
                    {
                        s.Dispose();
                        derniere = e;
                    }
                    catch
                    {
                        s.Dispose();
                        throw;
                    }
                }
                throw derniere ?? new SocketException((int)SocketError.HostNotFound);
            },
        };
        try
        {
            using var delai = CancellationTokenSource.CreateLinkedTokenSource(t);
            delai.CancelAfter(TimeSpan.FromSeconds(20));
            await ws.ConnectAsync(adresse, new HttpMessageInvoker(h), delai.Token);
            return ws;
        }
        catch
        {
            ws.Dispose();
            throw;
        }
    }

    async Task Controle(ReglagesDistance r, string pcId)
    {
        using var ws = await Ouvrir(ReglagesDistance.Adresse(r.Relais, "/pc/" + pcId), fin.Token);
        Connecte = true;
        Journal.Info($"à distance : relié au relais « {r.Relais} » (les téléphones jumelés peuvent joindre ce PC par Internet)");
        // Réglages changés (relais, coupé) : on se reconnecte avec les nouveaux
        using var change = CancellationTokenSource.CreateLinkedTokenSource(fin.Token);
        _ = Task.Run(async () =>
        {
            try
            {
                while (!change.IsCancellationRequested)
                {
                    await Task.Delay(3000, change.Token);
                    var n = ReglagesDistance.Lire(dossier);
                    if (!n.Actif || ReglagesDistance.Nettoyer(n.Relais) != ReglagesDistance.Nettoyer(r.Relais)) { change.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) { }
        });
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var m = await Telephone.Recevoir(ws, change.Token, 64 * 1024);
                if (m == null) break;
                if (m["t"]?.GetValue<string>() == "canal" && m["id"]?.GetValue<string>() is { Length: > 0 } id)
                {
                    Interlocked.Increment(ref Canaux);
                    _ = Task.Run(() => Canal(r, id));
                }
            }
        }
        catch (OperationCanceledException) when (!fin.IsCancellationRequested) { Journal.Info("à distance : réglages changés, reconnexion"); }
        finally
        {
            change.Cancel();
            Connecte = false;
            try { ws.Abort(); } catch { }
        }
    }

    /// <summary>Un téléphone arrivé par le relais.</summary>
    async Task Canal(ReglagesDistance r, string id)
    {
        try
        {
            using var tel = await Ouvrir(ReglagesDistance.Adresse(r.Relais, "/canal/" + id), fin.Token);
            if (PortSiloe.Lire(dossier) is int port)
            {
                ClientWebSocket local;
                try { local = await Ouvrir(new Uri($"ws://127.0.0.1:{port}/"), fin.Token); }
                catch (Exception e) when (e is WebSocketException or IOException or SocketException or HttpRequestException or OperationCanceledException)
                {
                    // Siloé Stream vient de se fermer (ou se ferme) : l'agent prend le téléphone
                    await SessionAgent(tel);
                    return;
                }
                using (local) await Pont(tel, local);
            }
            else await SessionAgent(tel);
        }
        catch (Exception e) when (e is WebSocketException or IOException or SocketException or HttpRequestException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e) { Journal.Erreur("à distance : téléphone", e); }
    }

    /// <summary>Passe les messages tels quels dans les deux sens (texte et binaire), jusqu'à la fermeture d'un côté.</summary>
    async Task Pont(WebSocket a, WebSocket b)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(fin.Token);
        async Task Copier(WebSocket de, WebSocket vers)
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var r = await de.ReceiveAsync(buf, stop.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    await vers.SendAsync(buf.AsMemory(0, r.Count), r.MessageType, r.EndOfMessage, stop.Token);
                }
            }
            catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException) { }
            finally { stop.Cancel(); }
        }
        await Task.WhenAll(Copier(a, b), Copier(b, a));
        foreach (var w in new[] { a, b })
        {
            try
            {
                using var d = new CancellationTokenSource(2000);
                if (w.State is WebSocketState.Open or WebSocketState.CloseReceived) await w.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", d.Token);
            }
            catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Siloé Stream fermé : l'agent fait la même vérification que Siloé Stream (défi + HMAC avec la clé de jumelage), puis répond
    /// « Siloé Stream est fermé » et démarre Siloé Stream quand le téléphone le demande.
    /// </summary>
    async Task SessionAgent(WebSocket ws)
    {
        var t = fin.Token;
        string Champ(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        async Task Envoyer(JsonObject o)
        {
            using var d = CancellationTokenSource.CreateLinkedTokenSource(t);
            d.CancelAfter(15000);
            await Telephone.EnvoyerBrut(ws, o.ToJsonString(), d.Token);
        }
        async Task Refus(string code, string message)
        {
            Journal.Info($"à distance : téléphone refusé ({code})");
            try { await Envoyer(new JsonObject { ["t"] = "auth.fail", ["code"] = code, ["message"] = message }); } catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        }

        JsonObject? hello;
        using (var d = CancellationTokenSource.CreateLinkedTokenSource(t))
        {
            d.CancelAfter(10000);
            hello = await Telephone.Recevoir(ws, d.Token);
        }
        if (hello == null) return;
        var store = new AppareilsJumeles(dossier); // relu à chaque fois : un téléphone jumelé entre-temps dans Siloé Stream est connu
        var deviceId = Champ(hello, "deviceId");
        var cnonce = Champ(hello, "cnonce");
        if (Champ(hello, "t") != "hello" || deviceId.Length == 0 || cnonce.Length == 0) { await Refus("protocol", "Message de connexion invalide."); return; }
        if (Champ(hello, "pcId") != store.PcId) { await Refus("wrong_pc", "Ce n'est pas le PC jumelé."); return; }
        if (hello["pair"] is JsonValue pv && pv.TryGetValue<bool>(out var pb) && pb)
        {
            await Refus("pairing_closed", "Siloé Stream est fermé sur le PC : ouvrez-le pour jumeler un téléphone (bouton « Téléphone »).");
            return;
        }
        var cle = store.Cle(deviceId);
        if (cle == null) { await Refus("revoked", "Ce téléphone n'est plus jumelé avec ce PC."); return; }

        var nonce = Telephone.Hasard(32);
        await Envoyer(new JsonObject { ["t"] = "challenge", ["v"] = Telephone.Protocole, ["nonce"] = nonce, ["proof"] = Telephone.PreuveServeur(cle, cnonce, nonce, Telephone.BonjourSiloe(hello)), ["pcName"] = nomPc });
        JsonObject? auth;
        using (var d = CancellationTokenSource.CreateLinkedTokenSource(t))
        {
            d.CancelAfter(10000);
            auth = await Telephone.Recevoir(ws, d.Token);
        }
        if (auth == null) return;
        var attendu = Encoding.ASCII.GetBytes(Telephone.MacClient(cle, nonce, deviceId));
        if (Champ(auth, "t") != "auth" || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Champ(auth, "mac")), attendu))
        {
            await Refus("revoked", "Ce téléphone n'est plus jumelé avec ce PC.");
            return;
        }
        var nom = Champ(hello, "deviceName").Trim();
        Journal.Info($"à distance : « {nom} » connecté (Siloé Stream fermé)");
        await Envoyer(new JsonObject
        {
            ["t"] = "welcome", ["v"] = Telephone.Protocole, ["pcName"] = nomPc, ["paired"] = false, ["proto"] = Telephone.Fonctions,
            ["codecs"] = new JsonArray("jpeg"), ["camHorloge"] = false, ["agent"] = true, ["app"] = "ferme", ["role"] = store.Role(deviceId),
        });

        while (ws.State == WebSocketState.Open && !t.IsCancellationRequested)
        {
            var m = await Telephone.Recevoir(ws, t);
            if (m == null) break;
            var type = Champ(m, "t");
            var rid = m["rid"]?.DeepClone();
            JsonObject Rep(JsonObject o) { if (rid != null) o["rid"] = rid; return o; }
            switch (type)
            {
                case "ping": await Envoyer(new JsonObject { ["t"] = "pong" }); break;
                case "app.start":
                {
                    // 1.7.1 : seul un téléphone « Admin » démarre Siloé Stream
                    if (store.Role(deviceId) != Roles.Admin)
                    {
                        await Envoyer(Rep(new JsonObject { ["t"] = "error", ["message"] = "Seul un téléphone « Admin » peut démarrer Siloé Stream." }));
                        break;
                    }
                    if (PortSiloe.Lire(dossier) == null)
                    {
                        Journal.Info($"à distance : « {nom} » démarre Siloé Stream");
                        if (!demarrerSiloe())
                        {
                            await Envoyer(Rep(new JsonObject { ["t"] = "error", ["message"] = "Siloé Stream n'a pas pu démarrer sur le PC." }));
                            break;
                        }
                    }
                    await Envoyer(Rep(new JsonObject { ["t"] = "app.etat", ["app"] = "demarrage" }));
                    var limite = DateTime.UtcNow + AttenteDemarrage;
                    while (PortSiloe.Lire(dossier) == null && DateTime.UtcNow < limite) await Task.Delay(500, t);
                    if (PortSiloe.Lire(dossier) == null)
                    {
                        await Envoyer(new JsonObject { ["t"] = "app.etat", ["app"] = "ferme" });
                        await Envoyer(new JsonObject { ["t"] = "error", ["message"] = "Siloé Stream ne répond pas sur le PC (il démarre peut-être encore). Réessayez dans un instant." });
                        break;
                    }
                    // Siloé Stream est prêt : le téléphone se reconnecte, et passe cette fois directement à Siloé Stream
                    await Envoyer(new JsonObject { ["t"] = "app.etat", ["app"] = "ouvert" });
                    using (var d = new CancellationTokenSource(2000))
                    {
                        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Siloé Stream ouvert", d.Token); }
                        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
                    }
                    return;
                }
                case "state.get" or "agenda.get" or "display.list" or "sync.get" or "cam.info" or "cam.ack":
                    break; // envoyés d'office par l'app à la connexion : rien à répondre tant que Siloé Stream est fermé
                default:
                    if (rid != null) await Envoyer(Rep(new JsonObject { ["t"] = "error", ["message"] = "Siloé Stream est fermé sur le PC de l'église : appuyez sur « Démarrer Siloé Stream »." }));
                    break;
            }
        }
    }
}
