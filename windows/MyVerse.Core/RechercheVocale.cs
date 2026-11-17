using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Siloé Stream 2.2.0 : la recherche vocale (le micro de la barre de recherche). Le texte reconnu (Whisper, sur le PC) devient
/// une recherche : en mode Bible, les références dites à voix haute (« Jean chapitre 3 verset 16 », « Romains 8, 28 à 30 ») d'abord,
/// puis les versets dont les mots ressemblent à ce qui a été entendu (toutes les Bibles installées) ; en mode Chant, les chants
/// dont les paroles contiennent le plus de mots entendus (n'importe où dans le chant, fautes et mots « à l'oreille » tolérés).
/// </summary>
public static class RechercheVocale
{
    public const string Chant = "chant", Bible = "bible";

    static readonly Regex Crochets = new(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|♪+", RegexOptions.Compiled);
    static readonly Regex Espaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Le texte de Whisper sans ses annotations (« [Musique] », « (applaudissements) », « ♪ »).</summary>
    public static string Nettoyer(string? texte)
    {
        var t = Crochets.Replace(texte ?? "", " ");
        t = Espaces.Replace(t, " ").Trim();
        // une hallucination fréquente de Whisper sur du silence ou de la musique
        if (Regex.IsMatch(t, @"^(sous-titr|merci d'avoir regard|thanks for watching|abonnez-vous)", RegexOptions.IgnoreCase)) return "";
        return t;
    }

    static readonly Regex Plage = new(@"(\d+)\s*(?:à|a|au|jusqu'au|jusqu'à|jusqu’à|jusqu’au|-|–|to|through)\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex MotsInutiles = new(@"\b(chapitres?|chap|versets?|chapter|verses?|sura|mstari|umurongo|igice)\b\.?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Jetons = new(@"\d+(?:-\d+)?|[\p{L}][\p{L}'’]*", RegexOptions.Compiled);
    static readonly HashSet<string> Ordinaux = new(StringComparer.OrdinalIgnoreCase)
        { "1", "2", "3", "premier", "première", "premiere", "1er", "1re", "deuxième", "deuxieme", "second", "seconde", "troisième", "troisieme", "i", "ii", "iii", "first", "third" };

    /// <summary>
    /// Les références bibliques dites dans le texte, écrites pour la recherche (« jean 3 16-18 », « 1 jean 1 9 ») : un nom de livre
    /// (avec « premier », « 2 »… devant), un chapitre, peut-être un verset ou une suite de versets.
    /// </summary>
    public static List<string> References(string texte)
    {
        var t = MotsInutiles.Replace(texte, " ");
        t = Plage.Replace(t, "$1-$2");
        var j = Jetons.Matches(t).Select(m => m.Value).ToList();
        static bool Nombre(string s) => char.IsDigit(s[0]);
        var l = new List<string>();
        for (int i = 1; i < j.Count; i++)
        {
            // un chapitre : un nombre juste après un mot
            if (!Nombre(j[i]) || Nombre(j[i - 1])) continue;
            string nums = j[i] + (i + 1 < j.Count && Nombre(j[i + 1]) ? " " + j[i + 1] : "");
            string? trouve = null;
            var simple = j[i - 1] + " " + nums;
            if (i >= 2 && Ordinaux.Contains(j[i - 2]) && Livres.Lire(j[i - 2] + " " + simple) is not null) trouve = j[i - 2] + " " + simple;
            else if (Livres.Lire(simple) is not null) trouve = simple;
            // un livre en deux mots (« Cantique des cantiques 2 ») : les deux derniers mots
            else if (i >= 3 && Livres.Lire(j[i - 3] + " " + j[i - 2] + " " + simple) is not null) trouve = j[i - 3] + " " + j[i - 2] + " " + simple;
            if (trouve != null && !l.Contains(trouve, StringComparer.OrdinalIgnoreCase)) l.Add(trouve);
        }
        return l;
    }

    // les petits mots ne disent rien du chant
    static readonly HashSet<string> Vides = new()
    {
        "les", "des", "une", "est", "que", "qui", "dans", "pour", "avec", "sur", "pas", "par", "mon", "ton", "son", "nos", "vos", "ses", "mes", "tes",
        "ils", "elle", "nous", "vous", "tout", "tous", "mais", "aux", "the", "and", "you", "for", "are", "with", "his", "her", "our", "your", "this", "that",
        "was", "him", "its", "all", "not", "but", "cet", "cette", "ces", "leur", "leurs", "comme", "plus", "car",
    };

    sealed class MotsDuChant { public string Texte = ""; public HashSet<string> Mots = new(); public HashSet<string>? Sons; }
    static readonly ConditionalWeakTable<Chant, MotsDuChant> cache = new();

    static MotsDuChant Mots(Chant c)
    {
        var texte = c.Title + "\n" + string.Join("\n", c.Sections.Select(s => s.Text));
        if (cache.TryGetValue(c, out var m) && m.Texte == texte) return m;
        m = new MotsDuChant { Texte = texte, Mots = Recherche.Mots(texte).ToHashSet() };
        cache.AddOrUpdate(c, m);
        return m;
    }

    /// <summary>Les chants qui contiennent le plus de mots entendus (note de 0 à 1) et la ligne qui en contient le plus.</summary>
    public static List<(Chant C, double Note, string Extrait)> ChantsProches(IEnumerable<Chant> chants, string texte, int limite = 6)
    {
        var q = Recherche.Mots(texte).Where(w => w.Length >= 3 && !Vides.Contains(w)).Distinct().ToList();
        var res = new List<(Chant, double, string)>();
        if (q.Count < 2) return res;
        var sons = q.Select(Recherche.Son).ToList();
        foreach (var c in chants)
        {
            var m = Mots(c);
            if (m.Mots.Count == 0) continue;
            double note = 0;
            for (int i = 0; i < q.Count; i++)
            {
                var w = q[i];
                if (m.Mots.Contains(w)) { note += 1; continue; }
                int tol = w.Length >= 6 ? 2 : 1;
                bool proche = false;
                foreach (var x in m.Mots)
                    if (Math.Abs(x.Length - w.Length) <= tol && Recherche.Distance(w, x, tol) <= tol) { proche = true; break; }
                if (!proche)
                {
                    m.Sons ??= m.Mots.Select(Recherche.Son).ToHashSet();
                    proche = m.Sons.Contains(sons[i]);
                }
                if (proche) note += 0.75;
            }
            double ratio = note / q.Count;
            if (ratio >= 0.45 && note >= 1.5) res.Add((c, ratio, Extrait(c, q)));
        }
        return res.OrderByDescending(x => x.Item2).Take(limite).ToList();
    }

    static string Extrait(Chant c, List<string> q)
    {
        string meilleure = "";
        int max = -1;
        foreach (var ligne in c.Sections.SelectMany(s => s.Text.Split('\n')).Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            int n = Recherche.Mots(ligne).Count(q.Contains);
            if (n > max) { max = n; meilleure = ligne; }
        }
        return meilleure;
    }

    /// <summary>Toutes les Bibles installées (principale d'abord), pour chercher un verset lu dans n'importe quelle langue.</summary>
    static List<Bible> ToutesLesBibles(Regie regie)
    {
        var l = regie.Bibles.Versions();
        foreach (var info in regie.Bibles.Toutes())
            if (l.All(b => b.Id != info.Id) && regie.Bibles.Charger(info.Id) is { } b) l.Add(b);
        return l;
    }

    /// <summary>
    /// Les résultats pour ce qui a été entendu (les plus sûrs d'abord, sans doublon), pour le mode « chant » ou « bible ».
    /// </summary>
    public static List<ResultatCommande> Chercher(Regie regie, string entendu, string mode, string? recueil, int limite = 8)
    {
        var texte = Nettoyer(entendu);
        var res = new List<ResultatCommande>();
        if (texte.Length == 0) return res;
        void Ajouter(IEnumerable<ResultatCommande> l)
        {
            foreach (var r in l)
            {
                if (res.Count >= limite) return;
                if (r.Action != null) continue;
                if (mode == Bible ? r.Ref == null : r.ChantId == null) continue;
                if (res.Any(x => x.Titre == r.Titre && x.ChantId == r.ChantId)) continue;
                res.Add(r);
            }
        }
        if (mode == Bible)
        {
            foreach (var c in References(texte)) Ajouter(regie.Commande(c, recueil, false));
            // les mots d'un verset lu, dans toutes les Bibles (même en anglais, en kirundi…)
            if (Recherche.Mots(texte).Count >= 3)
            {
                try
                {
                    Ajouter(Recherche.Chercher(ToutesLesBibles(regie), texte, limite, out _)
                        .Select(v => new ResultatCommande("BIBLE", Livres.Libelle(v.B, v.C, v.V), v.Texte, Ref: new Ref(v.B, v.C, v.V, v.V))));
                }
                catch (Exception e) { Journal.Erreur("recherche vocale (versets)", e); }
            }
        }
        else
        {
            // un numéro dit (« chant 45 », « wokovu 45 ») : la recherche habituelle le trouve
            if (texte.Any(char.IsDigit)) Ajouter(regie.Commande(texte, recueil, false));
            Ajouter(ChantsProches(regie.Chants.ToArray(), texte).Select(x =>
                new ResultatCommande("CHANT", x.C.Title, (x.C.Author.Length > 0 ? x.C.Author + " · " : "") + "« " + x.Extrait + " »", ChantId: x.C.Id)));
        }
        // la recherche habituelle en dernier (paroles dans l'ordre, titres)
        if (res.Count < limite) Ajouter(regie.Commande(texte, recueil, true));
        return res;
    }
}
