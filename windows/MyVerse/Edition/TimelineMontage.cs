using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MyVerse.Montages;

namespace MyVerse.Ui.Edition;

/// <summary>
/// Siloé Stream 2.2.0 : la ligne de temps du montage, dessinée d'un bloc (rapide même avec des centaines de séquences).
/// Pistes : la règle, V1 (génériques et séquences bout à bout), T (titres), A1 à A4 (sons ajoutés).
/// Souris : clic sur la règle = tête de lecture ; clic sur un bloc = le choisir ; glisser un bloc = le déplacer (V1 : changer
/// l'ordre ; T et A : changer l'instant, et la piste pour un son) ; glisser un bord = rogner. L'aimant colle aux bords et à la tête.
/// Molette : défiler ; Ctrl + molette : zoom.
/// </summary>
public sealed class TimelineMontage : FrameworkElement
{
    const double Gauche = 58, HRegle = 26, HVideo = 64, HTitres = 34, HAudio = 36, Ecart = 4, Bord = 7;

    ProjetMontage projet = new();
    public ProjetMontage Projet { get => projet; set { projet = value; InvalidateVisual(); } }

    /// <summary>Pixels par seconde (zoom).</summary>
    public double Echelle { get; private set; } = 4;
    /// <summary>L'instant (secondes) au bord gauche de la zone visible.</summary>
    public double Debut { get; private set; }
    public double Tete { get; private set; }
    public object? Selection { get; private set; }
    public bool Aimant { get; set; } = true;
    readonly ScrollBar barre;

    public event Action<object?>? SelectionChange;
    public event Action<double>? TeteChange;
    /// <summary>Juste avant une modification (pour « Annuler »).</summary>
    public event Action? AvantModification;
    /// <summary>Après une modification à la souris (glisser, rogner).</summary>
    public event Action? Modifie;

    public TimelineMontage(ScrollBar barre)
    {
        this.barre = barre;
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        barre.Scroll += (_, _) => { Debut = barre.Value; InvalidateVisual(); };
        SizeChanged += (_, _) => MajBarre();
        Palette.Change += InvalidateVisual;
    }

    public double HauteurTotale => HRegle + HVideo + Ecart + HTitres + Ecart + ClipAudio.Pistes * (HAudio + Ecart) + 6;

    protected override Size MeasureOverride(Size dispo) => new(double.IsInfinity(dispo.Width) ? 800 : dispo.Width, HauteurTotale);

    double Largeur => Math.Max(10, ActualWidth - Gauche);
    double X(double t) => Gauche + (t - Debut) * Echelle;
    double T(double x) => Debut + (x - Gauche) / Echelle;

    double YVideo => HRegle;
    double YTitres => HRegle + HVideo + Ecart;
    double YAudio(int piste) => YTitres + HTitres + Ecart + (piste - 1) * (HAudio + Ecart);

    double DureeAffichable => Math.Max(projet.Duree, projet.Audios.Select(a => a.Position + a.Longueur).DefaultIfEmpty(0).Max()) + 30;

    void MajBarre()
    {
        double visible = Largeur / Echelle;
        barre.Minimum = 0;
        barre.Maximum = Math.Max(0, DureeAffichable - visible);
        barre.ViewportSize = visible;
        barre.LargeChange = visible * 0.8;
        barre.SmallChange = visible * 0.05;
        if (Debut > barre.Maximum) Debut = barre.Maximum;
        barre.Value = Debut;
    }

    /// <summary>Tout le montage visible.</summary>
    public void Ajuster()
    {
        Echelle = Math.Clamp(Largeur / Math.Max(10, DureeAffichable - 25), 0.005, 400);
        Debut = 0;
        MajBarre();
        InvalidateVisual();
    }

    /// <summary>Zoom autour de l'instant <paramref name="autour"/> (facteur > 1 : plus près).</summary>
    public void Zoomer(double facteur, double? autour = null)
    {
        double t = autour ?? Tete;
        double x = X(t);
        Echelle = Math.Clamp(Echelle * facteur, 0.005, 400);
        Debut = Math.Max(0, t - (x - Gauche) / Echelle);
        MajBarre();
        InvalidateVisual();
    }

    public void Rafraichir() { MajBarre(); InvalidateVisual(); }

