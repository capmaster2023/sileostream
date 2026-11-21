using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MyVerse.Ui;

/// <summary>
/// La caméra virtuelle de Zoom (filtre DirectShow MyVirtualCam, installé une fois). Siloé Stream 1.6.0 : elle montre MyStream,
/// la même image que le direct YouTube. L'image est composée par <see cref="MyStreamMoteur"/> (sur son propre fil) ; ici, elle est
/// seulement mise à 1280 × 720, en BGR de bas en haut, et écrite dans la mémoire partagée que lit le filtre.
/// Entre deux images, seule l'heure est rafraîchie (le filtre montre du noir si elle a plus de 2 secondes).
/// </summary>
public sealed class MyVirtualCam : IDisposable
{
    public const string Clsid = "{A9D47A18-C751-4BDE-AB6B-A3D13F090501}";
    public const int Width = 1280, Height = 720, Stride = Width * 3, Bytes = Stride * Height;
    readonly MemoryMappedFile fichier;
    readonly MemoryMappedViewAccessor vue;
    readonly Mutex verrou = new(false, @"Local\MyChurch.MyVirtualCam.lock.v1");
    readonly object verrouVue = new();
    readonly byte[] trame = new byte[Bytes];
    readonly OpenCvSharp.Mat redim = new(), bgr = new();
    /// <summary>
    /// 2.5.1 : l'heure est aussi rafraîchie à part, quatre fois par seconde : si MyStream est occupé un instant (une page lourde,
    /// l'ordinateur chargé), Zoom garde la dernière image au lieu de passer au noir.
    /// </summary>
    readonly System.Threading.Timer battement;
    volatile bool dispose;
    int seq;
    /// <summary>La dernière image n'a pas pu être écrite (le filtre lisait) : on la réécrit au prochain battement.</summary>
    bool aRefaire;
    public static bool Installee => Registry.ClassesRoot.OpenSubKey(@"CLSID\" + Clsid + @"\InprocServer32") is { } k && Fermer(k);
    static bool Fermer(RegistryKey k) { k.Dispose(); return true; }

    public MyVirtualCam()
    {
        fichier = MemoryMappedFile.CreateOrOpen(@"Local\MyChurch.MyVirtualCam.frames.v1", 64 + Bytes, MemoryMappedFileAccess.ReadWrite);
        vue = fichier.CreateViewAccessor();
        Battre();
        battement = new System.Threading.Timer(_ => Battre(), null, 250, 250);
    }

    /// <summary>Une image de MyStream (BGRA, de haut en bas, toute taille) : mise à 1280 × 720 et écrite pour le filtre.</summary>
    public void Ecrire(OpenCvSharp.Mat bgra)
    {
        if (dispose || bgra.Empty()) return;
        long t0 = Stopwatch.GetTimestamp();
        lock (verrouVue)
        {
            if (dispose) return;
            var src = bgra;
            if (bgra.Width != Width || bgra.Height != Height)
            {
                OpenCvSharp.Cv2.Resize(bgra, redim, new OpenCvSharp.Size(Width, Height), 0, 0, OpenCvSharp.InterpolationFlags.Area);
                src = redim;
            }
            OpenCvSharp.Cv2.CvtColor(src, bgr, OpenCvSharp.ColorConversionCodes.BGRA2BGR);
            // DirectShow : lignes de bas en haut
            OpenCvSharp.Cv2.Flip(bgr, bgr, OpenCvSharp.FlipMode.X);
            if (bgr.IsContinuous()) Marshal.Copy(bgr.Data, trame, 0, Bytes);
            else for (int y = 0; y < Height; y++) Marshal.Copy(bgr.Ptr(y), trame, y * Stride, Stride);
            aRefaire = !EcrireMemoire(trame, 60);
        }
        Perf.Ajouter("MyVirtualCam : écriture", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    /// <summary>Rien de neuf : l'heure seulement (ou l'image entière si la dernière n'a pas pu être écrite).</summary>
    public void Battre()
    {
        if (dispose || !Monitor.TryEnter(verrouVue)) return;
        try { aRefaire = !EcrireMemoire(aRefaire ? trame : null, aRefaire ? 30 : 0) && aRefaire; }
        finally { Monitor.Exit(verrouVue); }
    }

    /// <summary>Écrit l'en-tête (et l'image s'il y en a une) sous le verrou partagé avec le filtre DirectShow. Faux si le filtre lisait.</summary>
    bool EcrireMemoire(byte[]? trameNeuve, int attenteMs)
    {
        if (dispose) return true;
        bool acquis;
        try { acquis = verrou.WaitOne(attenteMs); } catch (AbandonedMutexException) { acquis = true; }
        if (!acquis) return false;
        try
        {
            vue.Write(0, 0x4D43414D); vue.Write(4, 1);
            vue.Write(8, Environment.TickCount64); vue.Write(16, Width); vue.Write(20, Height); vue.Write(24, Stride);
            if (trameNeuve != null) { vue.WriteArray(64, trameNeuve, 0, Bytes); vue.Write(28, ++seq); }
            else vue.Write(28, seq);
            return true;
        }
        finally { verrou.ReleaseMutex(); }
    }

    public static void Installer(Window owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--virtualcam-install") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception e) { Dialogue.Informer(owner, "Installation interrompue : " + e.Message); }
    }
    /// <summary>1.6.8 : installe MyVirtualCam (Windows demande l'autorisation) et attend la fin. Vrai si elle est installée.</summary>
    public static async Task<bool> InstallerEtAttendre(Window owner)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--virtualcam-install") { UseShellExecute = true, Verb = "runas" });
            if (p != null) await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception e) { Dialogue.Informer(owner, "Installation interrompue : " + e.Message); return false; }
        return Installee;
    }
    public static int Enregistrer(bool installer)
    {
        // Register both architectures; consumers load the matching in-process DirectShow source.
        int resultat = 0;
        foreach (var (exe, dll) in new[] {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "regsvr32.exe"), "MyVirtualCam.dll"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "regsvr32.exe"), "MyVirtualCam32.dll") })
        {
            var path = Path.Combine(AppContext.BaseDirectory, dll);
            if (!File.Exists(path)) { resultat = 2; continue; }
            using var p = Process.Start(new ProcessStartInfo(exe, (installer ? "/s " : "/s /u ") + "\"" + path + "\"") { UseShellExecute = false });
            if (p == null || !p.WaitForExit(30000) || p.ExitCode != 0) resultat = 1;
        }
        return resultat;
    }
    public void Dispose()
    {
        if (dispose) return;
        battement.Dispose();
        lock (verrouVue)
        {
            dispose = true;
            try { vue.Write(8, 0L); } catch (Exception) { }
            vue.Dispose(); fichier.Dispose(); verrou.Dispose();
            redim.Dispose(); bgr.Dispose();
        }
    }
}
