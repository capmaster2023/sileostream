using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Siloé Stream 1.9.5 : une caméra Sony (α6100…) reçue directement par son Wi-Fi, comme dans Imaging Edge Mobile, sans câble
/// ni carte de capture. La caméra est en « Ctrl w/ Smartphone » ; le PC rejoint son Wi-Fi (DIRECT-xxxx) et reçoit l'image en
/// direct (« live view » : des JPEG les uns après les autres). Deux protocoles Sony, essayés l'un après l'autre :
/// <list type="bullet">
/// <item>l'API Web (JSON sur HTTP, ports 8080 / 10000, puis le flux « liveviewstream ») des Sony de 2013 à ~2019 ;</item>
/// <item>PTP/IP (port 15740) avec l'extension Sony « SDIO », des modèles plus récents ; l'image est lue avec GetObject 0xFFFFC002.</item>
/// </list>
/// Les images entrent dans le registre MyCam (<see cref="MyCams"/>) sous l'identifiant <see cref="Id"/> : la caméra est une
/// source « mycam » comme un téléphone (Display, Duo, MyStream, enregistrements), sans rien de plus à l'affichage.
/// </summary>
public sealed class CameraWifi : IDisposable
{
    public const string Id = "sony-wifi";
    /// <summary>L'adresse de la caméra sur son propre Wi-Fi (Wi-Fi Direct de Sony).</summary>
    public const string AdresseSony = "192.168.122.1";

    readonly MyCams cams;
    readonly object verrou = new();
    CancellationTokenSource? arret;
    Task? tache;

    public CameraWifi(MyCams cams) { this.cams = cams; }

    /// <summary>Nom de la source dans Display.</summary>
    public string Nom { get; set; } = "Caméra Sony (Wi-Fi)";
    /// <summary>Adresse imposée (vide : recherche, puis 192.168.122.1).</summary>
    public string Adresse { get; set; } = "";
    /// <summary>Ports (changés par les essais).</summary>
    public int PortPtp { get; set; } = 15740;
    public int[] PortsWeb { get; set; } = { 8080, 10000 };
    public bool Recherche { get; set; } = true;

    /// <summary>« Recherche de la caméra… », « En direct · 1024 × 576 · 28 i/s · PTP/IP »…</summary>
    public string Etat { get; private set; } = "Arrêtée";
    /// <summary>« API Web Sony » ou « PTP/IP » une fois connectée.</summary>
    public string Protocole { get; private set; } = "";
    /// <summary>Le modèle annoncé par la caméra (« ILCE-6100 »).</summary>
    public string Modele { get; private set; } = "";
    public bool EnDirect { get; private set; }
    public bool Actif => tache is { IsCompleted: false };
    /// <summary>L'état a changé (thread quelconque).</summary>
    public event Action? Change;

    public void Demarrer()
    {
        lock (verrou)
        {
            if (Actif) return;
            arret = new CancellationTokenSource();
            var ct = arret.Token;
            tache = Task.Run(() => Boucle(ct));
        }
    }

    public void Arreter()
    {
        Task? t;
        lock (verrou)
        {
            arret?.Cancel();
            t = tache;
            tache = null;
        }
        try { t?.Wait(3000); } catch { }
        EnDirect = false;
        cams.Deconnecte(Id);
        Dire("Arrêtée");
    }

    public void Dispose() => Arreter();

    void Dire(string etat)
    {
        if (etat == Etat) return;
        Etat = etat;
        try { Change?.Invoke(); } catch (Exception e) { Journal.Erreur("caméra Wi-Fi", e); }
    }

    // ------------------------------------------------------------ boucle : chercher, se connecter, recevoir, recommencer

