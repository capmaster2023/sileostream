using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MyVerse.Montages;

namespace MyVerse.Ui.Edition;

/// <summary>
/// L'aperçu du montage : les séquences jouées à la suite (lecteur de Windows), les titres, le logo et les génériques posés
/// par-dessus aux mêmes places que dans le rendu (une scène de 1920 × 1080 mise à l'échelle). Les fondus et les sons ajoutés ne
/// sont pas joués dans l'aperçu (le rendu, lui, les fait). Un format que Windows ne lit pas (HEVC sans l'extension…) est montré
/// image par image, tirée par FFmpeg.
/// </summary>
public sealed class ApercuMontage : Border
{
    const double L = 1920, H = 1080;
    readonly MediaElement media = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Stretch = Stretch.Uniform, Width = L, Height = H };
    readonly Image fixe = new() { Stretch = Stretch.Uniform, Width = L, Height = H, Visibility = Visibility.Collapsed };
    readonly Grid generique = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    readonly StackPanel genPile = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Image genLogo = new() { Height = H * 0.26, Margin = new Thickness(0, 0, 0, 40), HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock genTexte = Txt(66), genSous = Txt(38);
    readonly Canvas calque = new() { Width = L, Height = H, IsHitTestVisible = false };
    readonly Image logo = new() { Stretch = Stretch.Uniform };
    readonly TextBlock message = new() { Foreground = Brushes.White, FontSize = 34, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 1300,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
    readonly DispatcherTimer tic = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Stopwatch horloge = new();
    readonly HashSet<string> illisibles = new(StringComparer.OrdinalIgnoreCase);

    ProjetMontage projet = new();
    string? ouvert;
    bool pret;
    double? enAttente;
    bool lecture;
    double tete, departHorloge;
    ElementPiste? courant;
    string logoCharge = "";
    (string Fichier, double Pos)? fixeDemande;
    bool fixeEnCours;

    /// <summary>La tête de lecture avance pendant la lecture.</summary>
    public event Action<double>? Avance;
    /// <summary>La lecture démarre ou s'arrête.</summary>
    public event Action<bool>? LectureChange;
    public bool EnLecture => lecture;
    public Func<string> Ffmpeg { get; set; } = () => "";

    static TextBlock Txt(double taille) => new()
    {
        Foreground = Brushes.White, FontSize = taille, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI"), TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = L * 0.9,
    };

    public ApercuMontage()
    {
        Background = Brushes.Black;
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;
        var scene = new Grid { Width = L, Height = H, Background = Brushes.Black, ClipToBounds = true };
        scene.Children.Add(media);
        scene.Children.Add(fixe);
        genPile.Children.Add(genLogo);
        genPile.Children.Add(genTexte);
        genSous.Margin = new Thickness(0, 24, 0, 0);
        genSous.FontWeight = FontWeights.SemiBold;
        genPile.Children.Add(genSous);
        generique.Children.Add(genPile);
        scene.Children.Add(generique);
        scene.Children.Add(calque);
        scene.Children.Add(message);
        Child = new Viewbox { Stretch = Stretch.Uniform, Child = scene };
        media.MediaOpened += (_, _) =>
        {
            pret = true;
            message.Text = "";
            if (enAttente is { } p) { media.Position = TimeSpan.FromSeconds(p); enAttente = null; }
            if (lecture) media.Play(); else media.Pause();
        };
        media.MediaFailed += (_, e) =>
        {
            Journal.Info("aperçu du montage : " + ouvert + " : " + e.ErrorException?.Message);
            if (ouvert != null) illisibles.Add(ouvert);
            ouvert = null;
            pret = false;
            Arreter();
            Aller(tete);
        };
        media.MediaEnded += (_, _) => { if (lecture && courant != null) Suivant(); };
        tic.Tick += (_, _) => Tic();
        Unloaded += (_, _) => Arreter();
    }

    public void Projet(ProjetMontage p)
    {
        projet = p;
        Aller(Math.Min(tete, p.Duree));
    }

    /// <summary>Montre l'instant <paramref name="t"/> (et continue de jouer si la lecture était lancée).</summary>
    public void Aller(double t)
    {
        tete = Math.Max(0, t);
        courant = projet.ElementA(tete);
        Calque();
        if (courant == null)
        {
            Media(null, 0);
            generique.Visibility = Visibility.Collapsed;
            fixe.Visibility = Visibility.Collapsed;
            message.Text = projet.Sequences.Count == 0 ? "Ajoutez un enregistrement depuis la liste de gauche (double-clic ou « + Vidéo »)." : "";
            if (lecture) Arreter();
            return;
        }
        message.Text = "";
        if (courant.Type == TypeElement.Sequence)
        {
            var s = projet.Sequences[courant.Index];
            generique.Visibility = Visibility.Collapsed;
            media.Volume = s.Muet ? 0 : Math.Min(1, s.Volume * projet.VolumePrincipal);
            Media(s.Source, s.Debut + (tete - courant.Debut));
        }
        else
        {
            Media(null, 0);
            fixe.Visibility = Visibility.Collapsed;
            var g = courant.Type == TypeElement.Intro ? projet.Intro : projet.Outro;
            generique.Visibility = Visibility.Visible;
            genTexte.Text = g.Texte;
            genSous.Text = g.SousTexte;
            genSous.Visibility = g.SousTexte.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool avecLogo = projet.Logo.Chemin.Length > 0 && File.Exists(projet.Logo.Chemin);
            genLogo.Visibility = avecLogo ? Visibility.Visible : Visibility.Collapsed;
            if (avecLogo) genLogo.Source = Image(projet.Logo.Chemin);
            // le fondu au noir du générique
            double dans = tete - courant.Debut, f = Math.Min(0.8, courant.Longueur / 3);
            genPile.Opacity = Math.Clamp(Math.Min(dans / f, (courant.Longueur - dans) / f), 0, 1);
            if (lecture) { departHorloge = tete; horloge.Restart(); }
        }
    }

    void Media(string? fichier, double position)
    {
        if (fichier == null)
        {
            if (ouvert != null) media.Pause();
            media.Visibility = Visibility.Collapsed;
            return;
        }
        if (illisibles.Contains(fichier))
        {
            media.Visibility = Visibility.Collapsed;
            if (lecture) { Arreter(); message.Text = "Ce format ne se lit pas dans l'aperçu de Windows : déplacez la tête de lecture pour voir les images (le rendu, lui, le lit)."; }
            Fixe(fichier, position);
            return;
        }
        fixe.Visibility = Visibility.Collapsed;
        media.Visibility = Visibility.Visible;
        if (!string.Equals(ouvert, fichier, StringComparison.OrdinalIgnoreCase))
        {
            ouvert = fichier;
            pret = false;
            enAttente = position;
            try { media.Source = new Uri(fichier); media.Play(); if (!lecture) media.Pause(); }
            catch (Exception e) { Journal.Erreur("aperçu du montage", e); illisibles.Add(fichier); ouvert = null; Fixe(fichier, position); }
            return;
        }
        if (pret)
        {
            // en lecture, on ne recale que si l'écart est visible (sinon l'image saccade)
            if (!lecture || Math.Abs(media.Position.TotalSeconds - position) > 0.25) media.Position = TimeSpan.FromSeconds(position);
        }
        else enAttente = position;
    }

    /// <summary>Une image tirée par FFmpeg (format que Windows ne lit pas) ; la dernière demande gagne.</summary>
    async void Fixe(string fichier, double position)
    {
        fixe.Visibility = Visibility.Visible;
        fixeDemande = (fichier, position);
        if (fixeEnCours) return;
        fixeEnCours = true;
        try
        {
            while (fixeDemande is { } d)
            {
                fixeDemande = null;
                var octets = await Task.Run(async () =>
                {
                    try
                    {
                        using var p = Process.Start(Processus.Info(Ffmpeg(), new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-ss", d.Pos.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                            "-i", d.Fichier, "-frames:v", "1", "-vf", "scale=960:-2", "-f", "image2pipe", "-c:v", "mjpeg", "pipe:1" }));
                        if (p == null) return null;
                        _ = p.StandardError.ReadToEndAsync();
                        using var ms = new MemoryStream();
                        await p.StandardOutput.BaseStream.CopyToAsync(ms);
                        await p.WaitForExitAsync();
                        return ms.Length > 0 ? ms.ToArray() : null;
                    }
                    catch (Exception) { return null; }
                });
                if (octets == null) continue;
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.StreamSource = new MemoryStream(octets);
                b.EndInit();
                b.Freeze();
                fixe.Source = b;
            }
        }
        finally { fixeEnCours = false; }
    }

    static BitmapImage? Image(string chemin)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.UriSource = new Uri(chemin);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Les titres et le logo de l'instant, aux places du rendu.</summary>
    void Calque()
    {
        calque.Children.Clear();
        var seq = projet.Piste().Where(e => e.Type == TypeElement.Sequence).ToList();
        if (projet.Logo.Visible && seq.Count > 0 && tete >= seq[0].Debut && tete <= seq[^1].Fin)
        {
            if (logoCharge != projet.Logo.Chemin) { logo.Source = Image(projet.Logo.Chemin); logoCharge = projet.Logo.Chemin; }
            double lw = L * projet.Logo.Taille, marge = L * 0.03;
            logo.Width = lw;
            logo.Opacity = projet.Logo.Opacite;
            logo.Measure(new Size(lw, H));
            double lh = logo.DesiredSize.Height;
            Canvas.SetLeft(logo, projet.Logo.Coin is "hg" or "bg" ? marge : L - lw - marge);
            Canvas.SetTop(logo, projet.Logo.Coin is "hg" or "hd" ? marge : H - lh - marge);
            calque.Children.Add(logo);
        }
        foreach (var t in projet.Titres)
        {
            if (tete < t.Debut || tete > t.Debut + t.Duree) continue;
            double f = Math.Min(0.4, t.Duree / 4), alpha = Math.Clamp(Math.Min((tete - t.Debut) / f, (t.Debut + t.Duree - tete) / f), 0, 1);
            bool sous = t.SousTexte.Trim().Length > 0;
            double y1, y2;
            switch (t.Position)
            {
                case TitreMontage.Haut: y1 = H * 0.07; y2 = y1 + t.Taille * 1.4; break;
                case TitreMontage.Centre: y1 = H / 2 - t.Taille * (sous ? 1.0 : 0.5); y2 = H / 2 + t.Taille * 0.45; break;
                default: y1 = H * (sous ? 0.74 : 0.8); y2 = y1 + t.Taille * 1.45; break;
            }
            void Ligne(string texte, double taille, double y)
            {
                if (texte.Trim().Length == 0) return;
                var tb = new TextBlock { Text = texte.Trim(), FontSize = taille, FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI") };
                UIElement e = tb;
                if (t.Position == TitreMontage.Bas)
                    e = new Border { Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), Padding = new Thickness(taille * 0.35), Child = tb, Margin = new Thickness(-taille * 0.35) };
                else tb.Effect = new DropShadowEffect { ShadowDepth = 2, BlurRadius = 2, Opacity = 0.7 };
                e.Opacity = alpha;
                e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(e, t.Position == TitreMontage.Bas ? L * 0.06 : (L - e.DesiredSize.Width) / 2);
                Canvas.SetTop(e, y);
                calque.Children.Add(e);
            }
            Ligne(t.Texte, t.Taille, y1);
            if (sous) Ligne(t.SousTexte, t.Taille * 0.62, y2);
        }
    }

    public void Basculer() { if (lecture) Arreter(); else Lire(); }

    public void Lire()
    {
        if (projet.Duree <= 0) return;
        if (tete >= projet.Duree - 0.05) Aller(0);
        lecture = true;
        if (courant is { Type: TypeElement.Sequence })
        {
            if (ouvert != null && illisibles.Contains(ouvert)) { lecture = false; return; }
            media.Play();
        }
        departHorloge = tete;
        horloge.Restart();
        tic.Start();
        LectureChange?.Invoke(true);
    }

    public void Arreter()
    {
        bool etait = lecture;
        lecture = false;
        tic.Stop();
        horloge.Stop();
        try { if (ouvert != null) media.Pause(); } catch (Exception) { }
        if (etait) LectureChange?.Invoke(false);
    }

    void Suivant()
    {
        if (courant == null) { Arreter(); return; }
        double t = courant.Fin + 0.001;
        if (t >= projet.Duree) { Aller(projet.Duree); Arreter(); Avance?.Invoke(tete); return; }
        Aller(t);
        if (courant is { Type: TypeElement.Sequence }) media.Play();
        departHorloge = tete;
        horloge.Restart();
        Avance?.Invoke(tete);
    }

    void Tic()
    {
        if (!lecture || courant == null) { Arreter(); return; }
        if (courant.Type == TypeElement.Sequence)
        {
            if (!pret) return;
            var s = projet.Sequences[courant.Index];
            double pos = media.Position.TotalSeconds;
            tete = courant.Debut + (pos - s.Debut);
            if (pos >= s.Fin - 0.03) { Suivant(); return; }
        }
        else
        {
            tete = departHorloge + horloge.Elapsed.TotalSeconds;
            if (tete >= courant.Fin) { Suivant(); return; }
            double dans = tete - courant.Debut, f = Math.Min(0.8, courant.Longueur / 3);
            genPile.Opacity = Math.Clamp(Math.Min(dans / f, (courant.Longueur - dans) / f), 0, 1);
        }
        Calque();
        Avance?.Invoke(tete);
    }

    /// <summary>Libère le fichier ouvert (avant un rendu ou en quittant la page).</summary>
    public void Fermer()
    {
        Arreter();
        try { media.Close(); } catch (Exception) { }
        media.Source = null;
        ouvert = null;
        pret = false;
    }
}
