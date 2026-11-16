using System.Net;
using System.Net.Sockets;
using System.Text;
using MyVerse;

namespace MyVerse.Tests;

/// <summary>
/// Caméra Sony par Wi-Fi (1.9.5) : une fausse caméra sur cette machine, en PTP/IP (extension Sony SDIO, GetObject 0xFFFFC002)
/// et en API Web (JSON-RPC, flux « liveviewstream »), et les images qui arrivent dans le registre MyCam.
/// </summary>
public static class CameraWifiTests
{
    /// <summary>Un petit JPEG valable pour les en-têtes (SOI, SOF0 1024 × 576, EOI).</summary>
    public static byte[] Jpeg(byte marque)
    {
        return new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x02, 0x40, 0x04, 0x00, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xDA, 0x00, 0x02, marque, 0x55, 0xFF, 0xD9,
        };
    }

    static int PortLibre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    static byte[] Paquet(uint type, byte[] charge)
    {
        var p = new byte[8 + charge.Length];
        BitConverter.GetBytes((uint)p.Length).CopyTo(p, 0);
        BitConverter.GetBytes(type).CopyTo(p, 4);
        charge.CopyTo(p, 8);
        return p;
    }

    static async Task<(uint, byte[])> Lire(NetworkStream s)
    {
        var t = new byte[8];
        await s.ReadExactlyAsync(t);
        var c = new byte[BitConverter.ToUInt32(t, 0) - 8];
        await s.ReadExactlyAsync(c);
        return (BitConverter.ToUInt32(t, 4), c);
    }

    static byte[] ChainePtp(string s)
    {
        if (s.Length == 0) return new byte[] { 0 };
        var u = Encoding.Unicode.GetBytes(s + "\0");
        return new[] { (byte)(s.Length + 1) }.Concat(u).ToArray();
    }

    static byte[] DeviceInfo()
    {
        var m = new MemoryStream();
        void U16(ushort v) => m.Write(BitConverter.GetBytes(v));
        void U32(uint v) => m.Write(BitConverter.GetBytes(v));
        U16(100); U32(0x11); U16(100); m.Write(ChainePtp("Sony PTP Extensions")); U16(0);
        var ops = new ushort[] { 0x1001, 0x1002, 0x1009, 0x9201, 0x9202 };
        U32((uint)ops.Length); foreach (var o in ops) U16(o);
        for (int k = 0; k < 4; k++) U32(0);
        m.Write(ChainePtp("Sony Corporation"));
        m.Write(ChainePtp("ILCE-6100"));
        return m.ToArray();
    }

    /// <summary>La fausse caméra PTP/IP : poignée de main, puis une image différente à chaque GetObject 0xFFFFC002.</summary>
    static async Task FausseCameraPtp(TcpListener l, List<string> journal, CancellationToken ct)
    {
        using var cc = await l.AcceptTcpClientAsync(ct);
        var c = cc.GetStream();
        var (t1, _) = await Lire(c);
        journal.Add("init " + t1);
        await c.WriteAsync(Paquet(2, BitConverter.GetBytes(1u).Concat(new byte[16]).Concat(Encoding.Unicode.GetBytes("A6100\0")).ToArray()), ct);
        using var ce = await l.AcceptTcpClientAsync(ct);
        var e = ce.GetStream();
        var (t2, _) = await Lire(e);
        journal.Add("event " + t2);
        await e.WriteAsync(Paquet(4, Array.Empty<byte>()), ct);
        byte n = 0;
        while (!ct.IsCancellationRequested)
        {
            var (type, p) = await Lire(c);
            if (type != 6) continue;
            ushort op = BitConverter.ToUInt16(p, 4);
            uint tid = BitConverter.ToUInt32(p, 6);
            uint p1 = p.Length >= 14 ? BitConverter.ToUInt32(p, 10) : 0;
            journal.Add($"op {op:X4} {p1:X}");
            byte[]? donnees = op switch
            {
                0x1001 => DeviceInfo(),
                0x9202 => new byte[] { 0x2C, 0x01, 0, 0, 0, 0 },
                0x1009 when p1 == 0xFFFFC002 => Liveview(Jpeg(n++)),
                _ => null,
            };
            if (donnees != null)
            {
                await c.WriteAsync(Paquet(9, BitConverter.GetBytes(tid).Concat(BitConverter.GetBytes((ulong)donnees.Length)).ToArray()), ct);
                await c.WriteAsync(Paquet(12, BitConverter.GetBytes(tid).Concat(donnees).ToArray()), ct);
            }
            await c.WriteAsync(Paquet(7, BitConverter.GetBytes((ushort)0x2001).Concat(BitConverter.GetBytes(tid)).ToArray()), ct);
        }
    }

    static byte[] Liveview(byte[] jpeg)
    {
        var d = new byte[16 + jpeg.Length];
        BitConverter.GetBytes(16).CopyTo(d, 0);
        BitConverter.GetBytes(jpeg.Length).CopyTo(d, 4);
        jpeg.CopyTo(d, 16);
        return d;
    }

    /// <summary>Un paquet du flux « liveviewstream » de l'API Web Sony.</summary>
    public static byte[] PaquetLiveview(byte[] jpeg, ushort seq, int bourrage = 3)
    {
        var p = new byte[136 + jpeg.Length + bourrage];
        p[0] = 0xFF; p[1] = 0x01; p[2] = (byte)(seq >> 8); p[3] = (byte)seq;
        p[8] = 0x24; p[9] = 0x35; p[10] = 0x68; p[11] = 0x79;
        p[12] = (byte)(jpeg.Length >> 16); p[13] = (byte)(jpeg.Length >> 8); p[14] = (byte)jpeg.Length; p[15] = (byte)bourrage;
        jpeg.CopyTo(p, 136);
        return p;
    }

    public static async Task RunAsync(Action<bool, string, string?> ok)
    {
        // ---- lecture du flux liveviewstream, coupé n'importe où
        var flux = new MemoryStream();
        for (ushort k = 0; k < 3; k++) flux.Write(PaquetLiveview(Jpeg((byte)k), k));
        var tout = flux.ToArray();
        var lecteur = new FluxLiveview();
        var recus = new List<byte[]>();
        for (int i = 0; i < tout.Length; i += 37) recus.AddRange(lecteur.Pousser(tout.AsSpan(i, Math.Min(37, tout.Length - i))));
        ok(recus.Count == 3 && recus[2][25] == 2 && recus.All(r => r[0] == 0xFF && r[^1] == 0xD9), "liveviewstream : 3 JPEG retrouvés dans le flux coupé en morceaux", $"{recus.Count}");
        var brut = new FluxLiveview().Pousser(new byte[] { 1, 2, 3 }.Concat(Jpeg(7)).Concat(Jpeg(8)).ToArray());
        ok(brut.Count == 2 && brut[1][25] == 8, "flux de JPEG bruts (sans paquets Sony) : lu aussi", $"{brut.Count}");
        ok(CameraWifi.TailleJpeg(Jpeg(0)) == (1024, 576), "taille du JPEG lue (SOF) : 1024 × 576", null);
        var obj = Liveview(Jpeg(5));
        ok(CameraWifi.ImageLiveviewPtp(obj) == (16, Jpeg(5).Length), "objet live view PTP : position et taille du JPEG", null);
        ok(CameraWifi.ImageLiveviewPtp(new byte[] { 9, 9 }.Concat(Jpeg(1)).ToArray()) == (2, Jpeg(1).Length), "objet live view sans en-tête : JPEG cherché", null);

        // ---- fausse caméra PTP/IP
        {
            var cams = new MyCams();
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var journal = new List<string>();
            using var fin = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serveur = FausseCameraPtp(l, journal, fin.Token);
            var cam = new CameraWifi(cams) { Adresse = "127.0.0.1", Recherche = false, PortPtp = ((IPEndPoint)l.LocalEndpoint).Port, PortsWeb = new[] { PortLibre() } };
            cam.Demarrer();
            var t0 = DateTime.UtcNow;
            while (DateTime.UtcNow - t0 < TimeSpan.FromSeconds(10) && (cams.InfoDe(CameraWifi.Id) is not { EnDirect: true } || cams.InfoDe(CameraWifi.Id)?.Taille == "")) await Task.Delay(50);
            await Task.Delay(400);
            var info = cams.InfoDe(CameraWifi.Id);
            var c = cams.Dernier(CameraWifi.Id);
            ok(info is { EnDirect: true } && c != null && c.Jpeg.Length > 0 && c.Jpeg[0] == 0xFF, "PTP/IP : images reçues dans MyCam", cam.Etat + " | " + string.Join(", ", journal.Take(12)));
            ok(cam.Modele == "ILCE-6100" && cam.Protocole == "PTP/IP", "PTP/IP : modèle lu (" + cam.Modele + ")", null);
            ok(journal.Contains("op 9201 1") && journal.Contains("op 9201 2") && journal.Contains("op 9202 12C") && journal.Contains("op 9201 3"), "PTP/IP : poignée de main Sony (SDIO 1, 2, GetExtDeviceInfo 300, 3)", string.Join(", ", journal.Take(10)));
            ok(info?.Taille == "1024 × 576" && cam.Etat.StartsWith("En direct"), "PTP/IP : taille annoncée « " + info?.Taille + " », état « " + cam.Etat + " »", null);
            cam.Arreter();
            ok(cams.InfoDe(CameraWifi.Id) is { EnDirect: false }, "arrêt : la source ne diffuse plus", null);
            fin.Cancel();
            l.Stop();
            try { await serveur; } catch { }
        }

        // ---- fausse caméra API Web (JSON-RPC + liveviewstream)
        {
            var cams = new MyCams();
            int port = PortLibre();
            var h = new HttpListener();
            h.Prefixes.Add($"http://localhost:{port}/");
            h.Start();
            var methodes = new List<string>();
            using var fin = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serveur = Task.Run(async () =>
            {
                while (!fin.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await h.GetContextAsync(); } catch { break; }
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (ctx.Request.Url!.AbsolutePath == "/sony/camera")
                            {
                                var corps = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
                                var m = System.Text.Json.Nodes.JsonNode.Parse(corps)!["method"]!.GetValue<string>();
                                lock (methodes) methodes.Add(m);
                                var rep = m == "startLiveview" ? $"{{\"result\":[\"http://localhost:{port}/liveview/liveviewstream\"],\"id\":1}}" : "{\"result\":[0],\"id\":1}";
                                var b = Encoding.UTF8.GetBytes(rep);
                                ctx.Response.ContentType = "application/json";
                                await ctx.Response.OutputStream.WriteAsync(b);
                                ctx.Response.Close();
                            }
                            else
                            {
                                ctx.Response.SendChunked = true;
                                for (ushort k = 0; k < 400 && !fin.IsCancellationRequested; k++)
                                {
                                    await ctx.Response.OutputStream.WriteAsync(PaquetLiveview(Jpeg((byte)k), k));
                                    await ctx.Response.OutputStream.FlushAsync();
                                    await Task.Delay(30);
                                }
                                ctx.Response.Close();
                            }
                        }
                        catch { }
                    });
                }
            });
            var cam = new CameraWifi(cams) { Adresse = "localhost", Recherche = false, PortPtp = PortLibre(), PortsWeb = new[] { port } };
            cam.Demarrer();
            var t0 = DateTime.UtcNow;
            while (DateTime.UtcNow - t0 < TimeSpan.FromSeconds(10) && cams.InfoDe(CameraWifi.Id) is not { EnDirect: true }) await Task.Delay(50);
            await Task.Delay(2500);
            var info = cams.InfoDe(CameraWifi.Id);
            ok(info is { EnDirect: true } && cams.Dernier(CameraWifi.Id) != null && cam.Protocole == "API Web Sony", "API Web : images reçues dans MyCam", cam.Etat + " | " + string.Join(",", methodes));
            ok(methodes.Contains("startRecMode") && methodes.Contains("startLiveview"), "API Web : startRecMode puis startLiveview", string.Join(",", methodes));
            ok(info != null && info.FpsRecu >= 12, $"API Web : cadence reçue ({info?.FpsRecu} i/s)", null);
            cam.Arreter();
            fin.Cancel();
            h.Stop();
            try { await serveur; } catch { }
        }
    }
}