    async Task Boucle(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Dire("Recherche de la caméra sur le Wi-Fi…");
                var hotes = new List<(string Hote, string? Description)>();
                if (Adresse.Trim().Length > 0) hotes.Add((Adresse.Trim(), null));
                if (Recherche) foreach (var h in await Ssdp(ct)) if (!hotes.Any(x => x.Hote == h.Hote)) hotes.Add(h);
                if (!hotes.Any(x => x.Hote == AdresseSony) && Adresse.Trim().Length == 0) hotes.Add((AdresseSony, null));
                bool vue = false;
                foreach (var (hote, desc) in hotes)
                {
                    if (ct.IsCancellationRequested) break;
                    if (await EssayerApiWeb(hote, desc, ct)) { vue = true; break; }
                    if (ct.IsCancellationRequested) break;
                    if (await EssayerPtp(hote, ct)) { vue = true; break; }
                }
                if (!vue && !ct.IsCancellationRequested)
                    Dire("Caméra introuvable. Sur la caméra : MENU › Réseau › Ctrl w/ Smartphone : Oui ; ce PC doit être connecté à son Wi-Fi (DIRECT-…).");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                Journal.Erreur("caméra Wi-Fi", e);
                Dire("Caméra Wi-Fi : " + e.Message);
            }
            if (EnDirect)
            {
                EnDirect = false;
                cams.Deconnecte(Id);
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ------------------------------------------------------------ recherche SSDP (UPnP)

    /// <summary>Les caméras Sony qui répondent à la recherche UPnP : leur adresse et l'URL de leur description.</summary>
    static async Task<List<(string Hote, string? Description)>> Ssdp(CancellationToken ct)
    {
        var trouves = new List<(string, string?)>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            var cible = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            foreach (var st in new[] { "urn:schemas-sony-com:service:ScalarWebAPI:1", "urn:schemas-sony-com:service:DigitalImaging:1", "ssdp:all" })
            {
                var m = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: {st}\r\n\r\n");
                await udp.SendAsync(m, cible, ct);
            }
            var fin = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < fin)
            {
                using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
                delai.CancelAfter(fin - DateTime.UtcNow);
                UdpReceiveResult r;
                try { r = await udp.ReceiveAsync(delai.Token); }
                catch (OperationCanceledException) { break; }
                var texte = Encoding.ASCII.GetString(r.Buffer);
                if (!texte.Contains("sony", StringComparison.OrdinalIgnoreCase)) continue;
                var loc = Regex.Match(texte, @"(?im)^LOCATION:\s*(\S+)");
                var hote = r.RemoteEndPoint.Address.ToString();
                if (!trouves.Any(t => t.Item1 == hote)) trouves.Add((hote, loc.Success ? loc.Groups[1].Value : null));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { Journal.Info("caméra Wi-Fi : recherche UPnP : " + e.Message); }
        return trouves;
    }

    // ------------------------------------------------------------ API Web Sony (JSON-RPC, « liveviewstream »)

    static readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = Timeout.InfiniteTimeSpan };

