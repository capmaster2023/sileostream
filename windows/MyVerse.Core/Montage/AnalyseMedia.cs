using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyVerse.Montages;

/// <summary>Ce que FFmpeg dit d'un fichier : durée, image (codec, taille, images par seconde), son.</summary>
public sealed record InfoMedia(string Chemin, double Duree, bool Video, string CodecVideo, int Largeur, int Hauteur, double Ips, string FormatPixels,
    bool Audio, string CodecAudio, int Frequence, int Canaux, string Conteneur, long Taille, double DebutFlux = 0)
{
    /// <summary>Les images par seconde en fraction exacte (29,97 → 30000/1001).</summary>
    public (int Num, int Den) IpsExactes => AnalyseMedia.Rationnel(Ips);

    /// <summary>« 3840 × 2160 · 30 i/s · H.264 · AAC · 1:23:45 ».</summary>
    public string Resume
    {
        get
        {
            var l = new List<string>();
            if (Video) { l.Add($"{Largeur} × {Hauteur}"); if (Ips > 0) l.Add(Ips.ToString("0.##", CultureInfo.InvariantCulture) + " i/s"); l.Add(AnalyseMedia.NomCodec(CodecVideo)); }
            if (Audio) l.Add(AnalyseMedia.NomCodec(CodecAudio));
            if (Duree > 0) l.Add(ProjetMontage.Temps(Duree, false));
            return string.Join(" · ", l);
        }
    }
}

/// <summary>Lit les informations d'un fichier avec le ffmpeg livré (pas besoin de ffprobe).</summary>
public static class AnalyseMedia
{
    static readonly Dictionary<string, (DateTime Date, long Taille, InfoMedia Info)> cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] ExtensionsVideo = { ".mkv", ".mp4", ".mov", ".m4v", ".avi", ".ts", ".mts", ".m2ts", ".webm", ".flv", ".wmv", ".mpg", ".mpeg" };
    public static readonly string[] ExtensionsAudio = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma" };
    public static bool EstMedia(string chemin) => ExtensionsVideo.Contains(Path.GetExtension(chemin).ToLowerInvariant()) || ExtensionsAudio.Contains(Path.GetExtension(chemin).ToLowerInvariant());

    /// <summary>Les informations du fichier (gardées tant qu'il ne change pas).</summary>
    public static async Task<InfoMedia> LireAsync(string ffmpeg, string fichier, CancellationToken annuler = default)
    {
        var fi = new FileInfo(fichier);
        if (!fi.Exists) throw new FileNotFoundException($"« {Path.GetFileName(fichier)} » est introuvable (déplacé ou supprimé ?).", fichier);
        lock (cache) if (cache.TryGetValue(fichier, out var c) && c.Date == fi.LastWriteTimeUtc && c.Taille == fi.Length) return c.Info;
        var (_, journal, _) = await Processus.ExecuterAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", fichier }, TimeSpan.FromSeconds(60), annuler);
        var info = Interpreter(fichier, journal, fi.Length) ?? throw new InvalidDataException($"« {Path.GetFileName(fichier)} » n'est pas une vidéo ou un son lisible.");
        // une durée inconnue (enregistrement coupé avant la fin) : FFmpeg lit tout le fichier sans le décoder (rapide)
        if (info.Duree <= 0 && (info.Video || info.Audio))
        {
            var (_, sortie, _) = await Processus.ExecuterAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-i", fichier, "-map", "0", "-c", "copy", "-f", "null", "-progress", "pipe:2", "-" },
                TimeSpan.FromMinutes(30), annuler);
            double d = 0;
            foreach (Match m in Regex.Matches(sortie, @"out_time_us=(\d+)")) d = Math.Max(d, long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1e6);
            info = info with { Duree = d };
        }
        lock (cache) cache[fichier] = (fi.LastWriteTimeUtc, fi.Length, info);
        return info;
    }

    static readonly Regex DureeRx = new(@"Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);
    static readonly Regex DebutRx = new(@"Duration: [^,]+, start: (-?[\d.]+)", RegexOptions.Compiled);
    static readonly Regex EntreeRx =new(@"Input #0, (.+?), from '", RegexOptions.Compiled);
    static readonly Regex FluxRx = new(@"^\s*Stream #0:\d+(?:\[0x[0-9a-fA-F]+\])?(?:\([^)]*\))?: (Video|Audio): (\w+)(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
    static readonly Regex TailleRx = new(@", (\d{2,5})x(\d{2,5})[\s,\[]", RegexOptions.Compiled);
    static readonly Regex IpsRx = new(@", ([\d.]+) fps", RegexOptions.Compiled);
    static readonly Regex TbrRx = new(@", ([\d.]+k?) tbr", RegexOptions.Compiled);
    static readonly Regex PixRx = new(@", (yuvj?4\d\dp\w*|nv12|nv21|p010\w*|rgb\w*|bgr\w*|gray\w*|yuva\w*)[,( ]", RegexOptions.Compiled);
    static readonly Regex HzRx = new(@", (\d+) Hz", RegexOptions.Compiled);

    /// <summary>Lit le journal de « ffmpeg -i fichier » ; null s'il n'y a ni image ni son.</summary>
    public static InfoMedia? Interpreter(string chemin, string journal, long taille = 0)
    {
        double duree = 0;
        if (DureeRx.Match(journal) is { Success: true } d)
            duree = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + int.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                + double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture);
        double debutFlux = DebutRx.Match(journal) is { Success: true } st && double.TryParse(st.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv) ? sv : 0;
        string conteneur = EntreeRx.Match(journal) is { Success: true } e ? e.Groups[1].Value : "";
        bool video = false, audio = false;
        string cv = "", ca = "", pix = "";
        int l = 0, h = 0, hz = 0, canaux = 0;
        double ips = 0;
        foreach (Match m in FluxRx.Matches(journal))
        {
            string reste = m.Groups[3].Value;
            if (m.Groups[1].Value == "Video" && !video)
            {
                // une pochette (MP3, M4A) n'est pas une vidéo
                if (reste.Contains("(attached pic)") || m.Groups[2].Value is "png" or "mjpeg" && reste.Contains("attached")) continue;
                video = true;
                cv = m.Groups[2].Value;
                if (TailleRx.Match(reste + " ") is { Success: true } t) { l = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture); h = int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture); }
                if (IpsRx.Match(reste) is { Success: true } f) double.TryParse(f.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out ips);
                else if (TbrRx.Match(reste) is { Success: true } tb && !tb.Groups[1].Value.EndsWith('k')) double.TryParse(tb.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out ips);
                if (PixRx.Match(reste + " ") is { Success: true } p) pix = p.Groups[1].Value;
            }
            else if (m.Groups[1].Value == "Audio" && !audio)
            {
                audio = true;
                ca = m.Groups[2].Value;
                if (HzRx.Match(reste) is { Success: true } z) hz = int.Parse(z.Groups[1].Value, CultureInfo.InvariantCulture);
                canaux = reste.Contains(" mono") ? 1 : reste.Contains("stereo") ? 2 : reste.Contains("5.1") ? 6 : reste.Contains("7.1") ? 8 : 2;
            }
        }
        if (!video && !audio) return null;
        // une image fixe (PNG, JPEG) n'a pas de durée : ce n'est pas une séquence
        if (video && !audio && duree <= 0.05 && cv is "png" or "mjpeg" or "bmp" or "webp" or "gif") return null;
        return new InfoMedia(chemin, duree, video, cv, l, h, ips > 0 && ips < 400 ? ips : (video ? 30 : 0), pix, audio, ca, hz, canaux, conteneur, taille, debutFlux);
    }

    /// <summary>Les images par seconde en fraction : 29,97 → 30000/1001, 59,94 → 60000/1001, 23,976 → 24000/1001, sinon l'entier le plus proche.</summary>
    public static (int Num, int Den) Rationnel(double ips)
    {
        if (!(ips > 0)) return (30, 1);
        foreach (int b in new[] { 24, 30, 60, 120 })
            if (Math.Abs(ips - b * 1000.0 / 1001) < 0.02) return (b * 1000, 1001);
        return (Math.Clamp((int)Math.Round(ips), 1, 240), 1);
    }

    public static string NomCodec(string c) => c switch
    {
        "h264" => "H.264", "hevc" => "HEVC (H.265)", "av1" => "AV1", "vp9" => "VP9", "mpeg2video" => "MPEG-2", "prores" => "ProRes",
        "aac" => "AAC", "mp3" => "MP3", "opus" => "Opus", "flac" => "FLAC", "ac3" => "AC-3", "vorbis" => "Vorbis",
        _ when c.StartsWith("pcm_") => "PCM",
        _ => c.ToUpperInvariant(),
    };
}