    public void ChoisirTete(double t, bool suivre = true)
    {
        Tete = Math.Max(0, t);
        if (suivre)
        {
            double visible = Largeur / Echelle;
            if (Tete < Debut || Tete > Debut + visible * 0.95) { Debut = Math.Max(0, Tete - visible * 0.3); MajBarre(); }
        }
        InvalidateVisual();
    }

    public void Choisir(object? o)
    {
        Selection = o;
        InvalidateVisual();
        SelectionChange?.Invoke(o);
    }

    // ================================================================================================ dessin
    static Brush P(string cle) => MainWindow.Pinceau(cle);
    static readonly Typeface Police = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Typeface PoliceGras = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    FormattedText Texte(string s, double taille, Brush b, bool gras = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, gras ? PoliceGras : Police, taille, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double l = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(P("Page"), null, new Rect(0, 0, l, h));
        // les pistes
        var fondPiste = P("Nuit2");
        dc.DrawRectangle(fondPiste, null, new Rect(Gauche, YVideo, l, HVideo));
        dc.DrawRectangle(fondPiste, null, new Rect(Gauche, YTitres, l, HTitres));
        for (int a = 1; a <= ClipAudio.Pistes; a++) dc.DrawRectangle(fondPiste, null, new Rect(Gauche, YAudio(a), l, HAudio));
        // les noms des pistes
        dc.DrawRectangle(P("Rail"), null, new Rect(0, 0, Gauche, h));
        void Nom(string n, double y, double hh)
        {
            var t = Texte(n, 11.5, P("Brume"), true);
            dc.DrawText(t, new Point((Gauche - t.Width) / 2, y + (hh - t.Height) / 2));
        }
        Nom("V1", YVideo, HVideo);
        Nom("T", YTitres, HTitres);
        for (int a = 1; a <= ClipAudio.Pistes; a++) Nom("A" + a, YAudio(a), HAudio);

        dc.PushClip(new RectangleGeometry(new Rect(Gauche, 0, Math.Max(0, l - Gauche), h)));
        Regle(dc, l);
        // V1
        foreach (var e in projet.Piste())
        {
            object cle = e.Type switch { TypeElement.Intro => (object)projet.Intro, TypeElement.Outro => projet.Outro, _ => projet.Sequences[e.Index] };
            string nom = e.Type switch
            {
                TypeElement.Intro => "Générique de début",
                TypeElement.Outro => "Générique de fin",
                _ => projet.Sequences[e.Index].Nom,
            };
            Bloc(dc, cle, e.Debut, e.Longueur, YVideo + 4, HVideo - 8, e.Type == TypeElement.Sequence ? "TypeChant" : "TypeDuo", nom,
                ProjetMontage.Temps(e.Longueur, false) + (e.Type == TypeElement.Sequence && projet.Sequences[e.Index].Muet ? " · muet" : ""));
            if (e.Recouvrement > 0)
            {
                // le fondu : un losange sur le chevauchement
                double x0 = X(e.Debut), x1 = X(e.Debut + e.Recouvrement);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x0, YVideo + HVideo - 6), true, true);
                    c.LineTo(new Point(x1, YVideo + 6), true, false);
                    c.LineTo(new Point(x1, YVideo + HVideo - 6), true, false);
                }
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), new Pen(P("Blanc"), 1), g);
            }
        }
        // titres
        foreach (var t in projet.Titres)
            Bloc(dc, t, t.Debut, t.Duree, YTitres + 3, HTitres - 6, "Or", t.Texte.Length > 0 ? t.Texte : t.SousTexte, null);
        // sons
        foreach (var a in projet.Audios)
            Bloc(dc, a, a.Position, a.Longueur, YAudio(a.Piste) + 3, HAudio - 6, "Vert", a.Nom, a.Muet ? "muet" : (a.Volume != 1 ? $"{a.Volume * 100:0} %" : null));
        // le repère d'insertion pendant qu'on déplace une séquence
        if (glisse is { Mode: ModeGlisse.Ordre } gl && gl.Insertion >= 0)
        {
            double xi = X(InstantInsertion(gl.Insertion));
            dc.DrawRectangle(P("OrVif"), null, new Rect(xi - 2, YVideo, 4, HVideo));
        }
        // la tête de lecture
        double xt = X(Tete);
        var pen = new Pen(P("Direct"), 2);
        dc.DrawLine(pen, new Point(xt, 0), new Point(xt, h));
        var tri = new StreamGeometry();
        using (var c = tri.Open()) { c.BeginFigure(new Point(xt - 7, 0), true, true); c.LineTo(new Point(xt + 7, 0), true, false); c.LineTo(new Point(xt, 10), true, false); }
        dc.DrawGeometry(P("Direct"), null, tri);
        dc.Pop();
    }

    void Regle(DrawingContext dc, double l)
    {
        dc.DrawRectangle(P("Rail"), null, new Rect(Gauche, 0, l, HRegle));
        // un pas lisible : au moins 70 px entre deux graduations
        double[] pas = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
        double p = pas.FirstOrDefault(x => x * Echelle >= 70, 7200);
        double t0 = Math.Floor(Debut / p) * p;
        var trait = new Pen(P("Trait2"), 1);
        for (double t = t0; X(t) < l; t += p)
        {
            double x = X(t);
            if (x < Gauche - 1) continue;
            dc.DrawLine(trait, new Point(x, HRegle - 8), new Point(x, HRegle));
            var txt = Texte(ProjetMontage.Temps(t, p < 1), 10.5, P("Brume"));
            dc.DrawText(txt, new Point(x + 3, 4));
            for (int i = 1; i < 5; i++)
            {
                double xs = X(t + p * i / 5);
                dc.DrawLine(trait, new Point(xs, HRegle - 4), new Point(xs, HRegle));
            }
        }
    }

    void Bloc(DrawingContext dc, object cle, double debut, double longueur, double y, double hh, string couleur, string nom, string? info)
    {
        double x0 = X(debut), x1 = X(debut + longueur);
        if (x1 < Gauche || x0 > ActualWidth) return;
        bool choisi = ReferenceEquals(cle, Selection);
        var r = new Rect(x0, y, Math.Max(2, x1 - x0), hh);
        var b = (SolidColorBrush)P(couleur);
        var fond = new SolidColorBrush(Color.FromArgb(choisi ? (byte)235 : (byte)190, b.Color.R, b.Color.G, b.Color.B));
        dc.DrawRoundedRectangle(fond, new Pen(choisi ? P("OrVif") : new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), choisi ? 2.5 : 1), r, 5, 5);
        if (r.Width > 26)
        {
            dc.PushClip(new RectangleGeometry(new Rect(r.X + 4, r.Y, r.Width - 8, r.Height)));
            double xt = Math.Max(r.X + 7, Gauche + 4);
            var t = Texte(nom, 12, Brushes.White, true);
            dc.DrawText(t, new Point(xt, r.Y + (info != null && hh > 40 ? 6 : (hh - t.Height) / 2)));
            if (info != null && hh > 40) dc.DrawText(Texte(info, 11, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))), new Point(xt, r.Y + 26));
            dc.Pop();
        }
    }

    // ================================================================================================ souris
    enum ModeGlisse { Regle, Deplacer, Ordre, BordGauche, BordDroit }
    sealed class Glisse
    {
        public ModeGlisse Mode;
        public object Cle = null!;
        public Point Depart;
        public double D0, F0, P0;
        public int Piste0;
        public bool Commence;
        public int Insertion = -1;
    }
    Glisse? glisse;

    /// <summary>Ce qu'il y a sous la souris, et si on touche un bord.</summary>
    (object? Cle, int Bord) Sous(Point p)
    {
        if (p.X < Gauche || p.Y < HRegle) return (null, 0);
        int Cote(double x0, double x1) => Math.Abs(p.X - x0) <= Bord ? -1 : Math.Abs(p.X - x1) <= Bord ? 1 : 0;
        if (p.Y >= YVideo && p.Y < YVideo + HVideo)
        {
            // la dernière qui commence avant (pendant un fondu : celle qui arrive)
            foreach (var e in Enumerable.Reverse(projet.Piste()))
                if (p.X >= X(e.Debut) - Bord && p.X <= X(e.Fin) + Bord)
                {
                    object cle = e.Type switch { TypeElement.Intro => projet.Intro, TypeElement.Outro => projet.Outro, _ => projet.Sequences[e.Index] };
                    return (cle, e.Type == TypeElement.Sequence ? Cote(X(e.Debut), X(e.Fin)) : 0);
                }
            return (null, 0);
        }
        if (p.Y >= YTitres && p.Y < YTitres + HTitres)
        {
            foreach (var t in Enumerable.Reverse(projet.Titres))
                if (p.X >= X(t.Debut) - Bord && p.X <= X(t.Debut + t.Duree) + Bord) return (t, Cote(X(t.Debut), X(t.Debut + t.Duree)));
            return (null, 0);
        }
        int piste = PisteAudio(p.Y);
        if (piste > 0)
            foreach (var a in Enumerable.Reverse(projet.Audios))
                if (a.Piste == piste && p.X >= X(a.Position) - Bord && p.X <= X(a.Position + a.Longueur) + Bord) return (a, Cote(X(a.Position), X(a.Position + a.Longueur)));
        return (null, 0);
    }

    int PisteAudio(double y)
    {
        for (int a = 1; a <= ClipAudio.Pistes; a++) if (y >= YAudio(a) && y < YAudio(a) + HAudio) return a;
        return 0;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (p.Y < HRegle && p.X >= Gauche)
        {
            glisse = new Glisse { Mode = ModeGlisse.Regle, Depart = p, Commence = true };
            DeplacerTete(T(p.X));
            CaptureMouse();
            e.Handled = true;
            return;
        }
        var (cle, bord) = Sous(p);
        Choisir(cle);
        if (cle == null)
        {
            // un clic dans le vide d'une piste : la tête de lecture y va
            if (p.X >= Gauche) DeplacerTete(T(p.X));
            return;
        }
        var g = new Glisse { Cle = cle, Depart = p };
        switch (cle)
        {
            case SequenceMontage s:
                g.Mode = bord < 0 ? ModeGlisse.BordGauche : bord > 0 ? ModeGlisse.BordDroit : ModeGlisse.Ordre;
                g.D0 = s.Debut; g.F0 = s.Fin;
                break;
            case TitreMontage t:
                g.Mode = bord < 0 ? ModeGlisse.BordGauche : bord > 0 ? ModeGlisse.BordDroit : ModeGlisse.Deplacer;
                g.D0 = t.Debut; g.F0 = t.Debut + t.Duree;
                break;
            case ClipAudio a:
                g.Mode = bord < 0 ? ModeGlisse.BordGauche : bord > 0 ? ModeGlisse.BordDroit : ModeGlisse.Deplacer;
                g.D0 = a.Debut; g.F0 = a.Fin; g.P0 = a.Position; g.Piste0 = a.Piste;
                break;
            default:
                return;   // un générique ne se déplace pas (il se règle à droite)
        }
        glisse = g;
        CaptureMouse();
        e.Handled = true;
    }

    void DeplacerTete(double t)
    {
        Tete = Math.Clamp(t, 0, Math.Max(0, projet.Duree));
        InvalidateVisual();
        TeteChange?.Invoke(Tete);
    }

    /// <summary>Colle <paramref name="t"/> au bord ou à la tête le plus proche (moins de 8 px).</summary>
    double Coller(double t, object exclu)
    {
        if (!Aimant || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return t;
        var points = new List<double> { Tete, 0 };
        foreach (var e in projet.Piste()) { points.Add(e.Debut); points.Add(e.Fin); }
        foreach (var x in projet.Titres) if (!ReferenceEquals(x, exclu)) { points.Add(x.Debut); points.Add(x.Debut + x.Duree); }
        foreach (var x in projet.Audios) if (!ReferenceEquals(x, exclu)) { points.Add(x.Position); points.Add(x.Position + x.Longueur); }
        double meilleur = t, ecart = 8 / Echelle;
        foreach (var q in points) if (Math.Abs(q - t) < ecart) { ecart = Math.Abs(q - t); meilleur = q; }
        return meilleur;
    }

    double InstantInsertion(int index)
    {
        var piste = projet.Piste().Where(e => e.Type == TypeElement.Sequence).ToList();
        if (piste.Count == 0) return projet.Intro.Actif ? projet.Intro.Duree : 0;
        return index < piste.Count ? piste[index].Debut : piste[^1].Fin;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (glisse == null)
        {
            var (cle, bord) = Sous(p);
            Cursor = bord != 0 ? Cursors.SizeWE : cle is SequenceMontage or TitreMontage or ClipAudio ? Cursors.Hand : p.Y < HRegle ? Cursors.IBeam : Cursors.Arrow;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) { Finir(); return; }
        var g = glisse;
        if (!g.Commence)
        {
            if (Math.Abs(p.X - g.Depart.X) < 4 && Math.Abs(p.Y - g.Depart.Y) < 4) return;
            g.Commence = true;
            if (g.Mode != ModeGlisse.Regle) AvantModification?.Invoke();
        }
        double dt = (p.X - g.Depart.X) / Echelle;
        switch (g.Mode)
        {
            case ModeGlisse.Regle:
                DeplacerTete(T(p.X));
                return;
            case ModeGlisse.Ordre:
            {
                // l'endroit où la séquence irait : avant la première séquence dont le milieu est après la souris
                var seq = projet.Piste().Where(x => x.Type == TypeElement.Sequence).ToList();
                double t = T(p.X);
                int i = seq.FindIndex(x => x.Debut + x.Longueur / 2 > t);
                g.Insertion = i < 0 ? seq.Count : i;
                break;
            }
            case ModeGlisse.BordGauche when g.Cle is SequenceMontage s:
                // la séquence garde sa place (les suivantes se décalent) : son début dans le fichier avance ou recule
                s.Debut = Math.Clamp(g.D0 + dt, 0, s.Fin - 0.1);
                break;
            case ModeGlisse.BordDroit when g.Cle is SequenceMontage s:
            {
                double fin = g.F0 + dt;
                double surLigne = projet.DebutSequence(projet.Sequences.IndexOf(s));
                fin = Coller(surLigne + (fin - s.Debut), s) - surLigne + s.Debut;
                s.Fin = Math.Clamp(fin, s.Debut + 0.1, s.DureeSource > 0 ? s.DureeSource : double.MaxValue);
                break;
            }
            case ModeGlisse.Deplacer when g.Cle is TitreMontage t:
                t.Debut = Math.Max(0, Coller(g.D0 + dt, t));
                break;
            case ModeGlisse.BordGauche when g.Cle is TitreMontage t:
            {
                double d = Math.Clamp(Coller(g.D0 + dt, t), 0, g.F0 - 0.5);
                t.Debut = d; t.Duree = g.F0 - d;
                break;
            }
            case ModeGlisse.BordDroit when g.Cle is TitreMontage t:
                t.Duree = Math.Max(0.5, Coller(g.F0 + dt, t) - t.Debut);
                break;
            case ModeGlisse.Deplacer when g.Cle is ClipAudio a:
            {
                a.Position = Math.Max(0, Coller(g.P0 + dt, a));
                int piste = PisteAudio(p.Y);
                if (piste > 0) a.Piste = piste;
                break;
            }
            case ModeGlisse.BordGauche when g.Cle is ClipAudio a:
            {
                // rogner le début : le son garde sa place, il commence plus tard dans le fichier
                double nouvelle = Math.Clamp(Coller(g.P0 + dt, a), Math.Max(0, g.P0 - g.D0), g.P0 + (g.F0 - g.D0) - 0.1);
                a.Debut = g.D0 + (nouvelle - g.P0);
                a.Position = nouvelle;
                break;
            }
            case ModeGlisse.BordDroit when g.Cle is ClipAudio a:
            {
                double finLigne = Coller(a.Position + (g.F0 - a.Debut) + dt, a);
                a.Fin = Math.Clamp(a.Debut + (finLigne - a.Position), a.Debut + 0.1, a.DureeSource > 0 ? a.DureeSource : double.MaxValue);
                break;
            }
        }
        InvalidateVisual();
        SelectionChange?.Invoke(Selection);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => Finir();

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (glisse != null) Finir();
    }

    void Finir()
    {
        var g = glisse;
        glisse = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (g == null || !g.Commence || g.Mode == ModeGlisse.Regle) { InvalidateVisual(); return; }
        if (g.Mode == ModeGlisse.Ordre && g.Cle is SequenceMontage s && g.Insertion >= 0)
        {
            int de = projet.Sequences.IndexOf(s);
            int vers = g.Insertion > de ? g.Insertion - 1 : g.Insertion;
            if (de >= 0 && vers != de)
            {
                projet.Sequences.RemoveAt(de);
                projet.Sequences.Insert(Math.Clamp(vers, 0, projet.Sequences.Count), s);
            }
        }
        MajBarre();
        InvalidateVisual();
        Modifie?.Invoke();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Zoomer(e.Delta > 0 ? 1.25 : 0.8, T(e.GetPosition(this).X));
        else
        {
            Debut = Math.Clamp(Debut - e.Delta / 120.0 * (Largeur / Echelle) * 0.1, 0, Math.Max(0, barre.Maximum));
            MajBarre();
            InvalidateVisual();
        }
        e.Handled = true;
    }
}