    async Task<JsonNode?> Rpc(string url, string methode, JsonArray parametres, CancellationToken ct)
    {
        var corps = new JsonObject { ["method"] = methode, ["params"] = parametres, ["id"] = 1, ["version"] = "1.0" };
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(TimeSpan.FromSeconds(4));
        using var r = await http.PostAsync(url, new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json"), delai.Token);
        if (!r.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await r.Content.ReadAsStringAsync(delai.Token));
    }

    async Task<bool> EssayerApiWeb(string hote, string? description, CancellationToken ct)
    {
        var urls = new List<string>();
        if (description != null)
        {
            try
            {
                using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
                delai.CancelAfter(TimeSpan.FromSeconds(3));
                var xml = await http.GetStringAsync(description, delai.Token);
                var m = Regex.Match(xml, @"<av:X_ScalarWebAPI_ServiceType>\s*camera\s*</av:X_ScalarWebAPI_ServiceType>\s*<av:X_ScalarWebAPI_ActionList_URL>\s*([^<\s]+)\s*<", RegexOptions.IgnoreCase);
                if (m.Success) urls.Add(m.Groups[1].Value.TrimEnd('/') + "/camera");
                var modele = Regex.Match(xml, @"<modelName>\s*([^<]+?)\s*</modelName>", RegexOptions.IgnoreCase);
                if (modele.Success) Modele = modele.Groups[1].Value;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        foreach (var p in PortsWeb) urls.Add($"http://{Hote(hote)}:{p}/sony/camera");
        foreach (var url in urls.Distinct())
        {
            JsonNode? rep;
            try
            {
                // certains modèles demandent le mode prise de vue avant l'image en direct ; une erreur ici ne dit rien
                try { await Rpc(url, "startRecMode", new JsonArray(), ct); } catch (HttpRequestException) { continue; }
                rep = await Rpc(url, "startLiveview", new JsonArray(), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { continue; }
            if (rep?["result"] is JsonArray res && res.Count > 0 && res[0]?.GetValue<string>() is { Length: > 0 } flux)
            {
                Protocole = "API Web Sony";
                await LireFluxHttp(flux, ct);
                try { await Rpc(url, "stopLiveview", new JsonArray(), CancellationToken.None); } catch { }
                return true;
            }
        }
        return false;
    }

    static string Hote(string h) => h.Contains(':') && !h.StartsWith('[') ? $"[{h}]" : h;

    async Task LireFluxHttp(string url, CancellationToken ct)
    {
        Dire("Connexion à l'image en direct…");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(TimeSpan.FromSeconds(5));
        using var r = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, delai.Token);
        r.EnsureSuccessStatusCode();
        using var flux = await r.Content.ReadAsStreamAsync(ct);
        var lecteur = new FluxLiveview();
        var tampon = new byte[64 * 1024];
        DebutDirect();
        while (!ct.IsCancellationRequested)
        {
            using var attente = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attente.CancelAfter(TimeSpan.FromSeconds(5)); // plus rien pendant 5 s : la caméra est partie
            int n;
            try { n = await flux.ReadAsync(tampon, attente.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            if (n <= 0) break;
            foreach (var jpeg in lecteur.Pousser(tampon.AsSpan(0, n))) Publier(jpeg, 0, jpeg.Length);
        }
    }

    // ------------------------------------------------------------ PTP/IP (extension Sony SDIO)

    async Task<bool> EssayerPtp(string hote, CancellationToken ct)
    {
        SessionPtp s;
        try { s = await SessionPtp.Ouvrir(hote, PortPtp, "Siloe Stream", ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            Journal.Info($"caméra Wi-Fi : PTP/IP {hote} : {e.Message}");
            return false;
        }
        using (s)
        {
            Protocole = "PTP/IP";
            if (s.Modele.Length > 0) Modele = s.Modele;
            Dire("Connexion à l'image en direct…");
            int echecs = 0;
            bool debut = false;
            var minuteur = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                var (code, donnees) = await s.Commande(SessionPtp.GetObject, ct, 0xFFFFC002);
                if (code == SessionPtp.Ok && donnees != null && ImageLiveviewPtp(donnees) is { } j)
                {
                    echecs = 0;
                    if (!debut) { DebutDirect(); debut = true; }
                    Publier(donnees, j.Debut, j.Longueur);
                }
                else if (++echecs > 150)
                {
                    // ~5 s sans image : l'image en direct n'est pas prête sur cette caméra
                    if (!debut) Dire("La caméra répond (PTP/IP), mais ne donne pas d'image en direct. Mettez-la en mode photo ou vidéo.");
                    break;
                }
                else await Task.Delay(30, ct);
                // au plus ~30 images par seconde
                var reste = 28 - (int)minuteur.ElapsedMilliseconds;
                if (reste > 0) await Task.Delay(reste, ct);
                minuteur.Restart();
            }
            return debut;
        }
    }

    /// <summary>
    /// L'objet « live view » de Sony (0xFFFFC002) : en tête, la position et la taille du JPEG (2 × 4 octets, petit-boutiste),
    /// puis le JPEG ; à défaut, le premier JPEG trouvé (FF D8 … FF D9).
    /// </summary>
    public static (int Debut, int Longueur)? ImageLiveviewPtp(byte[] d)
    {
        if (d.Length >= 8)
        {
            int pos = BitConverter.ToInt32(d, 0), n = BitConverter.ToInt32(d, 4);
            if (pos >= 8 && n > 4 && (long)pos + n <= d.Length && d[pos] == 0xFF && d[pos + 1] == 0xD8) return (pos, n);
        }
        int a = IndexDe(d, 0, 0xD8);
        if (a < 0) return null;
        int b = IndexDe(d, a + 2, 0xD9);
        return b < 0 ? null : (a, b + 2 - a);
    }

    static int IndexDe(byte[] d, int depuis, byte marqueur)
    {
        for (int i = depuis; i + 1 < d.Length; i++) if (d[i] == 0xFF && d[i + 1] == marqueur) return i;
        return -1;
    }

    // ------------------------------------------------------------ vers MyCam

    long depuisMs;
    int images, fps;
    long seconde;
    string taille = "";
    DateTime derniereInfo;

    void DebutDirect()
    {
        depuisMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        images = 0;
        fps = 0;
        taille = "";
        EnDirect = true;
        Info(true);
    }

    void Publier(byte[] source, int debut, int n)
    {
        var m = new byte[4 + n];
        m[0] = (byte)'M'; m[1] = (byte)'C'; m[2] = 1; m[3] = 0;
        Buffer.BlockCopy(source, debut, m, 4, n);
        if (taille.Length == 0 && TailleJpeg(m.AsSpan(4)) is { } t) taille = $"{t.L} × {t.H}";
        cams.Recevoir(Id, Nom, m, m.Length);
        var s = Environment.TickCount64 / 1000;
        if (s != seconde) { fps = images; images = 0; seconde = s; }
        images++;
        if (DateTime.UtcNow - derniereInfo > TimeSpan.FromSeconds(2)) Info(true);
    }

    void Info(bool diffuse)
    {
        derniereInfo = DateTime.UtcNow;
        cams.Infos(Id, Nom, new JsonObject
        {
            ["nom"] = Nom,
            ["appareil"] = (Modele.Length > 0 ? "Sony " + Modele : "Caméra Sony") + " · Wi-Fi",
            ["taille"] = taille,
            ["fps"] = fps,
            ["fpsCible"] = 30,
            ["batterie"] = -1,
            ["lien"] = "wifi",
            ["diffuse"] = diffuse,
            ["depuis"] = depuisMs,
        });
        Dire("En direct" + (taille.Length > 0 ? " · " + taille : "") + (fps > 0 ? $" · {fps} i/s" : "") + " · " + Protocole);
    }

    /// <summary>Largeur et hauteur d'un JPEG (marqueur SOF), ou null.</summary>
    public static (int L, int H)? TailleJpeg(ReadOnlySpan<byte> j)
    {
        int i = 2;
        while (i + 9 < j.Length)
        {
            if (j[i] != 0xFF) return null;
            int m = j[i + 1];
            if (m == 0xD8 || (m >= 0xD0 && m <= 0xD7) || m == 0x01) { i += 2; continue; }
            int longueur = j[i + 2] << 8 | j[i + 3];
            if (m is >= 0xC0 and <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC)
                return (j[i + 7] << 8 | j[i + 8], j[i + 5] << 8 | j[i + 6]);
            if (m == 0xDA) return null;
            i += 2 + longueur;
        }
        return null;
    }
}

/// <summary>
/// Le flux « liveviewstream » de l'API Web Sony : des paquets (en-tête commun de 8 octets commençant par 0xFF, en-tête de
/// charge de 128 octets commençant par 24 35 68 79, taille sur 3 octets, bourrage), ou à défaut des JPEG à la suite.
/// </summary>
public sealed class FluxLiveview
{
    byte[] tampon = new byte[256 * 1024];
    int n;

    public List<byte[]> Pousser(ReadOnlySpan<byte> octets)
    {
        if (n + octets.Length > tampon.Length) Array.Resize(ref tampon, Math.Max(tampon.Length * 2, n + octets.Length));
        octets.CopyTo(tampon.AsSpan(n));
        n += octets.Length;
        var sortie = new List<byte[]>();
        int pos = 0;
        while (pos < n)
        {
            if (tampon[pos] == 0xFF && pos + 12 <= n && tampon[pos + 8] == 0x24 && tampon[pos + 9] == 0x35 && tampon[pos + 10] == 0x68 && tampon[pos + 11] == 0x79)
            {
                if (pos + 136 > n) break;
                int type = tampon[pos + 1];
                int taille = tampon[pos + 12] << 16 | tampon[pos + 13] << 8 | tampon[pos + 14];
                int bourrage = tampon[pos + 15];
                int total = 136 + taille + bourrage;
                if (pos + total > n) break;
                if (type == 1 && taille > 4 && tampon[pos + 136] == 0xFF && tampon[pos + 137] == 0xD8)
                    sortie.Add(tampon.AsSpan(pos + 136, taille).ToArray());
                pos += total;
                continue;
            }
            // pas un paquet : un JPEG brut, ou des octets à sauter jusqu'au prochain début possible
            int soi = -1;
            for (int i = pos; i + 1 < n; i++)
            {
                if (tampon[i] != 0xFF) continue;
                if (tampon[i + 1] == 0xD8) { soi = i; break; }
                if (i + 12 <= n && tampon[i + 8] == 0x24 && tampon[i + 9] == 0x35 && tampon[i + 10] == 0x68 && tampon[i + 11] == 0x79) { soi = -2 - i; break; }
            }
            if (soi <= -2) { pos = -2 - soi; continue; }
            if (soi < 0) { pos = Math.Max(pos, n - 12); break; }
            int eoi = -1;
            for (int i = soi + 2; i + 1 < n; i++) if (tampon[i] == 0xFF && tampon[i + 1] == 0xD9) { eoi = i; break; }
            if (eoi < 0) { pos = soi; break; }
            sortie.Add(tampon.AsSpan(soi, eoi + 2 - soi).ToArray());
            pos = eoi + 2;
        }
        if (pos > 0)
        {
            Buffer.BlockCopy(tampon, pos, tampon, 0, n - pos);
            n -= pos;
        }
        if (n > 16 * 1024 * 1024) n = 0; // flux incompréhensible : on repart de zéro
        return sortie;
    }
}

/// <summary>Une session PTP/IP minimale (commande, données, réponse), avec la poignée de main Sony « SDIO ».</summary>
public sealed class SessionPtp : IDisposable
{
    public const ushort Ok = 0x2001;
    public const ushort GetDeviceInfo = 0x1001;
    public const ushort OpenSession = 0x1002;
    public const ushort GetObject = 0x1009;
    public const ushort SdioConnect = 0x9201;
    public const ushort SdioGetExtDeviceInfo = 0x9202;

    const uint InitCommandRequest = 1, InitCommandAck = 2, InitEventRequest = 3, InitEventAck = 4, InitFail = 5;
    const uint CommandRequest = 6, CommandResponse = 7, StartData = 9, Data = 10, EndData = 12, Ping = 13, Pong = 14;

    readonly TcpClient commande, evenements;
    readonly NetworkStream sc, se;
    uint transaction = 1;
    readonly CancellationTokenSource fin = new();

    /// <summary>Le modèle annoncé par la caméra (GetDeviceInfo).</summary>
    public string Modele { get; private set; } = "";

    SessionPtp(TcpClient c, TcpClient e)
    {
        commande = c;
        evenements = e;
        sc = c.GetStream();
        se = e.GetStream();
        // le canal des événements est lu et ignoré : sinon la caméra finit par bloquer
        _ = Task.Run(async () =>
        {
            var b = new byte[4096];
            try { while (!fin.IsCancellationRequested && await se.ReadAsync(b, fin.Token) > 0) { } } catch { }
        });
    }

    public void Dispose()
    {
        fin.Cancel();
        try { commande.Dispose(); } catch { }
        try { evenements.Dispose(); } catch { }
    }

    static byte[] Guid16()
    {
        var b = new byte[16];
        var s = Encoding.UTF8.GetBytes("SiloeStream" + Environment.MachineName);
        var h = System.Security.Cryptography.SHA256.HashData(s);
        Array.Copy(h, b, 16);
        return b;
    }

    static async Task<TcpClient> Connecter(string hote, int port, CancellationToken ct)
    {
        var c = new TcpClient { NoDelay = true, ReceiveBufferSize = 1 << 20 };
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(TimeSpan.FromSeconds(3));
        try { await c.ConnectAsync(hote, port, delai.Token); }
        catch { c.Dispose(); throw; }
        return c;
    }

    public static async Task<SessionPtp> Ouvrir(string hote, int port, string nom, CancellationToken ct)
    {
        // version du protocole : 1.0 (0x00010000), sinon 1 (certains Sony)
        foreach (uint version in new uint[] { 0x00010000, 1 })
        {
            var c = await Connecter(hote, port, ct);
            try
            {
                var s = c.GetStream();
                var nomUtf16 = Encoding.Unicode.GetBytes(nom + "\0");
                var p = new byte[16 + nomUtf16.Length + 4];
                Guid16().CopyTo(p, 0);
                nomUtf16.CopyTo(p, 16);
                BitConverter.GetBytes(version).CopyTo(p, 16 + nomUtf16.Length);
                await Envoyer(s, InitCommandRequest, p, ct);
                var (type, ack) = await Lire(s, ct);
                if (type == InitFail) { c.Dispose(); continue; }
                if (type != InitCommandAck || ack.Length < 4) throw new IOException($"PTP/IP : réponse inattendue ({type})");
                uint connexion = BitConverter.ToUInt32(ack, 0);
                var e = await Connecter(hote, port, ct);
                var es = e.GetStream();
                await Envoyer(es, InitEventRequest, BitConverter.GetBytes(connexion), ct);
                var (te, _) = await Lire(es, ct);
                if (te != InitEventAck) { e.Dispose(); throw new IOException($"PTP/IP : canal des événements refusé ({te})"); }
                var session = new SessionPtp(c, e);
                try
                {
                    var (ouvert, _) = await session.CommandeTid(OpenSession, ct, 0, 1);
                    if (ouvert != Ok && ouvert != 0x201E /* déjà ouverte */) throw new IOException($"PTP/IP : OpenSession 0x{ouvert:X4}");
                    var (ci, info) = await session.Commande(GetDeviceInfo, ct);
                    var operations = new HashSet<ushort>();
                    if (ci == Ok && info != null) (session.Modele, operations) = LireDeviceInfo(info);
                    if (operations.Contains(SdioGetExtDeviceInfo)) await session.PoigneeSony(ct);
                    return session;
                }
                catch { session.Dispose(); throw; }
            }
            catch { c.Dispose(); throw; }
        }
        throw new IOException("PTP/IP : la caméra refuse la connexion");
    }

    /// <summary>La poignée de main Sony : SDIO Connect 1 et 2, GetExtDeviceInfo (version 300, sinon 200), SDIO Connect 3.</summary>
    async Task PoigneeSony(CancellationToken ct)
    {
        var (c1, _) = await Commande(SdioConnect, ct, 1, 0, 0);
        var (c2, _) = await Commande(SdioConnect, ct, 2, 0, 0);
        if (c1 != Ok || c2 != Ok) throw new IOException($"PTP/IP : poignée de main Sony refusée (0x{c1:X4}, 0x{c2:X4})");
        var (ce, _) = await Commande(SdioGetExtDeviceInfo, ct, 0x12C);
        if (ce != Ok) await Commande(SdioGetExtDeviceInfo, ct, 0xC8);
        var (c3, _) = await Commande(SdioConnect, ct, 3, 0, 0);
        if (c3 != Ok) throw new IOException($"PTP/IP : poignée de main Sony refusée (0x{c3:X4})");
    }

    static (string Modele, HashSet<ushort> Operations) LireDeviceInfo(byte[] d)
    {
        int i = 0;
        ushort U16() { var v = BitConverter.ToUInt16(d, i); i += 2; return v; }
        string Texte()
        {
            int n = d[i++];
            var s = n > 0 ? Encoding.Unicode.GetString(d, i, Math.Max(0, (n - 1) * 2)) : "";
            i += n * 2;
            return s;
        }
        HashSet<ushort> Liste()
        {
            int n = BitConverter.ToInt32(d, i); i += 4;
            var h = new HashSet<ushort>();
            for (int k = 0; k < n; k++) h.Add(U16());
            return h;
        }
        try
        {
            U16(); i += 4; U16(); Texte(); U16();
            var ops = Liste();
            Liste(); Liste(); Liste(); Liste();
            Texte(); // fabricant
            var modele = Texte();
            return (modele, ops);
        }
        catch { return ("", new HashSet<ushort>()); }
    }

    static async Task Envoyer(NetworkStream s, uint type, byte[] charge, CancellationToken ct)
    {
        var p = new byte[8 + charge.Length];
        BitConverter.GetBytes((uint)p.Length).CopyTo(p, 0);
        BitConverter.GetBytes(type).CopyTo(p, 4);
        charge.CopyTo(p, 8);
        await s.WriteAsync(p, ct);
    }

    static async Task<(uint Type, byte[] Charge)> Lire(NetworkStream s, CancellationToken ct)
    {
        var t = new byte[8];
        await s.ReadExactlyAsync(t, ct);
        uint longueur = BitConverter.ToUInt32(t, 0), type = BitConverter.ToUInt32(t, 4);
        if (longueur < 8 || longueur > 64 * 1024 * 1024) throw new IOException("PTP/IP : paquet illisible");
        var c = new byte[longueur - 8];
        await s.ReadExactlyAsync(c, ct);
        return (type, c);
    }

    /// <summary>Une opération (paramètres, sans données envoyées) : le code de réponse et les données reçues, s'il y en a.</summary>
    public async Task<(ushort Code, byte[]? Donnees)> CommandeTid(ushort operation, CancellationToken ct, uint? transactionImposee, params uint[] parametres)
    {
        uint tid = transactionImposee ?? transaction++;
        var p = new byte[4 + 2 + 4 + 4 * parametres.Length];
        BitConverter.GetBytes(1u).CopyTo(p, 0); // pas de données envoyées
        BitConverter.GetBytes(operation).CopyTo(p, 4);
        BitConverter.GetBytes(tid).CopyTo(p, 6);
        for (int k = 0; k < parametres.Length; k++) BitConverter.GetBytes(parametres[k]).CopyTo(p, 10 + 4 * k);
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(TimeSpan.FromSeconds(6));
        await Envoyer(sc, CommandRequest, p, delai.Token);
        MemoryStream? donnees = null;
        while (true)
        {
            var (type, c) = await Lire(sc, delai.Token);
            switch (type)
            {
                case StartData:
                    donnees = new MemoryStream(c.Length >= 8 ? (int)Math.Min(BitConverter.ToUInt32(c, 4), 32u << 20) : 0);
                    break;
                case Data:
                case EndData:
                    donnees ??= new MemoryStream();
                    if (c.Length > 4) donnees.Write(c, 4, c.Length - 4);
                    break;
                case CommandResponse:
                    ushort code = c.Length >= 2 ? BitConverter.ToUInt16(c, 0) : (ushort)0;
                    return (code, donnees?.ToArray());
                case Ping:
                    await Envoyer(sc, Pong, Array.Empty<byte>(), delai.Token);
                    break;
                default:
                    break; // Pong, paquets Sony inconnus (0xFFFF) : ignorés
            }
        }
    }

    public Task<(ushort Code, byte[]? Donnees)> Commande(ushort operation, CancellationToken ct, params uint[] parametres) =>
        CommandeTid(operation, ct, null, parametres);
}

/// <summary>Rejoindre le Wi-Fi de la caméra (DIRECT-xxxx) depuis Windows, avec netsh.</summary>
public static class WifiWindows
{
    /// <summary>Les cartes Wi-Fi du PC : nom, Wi-Fi connecté (SSID, vide sinon).</summary>
    public static List<(string Nom, string Ssid)> Cartes()
    {
        var liste = new List<(string, string)>();
        var sortie = Netsh("wlan show interfaces");
        string? nom = null;
        foreach (var l in sortie.Split('\n'))
        {
            var m = Regex.Match(l, @"^\s*([^:]+?)\s*:\s*(.*?)\s*$");
            if (!m.Success) continue;
            var cle = m.Groups[1].Value.ToLowerInvariant();
            var val = m.Groups[2].Value;
            if (cle is "name" or "nom") { if (nom != null) liste.Add((nom, "")); nom = val; }
            else if (nom != null && cle == "ssid") { liste.Add((nom, val)); nom = null; }
        }
        if (nom != null) liste.Add((nom, ""));
        return liste;
    }

    /// <summary>
    /// Ajoute le profil du Wi-Fi de la caméra et s'y connecte. S'il y a plusieurs cartes Wi-Fi, celle qui n'est pas connectée
    /// (l'autre garde Internet). Rend un message d'erreur, ou null si la demande est partie.
    /// </summary>
    public static string? Connecter(string ssid, string motDePasse)
    {
        if (!OperatingSystem.IsWindows()) return "Wi-Fi : seulement sous Windows.";
        ssid = ssid.Trim();
        if (ssid.Length == 0) return "Nom du Wi-Fi de la caméra manquant.";
        var cartes = Cartes();
        if (cartes.Count == 0) return "Ce PC n'a pas de carte Wi-Fi.";
        var carte = cartes.FirstOrDefault(c => c.Ssid == ssid).Nom
            ?? (cartes.Count > 1 ? cartes.FirstOrDefault(c => c.Ssid.Length == 0).Nom ?? cartes[^1].Nom : cartes[0].Nom);
        string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        var xml = $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>{X(ssid)}</name>
              <SSIDConfig><SSID><name>{X(ssid)}</name></SSID></SSIDConfig>
              <connectionType>ESS</connectionType>
              <connectionMode>manual</connectionMode>
              <MSM><security>
                <authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
                <sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{X(motDePasse)}</keyMaterial></sharedKey>
              </security></MSM>
            </WLANProfile>
            """;
        var fichier = Path.Combine(Path.GetTempPath(), "siloe-camera-wifi.xml");
        try
        {
            File.WriteAllText(fichier, xml);
            Netsh($"wlan add profile filename=\"{fichier}\" interface=\"{carte}\" user=current");
            var r = Netsh($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\" interface=\"{carte}\"");
            return r.Contains("success", StringComparison.OrdinalIgnoreCase) || r.Contains("réussi", StringComparison.OrdinalIgnoreCase) || r.Contains("effectu", StringComparison.OrdinalIgnoreCase)
                ? null : "Wi-Fi de la caméra : " + r.Trim();
        }
        catch (Exception e) { return "Wi-Fi de la caméra : " + e.Message; }
        finally { try { File.Delete(fichier); } catch { } }
    }

    static string Netsh(string args)
    {
        var psi = new ProcessStartInfo("netsh", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return o;
    }
}

/// <summary>Réglages de la caméra Wi-Fi, gardés à côté des données (camera-wifi.json).</summary>
public sealed class ReglagesCameraWifi
{
    public string Ssid { get; set; } = "";
    public string MotDePasse { get; set; } = "";
    public string Adresse { get; set; } = "";
    public string Nom { get; set; } = "Caméra Sony (Wi-Fi)";

    static string Fichier(string dossier) => Path.Combine(dossier, "camera-wifi.json");

    public static ReglagesCameraWifi Lire(string dossier)
    {
        try { return JsonSerializer.Deserialize<ReglagesCameraWifi>(File.ReadAllText(Fichier(dossier))) ?? new(); }
        catch { return new(); }
    }

    public void Sauver(string dossier)
    {
        try { File.WriteAllText(Fichier(dossier), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Journal.Erreur("caméra Wi-Fi : réglages", e); }
    }
}