/// <summary>Lance ffmpeg et attend sa fin (journal d'erreurs gardé, borné).</summary>
public static class Processus
{
    public static ProcessStartInfo Info(string executable, IEnumerable<string> arguments, string? dossier = null, bool entree = false, bool sortie = true)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = sortie, RedirectStandardInput = entree,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (dossier != null) info.WorkingDirectory = dossier;
        foreach (var a in arguments) info.ArgumentList.Add(a);
        return info;
    }

    /// <summary>Exécute et rend (code de sortie, journal d'erreurs, sortie standard en texte).</summary>
    public static async Task<(int Code, string Erreurs, string Sortie)> ExecuterAsync(string executable, IEnumerable<string> arguments, TimeSpan delai, CancellationToken annuler = default, string? dossier = null)
    {
        if (!File.Exists(executable)) throw new IOException("FFmpeg est introuvable (dossier « ffmpeg » de Siloé Stream) : réinstallez Siloé Stream.");
        using var p = Process.Start(Info(executable, arguments, dossier)) ?? throw new IOException("FFmpeg n'a pas démarré.");
        var err = p.StandardError.ReadToEndAsync();
        var outp = p.StandardOutput.ReadToEndAsync();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(annuler);
        limite.CancelAfter(delai);
        try { await p.WaitForExitAsync(limite.Token); }
        catch (OperationCanceledException)
        {
            Tuer(p);
            if (annuler.IsCancellationRequested) throw;
            throw new TimeoutException("FFmpeg ne répond plus.");
        }
        return (p.ExitCode, await err, await outp);
    }

    public static void Tuer(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(true); } catch (Exception) { }
    }

    /// <summary>Les dernières lignes utiles d'un journal de FFmpeg, pour un message.</summary>
    public static string Fin(string journal, int max = 600)
    {
        var lignes = journal.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.Contains('=') || x.Contains("Error") || x.Contains("rror")).ToList();
        var t = string.Join(" ", lignes.TakeLast(4));
        return t.Length > max ? "…" + t[^max..] : t;
    }
}
