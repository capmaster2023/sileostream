using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyVerse.Audio;

namespace MyVerse.Tests;

/// <summary>Real native WAV and FFmpeg queue/transport tests; no simulated encoder.</summary>
public static class RecordingIntegrationTests
{
    /// <summary>
    /// Une composition d'essai : une couleur fixe, ou une couleur selon l'instant du monde visible à l'écran. Avec un retard
    /// (une caméra MyCam), l'écran montre à l'instant t le monde de t − Retard, comme une vraie caméra.
    /// </summary>
    sealed class Frames : IRecordingFrameProvider
    {
        readonly object gate = new();
        byte color = 25;
        Func<long, byte>? selon;
        public long Retard { get; set; }
        public void Set(byte value) { lock (gate) { color = value; selon = null; } }
        public void Selon(Func<long, byte> f) { lock (gate) selon = f; }
        public void CopyFrame(byte[] destination, long monde)
        {
            lock (gate)
            {
                byte c = selon != null ? selon(Horloge.Ns() - Retard) : color;
                for (int i = 0; i < destination.Length; i += 4) { destination[i] = c; destination[i + 1] = c; destination[i + 2] = c; destination[i + 3] = 255; }
            }
        }
    }

    /// <summary>
    /// Le son comme OBS le donne : des blocs de 10 ms horodatés à l'instant de leur capture (t0 + n × 10 ms), livrés en temps réel
    /// avec le retard du mélange d'OBS (<paramref name="retardSonMs"/>). Clap : son fort des blocs 50 à 59 (0,5 à 0,6 s du monde).
    /// </summary>
    static async Task AlimenterAsync(RecordingSession session, long t0, int retardSonMs, int blocs = 150, int clap = 50)
    {
        var block = new float[AudioBlock.Samples];
        for (int n = 0; n < blocs; n++)
        {
            long quand = t0 + n * 10 * Horloge.Milliseconde + retardSonMs * Horloge.Milliseconde;
            while (Horloge.Ns() < quand) await Task.Delay(1);
            FillAudio(block, n, clap);
            if (!session.Accept(block, t0 + n * 10 * Horloge.Milliseconde)) throw new IOException(session.Error ?? "bloc refusé");
        }
    }
    /// <summary>L'image du monde : claire pendant 0,1 s à partir de <paramref name="debutMs"/> après t0 (le clap), sombre sinon.</summary>
    static Func<long, byte> Clap(long t0, int debutMs = 500) => monde => monde - t0 >= debutMs * Horloge.Milliseconde && monde - t0 < (debutMs + 100) * Horloge.Milliseconde ? (byte)240 : (byte)25;
    static async Task<string> ProcessAsync(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); } catch { p.Kill(true); throw; }
        if (p.ExitCode != 0) throw new IOException(await error); return await output;
    }
    /// <summary>Journal (stderr) de FFmpeg, où silencedetect et metadata écrivent leurs mesures.</summary>
    static async Task<string> JournalAsync(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); } catch { p.Kill(true); throw; }
        await output; return await error;
    }
    /// <summary>Le moment (secondes) où le son devient fort et celui où l'image devient claire : le « clap » des blocs 50 à 59.</summary>
    static async Task<(double? Son, double? Image)> ClapAsync(string ffmpeg, string path)
    {
        string a = await JournalAsync(ffmpeg, new[] { "-hide_banner", "-nostats", "-i", path, "-map", "0:a:0", "-af", "silencedetect=n=-20dB:d=0.04", "-f", "null", "-" });
        var fin = Regex.Match(a, @"silence_end:\s*([0-9.]+)");
        string v = await JournalAsync(ffmpeg, new[] { "-hide_banner", "-nostats", "-i", path, "-map", "0:v:0", "-vf", "signalstats,metadata=mode=print:key=lavfi.signalstats.YAVG", "-f", "null", "-" });
        double? image = null; double t = 0;
        foreach (var ligne in v.Split('\n'))
        {
            var mt = Regex.Match(ligne, @"pts_time:([0-9.]+)");
            if (mt.Success) t = double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
            var my = Regex.Match(ligne, @"YAVG=([0-9.]+)");
            if (my.Success && double.Parse(my.Groups[1].Value, CultureInfo.InvariantCulture) > 128) { image = t; break; }
        }
        return (fin.Success ? double.Parse(fin.Groups[1].Value, CultureInfo.InvariantCulture) : null, image);
    }
    /// <summary>
    /// Siloé Stream 1.2 : une vraie session vidéo où le son arrive en retard (mélange d'OBS) et la caméra aussi (MyCam), puis la mesure du clap :
    /// la synchro est automatique, sans réglage.
    /// </summary>
    static async Task<(double? Son, double? Image)> SessionClapAsync(string ffmpeg, string directory, int retardSonMs, int retardCameraMs)
    {
        var frame = new Frames { Retard = retardCameraMs * Horloge.Milliseconde };
        var profile = new RecordingProfile { Name = $"synchro-son{retardSonMs}-cam{retardCameraMs}", Format = "mkv", AudioOnly = false, Directory = directory, Width = 320, Height = 180, Fps = 30, Encoder = "mjpeg" };
        string path = RecordingFiles.ReservePath(directory, profile.Name, "mkv");
        using (var session = new RecordingSession(profile, path, ffmpeg, "mjpeg", frame))
        {
            // le clap à 1 s : le début du fichier (le temps que le premier son arrive) n'a pas d'image du passé
            long t0 = Horloge.Ns();
            frame.Selon(Clap(t0, 1000));
            await session.PrepareAsync();
            await AlimenterAsync(session, t0, retardSonMs, 200, 100);
            await session.StopAsync();
        }
        return await ClapAsync(ffmpeg, path);
    }
    /// <summary>Une vraie session x264 (encodeur avec retard : il garde des images avant de sortir la première).
    /// La vidéo doit suivre le son reçu, sinon ffmpeg et Siloé Stream s'attendent l'un l'autre et le fichier reste vide.</summary>
    static async Task<string> SessionX264Async(string ffmpeg, string directory, Frames frame)
    {
        var profile = new RecordingProfile { Name = "x264", Format = "mkv", AudioOnly = false, Directory = directory, Width = 640, Height = 360, Fps = 30, Encoder = "libx264" };
        string path = RecordingFiles.ReservePath(directory, profile.Name, "mkv");
        var block = new float[AudioBlock.Samples];
        using var session = new RecordingSession(profile, path, ffmpeg, "libx264", frame);
        await session.PrepareAsync();
        var debut = Stopwatch.StartNew();
        for (int n = 0; n < 300; n++)
        {
            frame.Set(n % 60 < 30 ? (byte)40 : (byte)200);
            FillAudio(block, n);
            if (!session.Accept(block)) throw new IOException(session.Error ?? "bloc refusé");
            // au rythme réel : un bloc de 10 ms toutes les 10 ms
            int attente = (int)(n * 10 - debut.ElapsedMilliseconds);
            if (attente > 0) await Task.Delay(attente);
        }
        await session.StopAsync();
        return path;
    }
    /// <summary>
    /// Siloé Stream 1.6.0 : un vrai direct (FLV vers RTMP, comme vers YouTube) reçu par un serveur local (ffmpeg lui-même, en écoute),
    /// puis relu : H.264 et AAC 48 kHz stéréo, une image clé toutes les 2 s, la bonne durée.
    /// </summary>
    static async Task<(bool Ok, string Detail)> SessionDirectAsync(string ffmpeg, string directory, Frames frame)
    {
        var libre = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); libre.Start();
        int port = ((System.Net.IPEndPoint)libre.LocalEndpoint).Port; libre.Stop();
        string recu = Path.Combine(directory, "direct-recu.flv"), adresse = $"rtmp://127.0.0.1:{port}/live2/cle-de-test";
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "warning", "-y", "-listen", "1", "-i", adresse, "-c", "copy", "-f", "flv", recu }) info.ArgumentList.Add(a);
        using var serveur = Process.Start(info)!;
        var journalServeur = serveur.StandardError.ReadToEndAsync();
        try
        {
            await Task.Delay(1200);
            var profile = new RecordingProfile { Name = "direct", Format = RecordingProfile.FormatDirect, AudioOnly = false, Directory = "", Width = 640, Height = 360, Fps = 30, VideoKbps = 1500, AudioKbps = 128, Encoder = "libx264" };
            var args = RecordingSession.FfmpegArguments(profile, "son", "image", "libx264", adresse).ToList();
            bool arguments = args.Contains("flv") && args[args.IndexOf("-g") + 1] == "60" && args[args.IndexOf("-maxrate") + 1] == "1500k" && args.Last() == adresse;
            var block = new float[AudioBlock.Samples];
            string? erreur;
            using (var session = new RecordingSession(profile, adresse, ffmpeg, "libx264", frame))
            {
                await session.PrepareAsync();
                var debut = Stopwatch.StartNew();
                for (int n = 0; n < 300; n++)
                {
                    frame.Set(n % 60 < 30 ? (byte)40 : (byte)200);
                    FillAudio(block, n);
                    if (!session.Accept(block)) throw new IOException(session.Error ?? "bloc refusé");
                    int attente = (int)(n * 10 - debut.ElapsedMilliseconds);
                    if (attente > 0) await Task.Delay(attente);
                }
                await session.StopAsync();
                erreur = session.Error;
            }
            await serveur.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            string json = await ProcessAsync(ffprobe, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", recu });
            using var doc = JsonDocument.Parse(json);
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
            bool video = streams.Any(x => x.GetProperty("codec_type").GetString() == "video" && x.GetProperty("codec_name").GetString() == "h264" && x.GetProperty("width").GetInt32() == 640);
            bool son = streams.Any(x => x.GetProperty("codec_type").GetString() == "audio" && x.GetProperty("codec_name").GetString() == "aac" && x.GetProperty("sample_rate").GetString() == "48000" && x.GetProperty("channels").GetInt32() == 2);
            double duree = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            // images clés : au moins une toutes les 2 s (YouTube l'exige)
            string cles = await ProcessAsync(ffprobe, new[] { "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey", "-show_entries", "frame=pts_time", "-of", "csv=p=0", recu });
            var temps = cles.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => double.Parse(t.Trim().TrimEnd(','), CultureInfo.InvariantCulture)).ToList();
            bool gop = temps.Count >= 2 && temps.Zip(temps.Skip(1), (x, y) => y - x).All(e => e <= 2.05);
            bool bon = arguments && erreur == null && video && son && Math.Abs(duree - 3) < 0.6 && gop;
            return (bon, $"arguments {arguments}, erreur {erreur ?? "aucune"}, vidéo {video}, son {son}, durée {duree:0.00} s, images clés {string.Join(" ", temps.Select(t => t.ToString("0.0", CultureInfo.InvariantCulture)))}");
        }
        finally
        {
            try { if (!serveur.HasExited) serveur.Kill(true); } catch (Exception) { }
            await journalServeur;
        }
    }
    static void FillAudio(float[] block, int n, int clap = 50)
    {
        for (int i = 0; i < block.Length; i += 2)
        {
            double amplitude = n >= clap && n < clap + 10 ? .8 : .02;
            block[i] = block[i + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * 1000 * (n * 480 + i / 2) / 48000));
        }
    }
    public static async Task RunAsync(Action<bool, string, string?> ok)
    {
        string? executable = Environment.GetEnvironmentVariable("SILOE_FFMPEG");
        string directory = Environment.GetEnvironmentVariable("SILOE_AUDIO_FIXTURES") ?? Path.Combine(Path.GetTempPath(), "siloestream-recording-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var frame = new Frames(); var block = new float[AudioBlock.Samples];
        // Siloé Stream 1.3 : le son seul s'enregistre en MP3 (léger, à partager) ou en WAV (studio) ; M4A si l'encodeur MP3 manquait ; FLAC encore lu
        var formats = executable == null ? new[] { "wav" } : new[] { "wav", "mp3", "m4a", "flac", "mkv" };
        foreach (string format in formats)
        {
            var profile = new RecordingProfile { Name = "integration-" + format, Format = format, AudioOnly = format != "mkv", Directory = directory, Width = 320, Height = 180, Fps = 30, Encoder = "mjpeg" };
            string path = RecordingFiles.ReservePath(directory, profile.Name, format);
            // vraie session : tubes nommés sous Windows, prises Unix ailleurs ; ffmpeg lit en direct, comme dans Siloé Stream
            using (var session = new RecordingSession(profile, path, executable ?? "", "mjpeg", format == "mkv" ? frame : null))
            {
                long t0 = Horloge.Ns();
                frame.Selon(Clap(t0));
                await session.PrepareAsync();
                if (format == "mkv") await AlimenterAsync(session, t0, 0);
                else
                    for (int n = 0; n < 150; n++)
                    {
                        FillAudio(block, n);
                        if (!session.Accept(block)) throw new IOException(session.Error);
                        if (format != "wav") await Task.Delay(10);
                    }
                await session.StopAsync();
                ok(session.Error == null && new FileInfo(path).Length > 1000, "Session réelle : " + format.ToUpperInvariant() + " finalisé après 150 blocs", null);
                ok(session.AcceptedFrames == 72000, "Session : 72 000 trames acceptées (" + format + ")", null);
                ok(!session.Accept(block), "Session arrêtée refuse les nouveaux blocs (" + format + ")", null);
            }
            if (executable != null)
            {
                string ffprobe = Path.Combine(Path.GetDirectoryName(executable)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
                string json = await ProcessAsync(ffprobe, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path });
                File.WriteAllText(path + ".ffprobe.json", json);
                using var doc = JsonDocument.Parse(json); var streams = doc.RootElement.GetProperty("streams");
                var audio = streams.EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "audio");
                ok(audio.GetProperty("sample_rate").GetString() == "48000" && audio.GetProperty("channels").GetInt32() == 2, "FFprobe : stéréo 48 kHz (" + format + ")", null);
                double duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                ok(Math.Abs(duration - 1.5) < .12, "FFprobe : durée du fichier (" + format + ")", duration.ToString());
                if (format == "mkv") ok(streams.EnumerateArray().Any(s => s.TryGetProperty("width", out var width) && width.GetInt32() == 320 && s.GetProperty("height").GetInt32() == 180), "FFprobe : vidéo 320 × 180 réelle", null);
                if (format == "mkv")
                {
                    // synchro : le son fort et l'image claire arrivent ensemble (blocs 50 à 59)
                    var (son, image) = await ClapAsync(executable, path);
                    ok(son != null && image != null && Math.Abs(son.Value - image.Value) < 0.05, "Synchro image/son mesurée dans le MKV", $"son {son:0.000} s · image {image:0.000} s");
                    // Siloé Stream 1.2 : synchro automatique, le son en retard (mélange d'OBS) et la caméra en retard (MyCam)
                    foreach (var (retardSon, retardCamera) in new[] { (150, 0), (150, 100), (0, 250), (300, 120), (500, 0) })
                    {
                        var (s2, i2) = await SessionClapAsync(executable, directory, retardSon, retardCamera);
                        ok(s2 != null && i2 != null && Math.Abs(s2.Value - i2.Value) < 0.05,
                            $"Synchro automatique : son en retard de {retardSon} ms, caméra de {retardCamera} ms, la voix tombe sur l'image", $"son {s2:0.000} s · image {i2:0.000} s");
                    }
                }
                await ProcessAsync(executable, new[] { "-v", "error", "-i", path, "-f", "null", "-" });
                ok(true, "Décodage complet FFmpeg sans erreur (" + format + ")", null);
            }
        }
        if (executable != null)
        {
            // 1.6.0 : un vrai direct RTMP, reçu par un serveur local
            try
            {
                var (bon, detail) = await SessionDirectAsync(executable, directory, frame);
                ok(bon, "Direct réel : FLV vers un serveur RTMP local (H.264, AAC 48 kHz, image clé toutes les 2 s)", detail);
            }
            catch (Exception e) { ok(false, "Direct réel : FLV vers un serveur RTMP local", e.Message); }
        }
        if (executable != null)
        {
            // l'encodeur des vraies vidéos (x264, comme les encodeurs des cartes graphiques) retient des images au début
            string chemin = "";
            try { chemin = await SessionX264Async(executable, directory, frame); }
            catch (Exception e) { ok(false, "Session x264 : vidéo réelle avec un encodeur qui retient des images", e.Message); }
            if (chemin.Length > 0)
            {
                string json = await ProcessAsync(Path.Combine(Path.GetDirectoryName(executable)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe"), new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", chemin });
                using var doc = JsonDocument.Parse(json);
                var flux = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
                double duree = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                ok(flux.Any(x => x.GetProperty("codec_name").GetString() == "h264") && flux.Any(x => x.GetProperty("codec_type").GetString() == "audio") && Math.Abs(duree - 3) < .2,
                    "Session x264 : vidéo H.264 + son de 3 s, finalisée sans blocage", $"{new FileInfo(chemin).Length} octets, {duree:0.00} s");
            }
        }
        if (executable == null) Console.WriteLine("  FFmpeg non configuré : tests MP3/FLAC/MKV facultatifs ignorés (SILOE_FFMPEG).");
        if (Environment.GetEnvironmentVariable("SILOE_AUDIO_FIXTURES") == null) Directory.Delete(directory, true);
    }
}
