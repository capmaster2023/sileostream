using System.Security.Cryptography;
using System.Diagnostics;
using MyVerse;

// Tests du cœur de MyVerse 4.0 (sans interface) : dotnet run --project MyVerse.Tests -- <dossier Ressources>
var res = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MyVerse", "Ressources");
int echecs = 0, total = 0;
string? groupe = null;
var filtre = args.Length > 1 ? args[1] : null;

void Groupe(string nom) { groupe = nom; Console.WriteLine($"\n== {nom}"); }
bool Actif() => filtre == null || (groupe ?? "").Contains(filtre, StringComparison.OrdinalIgnoreCase);
void Ok(bool cond, string quoi, string? detail = null)
{
    total++;
    if (!cond) echecs++;
    if (!cond || Environment.GetEnvironmentVariable("TOUT") == "1")
        Console.WriteLine($"  {(cond ? "✓" : "✗")} {quoi}{(detail == null || (cond && Environment.GetEnvironmentVariable("TOUT") != "1") ? "" : "  → " + detail)}");
}
void Egal<T>(T obtenu, T attendu, string quoi) => Ok(EqualityComparer<T>.Default.Equals(obtenu, attendu), quoi, $"obtenu « {obtenu} », attendu « {attendu} »");

var sw = Stopwatch.StartNew();
var lsg = Bible.DepuisJson("lsg", File.ReadAllText(Path.Combine(res, "lsg.json")));
var kjv = Bible.DepuisJson("kjv", File.ReadAllText(Path.Combine(res, "kjv.json")));
Console.WriteLine($"chargement LSG + KJV : {sw.ElapsedMilliseconds} ms ({lsg.NombreVersets()} + {kjv.NombreVersets()} versets)");

// ------------------------------------------------------------------ références
Groupe("Livres");
if (Actif())
{
    string L(string s) { var r = Livres.Lire(s); return r is { } x ? Livres.Libelle(x.B, x.C, x.V1, x.V2) : "∅"; }
    Egal(L("jn 3 16"), "Jean 3:16", "jn 3 16");
    Egal(L("Jean 3:16-18"), "Jean 3:16-18", "Jean 3:16-18");
    Egal(L("1 co 13"), "1 Corinthiens 13", "1 co 13");
    Egal(L("ps 23"), "Psaumes 23", "ps 23");
    Egal(L("premier jean 1,9"), "1 Jean 1:9", "premier jean 1,9");
    Egal(L("Rm 8.28"), "Romains 8:28", "Rm 8.28");
    Egal(L("genese 1 1"), "Genèse 1:1", "genese 1 1");
    Egal(L("Gen 1"), "Genèse 1", "Gen 1");
    Egal(L("ap 22 21"), "Apocalypse 22:21", "ap 22 21");
    Egal(L("2 rois 2"), "2 Rois 2", "2 rois 2");
    Egal(L("jude 3"), "Jude 3", "jude 3 (livre d'un seul chapitre)");
    Egal(L("bonjour"), "∅", "texte qui n'est pas une référence");
    Ok(Livres.Suggestions("co").Count > 0, "suggestions « co »");
    Egal(lsg.Texte(42, 3, 16, 16)[..20], "Car Dieu a tant aimé", "texte Jean 3:16");
    Egal(lsg.Borner(new Ref(42, 3, 16, 99)), new Ref(42, 3, 16, 36), "borner Jean 3:16-99");
    Egal(lsg.Borner(new Ref(42, 99, 1, 1)), (Ref?)null, "chapitre inexistant");
}

// ------------------------------------------------------------------ recherche floue
Groupe("Recherche");
if (Actif())
{
    var versions = new List<Bible> { lsg };
    sw.Restart();
    Recherche.Preparer(lsg);
    Console.WriteLine($"  index LSG : {sw.ElapsedMilliseconds} ms");
    void Cas(string q, string attendu, int n = 1)
    {
        sw.Restart();
        var r = Recherche.Chercher(versions, q, 50, out _);
        var ms = sw.ElapsedMilliseconds;
        int pos = r.FindIndex(x => Livres.Libelle(x.B, x.C, x.V) == attendu);
        Ok(pos >= 0 && pos < n, $"« {q} » → {attendu} (rang {pos + 1}/{r.Count}, {ms} ms)",
            string.Join(" | ", r.Take(6).Select(x => Livres.Libelle(x.B, x.C, x.V) + (x.Principale ? "" : " " + x.Version) + " " + x.Note.ToString("0.00"))));
    }
    Cas("charité patiente", "1 Corinthiens 13:4");
    Cas("charite pasiente", "1 Corinthiens 13:4");
    Cas("car dieu a tant aimé le monde", "Jean 3:16");
    Cas("kar dieu a tan aimer le monde", "Jean 3:16");
    Cas("dieu a tellement aimé le monde", "Jean 3:16", 3);
    Cas("jesu pleura", "Jean 11:35");
    Cas("léternel est mon berger", "Psaumes 23:1");
    Cas("le seigneur est mon berger", "Psaumes 23:1", 3);
    Cas("je suis le chemin la verite et la vie", "Jean 14:6");
    Cas("je suis le chemain la veriter et la vi", "Jean 14:6");
    Cas("au commencement etait la parole", "Jean 1:1");
    Cas("o comencement était la parolle", "Jean 1:1");
    Cas("tout concourt au bien de ceux qui aiment dieu", "Romains 8:28");
    Cas("toute choses concoure au bien", "Romains 8:28", 3);
    Cas("je puis tout par celui qui me fortifie", "Philippiens 4:13");
    Cas("je peux tout par celui qui me fortifit", "Philippiens 4:13");
    Cas("lumiere du monde", "Jean 8:12", 5);
    Cas("heureux les pauvres en esprit", "Matthieu 5:3");
    Cas("heureu les povres en esprit", "Matthieu 5:3");
    Cas("le salaire du peche cest la mort", "Romains 6:23");
    Cas("le salère du péché c la mort", "Romains 6:23");
    Cas("batème", "Matthieu 3:7", 40);
    var k = Recherche.Chercher(versions, "keur", 5, out var tk);
    Ok(k.Count > 0 && tk.Contains("coeur"), "« keur » trouve « coeur »", string.Join(",", tk.Take(10)));

    // fausse Bible kirundi
    var kir = new Bible { Id = "kir", Nom = "Bibiliya Yera", Court = "KIR" };
    for (int b = 0; b < 66; b++) kir.Livres.Add(new Bible.Livre { Nom = Livres.Nom(b) });
    void Put(int b, int c, int v, string t)
    {
        var ch = kir.Livres[b].Chapitres;
        while (ch.Count < c) ch.Add(new List<string>());
        while (ch[c - 1].Count < v) ch[c - 1].Add("");
        ch[c - 1][v - 1] = t;
    }
    Put(42, 3, 16, "Kuko Imana yakunze abari mw'isi cane, gushika n'aho yatanze Umwana wayo w'ikinege, kugira ngo uwumwizera wese ntazopfe, ariko aronke ubugingo budashira.");
    Put(42, 3, 17, "Kuko Imana itarungikiye Umwana wayo mw'isi ngo aciré isi urubanza, ariko ni ukugira ngo isi ikizwe na we.");
    Put(18, 23, 1, "Uhoraho ni we mwungere wanje, sinzokena.");
    for (int b = 0; b < 66; b++) for (int i = 1; i <= 30; i++) Put(b, 1, i, $"Amajambo {b} n'ivyanditswe vyose {i} hamwe n'abantu benshi");
    versions = new List<Bible> { lsg, kjv, kir };
    sw.Restart();
    Recherche.Preparer(kjv); Recherche.Preparer(kir);
    Console.WriteLine($"  index KJV + KIR : {sw.ElapsedMilliseconds} ms");
    Cas("for god so loved the world", "Jean 3:16");
    Cas("for god so lovd the wrld", "Jean 3:16");
    Cas("Imana yakunze isi", "Jean 3:16");
    Cas("imana yakunse abari mw isi", "Jean 3:16");
    Cas("mwungere wanje", "Psaumes 23:1");
    Cas("uhoraho ni mwungeri", "Psaumes 23:1");
    Cas("car dieu a tant aimé le monde", "Jean 3:16");
    Cas("charité patiente", "1 Corinthiens 13:4");
    var m = Recherche.Morceaux("L’Éternel est mon berger: je ne manquerai de rien.", new HashSet<string> { "eternel", "berger" });
    Egal(string.Concat(m.Select(x => x.Mot ? "[" + x.T + "]" : x.T)), "L’[Éternel] est mon [berger]: je ne manquerai de rien.", "surlignage");
    sw.Restart();
    for (int i = 0; i < 20; i++) Recherche.Chercher(versions, "dieu amour paix joie patience bonte", 50, out _);
    Console.WriteLine($"  20 recherches de 6 mots dans 3 Bibles : {sw.ElapsedMilliseconds} ms");
    Egal(Recherche.Son("jeunesse"), Recherche.Son("genese"), "son(jeunesse) = son(genèse)");
    Egal(Recherche.Son("keur"), Recherche.Son("coeur"), "son(keur) = son(coeur)");
}

// ------------------------------------------------------------------ import de Bibles
Groupe("Import Bible");
if (Actif())
{
    var zef = """
        <?xml version="1.0" encoding="utf-8"?>
        <XMLBIBLE biblename="Bibiliya Yera">
          <BIBLEBOOK bnumber="43" bname="Yohana">
            <CHAPTER cnumber="3">
              <VERS vnumber="16">Kuko Imana yakunze abari mw'isi cane</VERS>
              <VERS vnumber="17">Kuko Imana itarungikiye Umwana wayo</VERS>
            </CHAPTER>
          </BIBLEBOOK>
          <BIBLEBOOK bnumber="1" bname="Itanguriro">
            <CHAPTER cnumber="1"><VERS vnumber="1">Mu ntango Imana yarema ijuru n'isi.</VERS></CHAPTER>
            <CHAPTER cnumber="2">REMPLIR</CHAPTER>
          </BIBLEBOOK>
        </XMLBIBLE>
        """.Replace("REMPLIR", string.Concat(Enumerable.Range(1, 60).Select(i => $"<VERS vnumber=\"{i}\">Umurongo {i}</VERS>")));
    var b = BibleImport.Analyser("kirundi.xml", zef);
    Egal(b.Nom, "Bibiliya Yera", "Zefania : nom");
    Egal(b.Versets(42, 3).Count >= 17 ? b.Versets(42, 3)[15] : "", "Kuko Imana yakunze abari mw’isi cane", "Zefania : Jean 3:16 (apostrophe typographique)");
    Egal(b.Versets(0, 1).Count > 0 ? b.Versets(0, 1)[0] : "", "Mu ntango Imana yarema ijuru n’isi.", "Zefania : Genèse 1:1");
    Egal(b.Nom, "Bibiliya Yera", "Zefania : nom (biblename)");

    var osis = """
        <osis><osisText osisIDWork="KIR"><header><work osisWork="KIR"><title>Bibiliya</title></work></header>
        <div type="book" osisID="John"><chapter osisID="John.3"><verse osisID="John.3.16">Kuko Imana <w>yakunze</w> isi<note>note retirée</note></verse></chapter></div>
        REMPLIR</osisText></osis>
        """.Replace("REMPLIR", string.Concat(Enumerable.Range(1, 60).Select(i => $"<verse osisID=\"Gen.2.{i}\">Umurongo {i}</verse>")));
    var o = BibleImport.Analyser("x.osis.xml", osis);
    Egal(o.Versets(42, 3).Count >= 16 ? o.Versets(42, 3)[15] : "", "Kuko Imana yakunze isi", "OSIS : Jean 3:16");

    var txt = "Jean 3:16 Car Dieu a tant aimé le monde\nJean 3:17 Dieu, en effet, n'a pas envoyé\nGenèse 1:1 Au commencement\n" + string.Join("\n", Enumerable.Range(1, 60).Select(i => $"Exode 2:{i} verset {i}"));
    var t = BibleImport.Analyser("ma-bible.txt", txt);
    Egal(t.Versets(42, 3).Count >= 17 ? t.Versets(42, 3)[16] : "", "Dieu, en effet, n'a pas envoyé", "texte : Jean 3:17");

    var json = """{"name":"Test","short":"TST","books":[{"name":"Genèse","ch":[["Au commencement"],REMPLIR]}]}""".Replace("REMPLIR", "[" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"\"v{i}\"")) + "]");
    var j = BibleImport.Analyser("t.json", json);
    Egal(j.Versets(0, 1).Count > 0 ? j.Versets(0, 1)[0] : "", "Au commencement", "JSON MyVerse");
    var aller = Bible.DepuisJson("x", j.VersJson());
    Egal(aller.Texte(0, 1, 1, 1), "Au commencement", "JSON aller-retour");
}

// ------------------------------------------------------------------ fichiers de chants
Groupe("Chants");
if (Actif())
{
    var vp = "{Songs:[{Text:\"Grâce infinie\",Author:\"J. Newton\",Verses:[{Text:\"Grâce infinie\nquel doux son\"},{Tag:2,Text:\"Refrain ici\nencore\"},{Text:\"Deuxième\"}]}]}";
    var c = ChantsFormats.ParseSongFile("recueil.json", vp);
    Egal(c.Count, 1, "VideoPsalm : 1 chant");
    Egal(c[0].Title, "Grâce infinie", "VideoPsalm : titre");
    Egal(string.Join("|", c[0].Sections.Select(s => s.Label)), "Couplet 1|Refrain|Couplet 2", "VideoPsalm : étiquettes");
    Egal(c[0].Sections[0].Text, "Grâce infinie\nquel doux son", "VideoPsalm : texte multiligne");

    var ol = """
        <?xml version="1.0" encoding="UTF-8"?>
        <song xmlns="http://openlyrics.info/namespace/2009/song" version="0.8">
          <properties><titles><title>Amazing Grace</title></titles><authors><author>John Newton</author></authors>
          <verseOrder>v1 c v2 c</verseOrder></properties>
          <lyrics>
            <verse name="v1"><lines>Amazing grace how sweet<br/>the sound</lines></verse>
            <verse name="c"><lines>Chorus line</lines></verse>
            <verse name="v2"><lines>Second verse</lines></verse>
          </lyrics>
        </song>
        """;
    var o = ChantsFormats.ParseSongFile("a.xml", ol);
    Egal(o[0].Title, "Amazing Grace", "OpenLyrics : titre");
    Egal(string.Join("|", o[0].Sections.Select(s => s.Label)), "Couplet 1|Refrain|Couplet 2|Refrain", "OpenLyrics : ordre");
    Egal(o[0].Sections[0].Text, "Amazing grace how sweet\nthe sound", "OpenLyrics : <br/>");
    Egal(o[0].Author, "John Newton", "OpenLyrics : auteur");

    var os = """
        <song><title>Ma chanson</title><author>Moi</author><presentation>V1 C V2</presentation>
        <lyrics>[V]
        .G       D
        1 Premier couplet
        2 Deuxième couplet
        [C]
         Le refrain|en deux lignes</lyrics></song>
        """;
    var s = ChantsFormats.ParseSongFile("ma.xml", os);
    Egal(string.Join("|", s[0].Sections.Select(x => x.Label)), "Couplet 1|Refrain|Couplet 2", "OpenSong : couplets numérotés");
    Egal(s[0].Sections[1].Text, "Le refrain\nen deux lignes", "OpenSong : | = retour à la ligne");

    var cp = "{title: Reckless Love}\n{artist: Cory Asbury}\n\n{start_of_verse: Verse 1}\n[G]Before I spoke a [D]word\nYou were singing\n{end_of_verse}\n\n{soc}\nOh the over[C]whelming\n{eoc}\n\n{chorus}\n";
    var p = ChantsFormats.ParseSongFile("r.cho", cp);
    Egal(p[0].Title, "Reckless Love", "ChordPro : titre");
    Egal(string.Join("|", p[0].Sections.Select(x => x.Label)), "Couplet|Refrain|Refrain", "ChordPro : sections + {chorus}");
    Egal(p[0].Sections[0].Text, "Before I spoke a word\nYou were singing", "ChordPro : accords retirés");

    var ss = "Way Maker\n\nVerse 1\nYou are here moving in our midst\nI worship You\n\nChorus\nWay maker miracle worker\n\nOsinachi Kalu Okoro Egbu\nCCLI Song # 7115744\n© 2016 Integrity Music\nFor use solely with the SongSelect® Terms of Use.\nCCLI License # 1234";
    var w = ChantsFormats.ParseSongFile("waymaker.txt", ss);
    Egal(w[0].Title, "Way Maker", "SongSelect : titre");
    Egal(w[0].Ccli, "7115744", "SongSelect : numéro CCLI");
    Egal(w[0].Author, "Osinachi Kalu Okoro Egbu", "SongSelect : auteur seul");
    Egal(string.Join("|", w[0].Sections.Select(x => x.Label)), "Couplet|Refrain", "SongSelect : sections");

    var tx = "Mon Dieu est bon\n\nIl est bon\ntout le temps\n\n[Refrain]\nAlléluia\n\nCouplet 2 : Deuxième\nstrophe";
    var t = ChantsFormats.ParseSongFile("bon.txt", tx);
    Egal(t[0].Title, "Mon Dieu est bon", "texte : titre");
    Egal(string.Join("|", t[0].Sections.Select(x => x.Label)), "Couplet 1|Refrain|Couplet 2", "texte : étiquettes");
    Egal(t[0].Sections[2].Text, "Deuxième\nstrophe", "texte : « Couplet 2 : » retiré");
    Egal(ChantsFormats.FrLabel("chorus 2"), "Refrain 2", "étiquette chorus 2");
    Egal(ChantsFormats.FrLabel("Bridge"), "Pont", "étiquette Bridge");
    Egal(ChantsFormats.RtfToText(@"{\rtf1\ansi{\fonttbl\f0 Arial;}\f0 Gr\'e2ce infinie\par quel \u233?l\'e9gant}"), "Grâce infinie\nquel élégant", "RTF");
    try { ChantsFormats.ParseSongFile("x.pro", "\u0001\u0002binaire"); Ok(false, "ProPresenter 7 refusé"); }
    catch (InvalidDataException) { Ok(true, "ProPresenter 7 refusé"); }

    Ok(!File.Exists(Path.Combine(res, "chants-inclus.json")), "4.2 : aucun recueil de chants livré avec Siloé Stream");
}

// ------------------------------------------------------------------ rangement automatique
Groupe("Ranger");
if (Actif())
{
    var r = Ranger.Ranger_("Mon chant collé\n\nC G Am F\nLigne une du chant\nLigne deux du chant\nLigne trois du chant\nLigne quatre du chant\nLigne cinq du chant\nLigne six du chant\nLigne sept\n\nRefrain :\nGloire à toi\nGloire à toi\n");
    Egal(r.Titre, "Mon chant collé", "titre trouvé");
    Ok(!r.Sections.Any(x => x.Text.Contains("C G Am F")), "ligne d'accords retirée");
    Egal(r.Sections.Count, 3, "3 diapositives (4+3 lignes + refrain)");
    Egal(string.Join("|", r.Sections.Select(x => x.Label)), "Couplet 1 (1/2)|Couplet 1 (2/2)|Refrain", "étiquettes");
    var t = ChantsFormats.VersTexte(r.Sections);
    int i = t.IndexOf("Ligne deux");
    t = t[..i] + "\n" + t[i..];
    Egal(ChantsFormats.SplitSections(t).Count, 4, "Entrée devant « Ligne deux » : 4 diapositives");
    var r2 = Ranger.Ranger_("Titre\n\n1. Premier couplet ligne\nsuite\n\nRefrain\nGloire\n\n2. Deuxième couplet\nencore\n\nRefrain");
    Egal(string.Join("|", r2.Sections.Select(x => x.Label)), "Couplet 1|Refrain|Couplet 2|Refrain", "couplets numérotés + refrain repris");
    Egal(r2.Sections[3].Text, "Gloire", "« Refrain » seul = copie du refrain");
    var r3 = Ranger.Ranger_("Gloire à toi\nSeigneur\n\nTu es bon\nToujours\n\nGloire à toi\nSeigneur", 4);
    Egal(string.Join("|", r3.Sections.Select(x => x.Label)), "Refrain|Couplet 1|Refrain", "bloc répété = refrain");
    Egal(string.Join("|", Ranger.CouperLigne("Une très longue ligne de chant, qui ne tient pas sur une ligne", 40)), "Une très longue ligne de chant,|qui ne tient pas sur une ligne", "ligne trop longue coupée à la virgule");
    Ok(Ranger.LigneAccords("Do Ré Mim Sol") && !Ranger.LigneAccords("La"), "accords en français");
    Egal(string.Join(",", Ranger.Tailles(7, 5)), "4,3", "découpe équilibrée 7 → 4+3");
}

// ------------------------------------------------------------------ recueils
Groupe("Recueils");
if (Actif())
{
    var c = Recueils.ChantsDepuisTexte("1. GRÂCE INFINIE\nLigne a\nLigne b\n\n2. MON DIEU\nLigne c\n---\nAutre chant\nLigne d");
    Egal(string.Join("|", c.Select(x => x.Title)), "1. GRÂCE INFINIE|2. MON DIEU|Autre chant", "texte : 3 chants");
    var pdf = new List<Recueils.LignePdf>
    {
        new("1. GRÂCE INFINIE", 14, 1), new("1. Grâce infinie, ô quel doux son", 10, 1), new("Qui sauva un pécheur", 10, 1), new("", 0, 1),
        new("Refrain : Gloire", 10, 1), new("à Dieu", 10, 1), new("", 0, 1), new("2 DEUXIÈME CHANT", 14, 2), new("Paroles du chant", 10, 2), new("", 0, 2),
    };
    var cp = Recueils.ChantsDepuisLignes(pdf);
    Egal(cp.Count, 2, "PDF : 2 chants");
    Egal(cp[0].Title, "1. GRÂCE INFINIE", "PDF : titre");
    Egal(string.Join("|", cp[0].Sections.Select(x => x.Label)), "Couplet 1|Refrain", "PDF : strophes");
    Egal(cp[0].Sections[1].Text, "Gloire\nà Dieu", "PDF : refrain");
    var lignes = Recueils.LignesDepuisPages(new[]
    {
        (IReadOnlyList<Recueils.MorceauPdf>)new[] { new Recueils.MorceauPdf("doux", 120, 700, 10), new Recueils.MorceauPdf("Grâce", 50, 700.4, 10), new Recueils.MorceauPdf("son", 160, 699.8, 10), new Recueils.MorceauPdf("Refrain", 50, 640, 10) },
    });
    Egal(string.Join("|", lignes.Select(l => l.T)), "Grâce doux son||Refrain|", "PDF : lignes regroupées, écart = ligne vide");
    var html = """
        <!DOCTYPE html><html><head><title>Site</title><script>var a = 1 < 2;</script></head><body>
        <header><nav><a href="/">Accueil</a><h1>Mon site</h1></nav></header>
        <article><h1 class="entry-title">Quel ami fidèle</h1>
        <div class="entry-content"><p>1. Quel ami fidèle et tendre<br>Nous avons en Jésus-Christ</p><p>Refrain<br>Gloire &agrave; Dieu</p>
        <div class="sharedaddy">Partager sur Facebook</div></div></article><footer>© 2024</footer></body></html>
        """;
    var w = Recueils.ChantDepuisPage(html, "https://site.org/chants/ami");
    Egal(w?.Title, "Quel ami fidèle", "web : titre");
    Egal(string.Join("|", w?.Sections.Select(x => x.Label) ?? Array.Empty<string>()), "Couplet 1|Refrain", "web : strophes");
    Egal(w?.Sections[1].Text, "Gloire à Dieu", "web : entités décodées");
    var liste = "<a href='/chants/ami'>A</a><a href=\"chants/bon#x\">B</a><a href='https://site.org/chants/cri'>C</a><a href='/autre'>X</a><a href='https://autre.org/chants/dur'>D</a><a href='/chants/ami'>A</a><a href='/chants/x'>court</a>";
    Egal(string.Join(" ", Recueils.LiensDeChants(liste, "https://site.org/chants")), "https://site.org/chants/ami https://site.org/chants/bon https://site.org/chants/cri", "web : liens de chants");
}

// ------------------------------------------------------------------ données et reprise de la 3.x
string Temp() { var d = Path.Combine(Path.GetTempPath(), "myverse-test-" + Guid.NewGuid().ToString("n")[..8]); Directory.CreateDirectory(d); return d; }
Groupe("Donnees");
if (Actif())
{
    var d0 = Donnees.Charger(Temp(), res);
    Ok(d0.Chants.Count == 0 && d0.Agendas.Count == 0 && d0.Annonces.Count == 0, $"4.2 : premier démarrage vide, aucun chant ({d0.Chants.Count})");
    var a0 = d0.Reglages.Audio; a0.Normalize();
    Ok(a0.Sons.Count == 0 && a0.Profiles.Count == 0 && a0.VirtualSounds.Count == 0,
        "2.4.0 : premier démarrage : aucun son ajouté tout seul (on choisit ses sons), aucun enregistrement ni envoi");
    var bibles0 = new Bibles(d0).Toutes();
    Ok(bibles0.Count == 2 && bibles0.Any(b => b.Id == "lsg" && b.Integree) && bibles0.Any(b => b.Id == "kjv" && b.Integree), "4.2 : seulement les Bibles libres de droits Louis Segond 1910 et King James", string.Join(", ", bibles0.Select(b => b.Id)));
    Egal(d0.Reglages.Bibles.Principale, "lsg", "Bible principale : Louis Segond 1910");
    Egal(d0.Reglages.Theme, "nuit", "thème par défaut");
    var dir = Temp();
    Directory.CreateDirectory(Path.Combine(dir, "bibles"));
    File.WriteAllText(Path.Combine(dir, "bibles", "bk1.json"), """{"name":"Bibiliya","short":"KIR","books":[]}""");
    var cles = new Dictionary<string, string>
    {
        ["verselight-chants"] = """[{"id":"a1","title":"Mon chant","author":"Recueil 3x","sections":[{"label":"Couplet","text":"Ligne"}]}]""",
        ["verselight-theme"] = "\"aube\"",
        ["verselight-taille"] = "1.2",
        ["verselight-presentation"] = "\"bandeau\"",
        ["verselight-inclus-v1"] = "true",
        ["myverse-decoupes"] = """{"42.3.16":["Car Dieu","afin que"]}""",
        ["myverse-bibles"] = """{"principale":"bk1","seconde":"kjv","importees":[{"id":"bk1","name":"Bibiliya","short":"KIR","versets":3},{"id":"bk2","name":"Perdue","short":"X","versets":3}]}""",
        ["verselight-obs"] = """{"actif":true,"port":4456,"mdp":"secret","source":"BIBLE"}""",
        ["verselight-ecran-choisi"] = """{"p":false,"x":1920,"y":0,"w":1920,"h":1080,"nom":"Projecteur","mode":"plein"}""",
    };
    File.WriteAllText(Path.Combine(dir, "myverse-donnees.json"), System.Text.Json.JsonSerializer.Serialize(new { version = "3.0.4", date = "2026-11-09", donnees = cles }));
    var d = Donnees.Charger(dir, res);
    Egal(d.Chants.Count, 1, "3.x : chants repris (sans ajouter une 2e fois ceux de l'église)");
    Egal(d.Chants[0].Author, "Recueil 3x", "3.x : recueil du chant");
    Egal(d.Reglages.Theme, "aube", "3.x : thème");
    Egal(d.Reglages.Taille, 1.2, "3.x : taille");
    Egal(d.Reglages.Presentation, "bandeau", "3.x : présentation");
    Egal(d.Decoupes.TryGetValue("42.3.16", out var dec) ? dec.Count : 0, 2, "3.x : verset découpé");
    Egal(d.Reglages.Bibles.Principale, "bk1", "3.x : Bible principale importée");
    Egal(d.Reglages.Bibles.Importees.Count, 1, "3.x : Bible dont le fichier manque retirée");
    Egal(d.Reglages.Obs.Port, 4456, "3.x : réglages OBS");
    Egal(d.Reglages.Ecran?.X, 1920, "3.x : écran choisi");
    Ok(d.Migration.Contains("1 chants"), "résumé de la reprise : " + d.Migration);
    var d2 = Donnees.Charger(dir, res);
    Egal(d2.Chants.Count, 1, "2e démarrage : rien n'est repris une 2e fois");

    var ld = Ancien3x.LireDossier(Path.Combine(AppContext.BaseDirectory, "donnees", "leveldb-3x"));
    // le dossier du profil est trouvé même dans une arborescence WebView2
    var faux = Path.Combine(Temp(), "MyVerse.exe", "EBWebView", "Default", "Local Storage", "leveldb");
    Directory.CreateDirectory(faux);
    Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(faux)!)!, "Cache"));
    var trouve = typeof(Ancien3x).GetMethod("Chercher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .Invoke(null, new object[] { Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(faux)!)!)!)!, 5 }) as IEnumerable<string>;
    Egal(trouve?.FirstOrDefault(), faux, "profil WebView2 de la 3.x trouvé");
    Ok(ld.TryGetValue("verselight-chants", out var ch3) && ch3.StartsWith("[{") && ch3.Contains("Grâce infinie’"), "LevelDB : chants (UTF-16, table compressée)");
    Egal(ld.TryGetValue("verselight-theme", out var th) ? th : "∅", "\"vert\"", "LevelDB : valeur la plus récente (journal)");
    Egal(ld.TryGetValue("verselight-taille", out var ta) ? ta : "∅", "1.2", "LevelDB : valeur du journal");
    Ok(!ld.ContainsKey("verselight-presentation"), "LevelDB : clé supprimée ignorée");
    Ok(!ld.ContainsKey("myverse-journal"), "LevelDB : journal ignoré");
    Ok(ld.ContainsKey("myverse-decoupes"), "LevelDB : découpes");
    var d3 = Donnees.Charger(Temp(), res);
    d3.AppliquerCles(ld);
    Egal(d3.Chants.Count, 2000, "LevelDB : 2000 chants repris");
}

// ------------------------------------------------------------------ réglages de la 1.2 bêta (ancien mixeur local) → 3.0
Groupe("Reglages 1.2");
if (Actif())
{
    var dir = Temp();
    File.WriteAllText(Path.Combine(dir, "chants.json"), """[{"id":"c12","title":"Chant gardé","author":"Recueil 1.2","sections":[{"label":"Couplet","text":"Ligne"}]}]""");
    File.WriteAllText(Path.Combine(dir, "reglages.json"), """
    {"theme":"ciel","taille":1.3,"presentation":"bandeau","recueil":"Recueil 1.2","inclus":true,
     "bibles":{"principale":"kjv","seconde":"lsg","importees":[]},
     "audio":{"version":2,"sampleRate":48000,"monitorDeviceId":null,"monitorDb":-12,"listenZoom":true,
       "sources":[{"id":"a","name":"Micro","kind":"Capture","stereo":true,"channel":1,"sends":{"salle":{"enabled":true,"volumeDb":-3}},
                   "filters":[{"kind":"Equalizer","q":0.7},{"kind":"Gain","gainDb":3},{"kind":"Sidechain","sidechainSourceId":"b"}]},
                  {"id":"b","name":"PC","kind":"Loopback","filters":[{"kind":9}]},{"id":"c","kind":2}],
       "buses":[{"id":"salle","outputDeviceId":"x"}],"scenes":[{"name":"Culte","sources":[{"kind":"Capture"}]}],
       "profiles":[{"name":"Culte","icon":"●","format":"wav","allowedSources":["a"],"sendDb":{"a":-6},"applyToRoom":true}]}}
    """);
    var d12 = Donnees.Charger(dir, res);
    Egal(d12.Reglages.Theme, "ciel", "1.2 : thème conservé (les réglages se lisent malgré l'ancien audio)");
    Egal(d12.Reglages.Taille, 1.3, "1.2 : taille conservée");
    Egal(d12.Reglages.Bibles.Principale, "kjv", "1.2 : Bible principale conservée");
    Egal(d12.Reglages.Recueil, "Recueil 1.2", "1.2 : recueil conservé");
    Egal(d12.Chants.Count, 1, "1.2 : chants conservés");
    var a12 = d12.Reglages.Audio;
    a12.Normalize();
    Egal(string.Join(",", a12.Sons.Select(s => s.Genre)), "", "1.2 bêta : l'ancien mixeur n'est pas repris (2.4.0 : aucun son ajouté tout seul)");
    Egal(a12.Profiles.Count, 0, "1.2 bêta : anciens profils d'enregistrement non repris (aucun préréglage)");
    Egal(a12.Eglise.Appareil, "default", "1.2 bêta : l'église sur la sortie de Windows");
    Egal(a12.Version, MyVerse.Audio.AudioConfiguration.Actuelle, "1.2 bêta : version des réglages audio mise à jour");
    // un son ajouté : écrit sous « sons », relu tel quel
    var chaire = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Voix, "Chaire");
    chaire.Appareil = "{umc}"; chaire.Entree = 2; chaire.VolumeDb = -4.5; chaire.Coupe = true;
    a12.Ajouter(chaire);
    d12.SauverReglages();
    var json = File.ReadAllText(Path.Combine(dir, "reglages.json"));
    Ok(json.Contains("\"sons\"") && !json.Contains("\"obsSources\"") && !json.Contains("\"sources\"") && !json.Contains("Tracks") && !json.Contains("Monitoring"), "1.3 : sons écrits sous « sons », sans pistes ni monitoring");
    var r12 = Donnees.Charger(dir, res).Reglages.Audio;
    r12.Normalize();
    var relue = r12.Sons.FirstOrDefault(s => s.Nom == "Chaire");
    Ok(relue is { Genre: MyVerse.Audio.GenreSon.Voix, Appareil: "{umc}", Entree: 2, VolumeDb: -4.5, Coupe: true, EnleverBruit: false, VolumeRegulier: false, Eglise: false }, "1.3 : son relu avec ses réglages");
    Ok(r12.Sons.Count == 1, "2.4.0 : seulement le son ajouté (plus de son des vidéos ajouté tout seul)");
}

// ------------------------------------------------------------------ régie
Groupe("Transfert");
if (Actif())
{
    var dA = Donnees.Charger(Temp(), res);
    var bA = new Bibles(dA);
    var rA = new Regie(dA, bA);
    rA.AjouterChants(new[] {
        new Chant { Title = "1 - Un", Sections = { new Section { Label = "Couplet 1", Text = "Ligne une" } } },
        new Chant { Title = "2 - Deux", Sections = { new Section { Label = "Couplet 1", Text = "Ligne deux" } } } }, "Recueil T");
    var infoA = bA.Ajouter(Bible.DepuisJson("x", File.ReadAllText(Path.Combine(res, "lsg.json"))), "MAB");
    var fichier = Path.Combine(Temp(), "export" + Transfert.Extension);
    var m = Transfert.Exporter(fichier, dA, true, true, "test");
    Ok(m.Chants == dA.Chants.Count && m.Bibles.Count == 1, $"export : {m.Chants} chants, {m.Bibles.Count} Bible");
    Ok(Transfert.Lire(fichier).Recueils.Contains("Recueil T"), "contenu du fichier lisible");

    var dB = Donnees.Charger(Temp(), res);
    var bB = new Bibles(dB);
    var rB = new Regie(dB, bB);
    var bil = Transfert.Importer(fichier, rB, bB);
    Ok(bil.ChantsAjoutes == 2 && bil.ChantsMisAJour == 0 && bil.ChantsPareils == m.Chants - 2 && rB.Filtrer("", "Recueil T").Count == 2, $"import : {bil.ChantsAjoutes} ajoutés, {bil.ChantsPareils} déjà là");
    Egal(bil.BiblesAjoutees, 1, "import : Bible ajoutée");
    var bibB = bB.Toutes().FirstOrDefault(x => !x.Integree);
    Ok(bibB != null && bibB.Court == "MAB" && bB.Charger(bibB.Id)?.NombreVersets() == infoA.Versets, "Bible importée lisible");
    var bil2 = Transfert.Importer(fichier, rB, bB);
    Ok(bil2.ChantsAjoutes == 0 && bil2.ChantsMisAJour == 0 && bil2.BiblesAjoutees == 0 && bil2.BiblesPresentes == 1, "réimport : rien en double");
    rA.Filtrer("", "Recueil T")[0].Sections[0].Text = "Ligne changée";
    Transfert.Exporter(fichier, dA, true, false);
    var bil3 = Transfert.Importer(fichier, rB, bB);
    Ok(bil3.ChantsMisAJour == 1 && rB.Filtrer("changée", null).Count == 1, "chant modifié : mis à jour");
    Ok(Transfert.Lire(fichier).Bibles.Count == 0, "export des chants seuls");
    bool refuse = false;
    try { Transfert.Lire(Path.Combine(res, "lsg.json")); } catch (Exception) { refuse = true; }
    Ok(refuse, "fichier qui n'est pas un export refusé");
}

Groupe("Regie");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    File.Copy(Path.Combine(res, "lsg.json"), Path.Combine(don.Dossier, "lsg-copie.json"));
    var bib = new Bibles(don);
    var rg = new Regie(don, bib);
    int changes = 0; bool? oeil = null;
    rg.Change += () => changes++;
    rg.Oeil += v => oeil = v;
    rg.AfficherVerset(42, 3, 16, 16);
    Egal(rg.Etat.Ref, "Jean 3:16 (LSG)", "Jean 3:16 à l'écran");
    Ok(oeil == true && changes > 0, "OBS : afficher");
    Egal(rg.Prochaine()?.Label, "Jean 3:17", "Suivant = Jean 3:17");
    var tx = rg.TexteADecouper(rg.Cible!);
    int k = tx.IndexOf("afin que");
    var msg = rg.Decouper(rg.Cible!, tx[..k] + "\n" + tx[k..]);
    Ok(msg.Contains("2 diapositives"), "découpe : " + msg);
    Ok(rg.Etat.Lignes[0].StartsWith("Car Dieu") && !rg.Etat.Lignes[0].Contains("afin que"), "morceau 1 de Jean 3:16 à l'écran");
    Ok(rg.Prochaine()?.Label.Contains("morceau 2/2") == true, "Suivant display le morceau 2/2");
    rg.Bouger(1);
    Ok(rg.Etat.Lignes[0].StartsWith("afin que"), "Suivant → morceau 2");
    rg.Bouger(1);
    Egal(rg.Etat.Ref, "Jean 3:17 (LSG)", "Suivant → Jean 3:17");
    rg.Bouger(-1);
    Ok(rg.Etat.Lignes[0].StartsWith("afin que"), "Précédent → dernier morceau de 3:16");
    rg.AfficherVerset(42, 3, 36, 36);
    rg.Bouger(1);
    Egal(rg.Etat.Ref, "Jean 4:1 (LSG)", "fin du chapitre → chapitre suivant");
    rg.Bouger(-1);
    Egal(rg.Etat.Ref, "Jean 3:36 (LSG)", "Précédent → fin du chapitre précédent");
    bib.BasculerSeconde("kjv");
    rg.AfficherVerset(42, 3, 17, 17);
    Ok(rg.Etat.Second?.Texte.Contains("For God sent not his Son") == true, "2e version sous le verset");
    Egal(rg.Etat.Ref, "Jean 3:17 (LSG · KJV)", "référence bilingue");
    var res3 = Recherche.Chercher(bib.Versions(), "god so loved the world", 5, out _);
    Egal(res3.Count > 0 ? Livres.Libelle(res3[0].B, res3[0].C, res3[0].V) : "∅", "Jean 3:16", "recherche en anglais (2e version)");
    bib.BasculerSeconde("kjv");
    Ok(rg.Etat.Second == null && rg.Etat.Ref == "Jean 3:17 (LSG)", "retour à la Louis Segond seule (verset réaffiché)");
    rg.Effacer();
    Ok(rg.Etat.Vide && oeil == false && rg.Cible == null, "Retirer");
    rg.Selection = new Ref(18, 23, 1, 1);
    rg.Bouger(1);
    Egal(rg.Etat.Ref, "Psaumes 23:1 (LSG)", "Suivant sans rien à l'écran → versets choisis");
    Ok(rg.AfficherRef("jude 3") && rg.Etat.Ref == "Jude 1:3 (LSG)", "« jude 3 » → Jude 1:3 : " + rg.Etat.Ref);
    Ok(rg.AfficherRef("ps 23") && rg.Etat.Ref == "Psaumes 23:1 (LSG)", "chapitre entier → premier verset");
    Egal(rg.ApercuRef("ps 23")?.Versets.Count, 6, "aperçu du chapitre entier");
    Ok(!rg.AfficherRef("bonjour"), "référence inconnue refusée");

    var r = Ranger.Ranger_("Mon chant\n\nLigne une\nLigne deux\nLigne trois\n\nRefrain\nGloire");
    var id = rg.EnregistrerChant(null, r.Titre, r.Sections, "Mes chants");
    rg.AfficherSection(id, 0);
    Egal(string.Join("/", rg.Etat.Lignes), "Ligne une/Ligne deux/Ligne trois", "strophe à l'écran");
    Egal(rg.Prochaine()?.Label, "Refrain", "Suivant = Refrain");
    msg = rg.Decouper(new CibleChant(id, 0), "Ligne une\n\nLigne deux\nLigne trois");
    Egal(string.Join("|", rg.Trouver(id)!.Sections.Select(x => x.Label)), "Couplet 1 (1/2)|Couplet 1 (2/2)|Refrain", "strophe coupée en 2");
    Egal(string.Join("/", rg.Etat.Lignes), "Ligne une", "strophe affichée mise à jour");
    rg.Bouger(1); rg.Bouger(1);
    Egal(string.Join("/", rg.Etat.Lignes), "Gloire", "Suivant → Refrain");
    Ok(rg.Recueils().Any(x => x.Nom == "Mes chants"), "recueil « Mes chants »");
    Egal(rg.Filtrer("gloire", "Mes chants").Count, 1, "filtre par paroles et recueil");
    Ok(rg.ChercherChants("1").All(c => c.Title.TrimStart().StartsWith("1")), "chant par numéro");
    rg.AjouterChants(new[] { new Chant { Title = "6 - Six" }, new Chant { Title = "16 - Seize" }, new Chant { Title = "106. Cent six" }, new Chant { Title = "N° 60 Soixante" }, new Chant { Title = "Numva Yesu" } }, "Num A");
    rg.AjouterChants(new[] { new Chant { Title = "6 - Autre six" } }, "Num B");
    Egal(string.Join("|", rg.ParNumero("6", "Num A").Select(c => c.Title)), "6 - Six", "numéro exact dans le recueil (pas 16 ni 106)");
    Egal(rg.ParNumero("06", null).Count(c => c.Author.StartsWith("Num ")), 2, "numéro « 06 » dans tous les recueils");
    Egal(string.Join("|", rg.ParNumero("60", "Num A").Select(c => c.Title)), "N° 60 Soixante", "numéro « N° 60 »");
    Egal(rg.ParNumero("7", "Num A").Count, 0, "numéro absent");
    Ok(Regie.Numero("Numva Yesu") == null && Regie.Numero("106. Cent six") == 106, "numéro lu dans le titre");
    Ok(rg.Filtrer("6", null).Take(2).All(c => Regie.Numero(c.Title) == 6), "recherche « 6 » : les n° 6 d'abord");
    Ok(rg.ChercherChants("99999999999999").Count == 0, "numéro trop long sans erreur");
    rg.SupprimerRecueil("Num A"); rg.SupprimerRecueil("Num B");
    Ok(Regie.Comparer("2. B", "10. A") < 0 && Regie.Comparer("élan", "Etoile") < 0, "tri naturel et sans accents");
    rg.SupprimerChant(id);
    Ok(rg.Trouver(id) == null, "chant supprimé");
    var don2 = Donnees.Charger(don.Dossier, res);
    Ok(don2.Decoupes.ContainsKey("42.3.16") && don2.Reglages.Bibles.Seconde == "", "réglages et découpes enregistrés");
}

// ------------------------------------------------------------------ téléphone
Groupe("Telephone");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var bib = new Bibles(don);
    var rg = new Regie(don, bib);
    // 4.2 : Siloé Stream démarre sans chant ; quelques chants pour le téléphone
    rg.AjouterChants(new[] {
        new Chant { Title = "1 - Grâce infinie", Sections = { new Section { Label = "Couplet 1", Text = "Grâce infinie\nô quel doux son" }, new Section { Label = "Refrain", Text = "Gloire à Dieu" } } },
        new Chant { Title = "12 - Mon Dieu est bon", Sections = { new Section { Label = "Couplet 1", Text = "Mon Dieu est bon" } } } }, "Recueil de test");
    using var tel = new Telephone(don.Dossier, rg, f => f(), "PC-TEST");
    tel.Demarrer();
    Ok(tel.Port >= 47900 && tel.Port <= 47909, $"écoute sur le port {tel.Port}");
    var url = tel.OuvrirJumelage();
    var q = System.Web.HttpUtility.ParseQueryString(new Uri(url.Replace("siloe://pair", "http://x/pair")).Query);
    Egal(q["pcId"], tel.PcId, "code QR : pcId");
    Ok((q["ips"] ?? "").Length >= 0 && q["port"] == tel.Port.ToString(), "code QR : port");
    var secret = Telephone.B64Dec(q["secret"]!);

    async Task<System.Net.WebSockets.ClientWebSocket> Ouvrir()
    {
        var ws = new System.Net.WebSockets.ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{tel.Port}/"), CancellationToken.None);
        return ws;
    }
    async Task Env(System.Net.WebSockets.ClientWebSocket ws, object o) =>
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(o)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    async Task<System.Text.Json.Nodes.JsonObject?> Rec(System.Net.WebSockets.ClientWebSocket ws, string? type = null, int ms = 3000)
    {
        var fin = DateTime.Now.AddMilliseconds(ms);
        var buf = new byte[1 << 16];
        while (DateTime.Now < fin)
        {
            using var cts = new CancellationTokenSource(fin - DateTime.Now);
            using var mem = new MemoryStream();
            try
            {
                System.Net.WebSockets.WebSocketReceiveResult r;
                do { r = await ws.ReceiveAsync(buf, cts.Token); mem.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
            }
            catch (OperationCanceledException) { return null; }
            var o = System.Text.Json.Nodes.JsonNode.Parse(mem.ToArray())!.AsObject();
            if (type == null || (string?)o["t"] == type) return o;
        }
        return null;
    }
    async Task<System.Net.WebSockets.ClientWebSocket?> Connecter(string device, byte[] cle, bool pair)
    {
        var ws = await Ouvrir();
        await Env(ws, new { t = "hello", v = 1, pcId = tel.PcId, deviceId = device, deviceName = "Pixel test", pair, cnonce = "cn1" });
        var ch = await Rec(ws);
        if ((string?)ch?["t"] != "challenge") { Ok(false, "défi attendu", ch?.ToJsonString()); return null; }
        var nonce = (string)ch!["nonce"]!;
        Egal((string?)ch["proof"], Telephone.PreuveServeur(cle, "cn1", nonce), "le PC prouve qu'il connaît la clé");
        await Env(ws, new { t = "auth", mac = Telephone.MacClient(cle, nonce, device) });
        var w = await Rec(ws);
        Egal((string?)w?["t"], "welcome", "bienvenue (" + (pair ? "jumelage" : "reconnexion") + ")");
        Egal((bool?)w?["paired"], pair, "paired");
        return ws;
    }

    // 1.9.1 : membre de l'église (compte Google) : billet signé avec le secret du PC, sans code QR
    {
        var secEg = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        new CompteEglise { Eglise = "eg1", Nom = "Église test", Secret = Telephone.B64(secEg) }.Ecrire(don.Dossier);
        string Billet(string dev, string role, long exp)
        {
            var charge = Telephone.B64(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { pc = tel.PcId, dev, email = "membre@gmail.com", role, nom = "Membre", exp })));
            return charge + "." + Telephone.HmacB64(secEg, "siloe-billet|" + charge);
        }
        var maint = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Ok(CompteEglise.Verifier(Billet("tel-c", "bible", maint + 600), secEg, tel.PcId, "tel-c", DateTimeOffset.UtcNow)?.Role == "bible", "billet : valide, rôle Versets");
        Ok(CompteEglise.Verifier(Billet("tel-c", "bible", maint - 5), secEg, tel.PcId, "tel-c", DateTimeOffset.UtcNow) == null, "billet : expiré refusé");
        Ok(CompteEglise.Verifier(Billet("tel-x", "bible", maint + 600), secEg, tel.PcId, "tel-c", DateTimeOffset.UtcNow) == null, "billet : autre téléphone refusé");
        Ok(CompteEglise.Verifier(Billet("tel-c", "bible", maint + 600) + "x", secEg, tel.PcId, "tel-c", DateTimeOffset.UtcNow) == null, "billet : signature fausse refusée");
        var cleC = CompteEglise.CleTelephone(secEg, tel.PcId, "tel-c");
        var wsC = await Ouvrir();
        await Env(wsC, new { t = "hello", v = 2, pcId = tel.PcId, deviceId = "tel-c", deviceName = "Membre", pair = false, cnonce = "cn1", compte = Billet("tel-c", "bible", maint + 600) });
        var chC = await Rec(wsC);
        Egal((string?)chC?["t"], "challenge", "compte : défi");
        if (chC != null)
        {
            await Env(wsC, new { t = "auth", mac = Telephone.MacClient(cleC, (string)chC["nonce"]!, "tel-c") });
            var wC = await Rec(wsC);
            Egal((string?)wC?["role"], "bible", "compte : bienvenue avec le rôle du billet");
            Egal((string?)wC?["eglise"], "eg1", "compte : le PC donne son église");
            await wsC.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        // sans billet : refusé même si le téléphone est connu
        var wsD = await Ouvrir();
        await Env(wsD, new { t = "hello", v = 2, pcId = tel.PcId, deviceId = "tel-c", deviceName = "Membre", pair = false, cnonce = "cn1" });
        Egal((string?)(await Rec(wsD))?["t"], "auth.fail", "compte : sans billet, refusé");
        CompteEglise.Effacer(don.Dossier);
        tel.Revoquer("tel-c");
        await Task.Delay(100);
    }
    var cle = Telephone.DeriverCle(secret, tel.PcId, "tel-1");
    var ws1 = await Connecter("tel-1", cle, true);
    Ok(tel.Store.Liste().Any(a => a.DeviceId == "tel-1" && a.Nom == "Pixel test"), "téléphone jumelé enregistré");
    Ok(tel.Connectes().Contains("tel-1"), "téléphone connecté");
    if (ws1 != null)
    {
        await Env(ws1, new { t = "bible.show", @ref = "jn 3 16", rid = 1 });
        var st = await Rec(ws1, "state");
        Egal((string?)st?["ref"], "Jean 3:16 (LSG)", "bible.show → état reçu");
        Egal((string?)st?["suivantLabel"], "Jean 3:17", "état : suivant");
        await Env(ws1, new { t = "bible.preview", @ref = "ps 23 1-2", rid = 2 });
        var pv = await Rec(ws1, "preview");
        Egal(pv?["versets"]?.AsArray().Count, 2, "aperçu : 2 versets");
        Egal((int?)pv?["rid"], 2, "réponse avec rid");
        await Env(ws1, new { t = "bible.search", q = "charite pasiente", rid = "3" });
        var se = await Rec(ws1, "search", 8000);
        Egal((string?)se?["liste"]?[0]?["ref"], "1 Corinthiens 13:4", "recherche floue depuis le téléphone");
        Egal((string?)se?["rid"], "3", "rid texte conservé");
        await Env(ws1, new { t = "song.search", q = "1", rid = 4 });
        var so = await Rec(ws1, "songs");
        Ok((so?["liste"]?.AsArray().Count ?? 0) > 0, "chants par numéro");
        var sid = (string?)so?["liste"]?[0]?["id"];
        await Env(ws1, new { t = "song.open", id = sid, rid = 5 });
        var sg = await Rec(ws1, "song");
        Ok((sg?["sections"]?.AsArray().Count ?? 0) > 0, "chant ouvert");
        await Env(ws1, new { t = "song.show", id = sid, i = 0, rid = 6 });
        st = await Rec(ws1, "state");
        Egal((string?)st?["mode"], "chant", "chant à l'écran");
        Egal((string?)st?["chant"]?["id"], sid, "état : chant");
        await Env(ws1, new { t = "clear" });
        st = await Rec(ws1, "state");
        Egal((string?)st?["mode"], "vide", "Retirer depuis le téléphone");
        await Env(ws1, new { t = "sync.get", rid = 7 });
        var sy = await Rec(ws1, "sync");
        Ok(sy?["bibles"]?.AsArray().Any(b => (string?)b?["id"] == "kjv") == true && (string?)sy?["reglages"]?["principale"] == "lsg", "synchronisation : Bibles et réglages");
        await Env(ws1, new { t = "bible.get", id = "kjv", rid = 8 });
        var bg = await Rec(ws1, "bible", 15000);
        if (bg != null)
        {
            using var gz = new System.IO.Compression.GZipStream(new MemoryStream(Convert.FromBase64String((string)bg["gz"]!)), System.IO.Compression.CompressionMode.Decompress);
            using var lu = new StreamReader(gz);
            var kj = Bible.DepuisJson("kjv", lu.ReadToEnd());
            Ok(kj.Texte(42, 3, 16, 16).StartsWith("For God so loved"), $"Bible KJV envoyée ({((string)bg["gz"]!).Length / 1024} Ko)");
        }
        else Ok(false, "Bible KJV envoyée");
        await Env(ws1, new { t = "inconnue", rid = 9 });
        Egal((string?)(await Rec(ws1, "error"))?["message"], "Commande inconnue : inconnue", "commande inconnue");
        var display = rg.NouveauDisplay("Caméra USB", new SourceDisplay { Type = "camera", Peripherique = "device-test" });
        await Env(ws1, new { t = "display.list", rid = 10 });
        var ds = await Rec(ws1, "displays");
        Ok(ds?["liste"]?.AsArray().Any(x => (string?)x?["id"] == display.Id && (string?)x?["type"] == "camera") == true, "APK : liste des sources Display");
        await Env(ws1, new { t = "display.show", id = display.Id });
        st = await Rec(ws1, "state");
        Ok((string?)st?["mode"] == "media" && (string?)st?["displayId"] == display.Id && (string?)st?["titre"] == "Caméra USB", "APK : périphérique Display en direct");
        await Env(ws1, new { t = "display.show", id = "inexistant" });
        Ok(((string?)(await Rec(ws1, "error"))?["message"] ?? "").Contains("introuvable"), "APK : source Display inconnue refusée");
        await ws1.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }
    await Task.Delay(200);
    Ok(!tel.Connectes().Contains("tel-1"), "déconnexion vue");
    var ws2 = await Connecter("tel-1", cle, false);
    ws2?.Abort();
    // téléphone inconnu, mauvais PC, QR fermé
    async Task<string?> Refus(object hello)
    {
        var ws = await Ouvrir();
        await Env(ws, hello);
        var m = await Rec(ws);
        ws.Abort();
        return (string?)m?["code"];
    }
    Egal(await Refus(new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "inconnu", deviceName = "X", pair = false, cnonce = "c" }), "revoked", "téléphone inconnu refusé");
    Egal(await Refus(new { t = "hello", v = 1, pcId = "autre", deviceId = "tel-1", deviceName = "X", pair = false, cnonce = "c" }), "wrong_pc", "mauvais PC");
    tel.FermerJumelage();
    Egal(await Refus(new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "tel-2", deviceName = "X", pair = true, cnonce = "c" }), "pairing_closed", "code QR fermé");
    var ws3 = await Ouvrir();
    await Env(ws3, new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "tel-1", deviceName = "X", pair = false, cnonce = "c" });
    var ch3 = await Rec(ws3);
    await Env(ws3, new { t = "auth", mac = "faux" });
    Egal((string?)(await Rec(ws3))?["code"], "revoked", "mauvaise clé refusée");
    ws3.Abort();
    tel.Revoquer("tel-1");
    Ok(!tel.Store.Liste().Any(a => a.DeviceId == "tel-1"), "téléphone révoqué");
    using (var http = new HttpClient())
        Ok((await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/")).Contains("Siloé Stream ✓"), "page de test http://IP:port");
    using (var u = new System.Net.Sockets.UdpClient())
    {
        var b = System.Text.Encoding.UTF8.GetBytes("VERSELIGHT_DISCOVER " + tel.PcId);
        await u.SendAsync(b, b.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Telephone.PortDecouverte));
        var cts = new CancellationTokenSource(2000);
        try
        {
            var r = await u.ReceiveAsync(cts.Token);
            var o = System.Text.Json.Nodes.JsonNode.Parse(r.Buffer)!;
            Ok((string?)o["pcId"] == tel.PcId && (int?)o["port"] == tel.Port, "découverte UDP");
        }
        catch (OperationCanceledException) { Ok(false, "découverte UDP (pas de réponse)"); }
    }
    var dev = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(don.Dossier, "devices.json")))!;
    Ok((string?)dev["pcId"] == tel.PcId && dev["devices"] is System.Text.Json.Nodes.JsonArray, "devices.json au format de la 3.x");

    // page « suivre » : l'assemblée suit sur son téléphone (sans jumelage, lecture seule)
    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
        var page = await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/suivre");
        Ok(page.Contains("Siloé Stream") && page.Contains("/suivre/etat"), "page « suivre »");
        var e0 = System.Text.Json.Nodes.JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/suivre/etat?v=-1&id=test1"))!;
        int v0 = (int)e0["v"]!;
        var attente = http.GetStringAsync($"http://127.0.0.1:{tel.Port}/suivre/etat?v={v0}&id=test1");
        await Task.Delay(400);
        Ok(!attente.IsCompleted, "attente longue tant que l'écran ne change pas");
        rg.AfficherVerset(42, 3, 16, 16);
        var e1 = System.Text.Json.Nodes.JsonNode.Parse(await attente)!;
        Ok((int)e1["v"]! > v0 && ((string?)e1["ref"] ?? "").StartsWith("Jean 3:16") && ((string?)e1["principale"]) == "LSG", "l'assemblée voit le verset aussitôt");
        Ok(tel.Suiveurs() >= 1, $"téléphones qui suivent : {tel.Suiveurs()}");
        Ok(Telephone.AdressesLocales().Count == 0 || (tel.AdresseSuivre() ?? "").EndsWith($":{tel.Port}/suivre"), "adresse du code QR");
    }
}

Groupe("Agenda");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var bib = new Bibles(don);
    var rg = new Regie(don, bib);
    Egal(Regie.NomAgendaParDefaut(new DateTime(2026, 11, 9)), "Culte du dimanche 15 novembre", "nom par défaut : le prochain dimanche");
    Egal(Regie.NomAgendaParDefaut(new DateTime(2026, 11, 15)), "Culte du dimanche 15 novembre", "un dimanche : ce jour-là");
    var ag = rg.Agenda;
    Ok(rg.Agendas.Count == 1 && ag.Elements.Count == 0, "un agenda créé tout seul");
    var idChant = rg.EnregistrerChant(null, "1 - Chant de test", new List<Section> { new() { Label = "Couplet 1", Text = "Ligne A\nLigne B" }, new() { Label = "Refrain", Text = "Ligne C" } }, "Recueil A");
    var eC = rg.AjouterChantAgenda(idChant)!;
    var eB = rg.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
    var eT = rg.AjouterTexteAgenda("Annonces", "Bienvenue\n\nRepas après le culte");
    Egal(rg.Agenda.Elements.Count, 3, "chant, Bible et texte dans l'agenda");
    Egal($"{rg.Diapos(eC).Count}/{rg.Diapos(eB).Count}/{rg.Diapos(eT).Count}", "2/2/2", "diapositives de chaque élément");
    // 3.2 : vignettes de la page Culte et aperçu « Suivant » : chaque diapositive dessinée sans rien afficher
    {
        int avantChanges = 0;
        rg.Change += () => avantChanges++;
        var vC = rg.EtatCible(rg.Diapos(eC)[1].Cible, eC);
        var vB = rg.EtatCible(rg.Diapos(eB)[0].Cible, eB);
        var vT = rg.EtatCible(rg.Diapos(eT)[1].Cible);
        var eK = rg.AjouterCompteAgenda(7);
        var vK = rg.EtatCible(rg.Diapos(eK)[0].Cible);
        Ok(vC is { Mode: "chant" } && string.Join("|", vC.Lignes) == "Ligne C", "vignette : la 2e strophe du chant");
        Ok(vB is { Mode: "verset" } && vB.Ref.StartsWith("Jean 3:16") && vB.Lignes[0].Length > 20, "vignette : le verset avec sa référence");
        Ok(vT is { Mode: "chant" } && vT.Lignes[0] == "Repas après le culte", "vignette : le 2e paragraphe du texte");
        Ok(vK is { Mode: "chant" } && vK.Lignes.Count == 2 && vK.Lignes[1] == "07:00", "vignette : le compte à rebours à sa durée");
        Ok(rg.EtatCible(null) == null && rg.EtatCible(new CibleChant("absent", 0)) == null, "vignette d'une cible absente : rien");
        Ok(rg.Etat.Vide && rg.Cible == null && avantChanges == 0, "vignettes : rien ne s'affiche, aucun changement d'écran");
        rg.RetirerAgenda(eK.Id);
    }
    Egal(rg.Description(eB).Type, "BIBLE", "type affiché");
    rg.AfficherElement(eC, 0);
    Ok(rg.ElementCourant == eC && rg.Etat.Mode == "chant", "élément de l'agenda à l'écran");
    rg.Bouger(1);
    rg.Bouger(1);
    Ok(rg.ElementCourant == eB && rg.Etat.Ref.StartsWith("Jean 3:16"), "Suivant passe à l'élément suivant");
    Egal(rg.Prochaine()?.Label, "Jean 3:17", "Suivant = Jean 3:17");
    rg.Bouger(1);
    Egal(rg.Prochaine()?.Label, "Annonces", "fin du passage : l'élément suivant est annoncé (1.5.1 : son titre seul)");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eT && string.Join("|", rg.Etat.Lignes) == "Bienvenue", "puis le texte");
    rg.Bouger(1); rg.Bouger(1);
    Egal(string.Join("|", rg.Etat.Lignes), "Repas après le culte", "fin de l'agenda : on reste");
    rg.Bouger(-1); rg.Bouger(-1);
    Ok(rg.ElementCourant == eB && rg.Etat.Ref.StartsWith("Jean 3:17"), "Précédent revient à l'élément d'avant");
    rg.AfficherVerset(1, 1, 1, 1);
    Ok(rg.ElementCourant == null, "autre chose à l'écran : plus d'élément en cours");
    rg.DeplacerAgenda(eT.Id, -2);
    Egal(rg.Agenda.Elements[0].Id, eT.Id, "déplacer en tête");
    var a2 = rg.NouvelAgenda("Culte de Noël");
    Ok(rg.Agenda.Id == a2.Id && rg.Agenda.Elements.Count == 0, "nouvel agenda choisi");
    rg.ChoisirAgenda(ag.Id);
    Egal(rg.Agenda.Elements.Count, 3, "retour au premier agenda");
    var don2 = Donnees.Charger(don.Dossier, res);
    Ok(don2.Agendas.Count == 2 && don2.Agendas.Any(a => a.Nom == "Culte de Noël") && don2.Agendas.First(a => a.Id == ag.Id).Elements.Count == 3, "agendas enregistrés");
    rg.SupprimerAgenda(a2.Id);
    Egal(rg.Agendas.Count, 1, "agenda supprimé");

    var notes = "La grâce qui transforme\n1. Sauvés par grâce — Éphésiens 2:8-9\n2. Rm 5.8 et 2 Co 5:17\nAppel : Jean 3:16, puis Psaume 23. Une 2 fois, Rom 8, le 3:16, culte à 10:30";
    Egal(string.Join(" | ", rg.RefsDansNotes(notes).Select(x => x.Libelle)), "Éphésiens 2:8-9 | Romains 5:8 | 2 Corinthiens 5:17 | Jean 3:16 | Psaumes 23", "versets trouvés dans les notes");

    // 5.2 (design Aube) : compte à rebours, heures, éléments faits, résumé, displays
    var t0 = new DateTime(2026, 11, 15, 9, 55, 0);
    rg.Horloge = () => t0;
    rg.OublierResume();
    var eCpt = rg.AjouterCompteAgenda(5);
    Egal(rg.Agenda.Elements[0].Id, eCpt.Id, "compte à rebours : en tête du déroulé");
    rg.AfficherElement(eCpt);
    Egal(string.Join("|", rg.Etat.Lignes), "Le culte commence dans|05:00", "compte à rebours à l'écran");
    rg.Horloge = () => t0.AddSeconds(61);
    Ok(rg.TicCompte() && rg.Etat.Lignes[1] == "03:59", "le compte à rebours avance : " + rg.Etat.Lignes[1]);
    rg.Horloge = () => t0.AddMinutes(7);
    rg.TicCompte();
    Egal(rg.Etat.Lignes[1], "00:00", "compte à rebours fini");
    Ok(rg.EstFait(eCpt) && !rg.EstFait(eC), "élément fait (pas encore le chant)");
    Egal(Regie.LireHeure("10h12"), "10:12", "heure « 10h12 »");
    Egal(Regie.LireHeure("9h") + "|" + (Regie.LireHeure("25:00") ?? "null"), "09:00|null", "heures « 9h » et « 25:00 »");
    rg.HeureAgenda(eC.Id, "10h");
    Egal(rg.Agenda.Elements.First(x => x.Id == eC.Id).Heure, "10:00", "heure prévue enregistrée");
    rg.AfficherElement(eC, 0); rg.Bouger(1);
    Egal(rg.Resume(), "Accueil · 1 - Chant de test (2 couplets)", "résumé du culte");
    Ok(rg.ResumePartage().Contains("• 1 - Chant de test (2 couplets)") && rg.DeroulePartage().Contains("10:00 — 1 - Chant de test"), "résumé et déroulé à partager");
    var an = rg.NouvelleAnnonce("Bienvenue", "Bienvenue à tous !\n\nRepas après le culte");
    rg.AfficherAnnonce(an);
    Ok(rg.Etat.Lignes[0] == "Bienvenue à tous !" && rg.ElementCourant == null, "display à l'écran");
    Egal(rg.Prochaine()?.Texte, "Repas après le culte", "display : diapositive suivante");
    rg.Bouger(1);
    Egal(rg.Etat.Lignes[0], "Repas après le culte", "display : Suivant");
    var eAn = rg.AjouterAnnonceAgenda(an.Id)!;
    Ok(eAn.Type == "texte" && rg.Diapos(eAn).Count == 2, "display ajoutée au déroulé");
    rg.ModifierAnnonce(an.Id, "Bienvenue !", "Bonjour");
    Ok(Donnees.Charger(don.Dossier, res).Annonces.Any(a => a.Titre == "Bienvenue !"), "displays enregistrées");
    rg.SupprimerAnnonce(an.Id);
    Egal(rg.Annonces.Count, 0, "display supprimée");
    rg.Horloge = () => DateTime.Now;

    var c1 = rg.Commande("jean 3 16");
    Ok(c1.Count > 0 && c1[0].Type == "BIBLE" && c1[0].Titre == "Jean 3:16", "commande « jean 3 16 »");
    var c2 = rg.Commande("chant 1", "Recueil A");
    Ok(c2.Count > 0 && c2[0].ChantId == idChant, "commande « chant 1 » : le recueil choisi d'abord");
    Ok(!rg.Commande("minuteur 35").Any(x => x.Action == "minuteur"), "3.0 : plus de minuteur de prédication");
    Ok(rg.Commande("Chant de test").Any(x => x.ChantId == idChant), "commande : titre d'un chant");
    Ok(rg.Commande("romains").Any(x => x.Type == "BIBLE" && x.Titre == "Romains 1"), "commande : nom d'un livre");
}

Groupe("Ecran par defaut 3.3");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var idChant = rg.EnregistrerChant(null, "Chant de reprise", new List<Section> { new() { Label = "1", Text = "A" }, new() { Label = "2", Text = "B" }, new() { Label = "3", Text = "C" } }, "Recueil R");
    var logo = rg.NouveauDisplay("Logo MEPC", new SourceDisplay { Type = "image", Chemin = "logo.png" });
    var bienvenue = rg.NouvelleAnnonce("Bienvenue", "Bienvenue\nMEPC Montréal\n\nDeuxième diapositive");
    var vide = rg.NouvelleAnnonce("Vide", "");
    var eP = rg.AjouterPrincipalAgenda(logo.Id)!;
    var eC = rg.AjouterChantAgenda(idChant)!;
    var eT = rg.AjouterPrincipalAgenda(bienvenue.Id)!;
    var eF = rg.AjouterPrincipalAgenda(null)!;
    Ok(eP.Type == "principal" && eP.Source?.Chemin == "logo.png" && eT.Texte!.StartsWith("Bienvenue") && eF.Titre == "Fond seul", "écrans principaux ajoutés au déroulé (image, texte, fond seul)");
    Ok(rg.AjouterPrincipalAgenda(vide.Id) == null && rg.AjouterPrincipalAgenda("absent") == null, "un texte vide ou une source absente : rien n'est ajouté");
    Egal(rg.Description(eP).Type, "ÉCRAN PRINCIPAL", "type affiché dans le déroulé");
    Egal(rg.Description(eT).Sous, "Texte · reste à l'écran", "détail affiché dans le déroulé");
    Egal(rg.Diapos(eP).Count, 1, "un écran principal = une diapositive");
    Egal(string.Join("|", rg.EcransPrincipaux.Select(e => e.Titre)), "Logo MEPC|Bienvenue|Fond seul", "écrans principaux du déroulé, dans l'ordre");
    Egal(rg.EcranDefaut, "noir", "écran par défaut au départ : noir (comme avant)");

    rg.AfficherElement(eC, 1);
    rg.Effacer();
    Ok(rg.Etat.Vide && rg.Cible == null && rg.ElementCourant == null, "écran par défaut noir : Retirer vide l'écran comme avant");

    int oeil = -1;
    rg.Oeil += v => oeil = v ? 1 : 0;
    rg.AfficherElement(eP, 0);
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media?.Chemin == "logo.png" && rg.ElementCourant == eP && rg.Cible is CibleDefaut, "écran principal à l'écran (déroulé)");
    Egal(rg.EcranDefaut, eP.Id, "il devient l'écran par défaut");
    Egal(rg.LibelleEcranDefaut, "Logo MEPC", "libellé de l'écran par défaut");
    Egal(oeil, 0, "OBS : la source reste cachée pour un écran principal");
    Ok(rg.EtatDiffusion(rg.Etat).Vide && !rg.Etat.Vide, "OBS, caméra virtuelle et page publique : rien, la salle voit l'écran principal");
    Egal(rg.PositionAgenda(eP), 0, "l'écran principal est l'élément en direct");

    rg.Bouger(1);
    Ok(rg.ElementCourant == eC && rg.Etat.Lignes[0] == "A", "Suivant : le chant après l'écran principal");
    rg.Bouger(1);
    rg.Effacer();
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media?.Chemin == "logo.png" && rg.Cible is CibleDefaut { } cd1 && cd1.Id == eP.Id && rg.ElementCourant == null,
        "Retirer : retour direct sur l'écran par défaut");
    Ok(rg.EtatDiffusion(rg.Etat).Vide, "Retirer : OBS reste vide comme avant");
    Egal(rg.Prochaine()?.Label, "3", "Suivant annoncé : la strophe après celle retirée");
    Ok(rg.SuivanteDeroule() is { } sd && sd.E == eC && sd.I == 2, "vignette SUIVANT : la strophe après celle retirée");
    rg.Effacer();
    Ok(rg.Cible is CibleDefaut && rg.Prochaine()?.Label == "3", "Retirer deux fois : on reste sur l'écran par défaut, la reprise est gardée");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eC && rg.Etat.Lignes[0] == "C", "Suivant reprend après la strophe retirée");
    rg.Effacer();
    rg.Bouger(-1);
    Ok(rg.ElementCourant == eC && rg.Etat.Lignes[0] == "C", "Précédent remet la strophe retirée");

    rg.Bouger(1);
    Ok(rg.ElementCourant == eT && rg.Etat.Mode == "chant" && string.Join("|", rg.Etat.Lignes) == "Bienvenue|MEPC Montréal" && rg.EcranDefaut == eT.Id,
        "écran principal texte : sa première diapositive, et il devient l'écran par défaut");
    rg.Effacer();
    Ok(rg.Cible is CibleDefaut { } cd2 && cd2.Id == eT.Id && rg.ElementCourant == null, "Retirer sur un écran principal : il reste");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eF && rg.Etat.Mode == "fond", "Suivant après Retirer sur un écran principal : l'élément d'après");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eF && rg.Etat.Mode == "fond", "fin du déroulé : on reste");

    rg.ChoisirEcranDefaut(eT.Id);
    Ok(rg.ElementCourant == eF && rg.Etat.Mode == "fond" && rg.EcranDefaut == eT.Id, "choisir l'écran par défaut ne remplace pas un écran principal affiché depuis le déroulé");
    rg.AfficherElement(eC, 1);
    rg.ChoisirEcranDefaut("fond");
    Ok(rg.Etat.Lignes.FirstOrDefault() == "B" && rg.EcranDefaut == "fond", "choisir l'écran par défaut ne touche pas aux paroles à l'écran");
    rg.Effacer();
    Ok(rg.Etat.Mode == "fond" && !rg.Etat.Vide && rg.Etat.Lignes.Count == 0 && rg.Cible is CibleDefaut { Id: "fond" }, "fond seul : l'habillage sans texte");
    rg.ChoisirEcranDefaut(eP.Id);
    Ok(rg.Etat.Mode == "media" && rg.Cible is CibleDefaut { } cd3 && cd3.Id == eP.Id, "changer d'écran par défaut pendant qu'il est à l'écran : il est remplacé tout de suite");
    Egal(rg.Prochaine()?.Label, "3", "… et Suivant reprend toujours après les paroles retirées");
    rg.ChoisirEcranDefaut("absent");
    Egal(rg.EcranDefaut, eP.Id, "choix inconnu : ignoré");
    Egal(Donnees.Charger(don.Dossier, res).Reglages.EcranDefaut, eP.Id, "écran par défaut enregistré");

    rg.ChoisirEcranDefaut("noir");
    Ok(rg.Etat.Vide && rg.Cible == null, "écran par défaut noir pendant qu'il est à l'écran : l'écran se vide");
    rg.ChoisirEcranDefaut("fond");
    Ok(rg.Etat.Mode == "fond" && rg.Cible is CibleDefaut, "écran vide : le nouvel écran par défaut s'affiche tout de suite");

    Ok(rg.EtatCible(rg.Diapos(eT)[0].Cible, eT) is { Mode: "chant" } vt && vt.Lignes[0] == "Bienvenue", "vignette d'un écran principal texte");
    Ok(rg.EtatCible(rg.Diapos(eP)[0].Cible) is { Mode: "media" } vp && vp.Media?.Chemin == "logo.png", "vignette d'un écran principal image");
    Ok(rg.EtatCible(rg.Diapos(eF)[0].Cible, eF) is { Mode: "fond" } && rg.EtatCible(new CibleDefaut("fond")) is { Mode: "fond" }, "vignette du fond seul");

    rg.AfficherElement(eC, 0);
    // 2.5.0 : un calque reste quand on retire le contenu principal
    var annonceCalque = rg.NouvelleAnnonce("Annonce en calque", "Bienvenue à tous");
    Ok(rg.Superposer(annonceCalque) is { L: < 1 } && !rg.Etat.Vide, "2.5.0 : superposer garde ce qui est à l'écran (le calque dans un coin)");
    rg.Effacer();
    Ok(rg.Etat.Mode == "fond" && rg.Calques.Count == 1, "Retirer avec un calque : le fond par défaut, le calque reste");
    Ok(rg.EtatDiffusion(rg.Etat) is { Mode: "vide" } dd && dd.Calques is { Count: 1 }, "OBS : seulement le calque");
    rg.RetirerCalques();
    rg.SupprimerAnnonce(annonceCalque.Id);

    rg.AfficherVerset(42, 3, 16, 16);
    rg.Effacer();
    rg.Bouger(1);
    Ok(rg.Etat.Ref.StartsWith("Jean 3:17") && rg.ElementCourant == null, "Bible : Suivant après Retirer passe au verset suivant");
    rg.Effacer();
    rg.Bouger(-1);
    Ok(rg.Etat.Ref.StartsWith("Jean 3:17"), "Bible : Précédent remet le verset retiré");

    rg.AfficherSection(idChant, 0);
    rg.Effacer();
    rg.RetirerAgenda(eF.Id);
    rg.ChoisirAgenda(rg.NouvelAgenda("Autre culte").Id);
    Egal(rg.EcranDefaut, "fond", "autre culte : fond seul garde son sens");
    rg.ChoisirEcranDefaut(eP.Id);
    Egal(rg.EcranDefaut, "fond", "un écran principal d'un autre culte : refusé");
    don.Reglages.EcranDefaut = eP.Id;
    Egal(rg.EcranDefaut, "noir", "écran principal absent du culte choisi : noir");
}

Groupe("Display 6.0");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var texte = rg.NouvelleAnnonce("Ancien texte", "Ancienne diapositive");
    var image = rg.NouveauDisplay("Image", new SourceDisplay { Type = "image", Chemin = "/test/image.png" });
    var camera = rg.NouveauDisplay("Carte HDMI", new SourceDisplay { Type = "camera", Peripherique = "@device:pnp:test" });
    var fenetre = rg.NouveauDisplay("Fenêtre", new SourceDisplay { Type = "fenetre", Fenetre = 1234, Processus = 42 });
    var relu = Donnees.Charger(don.Dossier, res);
    Ok(relu.Annonces.Count == 4 && relu.Annonces.First().Source == null, "compatibilité des anciens textes après réouverture");
    Ok(relu.Annonces.Last().Source == fenetre.Source, "identité de fenêtre et processus conservée");
    Ok(relu.Annonces.First(a => a.Id == camera.Id).Source == camera.Source, "identité stable du périphérique vidéo conservée");
    rg.AfficherAnnonce(camera);
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media == camera.Source && rg.Etat.Lignes.Count == 0, "caméra en direct sans texte résiduel");
    rg.AfficherAnnonce(texte);
    Ok(rg.Etat.Mode == "chant" && rg.Etat.Media == null, "retour caméra vers texte : capture arrêtée");
    rg.AfficherAnnonce(image); rg.Effacer();
    Ok(rg.Etat.Vide && rg.Etat.Media == null, "retirer une source coupe la capture");
    var el = rg.AjouterAnnonceAgenda(camera.Id)!;
    var suivant = rg.AjouterTexteAgenda("Après caméra", "Bonjour");
    rg.AfficherElement(el);
    Ok(rg.ElementCourant == el && rg.PositionAgenda(el) == 0 && rg.Diapos(el).Count == 1, "source dans le déroulé du culte");
    rg.Bouger(1);
    Ok(rg.ElementCourant == suivant && rg.Etat.Media == null && rg.Etat.Lignes[0] == "Bonjour", "Suivant quitte la caméra pour la diapositive suivante");
    rg.Bouger(-1);
    Ok(rg.ElementCourant == el && rg.Etat.Media == camera.Source, "Précédent revient à la caméra");
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.Etat.Media == null, "afficher un verset coupe la capture");
    Ok(MiseAJour.PlusRecente("6.0.0", "6.0.0-beta") && !MiseAJour.PlusRecente("6.0.0-beta", "6.0.0"), "passage bêta vers version finale");
}

Groupe("Fenêtre : église, streaming ou les deux (1.3.1)");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var zoom = rg.NouveauDisplay("Zoom Meeting", new SourceDisplay
    {
        Type = "fenetre", Fenetre = 77, Processus = 7, Exe = "Zoom.exe", Classe = "ConfMultiTabContentWndClass", Titre = "Zoom Meeting",
        ZoneClient = true, Diffusion = SourceDisplay.Eglise,
    });
    var diapos = rg.NouveauDisplay("Diapos", new SourceDisplay { Type = "fenetre", Fenetre = 78, Processus = 8, Exe = "POWERPNT.EXE", Titre = "Annonces", Diffusion = SourceDisplay.Streaming });
    var partout = rg.NouveauDisplay("Navigateur", new SourceDisplay { Type = "fenetre", Fenetre = 79, Processus = 9, Exe = "msedge.exe", Titre = "Accueil" });
    var relu = Donnees.Charger(don.Dossier, res);
    Ok(relu.Annonces.First(a => a.Id == zoom.Id).Source == zoom.Source, "fenêtre : programme, classe, titre, zone client et diffusion conservés");
    Ok(relu.Annonces.First(a => a.Id == partout.Id).Source is { Diffusion: null, ZoneClient: false }, "les deux : réglage d'avant la 1.3.1");
    Egal(zoom.Description, "Capture de fenêtre · Dans l'église seulement", "description d'une fenêtre dans l'église");
    Egal(SourceDisplay.NomDiffusion(null), "Église et MyStream", "les deux, en mots");
    var ancienne = System.Text.Json.JsonSerializer.Deserialize<SourceDisplay>("{\"type\":\"fenetre\",\"fenetre\":5,\"processus\":6}")!;
    Ok(ancienne is { Diffusion: null, ZoneClient: false, Exe: null } && !ancienne.EgliseSeulement && !ancienne.StreamingSeulement,
        "source d'avant la 1.3.1 : les deux, fenêtre entière");

    rg.AfficherAnnonce(zoom);
    Ok(rg.Etat.Mode == "media" && rg.EtatSalle(rg.Etat) == rg.Etat, "Dans l'église : l'écran de projection montre la fenêtre");
    Ok(rg.EtatDiffusion(rg.Etat).Vide, "Dans l'église : ni MyVirtualCam, ni OBS, ni la page publique (pas d'effet miroir dans Zoom)");
    rg.AfficherAnnonce(partout);
    Ok(rg.EtatSalle(rg.Etat).Media == partout.Source && rg.EtatDiffusion(rg.Etat).Media == partout.Source, "les deux : partout, comme avant");

    rg.AfficherAnnonce(diapos);
    Ok(rg.EtatDiffusion(rg.Etat) is { Mode: "media" } ds && ds.Media == diapos.Source, "En streaming : MyVirtualCam et OBS la reçoivent");
    Ok(rg.EtatSalle(rg.Etat).Vide, "En streaming, écran par défaut noir : la salle reste vide");
    rg.ChoisirEcranDefaut("fond");
    Ok(rg.Etat.Media == diapos.Source && rg.EtatSalle(rg.Etat).Mode == "fond", "En streaming, écran par défaut « fond seul » : la salle garde l'habillage");
    var logo = rg.NouveauDisplay("Logo", new SourceDisplay { Type = "image", Chemin = "logo.png" });
    var eLogo = rg.AjouterPrincipalAgenda(logo.Id)!;
    rg.ChoisirEcranDefaut(eLogo.Id);
    Ok(rg.Etat.Media == diapos.Source && rg.EtatSalle(rg.Etat) is { Mode: "media" } sl && sl.Media?.Chemin == "logo.png",
        "En streaming : la salle garde l'écran principal choisi (le logo)");
    Ok(rg.EtatDiffusion(rg.Etat).Media == diapos.Source, "… pendant que MyVirtualCam reçoit les diapos");

    // 2.5.0 : les calques : un calque « Dans l'église » ne part pas en streaming, un calque « En streaming » n'est pas dans la salle
    rg.Superposer(zoom);
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.EtatSalle(rg.Etat) is { Mode: "verset" } s1 && s1.Calques?.Any(c => c.Media == zoom.Source) == true, "calques : la salle voit le verset et Zoom superposé");
    Ok(rg.EtatDiffusion(rg.Etat) is { Mode: "verset" } d1 && d1.Calques == null, "calques : MyVirtualCam reçoit le verset, sans Zoom");
    rg.RetirerCalques();
    rg.Superposer(diapos);
    rg.AfficherVerset(42, 3, 17, 17);
    Ok(rg.EtatSalle(rg.Etat).Calques == null && rg.EtatDiffusion(rg.Etat).Calques?.Any(c => c.Media == diapos.Source) == true, "calques : des diapos superposées « En streaming » restent hors de la salle");
    rg.RetirerCalques();

    rg.AfficherAnnonce(zoom);
    Ok(rg.ModifierSource(zoom.Id, "Zoom (culte)", zoom.Source! with { Diffusion = null }), "modifier une fenêtre : accepté");
    Ok(rg.Etat.Media is { Diffusion: null } && rg.Etat.Ref == "Zoom (culte)" && rg.EtatDiffusion(rg.Etat).Mode == "media",
        "à l'écran : le nouveau réglage s'applique tout de suite");
    Ok(Donnees.Charger(don.Dossier, res).Annonces.First(a => a.Id == zoom.Id) is { Titre: "Zoom (culte)", Source.Diffusion: null }, "réglage enregistré");
    rg.Superposer(zoom);
    Ok(rg.ModifierSource(zoom.Id, "", zoom.Source! with { Diffusion = SourceDisplay.Eglise }) && rg.Calques[^1].Media?.EgliseSeulement == true && rg.EtatDiffusion(rg.Etat).Calques == null,
        "superposée : le nouveau réglage suit aussi");
    Ok(!rg.ModifierSource(zoom.Id, "", new SourceDisplay { Type = "image", Chemin = "x.png" }) && !rg.ModifierSource("absent", "", zoom.Source!),
        "changer de type, ou une source absente : refusé");
}

Groupe("MyVirtualCam : l'écran principal au lieu du noir (1.3.2)");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var zoom = rg.NouveauDisplay("Zoom", new SourceDisplay { Type = "fenetre", Fenetre = 77, Processus = 7, Exe = "Zoom.exe", Titre = "Zoom Meeting", Diffusion = SourceDisplay.Eglise });
    var logo = rg.NouveauDisplay("Logo", new SourceDisplay { Type = "image", Chemin = "logo.png" });
    var eLogo = rg.AjouterPrincipalAgenda(logo.Id)!;

    rg.AfficherAnnonce(zoom);
    Ok(rg.EtatCamera(rg.Etat).Vide, "écran par défaut noir : MyVirtualCam reste noire pendant Zoom");
    rg.ChoisirEcranDefaut("fond");
    Ok(rg.EtatCamera(rg.Etat).Mode == "fond", "écran par défaut « fond seul » : MyVirtualCam montre l'habillage pendant Zoom");
    rg.ChoisirEcranDefaut(eLogo.Id);
    Ok(rg.Etat.Media == zoom.Source && rg.EtatSalle(rg.Etat).Media == zoom.Source, "Zoom reste à l'écran de l'église");
    Ok(rg.EtatCamera(rg.Etat) is { Mode: "media" } c1 && c1.Media?.Chemin == "logo.png",
        "pendant Zoom dans l'église : MyVirtualCam montre l'écran principal (le logo), pas du noir");
    Ok(rg.EtatDiffusion(rg.Etat).Vide, "OBS et la page publique : rien, comme avant");

    rg.Effacer();
    Ok(rg.Cible is CibleDefaut && rg.EtatCamera(rg.Etat).Media?.Chemin == "logo.png" && rg.EtatDiffusion(rg.Etat).Vide,
        "Retirer : l'écran principal va dans l'église et sur MyVirtualCam (OBS reste vide)");
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.EtatCamera(rg.Etat) == rg.Etat, "un verset : MyVirtualCam comme l'écran de projection");

    rg.Superposer(zoom);
    rg.AfficherVerset(42, 3, 17, 17);
    Ok(rg.EtatCamera(rg.Etat) is { Mode: "verset" } c2 && c2.Calques == null, "Zoom superposé : MyVirtualCam montre le verset, sans Zoom");
    rg.RetirerCalques();

    // Zoom lui-même comme écran principal : il ne repart jamais vers MyVirtualCam
    var eZoom = rg.AjouterPrincipalAgenda(zoom.Id)!;
    rg.ChoisirEcranDefaut(eZoom.Id);
    rg.AfficherAnnonce(zoom);
    Ok(rg.EtatCamera(rg.Etat).Vide, "écran principal « Dans l'église » : MyVirtualCam reste vide plutôt que de renvoyer Zoom");
    rg.Effacer();
    Ok(rg.Cible is CibleDefaut && rg.EtatSalle(rg.Etat).Media == zoom.Source && rg.EtatCamera(rg.Etat).Vide, "… même affiché comme écran par défaut");
}

Groupe("MyStream (1.6.4 / 1.6.6) : indépendant de l'église");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var cam = rg.NouveauDisplay("MyCam Samsung", new SourceDisplay { Type = "mycam", Peripherique = "tel-1" });
    var image = rg.NouveauDisplay("Annonce repas", new SourceDisplay { Type = "image", Chemin = "repas.png" });
    var texte = rg.NouveauDisplay("Bienvenue", new SourceDisplay { Type = "image", Chemin = "x.png" });
    texte.Source = null; texte.Texte = "Bienvenue à tous";

    // 1.6.6 : sans source principale, MyStream reste noir (les paroles et les versets ne partent jamais tout seuls)
    rg.AfficherVerset(43, 3, 16, 16);
    Ok(rg.SourcePrincipaleStream == null && rg.EtatStream(rg.Etat) is { Mode: "vide", Media: null } && rg.EtatStream(rg.Etat).Lignes.Count == 0,
        "1.6.6 : sans source principale, un verset reste dans l'église : MyStream noir");
    Ok(rg.EnvoyerDansStream() && rg.StreamSuitEglise && rg.EtatStream(rg.Etat) == rg.EtatCamera(rg.Etat),
        "1.6.6 : sans source principale, « Envoyer dans le stream » : MyStream montre l'église (comme Zoom avant)");
    Ok(rg.StreamPrincipale() && !rg.StreamSuitEglise && rg.EtatStream(rg.Etat).Mode == "vide", "1.6.6 : M : MyStream ne suit plus l'église (noir)");
    Ok(!rg.StreamPrincipale(), "1.6.6 : M sans source principale (et sans suivre l'église) : rien à faire");

    // une source principale : MyStream la montre, quoi que montre l'église
    rg.ChoisirSourceStream(texte.Id);
    Ok(rg.SourcePrincipaleStream == null, "un texte ne peut pas être la source principale");
    rg.ChoisirSourceStream(cam.Id);
    Ok(rg.SourcePrincipaleStream?.Id == cam.Id && Donnees.Charger(don.Dossier, res).Reglages.MyStreamSource == cam.Id, "la source principale est choisie et gardée");
    Ok(rg.SourcesStreamPossibles.Select(a => a.Id).SequenceEqual(new[] { cam.Id, image.Id }), "les sources possibles : MyCam d'abord, pas les textes");
    Ok(rg.StreamSurPrincipale && !rg.StreamSuitEglise, "MyStream sur sa source principale");
    var s1 = rg.EtatStream(rg.Etat);
    Ok(s1 is { Mode: "media", Presentation: "plein" } && s1.Media == cam.Source && s1.Lignes.Count == 0 && !Regie.TexteSurImage(s1),
        "un verset à l'église : MyStream garde la caméra, sans paroles par-dessus");
    rg.AfficherAnnonce(image);
    Ok(rg.EtatStream(rg.Etat).Media == cam.Source && rg.Etat.Media == image.Source, "une image à l'église : MyStream garde la caméra");
    rg.Effacer();
    Ok(rg.EtatStream(rg.Etat) is { Mode: "media" } s0 && s0.Media == cam.Source, "l'église vide : MyStream montre la caméra");

    // « Envoyer dans le stream » : MyStream montre ce que montre l'église et la suit
    rg.AfficherVerset(43, 3, 16, 16);
    Ok(rg.EnvoyerDansStream() && rg.StreamSuitEglise && !rg.StreamSurPrincipale, "« Envoyer dans le stream » : MyStream suit l'église");
    Ok(rg.EtatStream(rg.Etat) == rg.EtatCamera(rg.Etat) && rg.EtatStream(rg.Etat).Mode == "verset", "… le verset part sur MyStream, comme à l'église (plein écran)");
    rg.AfficherVerset(43, 3, 17, 17);
    Ok(rg.EtatStream(rg.Etat) is { Mode: "verset" } v17 && v17.Ref.Contains("3:17"), "le verset suivant part aussi sur MyStream");
    rg.AfficherAnnonce(image);
    Ok(rg.EtatStream(rg.Etat).Media == image.Source, "une image montrée ensuite : sur MyStream aussi");
    Ok(rg.EnvoyerDansStream() && rg.StreamSuitEglise, "« Envoyer » une 2e fois : MyStream suit toujours");
    // l'église n'a rien pour Internet : la source principale, et MyStream suit toujours l'église
    rg.ChoisirEcranDefaut("fond");
    rg.Effacer();
    Ok(rg.Cible is CibleDefaut && rg.EtatSalle(rg.Etat).Mode == "fond" && rg.EtatStream(rg.Etat).Media == cam.Source && rg.StreamSuitEglise,
        "l'église sur son écran de repos : MyStream sur la caméra (et suit toujours l'église)");
    var noir = rg.Etat with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null, Calques = null };
    Ok(rg.EtatStream(noir).Media == cam.Source, "écran noir : MyStream montre la caméra");
    rg.AfficherVerset(43, 3, 16, 16);
    Ok(rg.EtatStream(rg.Etat).Mode == "verset", "… et le verset suivant repart sur MyStream");
    // 2.5.0 : une source superposée (un calque), l'église sans rien pour Internet
    var annonceCoin = rg.NouvelleAnnonce("Coin", "Bienvenue");
    rg.Superposer(annonceCoin);
    rg.Effacer();
    Ok(rg.EtatStream(rg.Etat) is { Mode: "media" } s3 && s3.Media == cam.Source && s3.Calques?.FirstOrDefault()?.Libelle == "Coin", "un calque (MyStream suit l'église) : la caméra avec le calque");

    // « Source principale » (touche M) : MyStream revient sur la caméra, l'église garde ce qu'elle montre
    Ok(rg.StreamPrincipale() && rg.StreamSurPrincipale && !rg.StreamSuitEglise, "touche M : MyStream revient sur sa source principale");
    Ok(rg.EtatStream(rg.Etat) is { Mode: "media", Calques: null } s4 && s4.Media == cam.Source, "… la caméra seule, sans le calque");
    rg.RetirerCalques();
    rg.AfficherVerset(43, 3, 17, 17);
    Ok(rg.EtatStream(rg.Etat).Media == cam.Source && rg.Etat.Mode == "verset", "le verset suivant reste dans l'église : MyStream garde la caméra");
    Ok(rg.StreamPrincipale() && rg.StreamSurPrincipale, "touche M déjà sur la source principale : rien ne change");
    // choisir une autre source principale : MyStream la montre (et ne suit plus l'église)
    rg.EnvoyerDansStream();
    rg.ChoisirSourceStream(image.Id);
    Ok(!rg.StreamSuitEglise && rg.EtatStream(rg.Etat).Media == image.Source, "une autre source principale : MyStream la montre aussitôt");
    rg.ChoisirSourceStream(cam.Id);

    // une fenêtre « Dans l'église » (Zoom projeté) : jamais sur MyStream
    var zoomEglise = rg.NouveauDisplay("Zoom", new SourceDisplay { Type = "fenetre", Fenetre = 77, Processus = 7, Exe = "Zoom.exe", Titre = "Zoom Meeting", Diffusion = SourceDisplay.Eglise });
    rg.AfficherAnnonce(zoomEglise);
    Ok(rg.EtatSalle(rg.Etat).Media == zoomEglise.Source && rg.EtatStream(rg.Etat).Media == cam.Source, "une source « Dans l'église » : MyStream montre la caméra");
    rg.EnvoyerDansStream();
    Ok(rg.EtatStream(rg.Etat).Media == cam.Source, "… même quand MyStream suit l'église");
    rg.StreamPrincipale();
    // une source « MyStream seulement » : faite pour Internet, elle y part toujours
    var dansStream = rg.NouveauDisplay("Diapos", new SourceDisplay { Type = "fenetre", Fenetre = 78, Processus = 8, Titre = "Annonces", Diffusion = SourceDisplay.Streaming });
    rg.AfficherAnnonce(dansStream);
    Ok(rg.EtatStream(rg.Etat).Media == dansStream.Source && rg.EtatSalle(rg.Etat).Media != dansStream.Source, "une source « MyStream seulement » : sur MyStream, pas dans l'église");
    rg.AfficherVerset(43, 3, 16, 16);
    Ok(rg.EtatStream(rg.Etat).Media == cam.Source, "… le verset suivant (dans l'église) : MyStream revient sur la caméra");

    // une source principale supprimée : MyStream noir
    rg.SupprimerAnnonce(cam.Id);
    rg.AfficherVerset(43, 3, 16, 16);
    Ok(rg.SourcePrincipaleStream == null && rg.EtatStream(rg.Etat).Mode == "vide" && !rg.StreamPrincipale(), "source principale supprimée : MyStream noir, M ne fait rien");

    // 1.6.6 : la première caméra montrée devient la source principale (les paroles restent ensuite dans l'église)
    string? dit = null;
    rg.Message += m => dit = m;
    var usb = rg.NouveauDisplay("USB Video", new SourceDisplay { Type = "camera", Peripherique = "@device:usb" });
    rg.AfficherAnnonce(image);
    Ok(rg.SourcePrincipaleStream == null, "1.6.6 : une image montrée ne devient pas la source principale");
    rg.AfficherAnnonce(usb);
    Ok(rg.SourcePrincipaleStream?.Id == usb.Id && dit != null && dit.Contains("USB Video") && Donnees.Charger(don.Dossier, res).Reglages.MyStreamSource == usb.Id,
        "1.6.6 : une caméra montrée sans source principale le devient : « " + dit + " »");
    rg.AfficherVerset(43, 3, 16, 16);
    Ok(rg.EtatStream(rg.Etat).Media == usb.Source && rg.Etat.Mode == "verset", "1.6.6 : ensuite, le verset reste dans l'église : MyStream garde la caméra");
    // « Aucune » choisie exprès : plus d'adoption
    rg.ChoisirSourceStream(null);
    Ok(rg.AucuneSourceStream && rg.EtatStream(rg.Etat).Mode == "vide", "1.6.6 : « Aucune » : MyStream noir");
    rg.AfficherAnnonce(usb);
    Ok(rg.SourcePrincipaleStream == null && Donnees.Charger(don.Dossier, res).Reglages.MyStreamAucune, "1.6.6 : « Aucune » choisie exprès : la caméra montrée ne devient pas la source principale");
    rg.ChoisirSourceStream(usb.Id);
    Ok(!rg.AucuneSourceStream && rg.SourcePrincipaleStream?.Id == usb.Id, "1.6.6 : une source choisie de nouveau");
    // une caméra « Dans l'église seulement » ne devient pas la source principale
    var don2 = Donnees.Charger(Temp(), res);
    var rg2 = new Regie(don2, new Bibles(don2));
    var chaire = rg2.NouveauDisplay("Caméra chaire", new SourceDisplay { Type = "camera", Peripherique = "@device:x", Diffusion = SourceDisplay.Eglise });
    rg2.AfficherAnnonce(chaire);
    Ok(rg2.SourcePrincipaleStream == null, "1.6.6 : une caméra « Dans l'église seulement » ne devient pas la source principale");
    var tel = rg2.NouveauDisplay("MyCam", new SourceDisplay { Type = "mycam", Peripherique = "tel" });
    rg2.AfficherAnnonce(tel);
    Ok(rg2.SourcePrincipaleStream?.Id == tel.Id, "1.6.6 : MyCam montrée : elle devient la source principale");

    // 1.6.8 : MyVirtualCam choisie comme média (l'image de MyStream, montrée dans l'église) ne va jamais sur MyStream
    const string idVcam = @"@device:sw:{860BB310-5D01-11D0-BD3B-00A0C911CE86}\{a9d47a18-c751-4bde-ab6b-a3d13f090501}";
    Ok(new SourceDisplay { Type = "camera", Peripherique = idVcam }.EgliseSeulement && new SourceDisplay { Type = "camera", Peripherique = idVcam, Diffusion = SourceDisplay.Streaming } is { EgliseSeulement: true, StreamingSeulement: false }
        && !new SourceDisplay { Type = "camera", Peripherique = "@device:pnp:usb#vid_0fd9" }.EstMyVirtualCam && !Regie.SourceStreamPossible(new SourceDisplay { Type = "camera", Peripherique = idVcam }),
        "1.6.8 : MyVirtualCam reconnue à son identité : toujours « Dans l'église seulement », jamais source de MyStream");
    var don3 = Donnees.Charger(Temp(), res);
    var rg3 = new Regie(don3, new Bibles(don3));
    var vcam = rg3.NouveauDisplay("MyVirtualCam (MyStream)", new SourceDisplay { Type = "camera", Peripherique = idVcam, Diffusion = SourceDisplay.Eglise, Ips = 30, Taille = "720p" });
    rg3.AfficherAnnonce(vcam);
    Ok(rg3.Etat.Media == vcam.Source && rg3.EtatSalle(rg3.Etat).Media == vcam.Source && rg3.SourcePrincipaleStream == null && rg3.EtatStream(rg3.Etat).Mode == "vide",
        "1.6.8 : MyVirtualCam montrée dans l'église : elle ne devient pas la source principale, MyStream reste noir");
    rg3.ChoisirSourceStream(vcam.Id);
    Ok(rg3.SourcePrincipaleStream == null && !rg3.SourcesStreamPossibles.Contains(vcam), "1.6.8 : MyVirtualCam ne se choisit pas comme source de MyStream");
    rg3.EnvoyerDansStream();
    Ok(rg3.EtatStream(rg3.Etat).Media != vcam.Source, "1.6.8 : « Envoyer dans le stream » pendant MyVirtualCam : elle ne repart pas sur MyStream");
    var cam3 = rg3.NouveauDisplay("USB", new SourceDisplay { Type = "camera", Peripherique = "@device:usb" });
    rg3.ChoisirSourceStream(cam3.Id);
    rg3.EnvoyerDansStream();
    rg3.AfficherAnnonce(vcam);
    Ok(rg3.EtatStream(rg3.Etat).Media == cam3.Source, "1.6.8 : MyStream suit l'église, l'église montre MyVirtualCam : MyStream garde sa caméra (pas de tunnel)");
    Ok(vcam.Description.Contains("MyVirtualCam") && !vcam.Description.Contains("images/s"), "1.6.8 : sa description : « " + vcam.Description + " »");
}

Groupe("Calques de Médias (2.5.0)");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    int changes = 0; bool? oeil = null; string dit = "";
    rg.Change += () => changes++;
    rg.Oeil += v => oeil = v;
    rg.Message += m => dit = m;
    var texte = rg.NouvelleAnnonce("Annonce", "Repas après le culte\n\nDeuxième diapo");
    var camera = rg.NouveauDisplay("Caméra salle", new SourceDisplay { Type = "camera", Peripherique = "@device:pnp:salle" });
    var logo = rg.NouveauDisplay("Logo", new SourceDisplay { Type = "image", Chemin = "logo.png" });
    var vide = rg.NouvelleAnnonce("Vide", "");
    Ok(!System.Text.Json.JsonSerializer.Serialize(rg.Etat, Donnees.Json).Contains("\"calques\""), "etat.json sans calque : pas de clé « calques »");
    Ok(rg.Superposer(vide) == null && dit.Contains("vide") && rg.Calques.Count == 0, "superposer un texte vide : refusé, avec un message");
    // écran vide : la première source superposée prend tout l'écran
    oeil = null;
    var c1 = rg.Superposer(camera)!;
    Ok(c1 is { X: 0, Y: 0, L: 1, H: 1, Visible: true } && c1.Etat.Mode == "media" && c1.Media == camera.Source && rg.Etat.Vide && oeil == true,
        "écran vide : la caméra superposée prend tout l'écran (la source OBS se montre)");
    // un verset (contenu principal) : les calques restent
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.Etat.Mode == "verset" && rg.Calques.Count == 1 && rg.Etat.Rang == 0, "un verset à l'écran : le calque reste");
    var c2 = rg.Superposer(texte)!;
    Ok(c2.L < 1 && c2.X > 0.5 && c2.Y < 0.1 && c2.Etat.Mode == "chant" && c2.Etat.Lignes[0] == "Repas après le culte", "un texte superposé : un coin (haut droite), sa première diapositive");
    var c3 = rg.Superposer(logo)!;
    Ok(c3.X < 0.1 && c3.Y < 0.1, "la source suivante : un autre coin (haut gauche)");
    Ok(rg.Calques.Select(c => c.Id).SequenceEqual(new[] { c1.Id, c2.Id, c3.Id }), "les calques, du dessous vers le dessus (la dernière superposée en haut)");
    // place et taille gardées dans l'écran
    Ok(rg.PlacerCalque(c2.Id, 0.9, -0.5, 0.5, 2) && rg.Calques[1] is { X: 0.5, Y: 0, L: 0.5, H: 1 }, "place et taille gardées dans l'écran");
    Ok(rg.PlacerCalque(c2.Id, 0.2, 0.3, 0.001, 0.4) && rg.Calques[1].L == Regie.TailleMinCalque, "taille minimale : 5 % de l'écran");
    Ok(!rg.PlacerCalque("absent", 0, 0, 1, 1), "un calque absent : refusé");
    // ▲ ▼ : la pile entière, le contenu principal compris
    Ok(!rg.MonterCalque(c3.Id, 1), "le calque du haut ne monte pas plus haut");
    Ok(rg.MonterCalque(c3.Id, -1) && rg.Calques.Select(c => c.Id).SequenceEqual(new[] { c1.Id, c3.Id, c2.Id }), "▼ : le logo passe derrière le texte");
    Ok(rg.MonterCalque(c1.Id, -1) && rg.Etat.Rang == 1 && rg.Calques[0].Id == c1.Id, "▼ : la caméra passe sous le verset (le verset par-dessus la caméra)");
    Ok(!rg.MonterCalque(c1.Id, -1), "tout en bas : ▼ ne fait rien");
    Ok(rg.MonterCalque(c1.Id, 1) && rg.Etat.Rang == 0, "▲ : la caméra repasse devant le verset");
    Ok(rg.MonterPrincipal(1) && rg.Etat.Rang == 1 && rg.MonterPrincipal(-1) && !rg.MonterPrincipal(-1) && rg.Etat.Rang == 0, "le contenu principal monte et descend dans la pile");
    // la case : cacher, montrer
    oeil = null;
    Ok(rg.MontrerCalque(c3.Id, false) && !rg.Calques.First(c => c.Id == c3.Id).Visible && rg.Etat.AvecCalques && oeil == true, "la case cache un calque (il reste dans la liste)");
    rg.MontrerCalque(c3.Id, true);
    // etat.json (page OBS) : les calques et leur place
    var json = System.Text.Json.JsonSerializer.Serialize(rg.Etat, Donnees.Json);
    Ok(json.Contains("\"calques\"") && json.Contains("\"rang\"") && json.Contains("\"annonce\"") && json.Contains("\"visible\""), "etat.json contient les calques (pour la page OBS)");
    var relu = System.Text.Json.JsonSerializer.Deserialize<EtatEcran>(json, Donnees.Json)!;
    Ok(relu.Calques is { Count: 3 } rc && rc[1].Id == c3.Id && rc[2].Etat.Lignes[0] == "Repas après le culte", "etat.json relu : mêmes calques");
    // Retirer de l'écran (Échap) : le verset part, les calques restent
    oeil = null;
    rg.Effacer();
    Ok(rg.Etat.Vide && rg.Calques.Count == 3 && oeil == true, "Retirer de l'écran : les calques restent (la source OBS reste visible)");
    // une source modifiée : ses calques suivent ; supprimée : ils partent
    rg.ModifierAnnonce(texte.Id, "Annonce du jour", "Culte à 10 h");
    Ok(rg.Calques.First(c => c.Id == c2.Id) is { Libelle: "Annonce du jour" } cm && cm.Etat.Lignes[0] == "Culte à 10 h", "texte modifié : son calque suit");
    Ok(rg.ModifierSource(camera.Id, "", camera.Source! with { Diffusion = SourceDisplay.Eglise }) && rg.Calques[0].Media?.EgliseSeulement == true, "source modifiée : son calque suit");
    // où vont les calques : « Dans l'église » ni sur Internet ni dans OBS, « MyStream seulement » pas dans la salle
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.EtatSalle(rg.Etat).Calques?.Count == 3, "la salle voit tous les calques de l'église");
    Ok(rg.EtatDiffusion(rg.Etat) is { Calques: { Count: 2 } cd } && cd.All(c => c.Id != c1.Id), "OBS et MyVirtualCam : sans la caméra « Dans l'église »");
    rg.ModifierSource(camera.Id, "", camera.Source! with { Diffusion = SourceDisplay.Streaming });
    Ok(rg.EtatSalle(rg.Etat).Calques is { Count: 2 } && rg.EtatDiffusion(rg.Etat).Calques?.Count == 3, "une caméra « MyStream seulement » : hors de la salle, sur Internet");
    // le rang suit quand un calque du dessous part
    rg.MonterCalque(c1.Id, -1);
    Ok(rg.Etat.Rang == 1 && rg.EtatSalle(rg.Etat).Rang == 0, "salle sans la caméra du dessous : le verset reste au bon étage");
    rg.SupprimerAnnonce(texte.Id);
    Ok(rg.Calques.Count == 2 && rg.Calques.All(c => c.Id != c2.Id), "source supprimée : son calque part");
    Ok(rg.RetirerCalque(c1.Id) && rg.Etat.Rang == 0 && rg.Calques.Count == 1, "retirer le calque du dessous : le rang du verset suit");
    // témoin MyCam : un téléphone dans un calque montré est « à l'écran »
    var tel = rg.NouveauDisplay("Téléphone", new SourceDisplay { Type = "mycam", Peripherique = "tel-1" });
    rg.Superposer(tel);
    Ok(rg.TemoinMyCam("tel-1") == "live", "témoin MyCam : rouge quand le téléphone est dans un calque montré");
    // Tout retirer
    rg.Effacer();
    oeil = null;
    rg.RetirerCalques();
    Ok(rg.Calques.Count == 0 && rg.Etat.Rang == 0 && oeil == false, "Tout retirer : plus de calque, la source OBS se cache");
    // les calques ne sont pas gardés d'une séance à l'autre
    var rg2 = new Regie(Donnees.Charger(don.Dossier, res), new Bibles(don));
    Ok(rg2.Calques.Count == 0 && rg2.Etat.Vide, "au démarrage : pas de calque");
}

Groupe("Plus de Duo (2.5.0)");
if (Actif())
{
    var dir = Temp();
    File.WriteAllText(Path.Combine(dir, "reglages.json"), "{}");
    File.WriteAllText(Path.Combine(dir, "annonces.json"), """[{"id":"a1","titre":"Verset et annonce","source":{"type":"duo","chemin":"d1"},"grille":true},{"id":"a2","titre":"Logo","source":{"type":"image","chemin":"logo.png"}}]""");
    File.WriteAllText(Path.Combine(dir, "agendas.json"), """[{"id":"g1","nom":"Culte","elements":[{"id":"e1","type":"display","titre":"Duo","source":{"type":"duo","chemin":"d1"}},{"id":"e2","type":"display","titre":"Logo","source":{"type":"image","chemin":"logo.png"}}]}]""");
    File.WriteAllText(Path.Combine(dir, "duos.json"), """[{"id":"d1","nom":"Verset et annonce"}]""");
    var d = Donnees.Charger(dir, res);
    Ok(d.Annonces.Count == 1 && d.Annonces[0].Id == "a2" && d.Agendas[0].Elements.Count == 1 && d.Agendas[0].Elements[0].Id == "e2",
        "2.5.0 : les dossiers Duo quittent les médias et les programmes (le reste est gardé)");
    Ok(Donnees.Charger(dir, res) is { Annonces.Count: 1 } d2 && d2.Agendas[0].Elements.Count == 1, "… et c'est enregistré");
    Ok(Roles.Pages(Roles.Admin).All(x => x != "duo"), "plus de page Duo");
}

Groupe("Enregistrements et sorties");
if (Actif())
{
    // dispositions de la vidéo
    var c1 = MyVerse.Audio.RecordingProfile.Cadres(1, "incrustation");
    Ok(c1.Count == 1 && c1[0] == (0, 0, 1, 1), "une source Display : plein écran");
    var c3 = MyVerse.Audio.RecordingProfile.Cadres(3, "incrustation");
    Ok(c3.Count == 3 && c3[0] == (0, 0, 1, 1) && c3.Skip(1).All(x => x.W == 0.3 && x.X >= 0 && x.Y >= 0 && x.X + x.W <= 1 && x.Y + x.H <= 1) && c3[1].X > c3[2].X, "incrustation : la 1re en grand, les autres en petit en bas à droite");
    var c2 = MyVerse.Audio.RecordingProfile.Cadres(2, "cote");
    Ok(c2.Count == 2 && c2[0] == (0, 0.25, 0.5, 0.5) && c2[1] == (0.5, 0.25, 0.5, 0.5), "côte à côte : deux moitiés à la forme de l'écran, centrées en hauteur");
    var c5 = MyVerse.Audio.RecordingProfile.Cadres(5, "cote");
    Ok(c5.Count == 5 && c5.All(x => Math.Abs(x.W - 1 / 3d) < 1e-9 && x.W == x.H && x.Y >= 0 && x.Y + x.H <= 1.000001), "côte à côte : 5 sources en grille 3 × 2, sans déformation");
    var c4 = MyVerse.Audio.RecordingProfile.Cadres(4, "cote");
    Ok(c4.Count == 4 && c4.All(x => x.W == 0.5 && x.H == 0.5) && c4[3] == (0.5, 0.5, 0.5, 0.5), "côte à côte : 4 sources en grille 2 × 2");

    // une entrée d'une carte son (0 = toutes ensemble), bornée ; 0 hors micro
    var ent = new MyVerse.Audio.AudioConfiguration();
    ent.Sons.Add(new MyVerse.Audio.Son { Id = "e3", Genre = MyVerse.Audio.GenreSon.Voix, Entree = 3 });
    ent.Sons.Add(new MyVerse.Audio.Son { Id = "trop", Genre = MyVerse.Audio.GenreSon.Voix, Entree = 99 });
    ent.Sons.Add(new MyVerse.Audio.Son { Id = "neg", Genre = MyVerse.Audio.GenreSon.Instrument, Entree = -2 });
    ent.Sons.Add(new MyVerse.Audio.Son { Id = "fichier", Genre = MyVerse.Audio.GenreSon.Fichier, Entree = 2 });
    ent.Normalize();
    Ok(ent.Sons.Take(4).Select(x => x.Entree).SequenceEqual(new[] { 3, MyVerse.Audio.AudioConfiguration.MaxEntrees, 0, 0 }), "entrée d'un micro : gardée, bornée, et 0 hors micro", string.Join(",", ent.Sons.Select(x => x.Entree)));
    var entRelu = System.Text.Json.JsonSerializer.Deserialize<MyVerse.Audio.AudioConfiguration>(System.Text.Json.JsonSerializer.Serialize(ent, Donnees.Json), Donnees.Json)!;
    Ok(entRelu.Sons[0].Entree == 3, "l'entrée d'un son est enregistrée dans les réglages");

    // 1.3 : chaque destination reçoit « tout le son » ou les sons choisis ; Siloé Stream donne les pistes d'OBS tout seul
    var cfg = new MyVerse.Audio.AudioConfiguration();
    foreach (var n in new[] { "guitare", "piano", "chanteur", "pasteur" }) cfg.Ajouter(new MyVerse.Audio.Son { Id = n, Nom = n, Genre = MyVerse.Audio.GenreSon.Voix });
    cfg.Ajouter(MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Video, "Vidéos de Display"));
    var louange = new MyVerse.Audio.RecordingProfile { Name = "Louange", ChoixSons = new() { "guitare", "piano", "chanteur" } };
    var predication = new MyVerse.Audio.RecordingProfile { Name = "Prédication", ChoixSons = new() { "pasteur", "inconnu" }, DisplayIds = { "projection", "camera1" }, Layout = "cote" };
    var culte = new MyVerse.Audio.RecordingProfile { Name = "Culte" };
    cfg.Profiles.Add(louange); cfg.Profiles.Add(predication); cfg.Profiles.Add(culte);
    var zoom = new MyVerse.Audio.VirtualSoundSettings { Name = "Zoom", Enabled = true };
    var flux = new MyVerse.Audio.VirtualSoundSettings { Name = "Direct YouTube", Enabled = true, ChoixSons = new() { "piano", "chanteur", "guitare" } };
    cfg.VirtualSounds.Add(zoom); cfg.VirtualSounds.Add(flux);
    cfg.Normalize();
    Ok(louange.AudioOnly && louange.Format == "mp3" && !predication.AudioOnly && predication.Format == "mkv", "sans source Display : le son seul, en MP3 ; avec : vidéo MKV");
    Ok(louange.Directory.EndsWith("Louange"), "dossier propre à chaque enregistrement : Vidéos\\Siloé Stream\\Louange");
    var vis = predication.VisuelsDisplay();
    Ok(vis.Count == 2 && vis[0].Kind == "projection" && vis[1].Kind == "annonce" && vis[1].Annonce == "camera1" && vis[1].X == 0.5, "vidéo : la projection et la caméra côte à côte");
    Ok(predication.ChoixSons!.SequenceEqual(new[] { "pasteur" }), "un son inconnu est oublié");
    var plan = cfg.Planifier();
    Ok(culte.Track == 1 && zoom.Track == 1 && plan.PisteDe(MyVerse.Audio.AudioConfiguration.IdCasque) == 1, "tout le son : la piste 1 (Culte, Zoom, casque)", $"{culte.Track} {zoom.Track}");
    Ok(flux.Track == louange.Track && louange.Track > 1 && predication.Track > 1 && predication.Track != louange.Track, "les mêmes sons choisis : la même piste ; d'autres sons : une autre piste", $"louange {louange.Track}, YouTube {flux.Track}, prédication {predication.Track}");
    Ok(flux.Track == 2 && predication.Track == 3, "les pistes suivent l'ordre des destinations (les envois, puis les enregistrements)", $"YouTube {flux.Track}, prédication {predication.Track}");
    string Pistes(string id) => string.Concat(Enumerable.Range(1, 6).Select(t => plan.Moteur.First(x => x.Id == id).DansPiste(t) ? t.ToString() : "·"));
    Egal(Pistes("pasteur"), "1·3···", "Pasteur : tout le son et Prédication");
    Egal(Pistes("piano"), "12····", "Piano : tout le son et Louange (avec YouTube)");
    Egal(Pistes(cfg.SonVideos!.Id), "1·····", "Vidéos : tout le son seulement");
    Egal(cfg.Recoit(predication), "pasteur seulement", "ce que reçoit une destination, en mots");
    Egal(cfg.Recoit(louange), "guitare, piano et chanteur", "plusieurs sons, en mots");
    Egal(cfg.Recoit(zoom), "Tout le son", "tout le son, en mots");
    flux.Enabled = false;
    cfg.Planifier(actives: new HashSet<string> { predication.Id });
    Ok(predication.Track == 3 && flux.Track == 2, "un enregistrement qui démarre garde sa piste : rien ne bouge (pas d'ancien mélange au début du fichier)", $"prédication {predication.Track}, YouTube {flux.Track}");
    // retirer un son : il disparaît des choix ; 2.4.0 : le son des vidéos se retire aussi
    Ok(cfg.Retirer(cfg.Sons.First(x => x.Id == "pasteur")) && predication.ChoixSons!.Count == 0 && cfg.Retirer(cfg.SonVideos!) && cfg.SonVideos == null,
        "retirer un son l'enlève des destinations ; le son des vidéos se retire aussi (2.4.0)");
    Egal(cfg.Recoit(predication), "Aucun son", "plus aucun son choisi");
    // trop de mélanges différents : les suivants reçoivent tout le son, avec une alerte
    var beaucoup = new MyVerse.Audio.AudioConfiguration();
    for (int i = 0; i < 7; i++) beaucoup.Ajouter(new MyVerse.Audio.Son { Id = "s" + i, Nom = "Son " + i });
    for (int i = 0; i < 5; i++) beaucoup.Profiles.Add(new MyVerse.Audio.RecordingProfile { Name = "E" + i, ChoixSons = new() { "s" + i } });
    for (int i = 0; i < 2; i++) beaucoup.VirtualSounds.Add(new MyVerse.Audio.VirtualSoundSettings { Name = "V" + i, ChoixSons = new() { "s" + (5 + i) } });
    beaucoup.Normalize();
    var pb = beaucoup.Planifier();
    // 1.6.0 : le direct (YouTube) reçoit tout le son par défaut : la piste 1, sans prendre de piste
    Ok(pb.Alertes.Count == 2 && beaucoup.Direct.Track == 1 && beaucoup.Destinations().Count(d => d.Track == 1 && d.Id != MyVerse.Audio.AudioConfiguration.IdDirect) == 2 && beaucoup.Destinations().Select(d => d.Track).Where(t => t > 1).Distinct().Count() == 5,
        "6 pistes : 5 mélanges choisis au plus, les autres reçoivent tout le son (avec une alerte)", string.Join(" | ", pb.Alertes));
    // plus de mélanges que de pistes : un enregistrement qui tourne passe en premier
    var dernier = beaucoup.Profiles[^1];
    beaucoup.Planifier();
    Ok(dernier.Track == 1, "sans priorité, le dernier mélange reçoit tout le son", dernier.Track.ToString());
    beaucoup.Planifier(actives: new HashSet<string> { dernier.Id });
    Ok(dernier.Track > 1, "plus de mélanges que de pistes : l'enregistrement qui tourne passe en premier", dernier.Track.ToString());
    // plus de 5 enregistrements ou 4 envois
    var trop = new MyVerse.Audio.AudioConfiguration();
    for (int i = 0; i < 7; i++) trop.Profiles.Add(new MyVerse.Audio.RecordingProfile());
    for (int i = 0; i < 6; i++) trop.VirtualSounds.Add(new MyVerse.Audio.VirtualSoundSettings());
    trop.Normalize();
    Ok(trop.Profiles.Count == 5 && trop.VirtualSounds.Count == 4, "5 enregistrements et 4 envois au plus");
    // réglages de la 3.0.0 : une seule sortie MyVirtualSound sur une piste choisie à la main
    var json30 = """{"version":3,"virtualSound":{"enabled":true,"deviceId":"","track":2},"obsSources":[{"id":"a","name":"Micro","tracks":2},{"id":"b","name":"Ordi","kind":1,"tracks":1}],"profiles":[{"name":"Enregistrement","track":1}]}""";
    var a30 = System.Text.Json.JsonSerializer.Deserialize<MyVerse.Audio.AudioConfiguration>(json30, Donnees.Json)!;
    a30.Normalize();
    var z30 = a30.VirtualSounds.FirstOrDefault();
    Ok(a30.VirtualSounds.Count == 1 && z30 is { Name: "Zoom", Enabled: true } && z30.ChoixSons is ["a"],
        "3.0.0 : la sortie MyVirtualSound devient « Zoom », avec les sources de son ancienne piste", string.Join(",", z30?.ChoixSons ?? new()));
    Ok(a30.Sons.Select(x => x.Genre).SequenceEqual(new[] { MyVerse.Audio.GenreSon.Voix, MyVerse.Audio.GenreSon.Ordinateur }), "3.0.0 : un micro et le son de l'ordinateur (2.4.0 : pas de son des vidéos ajouté tout seul)");
    Ok(a30.Profiles.Count == 0 && a30.AncienneSortie == null && a30.Version == MyVerse.Audio.AudioConfiguration.Actuelle, "3.0.0 : le profil automatique n'est pas repris");
    var relu30 = System.Text.Json.JsonSerializer.Serialize(a30, Donnees.Json);
    Ok(relu30.Contains("\"sortiesVirtuelles\"") && relu30.Contains("\"enregistrements\"") && relu30.Contains("\"sons\"") && !relu30.Contains("\"virtualSound\"") && !relu30.Contains("SourceIds")
        && !relu30.Contains("obsSources") && !relu30.Contains("\"track\""), "enregistré sous les nouveaux noms, sans les anciennes listes ni pistes", relu30);
}

Groupe("MyCam H.264 (4.1)");
if (Actif())
{
    // en-tête version 2 : une unité d'accès H.264 (Annex B), bit 3 = image clé
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 1 | 8, 0, 0, 0, 1, 0x67, 0x42 }) is { H264: true, Cle: true, Rotation: 1, Miroir: false, Debut: 4 }, "en-tête MC 2 : H.264, image clé, quart de tour");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 4, 0, 0, 1, 0x41 }) is { H264: true, Cle: false, Miroir: true }, "en-tête MC 2 : code de début court (00 00 01), miroir");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 0, 0xFF, 0xD8, 0, 0 }) == null, "en-tête MC 2 : pas du H.264, refusé");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 3, 0, 0, 0, 0, 1 }) == null, "en-tête : version inconnue refusée");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 1, 0, 0xFF, 0xD8, 0, 0 }) is { H264: false, Cle: false }, "en-tête MC 1 : toujours le JPEG");

    // FFmpeg : celui d'OBS (SILOE_OBS) sous Windows, celui du système sous Linux
    bool ffmpegAttendu = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("SILOE_OBS") != null
        : new[] { "/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib" }.Any(d => Directory.Exists(d) && Directory.GetFiles(d, "libavcodec.so.*").Length > 0);
    Console.WriteLine($"  décodeur : {DecodeurH264.Etat}");
    if (!ffmpegAttendu && !DecodeurH264.Disponible) Console.WriteLine("  (FFmpeg absent : essais du décodage H.264 non faits)");
    else
    {
        Ok(DecodeurH264.Disponible, "FFmpeg chargé, la mire H.264 se décode (couleurs vérifiées)", DecodeurH264.Etat);
        using var dec = DecodeurH264.Creer();
        Ok(dec != null, "décodeur H.264 créé");
        if (dec != null)
        {
            var m = DecodeurH264.Mire;
            var c = DecodeurH264.CouleursMire;
            for (int k = 0; k < m.Length; k++)
            {
                var px = dec.Decoder(m[k], 0, m[k].Length, out var l, out var h);
                Ok(px != null && l == 64 && h == 48 && px.Length == 64 * 48 * 4 && DecodeurH264.Proche(px, l, 32, 24, c[k]) && DecodeurH264.Proche(px, l, 2, 2, c[k]),
                    $"image {k} ({(k == 0 ? "clé" : "P")}) : 64 × 48 BGRA, couleurs plein écart BT.601 comme le JPEG",
                    px == null ? "rien" : $"{l} × {h}, centre {px[(24 * 64 + 32) * 4]},{px[(24 * 64 + 32) * 4 + 1]},{px[(24 * 64 + 32) * 4 + 2]}");
            }
            var bidon = new byte[] { 0, 0, 0, 1, 0x65, 0x88, 0x84, 0, 0x33, 0xFF, 1, 2, 3, 4 };
            Ok(dec.Decoder(bidon, 0, bidon.Length, out _, out _) == null, "données invalides : aucune image, pas de plantage");
            Ok(dec.Decoder(bidon, 0, 0, out _, out _) == null && dec.Decoder(bidon, 10, 9, out _, out _) == null, "longueurs incohérentes refusées");
        }

        // MyCams : P avant la clé → rien, image clé demandée ; clé → BGRA tourné ; P → image suivante ; arrêt → décodeur libéré
        byte[] Au(byte[] au, bool cle, int rotation = 0)
        {
            var b = new byte[au.Length + 4];
            b[0] = (byte)'M'; b[1] = (byte)'C'; b[2] = 2; b[3] = (byte)(rotation | (cle ? 8 : 0));
            au.CopyTo(b, 4);
            return b;
        }
        var camsH = new MyCams();
        var demandes = new List<string>();
        camsH.Demande += (id, j) => { lock (demandes) demandes.Add(id + ":" + (string?)j["t"]); };
        var p1 = Au(DecodeurH264.Mire[1], false);
        Ok(camsH.Recevoir("tel-h264", "Pixel", p1, p1.Length), "H.264 : message MyCam reconnu");
        Ok(camsH.Dernier("tel-h264") == null && demandes.Contains("tel-h264:cam.cle"), "H.264 sans image clé : rien d'affiché, image clé demandée au téléphone", string.Join(",", demandes));
        camsH.Recevoir("tel-h264", "Pixel", p1, p1.Length);
        Egal(demandes.Count(x => x == "tel-h264:cam.cle"), 1, "image clé demandée au plus une fois par seconde");
        var k0 = Au(DecodeurH264.Mire[0], true, 1);
        camsH.Recevoir("tel-h264", "Pixel", k0, k0.Length);
        var ch = camsH.Dernier("tel-h264");
        Ok(ch is { Pixels: not null, Largeur: 64, Hauteur: 48, Rotation: 1, Jpeg.Length: 0 } && DecodeurH264.Proche(ch.Pixels, 64, 32, 24, DecodeurH264.CouleursMire[0]),
            "H.264 : image clé décodée en BGRA, quart de tour gardé");
        Ok(camsH.InfoDe("tel-h264") is { EnDirect: true, Codec: "H.264" } i && i.Resume.Contains("H.264"), "H.264 : le téléphone diffuse, format affiché", camsH.InfoDe("tel-h264")?.Resume);
        long n0 = ch?.Numero ?? 0;
        var p2 = Au(DecodeurH264.Mire[2], false, 1);
        camsH.Recevoir("tel-h264", "Pixel", p1, p1.Length);
        camsH.Recevoir("tel-h264", "Pixel", p2, p2.Length);
        var ch2 = camsH.Dernier("tel-h264");
        Ok(ch2 is { Pixels: not null } && ch2.Numero == n0 + 2 && DecodeurH264.Proche(ch2.Pixels, 64, 32, 24, DecodeurH264.CouleursMire[2]), "H.264 : images P décodées à la suite");
        camsH.Infos("tel-h264", "Pixel", System.Text.Json.Nodes.JsonNode.Parse("""{"t":"cam.info","diffuse":false}""")!.AsObject());
        Ok(camsH.Dernier("tel-h264") == null, "diffusion arrêtée : plus d'image");
        demandes.Clear();
        camsH.Recevoir("tel-h264", "Pixel", p2, p2.Length);
        Ok(camsH.Dernier("tel-h264") == null && demandes.Contains("tel-h264:cam.cle"), "reprise : le nouveau décodeur attend une image clé", string.Join(",", demandes));
        camsH.Recevoir("tel-h264", "Pixel", k0, k0.Length);
        Ok(camsH.Dernier("tel-h264") is { Pixels: not null }, "reprise : l'image clé repart");
        camsH.Deconnecte("tel-h264");
        Ok(camsH.Dernier("tel-h264") == null && camsH.InfoDe("tel-h264") is { EnDirect: false }, "déconnecté : décodeur libéré, plus d'image");
    }
}

Groupe("MyCam 4.0");
if (Actif())
{
    // images : en-tête « MC », version 1, quarts de tour et miroir, puis le JPEG
    var img = new byte[] { (byte)'M', (byte)'C', 1, 5, 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
    Ok(MyCams.LireEntete(img) is { Rotation: 1, Miroir: true, Debut: 4 }, "en-tête MC : quart de tour et miroir");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 0, 0xFF, 0xD8, 0, 0 }) == null, "en-tête : autre version refusée");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 1, 0, 0x89, 0x50, 0, 0 }) == null, "en-tête : pas un JPEG, refusé");
    var cams = new MyCams();
    int nCadres = 0;
    cams.NouveauCadre += _ => nCadres++;
    Ok(cams.Recevoir("tel-cam", "Pixel", img, img.Length), "image reçue");
    var c1 = cams.Dernier("tel-cam");
    Ok(c1 != null && c1.Jpeg.Length == img.Length - 4 && c1.Jpeg[0] == 0xFF && c1.Rotation == 1 && c1.Miroir, "dernière image : le JPEG sans l'en-tête");
    Ok(nCadres == 1 && cams.InfoDe("tel-cam") is { EnDirect: true }, "le téléphone diffuse");
    cams.Infos("tel-cam", "Pixel", System.Text.Json.Nodes.JsonNode.Parse("""{"t":"cam.info","nom":"Caméra · Chaire","taille":"1280 × 720","fps":30,"batterie":78,"charge":false,"temperature":34.5,"lien":"wifi","diffuse":true,"depuis":1700000000000}""")!.AsObject());
    var inf = cams.InfoDe("tel-cam");
    Ok(inf is { Nom: "Caméra · Chaire", Batterie: 78, Lien: "wifi", Temperature: 34.5, Depuis: 1700000000000 } && inf.Resume.Contains("1280 × 720") && inf.Resume.Contains("78 %"), "cam.info : nom, image, batterie", inf?.Resume);
    cams.Infos("tel-cam", "Pixel", System.Text.Json.Nodes.JsonNode.Parse("""{"t":"cam.info","diffuse":false}""")!.AsObject());
    Ok(cams.Dernier("tel-cam") == null && cams.InfoDe("tel-cam") is { EnDirect: false, Nom: "Caméra · Chaire" }, "diffusion arrêtée : plus d'image, le nom reste");
    Ok(cams.Recevoir("tel-cam", "Pixel", new byte[] { 1, 2, 3 }, 3) == false, "message binaire inconnu ignoré");
    cams.Deconnecte("tel-cam");
    Ok(cams.Liste().Count == 1 && !cams.Liste()[0].EnDirect, "déconnecté : le téléphone reste connu");
    Egal(MyCams.Duree(0), "", "durée vide");

    // Siloé Stream 1.2 : l'âge de chaque image (bit 4 + 4 octets, µs) et l'aller-retour « cam.ping » datent l'image sur l'horloge du PC
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 8 | 0x10, 0x40, 0x0D, 0x03, 0, 0, 0, 0, 1, 0x65 }) is { H264: true, Cle: true, Debut: 8, AgeUs: 200_000 }, "en-tête MC 2 avec âge : 200 ms, données après 8 octets");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 1, 0x10, 0xA0, 0x86, 0x01, 0, 0xFF, 0xD8, 0xFF, 0xE0 }) is { H264: false, Debut: 8, AgeUs: 100_000 }, "en-tête MC 1 (JPEG) avec âge : 100 ms");
    Ok(MyCams.LireEntete(img) is { AgeUs: -1 }, "en-tête sans âge (téléphone 1.1) : âge inconnu");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 0x10, 0, 0, 0, 0, 0xFF, 0xD8, 0, 0 }) == null, "en-tête avec âge : toujours vérifié après l'âge");
    var camsT = new MyCams();
    camsT.AllerRetour("tel-t", 10 * Horloge.Milliseconde);
    long tCam = Horloge.Ns() - 300 * Horloge.Milliseconde;
    for (int k = 0; k < 6; k++)
    {
        // une image toutes les 33 ms, prise 120 ms avant d'être envoyée
        long age = 120_000;
        var m = new byte[] { (byte)'M', (byte)'C', 1, 0x10, (byte)(age & 0xFF), (byte)(age >> 8 & 0xFF), (byte)(age >> 16 & 0xFF), 0, 0xFF, 0xD8, 0xFF, 0xE0, (byte)k };
        camsT.Recevoir("tel-t", "Pixel", m, m.Length, tCam + k * 33 * Horloge.Milliseconde);
    }
    var der = camsT.Dernier("tel-t")!;
    long attendu = tCam + 5 * 33 * Horloge.Milliseconde - 5 * Horloge.Milliseconde - 120 * Horloge.Milliseconde;
    Ok(Math.Abs(der.Capture - attendu) < Horloge.Milliseconde, "image datée : réception − trajet (aller-retour / 2) − âge", $"{(der.Capture - attendu) / 1e6:0.###} ms");
    var proche = camsT.Proche("tel-t", tCam + 2 * 33 * Horloge.Milliseconde - 125 * Horloge.Milliseconde + 3 * Horloge.Milliseconde);
    Ok(proche != null && proche.Jpeg[^1] == 2, "l'image prise au plus près d'un instant est retrouvée", proche?.Jpeg[^1].ToString());
    long retard = camsT.Retard("tel-t");
    Ok(retard > 100 * Horloge.Milliseconde && retard < 1500 * Horloge.Milliseconde, "retard mesuré des images (prise de vue → PC)", $"{retard / 1e6:0} ms");
    Ok(new MyCams().Retard("inconnu") >= MyCams.AgeParDefaut, "téléphone sans mesure : retard par défaut", $"{new MyCams().Retard("inconnu") / 1e6:0} ms");

    // source Display MyCam et témoin rouge / orange
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var cam = rg.NouveauDisplay("Caméra · Chaire", new SourceDisplay { Type = "mycam", Peripherique = "tel-cam" });
    Ok(cam.Description == "Téléphone MyCam", "source Display MyCam");
    rg.AfficherAnnonce(cam);
    Ok(rg.Etat is { Mode: "media", Media.Type: "mycam" }, "MyCam à l'écran");
    Egal(rg.TemoinMyCam("tel-cam"), "live", "témoin rouge : la caméra est à l'écran");
    Egal(rg.TemoinMyCam("autre"), "off", "témoin éteint pour un autre téléphone");
    bool noir = true;
    rg.EstNoir = () => noir;
    Egal(rg.TemoinMyCam("tel-cam"), "off", "écran noir : témoin éteint");
    noir = false;
    rg.Effacer();
    Egal(rg.TemoinMyCam("tel-cam"), "off", "retirée : témoin éteint");
    rg.ViderAgenda();
    var eV = rg.AjouterBibleAgenda(new Ref(43, 3, 16, 16));
    var eC = rg.AjouterAnnonceAgenda(cam.Id)!;
    Ok(eC.Type == "display" && rg.Description(eC).Sous == "Téléphone MyCam", "MyCam dans le déroulé");
    rg.AfficherElement(eV);
    Egal(rg.TemoinMyCam("tel-cam"), "next", "témoin orange : la caméra vient avec Suivant");
    rg.Bouger(1);
    Egal(rg.TemoinMyCam("tel-cam"), "live", "Suivant : la caméra passe à l'écran");
    var eP = rg.AjouterPrincipalAgenda(cam.Id)!;
    Ok(eP.Source is { Type: "mycam" } && rg.Description(eP).Sous.StartsWith("MyCam"), "écran principal MyCam");
    rg.AfficherElement(eP);
    Ok(rg.Etat.Mode == "media" && rg.TemoinMyCam("tel-cam") == "live", "écran principal MyCam : témoin rouge");
    rg.Effacer();
    Egal(rg.TemoinMyCam("tel-cam"), "live", "Retirer revient sur l'écran principal MyCam : témoin rouge");
    rg.ChoisirEcranDefaut("noir");
    Ok(rg.Etat.Vide && rg.TemoinMyCam("tel-cam") == "off", "écran par défaut noir : l'écran se vide, témoin éteint");

    // protocole du téléphone : bienvenue 2, images binaires, témoin, messages, déroulé, écran par défaut, noir
    using var tel = new Telephone(don.Dossier, rg, f => f(), "PC-TEST");
    tel.Demarrer();
    bool noirPc = false;
    rg.EstNoir = () => noirPc;
    rg.BasculerNoir = () => { noirPc = !noirPc; tel.Actualiser(); };
    var url = tel.OuvrirJumelage();
    var q = System.Web.HttpUtility.ParseQueryString(new Uri(url.Replace("siloe://pair", "http://x/pair")).Query);
    var cle = Telephone.DeriverCle(Telephone.B64Dec(q["secret"]!), tel.PcId, "tel-cam");
    var ws = new System.Net.WebSockets.ClientWebSocket();
    await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{tel.Port}/"), CancellationToken.None);
    async Task Env(object o)
    {
        if (ws.State != System.Net.WebSockets.WebSocketState.Open) return;
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(o)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    }
    async Task<System.Text.Json.Nodes.JsonObject?> Rec(string? type = null, int ms = 3000, Func<System.Text.Json.Nodes.JsonObject, bool>? si = null)
    {
        var fin = DateTime.Now.AddMilliseconds(ms);
        var buf = new byte[1 << 16];
        while (DateTime.Now < fin && ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(fin - DateTime.Now);
            using var mem = new MemoryStream();
            try
            {
                System.Net.WebSockets.WebSocketReceiveResult r;
                do { r = await ws.ReceiveAsync(buf, cts.Token); mem.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException) { return null; }
            var o = System.Text.Json.Nodes.JsonNode.Parse(mem.ToArray())!.AsObject();
            if ((type == null || (string?)o["t"] == type) && (si == null || si(o))) return o;
        }
        return null;
    }
    await Env(new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "tel-cam", deviceName = "Pixel 8", pair = true, cnonce = "cn" });
    var ch = await Rec("challenge");
    await Env(new { t = "auth", mac = Telephone.MacClient(cle, (string)ch!["nonce"]!, "tel-cam") });
    var w = await Rec("welcome");
    Egal((int?)w?["proto"], 2, "bienvenue : fonctions 4.0 annoncées");
    var codecsPc = w?["codecs"]?.AsArray().Select(x => (string?)x).ToList() ?? new();
    Ok(codecsPc.Contains("jpeg") && codecsPc.Contains("h264") == MyCams.H264, "bienvenue : formats MyCam annoncés (H.264 si FFmpeg est là)", string.Join(",", codecsPc));
    await Env(new { t = "state.get" });
    var st = await Rec("state");
    Ok(st?["defauts"]?.AsArray().Count >= 2 && (string?)st?["defaut"]?["id"] == "noir" && st?["noir"] is not null && st?["agendaV"] is not null, "état : écran par défaut, noir, déroulé");

    await Env(new { t = "cam.info", nom = "Caméra · Chaire", taille = "1280 × 720", fps = 30, batterie = 81, diffuse = true, lien = "wifi" });
    var cadre = new byte[] { (byte)'M', (byte)'C', 1, 0, 0xFF, 0xD8, 0xFF, 0xD9 };
    if (ws.State == System.Net.WebSockets.WebSocketState.Open) await ws.SendAsync(cadre, System.Net.WebSockets.WebSocketMessageType.Binary, true, CancellationToken.None);
    for (int k = 0; k < 40 && tel.Cameras.Dernier("tel-cam") == null; k++) await Task.Delay(25);
    Ok(tel.Cameras.Dernier("tel-cam") is { Jpeg.Length: 4 }, "image MyCam reçue par le PC");
    Ok(tel.Cameras.InfoDe("tel-cam") is { Nom: "Caméra · Chaire", Batterie: 81 }, "infos MyCam reçues par le PC");
    var t0 = await Rec("cam.tally");
    Egal((string?)t0?["etat"], "off", "témoin envoyé dès la première image");
    rg.AfficherAnnonce(cam);
    var t1 = await Rec("cam.tally");
    Egal((string?)t1?["etat"], "live", "témoin rouge envoyé au téléphone");
    st = await Rec("state", si: o => (string?)o["mode"] == "media");
    Ok((string?)st?["sous"] != null && ((string?)st?["sous"] ?? "").StartsWith("MyCam"), "état : sous-titre MyCam", st?["sous"]?.ToJsonString());
    await Env(new { t = "noir" });
    var t2 = await Rec("cam.tally");
    Ok(noirPc && (string?)t2?["etat"] == "off", "Noir depuis le téléphone : écran noir et témoin éteint");
    st = await Rec("state", si: o => (bool?)o["noir"] == true);
    Ok(st != null, "état : noir");
    await Env(new { t = "noir" });
    Egal((string?)(await Rec("cam.tally"))?["etat"], "live", "Noir de nouveau : la caméra revient");

    string? vu = null;
    tel.MessageVu += (_, texte) => vu = texte;
    Ok(tel.EnvoyerMessageCam("tel-cam", "Plan serré sur le pasteur"), "message au caméraman envoyé");
    Ok(!tel.EnvoyerMessageCam("inconnu", "x"), "message à un téléphone absent : refusé");
    var msg = await Rec("cam.msg");
    Egal((string?)msg?["texte"], "Plan serré sur le pasteur", "message reçu par le téléphone");
    await Env(new { t = "cam.ack", id = (string?)msg?["id"] });
    for (int k = 0; k < 40 && vu == null; k++) await Task.Delay(25);
    Egal(vu, "Plan serré sur le pasteur", "« Vu » du caméraman reçu par la régie");

    await Env(new { t = "agenda.add", type = "bible", @ref = "ps 23 1", rid = 1 });
    await Env(new { t = "agenda.get", rid = 2 });
    var ag = await Rec("agenda", si: o => o["elements"]?.AsArray().Any(x => ((string?)x?["titre"] ?? "").StartsWith("Psaume")) == true);
    Ok(ag != null && ag["elements"]!.AsArray().Any(x => (string?)x?["source"] == "mycam"), "déroulé envoyé au téléphone (avec la source MyCam)", ag?.ToJsonString());
    var idPs = (string?)ag?["elements"]?.AsArray().FirstOrDefault(x => ((string?)x?["titre"] ?? "").StartsWith("Psaume"))?["id"];
    await Env(new { t = "agenda.show", id = idPs });
    st = await Rec("state", si: o => (string?)o["element"] == idPs);
    Ok(st != null && (string?)st["mode"] == "verset", "élément du déroulé affiché depuis le téléphone");
    await Env(new { t = "agenda.show", id = "absent" });
    Ok(((string?)(await Rec("error"))?["message"] ?? "").Contains("déroulé"), "élément absent : refusé");
    await Env(new { t = "default.set", id = "fond" });
    st = await Rec("state", si: o => (string?)o["defaut"]?["id"] == "fond");
    Ok(st != null, "écran par défaut choisi depuis le téléphone");
    await Env(new { t = "clear" });
    st = await Rec("state", si: o => (bool?)o["surDefaut"] == true);
    Ok(st != null && (string?)st["ecran"] == "fond" && (string?)st["mode"] == "vide", "Retirer : le fond seul (« vide » pour les anciennes apps)");
    if (ws.State == System.Net.WebSockets.WebSocketState.Open) await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    else ws.Abort();
    for (int k = 0; k < 40 && tel.Cameras.InfoDe("tel-cam")?.Diffuse == true; k++) await Task.Delay(25);
    Ok(tel.Cameras.InfoDe("tel-cam") is { Diffuse: false }, "téléphone parti : MyCam ne diffuse plus");
}

Groupe("Son simple (1.3)");
if (Actif())
{
    // un nouveau son : les réglages intelligents de son genre
    var voix = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Voix, "Pasteur");
    var piano = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Instrument, "Piano");
    var table = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Table, "Table");
    var ordi = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Ordinateur, "Ordinateur");
    var musique = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Fichier, "Attente");
    Ok(voix is { EnleverBruit: false, VolumeRegulier: false, Eglise: false, Capture: MyVerse.Audio.AudioInputKind.Microphone }, "2.4.2 : micro : aucun traitement automatique, pas dans les haut-parleurs (larsen)");
    Ok(piano is { EnleverBruit: false, VolumeRegulier: false, Eglise: false } && table is { EnleverBruit: false, Eglise: false }, "instrument et table de mixage : la musique n'est pas « nettoyée »");
    Ok(ordi.Capture == MyVerse.Audio.AudioInputKind.Sortie && !ordi.Eglise && musique is { Capture: MyVerse.Audio.AudioInputKind.Media, Eglise: true }, "ordinateur : capté sur sa sortie ; musique : entendue dans l'église");
    var cfg = new MyVerse.Audio.AudioConfiguration();
    foreach (var s in new[] { voix, piano, table, ordi, musique }) cfg.Ajouter(s);
    cfg.Normalize();
    Ok(cfg.Sons.Count == 5 && cfg.SonVideos == null, "2.4.0 : le son des vidéos n'est plus ajouté tout seul");
    cfg.Ajouter(MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Video, "Vidéos de Display"));
    cfg.Normalize();
    var deuxieme = MyVerse.Audio.Son.Nouveau(MyVerse.Audio.GenreSon.Voix, "Chorale"); cfg.Ajouter(deuxieme);
    Ok(cfg.Sons[^1].Genre == MyVerse.Audio.GenreSon.Video && cfg.Sons[^2] == deuxieme, "choisi, le son des vidéos reste le dernier : un son ajouté passe avant lui");
    cfg.Retirer(deuxieme);
    Egal(cfg.NomLibre("pasteur"), "pasteur 2", "nom suivant (sans tenir compte des majuscules)");
    Egal(MyVerse.Audio.AudioConfiguration.Description(new MyVerse.Audio.Son { Genre = MyVerse.Audio.GenreSon.Voix, Appareil = "{umc}", NomAppareil = "Line In (Behringer UMC404HD)", Entree = 2 }),
        "Micro · Behringer UMC404HD · entrée 2", "description : ce que c'est et où c'est branché");
    Egal(MyVerse.Audio.AudioConfiguration.Description(ordi), "Tout ce que joue l'ordinateur", "description du son de l'ordinateur");
    var plan = cfg.Planifier("C:\\videos\\annonce.mp4");
    var mv = plan.Moteur.First(x => x.Id == voix.Id);
    Ok(mv.Mono && mv.Filters.Count == 0
        && mv.Monitoring == MyVerse.Audio.AudioMonitoring.Off && mv.Kind == MyVerse.Audio.AudioInputKind.Microphone, "2.4.2 : moteur : une voix est mono, sans traitement automatique, hors de l'église");
    Ok(plan.Moteur.All(x => x.Kind != MyVerse.Audio.AudioInputKind.Application), "2.4.3 : sans la case, pas de son de Zoom dans l'église");
    cfg.Eglise.Zoom = true; cfg.Eglise.ZoomVolumeDb = -6;
    var planZoom = cfg.Planifier("");
    var zoomEglise = planZoom.Moteur.FirstOrDefault(x => x.Kind == MyVerse.Audio.AudioInputKind.Application);
    Ok(zoomEglise is { DeviceId: "Zoom.exe", Tracks: 0, Monitoring: MyVerse.Audio.AudioMonitoring.Only, VolumeDb: -6 }, "2.4.3 : le son de Zoom joué dans l'église seulement (ni enregistré, ni renvoyé vers Zoom), avec son volume");
    cfg.Eglise.Zoom = false;
    var mm = plan.Moteur.First(x => x.Id == musique.Id);
    Ok(mm.DemarrerArrete && !mm.Mono && mm.Filters.Count == 0 && mm.Monitoring == MyVerse.Audio.AudioMonitoring.AndOutput, "moteur : la musique attend « Lire », stéréo, dans l'église");
    var mvid = plan.Moteur.First(x => x.Id == cfg.SonVideos!.Id);
    Ok(mvid.FollowProjection && mvid.MediaPath == "C:\\videos\\annonce.mp4" && !mvid.DemarrerArrete && mvid.Monitoring == MyVerse.Audio.AudioMonitoring.AndOutput, "moteur : le son des vidéos joue la vidéo à l'écran, dans l'église");
    var filtres = plan.Moteur.SelectMany(x => x.Filters.Select(f => f.Id)).ToList();
    Ok(filtres.Distinct().Count() == filtres.Count, "chaque aide a son propre identifiant dans OBS");
    piano.EnleverBruit = true; ordi.Entree = 3; ordi.Capture = MyVerse.Audio.AudioInputKind.Microphone; cfg.Normalize();
    Ok(!piano.EnleverBruit && ordi.Capture == MyVerse.Audio.AudioInputKind.Sortie && ordi.Entree == 0, "une musique ne se nettoie pas ; le son de l'ordinateur reste capté sur sa sortie");
    // un envoi vers une sortie de l'ordinateur ne reçoit pas le son de cette même sortie (sinon : larsen)
    var salle = new MyVerse.Audio.VirtualSoundSettings { Name = "Salle 2", Enabled = true, DeviceId = "default" };
    var cable = new MyVerse.Audio.VirtualSoundSettings { Name = "Zoom", Enabled = true };
    cfg.VirtualSounds.Add(salle); cfg.VirtualSounds.Add(cable); cfg.Casque.Actif = true;
    var p2 = cfg.Planifier();
    Ok(cable.Track == 1 && salle.Track > 1 && !p2.SonsDesPistes[salle.Track].Contains(ordi.Id) && p2.SonsDesPistes[salle.Track].Contains(voix.Id), "un envoi vers la sortie de Windows ne reçoit pas le son de l'ordinateur (pas de larsen)");
    Ok(p2.PisteDe(MyVerse.Audio.AudioConfiguration.IdCasque) == salle.Track, "le casque sur la sortie de Windows : pareil");
    cfg.Casque.Appareil = "{casque-usb}";
    Ok(cfg.Planifier().PisteDe(MyVerse.Audio.AudioConfiguration.IdCasque) == 1, "le casque sur son propre appareil : tout le son");
    Ok(MyVerse.Audio.AudioConfiguration.DevinerGenre("Piano droit") == MyVerse.Audio.GenreSon.Instrument && MyVerse.Audio.AudioConfiguration.DevinerGenre("Table de mixage") == MyVerse.Audio.GenreSon.Table
        && MyVerse.Audio.AudioConfiguration.DevinerGenre("Micro pasteur") == MyVerse.Audio.GenreSon.Voix, "un ancien micro est reconnu d'après son nom (piano, table, voix)");

    // le volume en % : 100 % = normal
    Ok(MyVerse.Audio.VolumeCurseur.Pourcent(0) == 100 && MyVerse.Audio.VolumeCurseur.Pourcent(6) == 125 && MyVerse.Audio.VolumeCurseur.Pourcent(-60) == 0 && MyVerse.Audio.VolumeCurseur.Pourcent(-18) == 50,
        "volume en % : 100 % = normal, 125 % au plus, 50 % ≈ −18 dB");
    var courbe = Enumerable.Range(0, 101).Select(i => MyVerse.Audio.VolumeCurseur.Db(i / 100.0)).ToList();
    Ok(courbe.Zip(courbe.Skip(1)).All(x => x.Second >= x.First) && Math.Abs(MyVerse.Audio.VolumeCurseur.Db(MyVerse.Audio.VolumeCurseur.Position(-7.5)) + 7.5) < 1e-9, "le curseur de volume monte toujours, et retombe sur le même volume");
    Egal(MyVerse.Audio.VolumeCurseur.Texte(-60), "Muet", "volume au minimum : Muet");
    // ce que dit la carte d'un son
    var suivi = new MyVerse.Audio.SuiviNiveau();
    suivi.Ajouter(double.NegativeInfinity, 0); Egal(suivi.Verdict(0), MyVerse.Audio.VerdictNiveau.Silence, "rien : silence");
    suivi.Ajouter(-40, 100); Egal(suivi.Verdict(100), MyVerse.Audio.VerdictNiveau.Faible, "−40 dBFS : un peu faible");
    suivi.Ajouter(-12, 200); Egal(suivi.Verdict(200), MyVerse.Audio.VerdictNiveau.Bon, "−12 dBFS : bon niveau");
    suivi.Ajouter(-0.2, 300); Egal(suivi.Verdict(1500), MyVerse.Audio.VerdictNiveau.TropFort, "le plafond touché : trop fort, pendant 2 s");
    Egal(suivi.Verdict(2400), MyVerse.Audio.VerdictNiveau.Bon, "puis de nouveau bon");
    Egal(suivi.Verdict(3500), MyVerse.Audio.VerdictNiveau.Silence, "3 s sans rien : silence");
    // la protection anti-saturation des destinations
    var prot = new MyVerse.Audio.ProtectionSaturation();
    var fort = Enumerable.Range(0, 4800).Select(i => (float)(1.8 * Math.Sin(i / 10.0))).ToArray();
    prot.Traiter(fort, fort.Length);
    Ok(fort.All(x => Math.Abs(x) <= 0.8913f) && prot.Retenu(), "protection : un son trop fort ne dépasse jamais −1 dBFS", fort.Max(Math.Abs).ToString("0.0000"));
    var doux = Enumerable.Range(0, 4800).Select(i => (float)(0.3 * Math.Sin(i / 10.0))).ToArray(); var copie = doux.ToArray();
    var prot2 = new MyVerse.Audio.ProtectionSaturation(); prot2.Traiter(doux, doux.Length);
    Ok(doux.SequenceEqual(copie) && !prot2.Retenu(), "protection : un son normal passe sans changement");
    var pic = new float[96000]; pic[0] = pic[1] = 1.5f; for (int i = 2; i < pic.Length; i++) pic[i] = 0.5f;
    var prot3 = new MyVerse.Audio.ProtectionSaturation(); prot3.Traiter(pic, pic.Length);
    Ok(Math.Abs(pic[^1] - 0.5f) < 0.01f, "protection : après une pointe, le son revient à son niveau", pic[^1].ToString("0.000"));

    // réglages de la 4.1 (audio « comme OBS 32 ») : les sources deviennent des sons, chaque destination reçoit ses sources
    var dir41 = Temp();
    File.WriteAllText(Path.Combine(dir41, "reglages.json"), """
    {"version":4,"theme":"nuit","audio":{"Version":5,"SampleRate":44100,"Channels":"5.1","MeterDecay":"lente","PeakMeter":"reelle","DisableDucking":true,"LowLatency":true,
      "AutoAddInputs":true,"KnownInputs":["{yeti}"],"KnownInputsReady":true,"GlobalsInitialized":true,"ShowHidden":true,"ShowInactive":false,"HiddenRight":true,"InactiveRight":false,
      "VerticalLayout":false,"FfmpegPath":"C:\\ffmpeg.exe","MonitorDeviceId":"{casque}","MonitorDeviceName":"Casque (USB)","MicrophoneId":"default","OutputId":"default","PanelVisible":true,
      "obsSources":[
        {"Id":"bureau","Name":"Audio du bureau","Kind":1,"DeviceId":"default","Global":"bureau1","Tracks":3},
        {"Id":"micro","Name":"Micro/Aux","Kind":0,"DeviceId":"default","Global":"micro1","Tracks":0},
        {"Id":"pasteur","Name":"Pasteur","Kind":0,"DeviceId":"{umc}","DeviceName":"Line (UMC404HD)","Channel":1,"Hidden":true,"Pinned":false,"PushToTalk":true,"PushToTalkKey":"F13","Balance":0.25,"SyncOffsetMs":120,"VolumeDb":-3},
        {"Id":"chorale","Name":"Chorale","Kind":0,"DeviceId":"{umc}","Channel":2,"Pinned":true,"Monitoring":2},
        {"Id":"guitare","Name":"Guitare","Kind":0,"DeviceId":"{umc}","Channel":3,"Mono":true}],
      "enregistrements":[
        {"Id":"culte","Name":"Culte","Track":0,"SourceIds":["pasteur","chorale","guitare","bureau","inconnue"],"MuteOthers":false,"Format":"wav"},
        {"Id":"predication","Name":"Prédication","Track":4,"SourceIds":["pasteur"],"MuteOthers":true,"Format":"flac"}],
      "sortiesVirtuelles":[{"Id":"zoom","Name":"Zoom","Enabled":true,"Track":2,"SourceIds":["chorale","pasteur"],"MuteOthers":true,"VolumeDb":-2}]}}
    """);
    var d41 = Donnees.Charger(dir41, res);
    var a41 = d41.Reglages.Audio;
    a41.Normalize();
    Egal(string.Join(",", a41.Sons.Select(x => x.Nom)), "Audio du bureau,Micro/Aux,Pasteur,Chorale,Guitare", "4.1 : toutes les sources deviennent des sons, dans le même ordre (2.4.0 : pas de son des vidéos ajouté tout seul)");
    Egal(string.Join(",", a41.Sons.Select(x => x.Genre)), "Ordinateur,Voix,Voix,Voix,Instrument", "4.1 : chaque son reconnu (l'ordinateur, des micros, une guitare)");
    var pasteur41 = a41.Sons.First(x => x.Id == "pasteur");
    Ok(pasteur41 is { Entree: 1, VolumeDb: -3, Appareil: "{umc}", NomAppareil: "Line (UMC404HD)", Eglise: false } && a41.Sons.First(x => x.Id == "chorale").Eglise,
        "4.1 : entrée, volume et appareil gardés ; le monitoring devient « dans l'église »");
    var culte41 = a41.Profiles.First(x => x.Id == "culte"); var pred41 = a41.Profiles.First(x => x.Id == "predication"); var zoom41 = a41.VirtualSounds.First();
    Egal(a41.Recoit(culte41), "Audio du bureau, Pasteur, Chorale et Guitare", "4.1 : Culte reçoit ses sources (sans Micro/Aux)");
    Egal(a41.Recoit(pred41), "Pasteur seulement", "4.1 : Prédication, le pasteur seul");
    Egal(a41.Recoit(zoom41), "Pasteur et Chorale", "4.1 : Zoom, le pasteur et la chorale");
    Ok(a41.Eglise.Appareil == "{casque}" && a41.Eglise.NomAppareil == "Casque (USB)" && zoom41.Enabled && culte41.Format == "wav" && pred41.Format == "wav",
        "4.1 : le périphérique de monitoring devient les haut-parleurs de l'église ; Zoom gardé ; FLAC devient WAV");
    Egal(a41.Version, MyVerse.Audio.AudioConfiguration.Actuelle, "4.1 : réglages audio passés en version 7");
    d41.SauverReglages();
    var json13 = File.ReadAllText(Path.Combine(dir41, "reglages.json"));
    var oublies = new[] { "SampleRate", "Channels", "MeterDecay", "PeakMeter", "DisableDucking", "LowLatency", "AutoAddInputs", "KnownInputs", "GlobalsInitialized", "ShowHidden", "HiddenRight",
        "VerticalLayout", "FfmpegPath", "MicrophoneId", "\"Global\"", "Pinned", "Hidden", "PushToTalk", "MuteOthers", "SourceIds", "obsSources", "Tracks", "Monitoring", "MonitorDevice", "\"track\"", "\"muted\"" };
    Ok(oublies.All(x => !json13.Contains(x, StringComparison.OrdinalIgnoreCase)), "1.3 : les anciens réglages ne sont plus écrits (ni pistes ni monitoring)", string.Join(", ", oublies.Where(x => json13.Contains(x, StringComparison.OrdinalIgnoreCase))));
    var r13 = Donnees.Charger(dir41, res).Reglages.Audio; r13.Normalize();
    Ok(r13.Sons.Select(x => x.Id).SequenceEqual(a41.Sons.Select(x => x.Id)) && r13.Recoit(r13.Profiles.First(x => x.Id == "culte")) == a41.Recoit(culte41) && r13.Recoit(r13.VirtualSounds[0]) == "Pasteur et Chorale",
        "1.3 relue : les sons et ce que reçoit chaque destination sont gardés");

    // réglages de la 1.2 (version 6, « comme OBS ») : la piste 6 de Zoom, une capture ASIO, la vidéo projetée sans église
    var dir12 = Temp();
    File.WriteAllText(Path.Combine(dir12, "reglages.json"), """
    {"version":4,"theme":"nuit","audio":{"Version":6,"MonitorDeviceId":"default","MonitorDeviceName":"","PanelVisible":true,
      "sortiesVirtuelles":[{"Id":"zoom","Name":"Zoom","Enabled":true,"DeviceId":"","DeviceName":"","Track":6,"VolumeDb":0,"Muted":false}],
      "obsSources":[
        {"Id":"micro","Name":"Capture audio (entrée)","Kind":0,"DeviceId":"{umc}","DeviceName":"Micro (UMC)","Channel":1,"VolumeDb":-2,"Muted":false,"Mono":false,"Tracks":63,"Monitoring":0,
         "Filters":[{"Id":"f1","Kind":0,"Enabled":true},{"Id":"f2","Kind":1,"Enabled":true}],"Balance":0.5,"SyncOffsetMs":0},
        {"Id":"piano","Name":"Piano","Kind":0,"DeviceId":"{umc}","Channel":2,"Tracks":31,"Monitoring":0,"Filters":[]},
        {"Id":"video","Name":"Vidéo Display","Kind":2,"FollowProjection":true,"Tracks":63,"Monitoring":0},
        {"Id":"asio","Name":"Chant","Kind":3,"DeviceId":"Focusrite USB ASIO","AsioCanaux":[1],"Tracks":63}],
      "enregistrements":[{"Id":"culte","Name":"Culte","Track":1,"Format":"flac","AudioOnly":true,"DisplayIds":[]},
                         {"Id":"film","Name":"Vidéo","Track":6,"Format":"mkv","AudioOnly":false,"DisplayIds":["projection"]}]}}
    """);
    var a6 = Donnees.Charger(dir12, res).Reglages.Audio; a6.Normalize();
    Egal(string.Join(",", a6.Sons.Select(x => x.Nom + ":" + x.Genre)), "Capture audio (entrée):Voix,Piano:Instrument,Chant:Voix,Vidéo Display:Video", "1.2 : chaque source devient un son ; la vidéo projetée passe à la fin");
    Ok(a6.Sons.Count(x => x.Genre == MyVerse.Audio.GenreSon.Video) == 1 && a6.SonVideos!.Id == "video" && a6.SonVideos.Eglise, "1.2 : le son de la vidéo projetée est gardé (un seul) et s'entend maintenant dans l'église");
    Ok(a6.Sons.First(x => x.Id == "micro") is { EnleverBruit: true, VolumeRegulier: false, Entree: 1, VolumeDb: -2 }, "1.2 : réduction du bruit gardée ; entrée et volume gardés");
    var asio6 = a6.Sons.First(x => x.Id == "asio");
    Ok(asio6.Capture == MyVerse.Audio.AudioInputKind.Asio && asio6.EntreesAsio.SequenceEqual(new[] { 1 }) && asio6.Appareil == "Focusrite USB ASIO", "1.2 : capture ASIO gardée");
    Egal(a6.Recoit(a6.Profiles.First(x => x.Id == "culte")), "Tout le son", "1.2 : piste 1 (toutes les sources) → tout le son");
    Egal(a6.Recoit(a6.VirtualSounds[0]), "Capture audio (entrée), Chant et Vidéo Display", "1.2 : la piste 6 de Zoom → ses sources");
    Egal(a6.Recoit(a6.Profiles.First(x => x.Id == "film")), "Capture audio (entrée), Chant et Vidéo Display", "1.2 : la vidéo sur la piste 6 → les mêmes sources");
    a6.Planifier();
    Ok(a6.VirtualSounds[0].Track == a6.Profiles.First(x => x.Id == "film").Track && a6.VirtualSounds[0].Track == 2, "1.2 : Zoom et la vidéo partagent une piste, donnée par Siloé Stream", a6.VirtualSounds[0].Track.ToString());

    // capture ASIO : une entrée (mono) ou deux (stéréo)
    var asio = new MyVerse.Audio.AudioConfiguration();
    asio.Ajouter(new MyVerse.Audio.Son { Id = "chanteur", Nom = "CHANTEUR", Genre = MyVerse.Audio.GenreSon.Voix, Capture = MyVerse.Audio.AudioInputKind.Asio, Appareil = "Focusrite USB ASIO", EntreesAsio = new() { 0 } });
    asio.Ajouter(new MyVerse.Audio.Son { Id = "clavier", Nom = "Clavier", Genre = MyVerse.Audio.GenreSon.Instrument, Capture = MyVerse.Audio.AudioInputKind.Asio, Appareil = "Focusrite USB ASIO", EntreesAsio = new() { 0, 1, 5 } });
    asio.Ajouter(new MyVerse.Audio.Son { Id = "vide", Nom = "Vide", Genre = MyVerse.Audio.GenreSon.Voix, Capture = MyVerse.Audio.AudioInputKind.Asio, Appareil = "X", EntreesAsio = new() { -7 } });
    asio.Normalize();
    Ok(asio.Sons[0].EntreesAsio.SequenceEqual(new[] { 0 }), "ASIO mono : Input 1");
    Ok(asio.Sons[1].EntreesAsio.SequenceEqual(new[] { 0, 1 }), "ASIO stéréo : deux entrées au plus");
    Ok(asio.Sons[2].EntreesAsio.SequenceEqual(new[] { -1 }), "ASIO : entrée hors limites ramenée à Muet");
    Egal(MyVerse.Audio.AudioConfiguration.Description(asio.Sons[0]), "Micro · Focusrite USB ASIO · entrée 1", "description d'une capture ASIO");
}

Groupe("Projecteur (1.0)");
if (Actif())
{
    var regie = new Moniteur("Écran principal", 0, 0, 1920, 1080, true, @"\\.\DISPLAY1");
    var pj = new Moniteur("EPSON PJ", 1920, 0, 1280, 720, false, @"\\.\DISPLAY2");
    var scene = new Moniteur("DELL P2419H", 3200, 0, 1920, 1080, false, @"\\.\DISPLAY3");
    var choix = Projecteur.Choix(pj, true);
    Ok(choix is { Mode: "plein", Nom: "EPSON PJ", X: 1920, W: 1280, Principal: false, Ouvert: true } && choix.Peripherique == pj.Peripherique, "choix enregistré : écran, sortie, projection ouverte");
    Egal(Projecteur.Retrouver(choix, new[] { regie, pj, scene }), pj, "projecteur branché : retrouvé");
    Egal(Projecteur.Retrouver(choix, new[] { regie, scene }), (Moniteur?)null, "projecteur débranché : l'écran de scène n'est pas pris pour lui");
    Egal(Projecteur.Retrouver(choix, new[] { regie }), (Moniteur?)null, "projecteur débranché : jamais l'écran de la régie");
    var ailleurs = pj with { X = -1920, W = 1920, H = 1080, Peripherique = @"\\.\DISPLAY5" };
    Egal(Projecteur.Retrouver(choix, new[] { regie, ailleurs }), ailleurs, "rebranché sur une autre prise, ailleurs, autre résolution : retrouvé par son nom");
    var sansNom = pj with { Nom = "Écran 2" };
    Egal(Projecteur.Retrouver(choix, new[] { regie, sansNom }), sansNom, "Windows ne donne pas le nom cette fois : même sortie, retrouvé");
    Egal(Projecteur.Retrouver(choix, new[] { regie, scene with { Peripherique = pj.Peripherique } }), (Moniteur?)null, "même sortie mais un autre vrai nom : ce n'est pas le projecteur");
    var deux = pj with { Peripherique = @"\\.\DISPLAY4", X = 5120 };
    Egal(Projecteur.Retrouver(choix, new[] { regie, deux, pj }), pj, "deux écrans du même nom : celui de la même sortie");
    Egal(Projecteur.Retrouver(choix, new[] { pj with { Principal = true } }), (Moniteur?)null, "le projecteur devenu écran principal : pas de projection par-dessus la régie");
    Egal(Projecteur.Retrouver(Projecteur.Choix(regie, true), new[] { regie, pj }), (Moniteur?)null, "projection sur l'écran principal : jamais rouverte toute seule");
    Egal(Projecteur.Retrouver(Projecteur.Choix(regie, true), new[] { regie, pj }, principal: true), regie, "… mais une projection ouverte par l'opérateur sur l'écran principal y reste");
    Egal(Projecteur.Retrouver(Projecteur.Choix(pj, false), new[] { regie, pj }), (Moniteur?)null, "projection en fenêtre : rien à retrouver");
    var generique = Projecteur.Choix(pj with { Nom = "Écran 2" }, true);
    Egal(Projecteur.Retrouver(generique, new[] { regie, pj with { Nom = "Écran 2", Peripherique = "" } }), pj with { Nom = "Écran 2", Peripherique = "" }, "sans nom ni sortie : même place, même taille");
    Egal(Projecteur.Retrouver(generique, new[] { regie, scene with { Nom = "Écran 2", Peripherique = "" } }), (Moniteur?)null, "sans nom : un écran à une autre place n'est pas pris");
    Egal(Projecteur.Choix(sansNom, true, choix).Nom, "EPSON PJ", "même sortie, nom absent cette fois : le vrai nom est gardé");
    Egal(Projecteur.Choix(scene with { Nom = "Écran 3" }, true, choix).Nom, "Écran 3", "autre écran sans nom : pas le nom du projecteur");
    Ok(Projecteur.NomGenerique("Écran 2") && Projecteur.NomGenerique("Écran principal") && Projecteur.NomGenerique("") && !Projecteur.NomGenerique("EPSON PJ"), "noms génériques");
    Ok(Projecteur.ARouvrir(choix), "démarrage : projection laissée ouverte, rouverte");
    Ok(!Projecteur.ARouvrir(new EcranChoisi { Mode = "plein", Nom = "EPSON PJ", Ouvert = false }), "démarrage : arrêtée par l'opérateur, pas rouverte");
    Ok(Projecteur.ARouvrir(new EcranChoisi { Mode = "plein", Nom = "EPSON PJ" }), "démarrage : réglage de Siloé Stream 4 (sans « ouvert »), rouverte");
    Ok(!Projecteur.ARouvrir(Projecteur.Choix(regie, true)), "démarrage : jamais de plein écran automatique sur l'écran de la régie");
    Ok(Projecteur.ARouvrir(Projecteur.Choix(regie, false)), "démarrage : une fenêtre se rouvre");
    Ok(!Projecteur.ARouvrir(new EcranChoisi { Mode = "obs" }) && !Projecteur.ARouvrir(null), "démarrage : OBS seulement ou rien de choisi, rien à rouvrir");
    // les réglages : écrits, relus, et ceux de Siloé Stream 4
    var dirP = Temp();
    var dP = Donnees.Charger(dirP, res);
    dP.Reglages.Ecran = choix;
    dP.SauverReglages();
    var relu = Donnees.Charger(dirP, res).Reglages.Ecran;
    Ok(relu is { Ouvert: true, Nom: "EPSON PJ" } && relu.Peripherique == pj.Peripherique, "réglages : sortie et « ouvert » relus");
    var json = File.ReadAllText(Path.Combine(dirP, "reglages.json"));
    var ancien = json.Replace("\"ouvert\"", "\"ancien-ouvert\"").Replace("\"periph\"", "\"ancien-periph\"");
    File.WriteAllText(Path.Combine(dirP, "reglages.json"), ancien);
    var relu4 = Donnees.Charger(dirP, res).Reglages.Ecran;
    Ok(relu4 is { Ouvert: null, Peripherique: "" } && Projecteur.ARouvrir(relu4) && Projecteur.Retrouver(relu4, new[] { regie, ailleurs }) == ailleurs, "réglage de Siloé Stream 4 : rouvert au démarrage, projecteur retrouvé par son nom");
}

Groupe("Audio OBS");
if (Actif()) MyVerse.Tests.ObsTests.Run(Ok);
Groupe("Recherche 1.4.0 : recueil + numéro, paroles mal écrites");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    rg.AjouterChants(new[] { new Chant { Title = "45 - Ndashima", Sections = { new Section { Label = "Couplet 1", Text = "Je leva les yeux vers Dieu\nIl m'a sauvé" } } } }, "Nyimbo za Wokovu");
    rg.AjouterChants(new[] { new Chant { Title = "45 - Autre", Sections = { new Section { Label = "Couplet 1", Text = "Rien à voir" } } } }, "Cantiques");
    var w = rg.Commande("wokovu 45");
    Ok(w.Count > 0 && w[0].Titre == "45 - Ndashima", "« wokovu 45 » : le chant 45 du recueil Wokovu");
    Ok(!w.Any(x => x.Titre == "45 - Autre"), "« wokovu 45 » : pas le 45 d'un autre recueil");
    Ok(rg.Commande("wokovo 45").Any(x => x.Titre == "45 - Ndashima"), "faute dans le nom du recueil tolérée");
    Ok(rg.Commande("45 wokovu").Any(x => x.Titre == "45 - Ndashima") && !rg.Commande("45 wokovu").Any(x => x.Titre == "45 - Autre"), "1.5.1 : « 45 wokovu » : le numéro d'abord, seulement ce recueil");
    Ok(rg.Commande("45 wok").Any(x => x.Titre == "45 - Ndashima") && rg.Commande("45wok").Any(x => x.Titre == "45 - Ndashima"), "1.5.1 : « 45 wok » et « 45wok » (sans espace)");
    Ok(rg.Commande("je lava vers dieu").Any(x => x.Titre == "45 - Ndashima"), "« je lava vers dieu » trouve « je leva les yeux vers Dieu »");
    Ok(!rg.Commande("bonjour la maison").Any(x => x.Titre == "45 - Ndashima"), "des mots sans rapport ne trouvent pas le chant");
}

Groupe("Recherche vocale (2.2.0) : ce qui est entendu devient un chant ou un verset");
if (Actif())
{
    Egal(RechercheVocale.Nettoyer(" [Musique] Jean 3:16 (applaudissements) ♪ "), "Jean 3:16", "texte de Whisper nettoyé (annotations retirées)");
    Egal(RechercheVocale.Nettoyer("Sous-titrage ST' 501"), "", "hallucination de Whisper sur du silence ignorée");
    var refs = RechercheVocale.References("Ouvrons nos Bibles dans Jean chapitre 3, verset 16 à 18. Puis Romains 8:28.");
    Ok(refs.Any(r => Livres.Lire(r) is { B: 42, C: 3, V1: 16, V2: 18 }) && refs.Any(r => Livres.Lire(r) is { B: 44, C: 8, V1: 28 }),
        "références dites à voix haute : « Jean chapitre 3, verset 16 à 18 », « Romains 8:28 »", string.Join(" | ", refs));
    Ok(RechercheVocale.References("lisons le psaume 23").Any(r => Livres.Lire(r) is { B: 18, C: 23 }), "« le psaume 23 »", string.Join(" | ", RechercheVocale.References("lisons le psaume 23")));
    Ok(RechercheVocale.References("premier Jean 1 verset 9").Any(r => Livres.Lire(r) is { B: 61, C: 1, V1: 9 }), "« premier Jean 1 verset 9 »", string.Join(" | ", RechercheVocale.References("premier Jean 1 verset 9")));
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    rg.AjouterChants(new[] { new Chant { Title = "45 - Ndashima", Sections = { new Section { Label = "Couplet 1", Text = "Je leva les yeux vers Dieu\nIl m'a sauvé de la mort" } } } }, "Nyimbo za Wokovu");
    var b = RechercheVocale.Chercher(rg, "Comme le dit Jean chapitre 3 verset 16 : car Dieu a tant aimé le monde", RechercheVocale.Bible, null);
    Ok(b.Count > 0 && b[0].Ref is { B: 42, C: 3, V1: 16 } && b.All(x => x.Ref != null), "Bible : la référence citée d'abord, que des versets", string.Join(" | ", b.Select(x => x.Titre)));
    var lu = RechercheVocale.Chercher(rg, "car Dieu a tellement aimé le monde qu'il a donné son fils unique", RechercheVocale.Bible, null);
    Ok(lu.Any(x => x.Ref is { B: 42, C: 3, V1: 16 }), "Bible : un verset lu sans sa référence est retrouvé par ses mots", string.Join(" | ", lu.Select(x => x.Titre)));
    var en = RechercheVocale.Chercher(rg, "For God so loved the world, that he gave his only begotten Son", RechercheVocale.Bible, null);
    Ok(en.Any(x => x.Ref is { B: 42, C: 3, V1: 16 }), "Bible : un verset lu en anglais (King James)", string.Join(" | ", en.Select(x => x.Titre)));
    var c = RechercheVocale.Chercher(rg, "je lève les yeux vers Dieu, il m'a sauvé", RechercheVocale.Chant, null);
    Ok(c.Count > 0 && c[0].Titre == "45 - Ndashima" && c.All(x => x.ChantId != null), "Chant : les paroles chantées retrouvent le chant, que des chants", string.Join(" | ", c.Select(x => x.Titre)));
    Ok(RechercheVocale.Chercher(rg, "[Musique]", RechercheVocale.Chant, null).Count == 0, "rien d'entendu : aucun résultat");
}

Groupe("Miniatures YouTube (2.3.0)");
if (Actif())
{
    var (t, acc) = MyVerse.Miniatures.Calque.Accents("la *grâce* de Dieu");
    Ok(t == "la grâce de Dieu" && acc.Count == 1 && acc[0] == (3, 5), "mots en couleur : « la *grâce* de Dieu »", t);
    Egal(MyVerse.Miniatures.Calque.Accents("5 * 3 = 15").Texte, "5 * 3 = 15", "un astérisque seul reste tel quel");
    Egal(MyVerse.Miniatures.Calque.AccentAuto("La puissance de la prière"), "La *puissance* de la prière", "mot fort mis en avant tout seul");
    Egal(MyVerse.Miniatures.Calque.AccentAuto("Le *salut* gratuit"), "Le *salut* gratuit", "un accent choisi par l'utilisateur est gardé");
    bool tousBons = true;
    string detail = "";
    foreach (var m in MyVerse.Miniatures.ModelesMiniature.Tous)
    {
        var p = MyVerse.Miniatures.ModelesMiniature.Creer(m.Id, "fond.jpg", "La puissance de la prière", "Pasteur Jean", "logo.png");
        bool ok = p.Role("fond")?.Chemin == "fond.jpg" && p.Role("titre") != null && p.Role("logo") != null
            && p.Calques.All(c => c.X > -200 && c.Y > -200 && c.X + c.L < 1500 && c.Y + c.H < 950);
        if (!ok) { tousBons = false; detail += m.Id + " "; }
        var relu = MyVerse.Miniatures.ProjetMiniature.DepuisJson(p.VersJson());
        if (relu.VersJson() != p.VersJson()) { tousBons = false; detail += m.Id + "(json) "; }
    }
    Ok(tousBons, "les 6 modèles : fond, titre et logo à leur place, relus à l'identique", detail);
    var sans = MyVerse.Miniatures.ModelesMiniature.Creer("predication", null, "Titre", "", null);
    Ok(sans.Role("fond") == null && sans.Role("logo") == null && sans.Role("sous") == null && sans.Role("titre") != null, "modèle sans image ni logo ni sous-titre : seulement ce qu'on a");
    var mal = new MyVerse.Miniatures.ProjetMiniature();
    mal.Calques.Add(new MyVerse.Miniatures.Calque { Type = MyVerse.Miniatures.Calque.TypeTexte, Texte = "Écoutez", X = 1100, Y = 650, L = 170, H = 60, Taille = 30 });
    var conseils = mal.Conseils();
    Ok(conseils.Any(c => c.Contains("illisible")) && conseils.Any(c => c.Contains("minutage")), "conseils : texte trop petit pour un téléphone, sous le minutage de YouTube", string.Join(" | ", conseils));
    Ok(new MyVerse.Miniatures.ProjetMiniature().Conseils().Any(c => c.Contains("Pas de texte")), "conseil : pas de texte");
    var bizarre = MyVerse.Miniatures.ProjetMiniature.DepuisJson("{\"calques\":[{\"type\":\"inconnu\",\"couleur\":\"rouge\",\"taille\":-5,\"opacite\":7},null]}");
    Ok(bizarre.Calques.Count == 1 && bizarre.Calques[0].Type == "texte" && bizarre.Calques[0].Couleur == "#FFFFFF" && bizarre.Calques[0].Taille == 6 && bizarre.Calques[0].Opacite == 1,
        "fichier modifié à la main : valeurs impossibles corrigées");
}

Groupe("YouTube (1.6.0)");
if (Actif()) MyVerse.Tests.YouTubeTests.RunAsync(Ok).GetAwaiter().GetResult();

Groupe("À distance par Internet (1.7.0) : relais, agent, démarrer Siloé Stream");
if (Actif())
{
    // le vrai relais (relais/relais.js, Node + ws) sur ce PC, en ws:// ; l'agent et un « téléphone » s'y relient
    var script = Path.GetFullPath(Path.Combine(res, "..", "..", "..", "relais", "relais.js"));
    int portRelais = 18000 + Random.Shared.Next(2000);
    System.Diagnostics.Process? node = null;
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("node", $"\"{script}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["PORT"] = portRelais.ToString();
        if (File.Exists(script)) node = System.Diagnostics.Process.Start(psi);
    }
    catch (System.ComponentModel.Win32Exception) { }
    string? pret = null;
    if (node != null)
    {
        var lu = node.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(lu, Task.Delay(8000)) == lu) pret = lu.Result;
    }
    if (pret == null || !pret.Contains("relais SiloeStream"))
    {
        Console.WriteLine($"   (relais non essayé : node ou le module ws absent — {script})" + (node != null ? " " + await node.StandardError.ReadToEndAsync() : ""));
        try { node?.Kill(); } catch { }
    }
    else
    {
        var relais = $"ws://127.0.0.1:{portRelais}";
        Egal(ReglagesDistance.Nettoyer(" https://2-25-216-99.sslip.io/ "), "2-25-216-99.sslip.io", "adresse du relais nettoyée");
        Egal(ReglagesDistance.Adresse("2-25-216-99.sslip.io", "/pc/x").ToString(), "wss://2-25-216-99.sslip.io/pc/x", "relais en HTTPS (wss://)");
        Egal(ReglagesDistance.AdresseEtat("2-25-216-99.sslip.io", "x").ToString(), "https://2-25-216-99.sslip.io/etat/x", "page d'état du relais");

        var don = Donnees.Charger(Temp(), res);
        var dossier = don.Dossier;
        new ReglagesDistance { Actif = true, Relais = relais }.Ecrire(dossier);
        var store = new AppareilsJumeles(dossier);
        var pcId = store.PcId;
        var cleTel = RandomNumberGenerator.GetBytes(32);
        store.Ajouter("tel-maison", "Pixel de Mike", cleTel);
        Ok(await ReglagesDistance.PcJoignable(relais, pcId) == false, "relais en marche, PC pas encore relié : « pas joignable »");

        Telephone? pcSiloe = null;
        Regie? rgD = null;
        int demarrages = 0;
        bool Demarrer()
        {
            demarrages++;
            rgD = new Regie(don, new Bibles(don));
            pcSiloe = new Telephone(dossier, rgD, f => f(), "PC-EGLISE");
            pcSiloe.FermerDemande = () => null;
            pcSiloe.Demarrer();
            return true;
        }
        using var agent = new AgentDistance(dossier, Demarrer, "PC-EGLISE") { PauseReconnexion = TimeSpan.FromMilliseconds(300) };
        var tache = agent.Executer();
        for (int i = 0; i < 50 && !agent.Connecte; i++) await Task.Delay(100);
        Ok(agent.Connecte, "l'agent se relie au relais tout seul", string.Join(" | ", Journal.Lignes().Where(l => l.Contains("distance")).TakeLast(3)));
        Ok(await ReglagesDistance.PcJoignable(relais, pcId) == true, "le relais dit : PC joignable de l'extérieur (Réglages)");

        async Task<System.Net.WebSockets.ClientWebSocket> TelOuvrir()
        {
            var ws = new System.Net.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri($"{relais}/telephone/{pcId}"), CancellationToken.None);
            return ws;
        }
        async Task TelEnv(System.Net.WebSockets.ClientWebSocket ws, object o) =>
            await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(o)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        async Task<System.Text.Json.Nodes.JsonObject?> TelRec(System.Net.WebSockets.ClientWebSocket ws, string? type = null, int ms = 5000)
        {
            var finR = DateTime.Now.AddMilliseconds(ms);
            var buf = new byte[1 << 16];
            while (DateTime.Now < finR)
            {
                using var cts = new CancellationTokenSource(finR - DateTime.Now);
                using var mem = new MemoryStream();
                try
                {
                    System.Net.WebSockets.WebSocketReceiveResult r;
                    do { r = await ws.ReceiveAsync(buf, cts.Token); mem.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                    if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
                }
                catch (Exception e) when (e is OperationCanceledException or System.Net.WebSockets.WebSocketException) { return null; }
                var o = System.Text.Json.Nodes.JsonNode.Parse(mem.ToArray())!.AsObject();
                if (type == null || (string?)o["t"] == type) return o;
            }
            return null;
        }
        async Task<(System.Net.WebSockets.ClientWebSocket Ws, System.Text.Json.Nodes.JsonObject? Bienvenue)> TelConnecter(byte[] cle, string device = "tel-maison")
        {
            var ws = await TelOuvrir();
            await TelEnv(ws, new { t = "hello", v = 1, pcId, deviceId = device, deviceName = "Pixel de Mike", pair = false, cnonce = "cn9" });
            var ch = await TelRec(ws);
            if ((string?)ch?["t"] != "challenge") return (ws, ch);
            var nonce = (string)ch!["nonce"]!;
            Ok((string?)ch["proof"] == Telephone.PreuveServeur(cle, "cn9", nonce), "par Internet aussi, le PC prouve qu'il connaît la clé du téléphone");
            await TelEnv(ws, new { t = "auth", mac = Telephone.MacClient(cle, nonce, device) });
            return (ws, await TelRec(ws));
        }

        // Siloé Stream fermé : l'agent répond
        var (w1, b1) = await TelConnecter(cleTel);
        Ok((string?)b1?["t"] == "welcome" && (bool?)b1?["agent"] == true && (string?)b1?["app"] == "ferme", "Siloé Stream fermé : l'agent accueille le téléphone (« Siloé Stream est fermé »)", b1?.ToJsonString());
        await TelEnv(w1, new { t = "bible.show", @ref = "jn 3 16", rid = "7" });
        var er = await TelRec(w1, "error");
        Ok(er != null && (string?)er["rid"] == "7" && ((string?)er["message"] ?? "").Contains("Démarrer Siloé Stream"), "une commande pendant que Siloé Stream est fermé : « appuyez sur Démarrer Siloé Stream »");
        // démarrer Siloé Stream depuis le téléphone
        await TelEnv(w1, new { t = "app.start", rid = "8" });
        var e1 = await TelRec(w1, "app.etat");
        var e2 = await TelRec(w1, "app.etat", 10000);
        Ok((string?)e1?["app"] == "demarrage" && (string?)e2?["app"] == "ouvert" && demarrages == 1 && pcSiloe?.Port > 0, "« Démarrer Siloé Stream » : l'agent le lance, puis dit « ouvert »");
        Ok(await TelRec(w1, null, 3000) == null, "… et ferme la connexion : le téléphone se reconnecte");
        Egal(PortSiloe.Lire(dossier), pcSiloe?.Port, "Siloé Stream ouvert : son port est connu de l'agent");

        // Siloé Stream ouvert : le téléphone passe par l'agent jusqu'à Siloé Stream (même vérification, mêmes commandes)
        var (w2, b2) = await TelConnecter(cleTel);
        Ok((string?)b2?["t"] == "welcome" && b2?["agent"] == null && (string?)b2?["relay"] == ReglagesDistance.Nettoyer(relais), "Siloé Stream ouvert : le téléphone joint Siloé Stream lui-même (et apprend le relais)", b2?.ToJsonString());
        await TelEnv(w2, new { t = "bible.show", @ref = "jn 3 16", rid = 1 });
        var st = await TelRec(w2, "state");
        Egal((string?)st?["ref"], "Jean 3:16 (LSG)", "par Internet : un verset affiché à l'église depuis la maison");
        Ok(pcSiloe!.Connectes().Contains("tel-maison"), "Siloé Stream voit le téléphone connecté");
        Ok(pcSiloe.OuvrirJumelage().Contains("&relay=" + Uri.EscapeDataString(ReglagesDistance.Nettoyer(relais))), "code QR : le relais y est (nouveaux téléphones)");
        pcSiloe.FermerJumelage();
        await TelEnv(w2, new { t = "app.quit", rid = "9" });
        var fq = await TelRec(w2, "app.etat");
        Egal((string?)fq?["app"], "fermeture", "« Fermer Siloé Stream » depuis le téléphone");
        try { w2.Abort(); } catch { }

        // un téléphone inconnu (pas jumelé) est refusé, par l'agent comme par Siloé Stream
        var (w3, b3) = await TelConnecter(RandomNumberGenerator.GetBytes(32), "intrus");
        Ok((string?)b3?["t"] == "auth.fail", "un téléphone pas jumelé est refusé par Internet", b3?.ToJsonString());
        try { w3.Abort(); } catch { }
        pcSiloe.Dispose();
        Ok(PortSiloe.Lire(dossier) == null, "Siloé Stream fermé : le port n'est plus annoncé");
        var (w4, b4) = await TelConnecter(cleTel);
        Ok((bool?)b4?["agent"] == true, "Siloé Stream refermé : l'agent reprend le téléphone");
        try { w4.Abort(); } catch { }


        // coupé dans les Réglages : l'agent s'arrête tout seul
        new ReglagesDistance { Actif = false, Relais = relais }.Ecrire(dossier);
        var fini = await Task.WhenAny(tache, Task.Delay(10000)) == tache;
        Ok(fini, "contrôle à distance coupé dans les Réglages : l'agent s'arrête");
        try { node!.Kill(); } catch { }
    }
}

Groupe("Rôles des téléphones (1.7.1)");
if (Actif())
{
    var dossierR = Temp();
    var st = new AppareilsJumeles(dossierR);
    st.Ajouter("t1", "Tel 1", new byte[32]);
    Egal(st.Role("t1"), Roles.Admin, "un téléphone déjà jumelé reste Admin");
    st.ChangerRole("t1", Roles.Chants);
    Egal(new AppareilsJumeles(dossierR).Role("t1"), Roles.Chants, "le rôle est gardé (devices.json)");
    Ok(Roles.Permis(Roles.Chants, "song.show") && !Roles.Permis(Roles.Chants, "bible.show") && !Roles.Permis(Roles.Chants, "display.show") && !Roles.Permis(Roles.Chants, "app.quit"), "Chants : les chants, pas la Bible, ni les médias, ni fermer Siloé Stream");
    Ok(Roles.Permis(Roles.Bible, "bible.show") && Roles.Permis(Roles.Bible, "next") && !Roles.Permis(Roles.Bible, "song.show"), "Versets : la Bible et Suivant, pas les chants");
    Ok(Roles.Permis(Roles.Camera, "cam.ack") && !Roles.Permis(Roles.Camera, "next") && Roles.Images(Roles.Camera) && !Roles.Images(Roles.Bible), "Caméraman : MyCam seulement");
    Ok(Roles.Permis(Roles.Admin, "app.quit") && Roles.Permis(Roles.Admin, "display.show"), "Admin : tout");
    st.ChangerRole("t1", Roles.Admin);
    Egal(new AppareilsJumeles(dossierR).Role("t1"), Roles.Admin, "redevenu Admin");
}

Groupe("Comptes Google sur le PC (1.9.3)");
if (Actif())
{
    // le rôle Affichage, pour les téléphones et pour le logiciel
    Egal(Roles.Nettoyer(Roles.Affichage), Roles.Affichage, "Affichage : un rôle connu des téléphones");
    Ok(Roles.Connu("inconnu") == null && Roles.Connu(null) == null && Roles.Pages("inconnu").Count == 0, "rôle inconnu d'un compte : aucune page, jamais Admin");
    Ok(Roles.Permis(Roles.Affichage, "display.show") && Roles.Permis(Roles.Affichage, "agenda.show") && Roles.Permis(Roles.Affichage, "next")
        && !Roles.Permis(Roles.Affichage, "bible.show") && !Roles.Permis(Roles.Affichage, "song.show") && !Roles.Permis(Roles.Affichage, "app.quit"),
        "Affichage (téléphone) : médias, culte et Suivant ; pas la Bible, ni les chants, ni fermer");
    Ok(!Roles.Permis(Roles.Bible, "display.show") && !Roles.Permis(Roles.Bible, "agenda.show"), "Versets : ni les médias, ni le culte");
    Ok(Roles.Pages(Roles.Bible).SequenceEqual(new[] { "bible" }) && Roles.Pages(Roles.Chants).SequenceEqual(new[] { "chants" }), "pages du logiciel : Versets = Bible, Chants = chants");
    Ok(Roles.Pages(Roles.Affichage).SequenceEqual(new[] { "agenda", "annonces" }), "pages du logiciel : Affichage = Culte, Médias");
    Ok(Roles.Pages(Roles.Admin).Count == 8 && Roles.Pages(Roles.Admin).Contains("montage") && Roles.Pages(Roles.Admin).Contains("miniature") && Roles.Pages(Roles.Admin).Contains("reglages") && Roles.Pages(Roles.Camera).Count == 0, "pages du logiciel : Admin tout, Caméraman aucune");

    // la page siloestream.tech/connexion renvoie le jeton Google au logiciel, sur 127.0.0.1
    var rq = ConnexionGoogle.Analyser("POST /connexion HTTP/1.1\r\nHost: 127.0.0.1", "idToken=abc.def%2Bg+h&etat=XyZ_-1");
    Ok(rq is { Methode: "POST", Chemin: "/connexion" } && rq.Champs["idToken"] == "abc.def+g h" && rq.Champs["etat"] == "XyZ_-1", "formulaire de la page de connexion lu");
    using (var cx = new ConnexionGoogle())
    {
        Ok(cx.Adresse.StartsWith($"https://siloestream.tech/connexion/?port={cx.Port}&etat=") && cx.Etat.Length >= 20, "adresse de la page de connexion (port et code etat)");
        using var fin = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var attente = cx.Attendre(fin.Token);
        using var http = new HttpClient();
        var faux = await http.PostAsync($"http://127.0.0.1:{cx.Port}/connexion",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["idToken"] = new string('j', 40), ["etat"] = "mauvais" }));
        Ok(faux.StatusCode == System.Net.HttpStatusCode.NotFound && !attente.IsCompleted, "mauvais code etat : refusé, la connexion attend toujours");
        var jeton = "eyJhbGciOi." + new string('x', 60) + ".signature";
        var bon = await http.PostAsync($"http://127.0.0.1:{cx.Port}/connexion",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["idToken"] = jeton, ["etat"] = cx.Etat }));
        Ok(bon.IsSuccessStatusCode && (await bon.Content.ReadAsStringAsync()).Contains("connecté"), "bon code : la page « Vous êtes connecté »");
        Egal(await attente, jeton, "le jeton Google arrive au logiciel");
    }
    using (var cx2 = new ConnexionGoogle())
    {
        using var vite = new CancellationTokenSource(300);
        Egal(await cx2.Attendre(vite.Token), (string?)null, "connexion annulée : aucun jeton");
    }

    // le compte gardé sur le PC, et la connexion obligatoire gardée avec l'église du PC
    var dossierC = Temp();
    new SessionPc { Jeton = "j1", Email = "a@b.c", Nom = "A", Role = Roles.Affichage, Eglise = "e1", EgliseNom = "Église", Relais = "r" }.Ecrire(dossierC);
    Ok(SessionPc.Lire(dossierC) is { Role: Roles.Affichage, Email: "a@b.c", Eglise: "e1" }, "compte gardé (compte-pc.json)");
    SessionPc.Effacer(dossierC);
    Ok(SessionPc.Lire(dossierC) == null, "déconnexion : compte effacé");
    new CompteEglise { Eglise = "e1", Nom = "Église", Secret = Telephone.B64(new byte[32]), ConnexionObligatoire = true }.Ecrire(dossierC);
    Ok(CompteEglise.Lire(dossierC) is { ConnexionObligatoire: true, Eglise: "e1" }, "connexion obligatoire gardée (eglise.json)");
}

Groupe("Caméras USB fluides (1.6.5)");
if (Actif())
{
    var cam = new SourceDisplay { Type = "camera", Peripherique = "@device:pnp:usb-video" };
    Ok(cam.IpsVoulues == 60 && cam.TailleVoulue == "1080p", "caméra USB : 60 images par seconde en 1080p par défaut");
    Ok((cam with { Ips = 40 }).IpsVoulues == 40 && (cam with { Ips = 30 }).IpsVoulues == 30 && (cam with { Ips = 25 }).IpsVoulues == 60, "60, 40 ou 30 (autre chose : 60)");
    Ok((cam with { Taille = "4k" }).TailleVoulue == "4k" && (cam with { Taille = "720p" }).TailleVoulue == "720p" && (cam with { Taille = "8k" }).TailleVoulue == "1080p", "1080p, 4K ou 720p (autre chose : 1080p)");
    Ok(cam != cam with { Ips = 30 }, "changer les images par seconde change la source (la caméra se rouvre)");
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var a = rg.NouveauDisplay("USB Video", cam);
    Ok(rg.ModifierSource(a.Id, "", cam with { Ips = 40, Taille = "720p" }), "réglage changé (clic droit › Images par seconde)");
    var relu = Donnees.Charger(don.Dossier, res).Annonces.First(x => x.Id == a.Id);
    Ok(relu.Source is { Ips: 40, TailleVoulue: "720p" } && relu.Description.Contains("40 images/s") && relu.Description.Contains("720p"), "gardé : " + relu.Description);
    var json = System.Text.Json.JsonSerializer.Serialize(new SourceDisplay { Type = "camera", Peripherique = "x" }, Donnees.Json);
    Ok(!json.Contains("ips") && !json.Contains("taille"), "réglages par défaut : rien d'écrit en plus (" + json + ")");
}

Groupe("Directs automatiques (1.6.4)");
if (Actif())
{
    // les heures comme on les écrit
    Ok(DirectProgramme.LireHeure("10:00") == new TimeSpan(10, 0, 0) && DirectProgramme.LireHeure("17h40") == new TimeSpan(17, 40, 0)
        && DirectProgramme.LireHeure("10h") == new TimeSpan(10, 0, 0) && DirectProgramme.LireHeure("9 h 05") == new TimeSpan(9, 5, 0)
        && DirectProgramme.LireHeure("9.30") == new TimeSpan(9, 30, 0), "heures : « 10:00 », « 17h40 », « 10h », « 9 h 05 », « 9.30 »");
    Ok(DirectProgramme.LireHeure("25:00") == null && DirectProgramme.LireHeure("abc") == null && DirectProgramme.LireHeure("") == null && DirectProgramme.LireHeure("10:75") == null,
        "pas une heure : « 25:00 », « abc », vide, « 10:75 »");

    // chaque dimanche à 10:00 (le 15 novembre 2026 est un dimanche)
    var dim = new DirectProgramme { Jours = new() { 0 }, Heure = "10h" };
    dim.Normaliser();
    var dimanche = new DateTime(2026, 11, 15);
    Ok(dimanche.DayOfWeek == DayOfWeek.Sunday && dim.Heure == "10:00", "heure normalisée : 10:00");
    Ok(dim.Prochain(dimanche.AddHours(8)) == dimanche.AddHours(10), "dimanche 8 h : prochain direct à 10:00 le jour même");
    Ok(dim.Prochain(dimanche.AddHours(10)) == dimanche.AddHours(10), "dimanche 10:00 pile : c'est maintenant");
    Ok(dim.Prochain(dimanche.AddHours(11)) == dimanche.AddDays(7).AddHours(10), "dimanche 11 h : le prochain, dimanche suivant");
    Ok(dim.Prochain(new DateTime(2026, 11, 18, 12, 0, 0)) == new DateTime(2026, 11, 22, 10, 0, 0), "mercredi : le prochain, dimanche 22 novembre");
    Ok(dim.AFaire(dimanche.AddHours(9).AddMinutes(59)) == null, "9:59 : pas encore");
    Ok(dim.AFaire(dimanche.AddHours(10)) == dimanche.AddHours(10), "10:00 : le direct démarre");
    Ok(dim.AFaire(dimanche.AddHours(10).AddMinutes(14)) == dimanche.AddHours(10), "10:14 (Siloé Stream ouvert en retard) : il démarre encore");
    Ok(dim.AFaire(dimanche.AddHours(10).AddMinutes(16)) == null, "10:16 : trop tard, il ne démarre plus tout seul");
    dim.Dernier = dimanche.AddHours(10);
    Ok(dim.AFaire(dimanche.AddHours(10).AddMinutes(5)) == null, "déjà démarré ce dimanche : il ne repart pas");
    Ok(dim.AFaire(dimanche.AddDays(7).AddHours(10).AddMinutes(1)) == dimanche.AddDays(7).AddHours(10), "le dimanche suivant : il repart");
    dim.Actif = false;
    Ok(dim.AFaire(dimanche.AddDays(7).AddHours(10)) == null, "désactivé : il ne démarre pas");

    // chaque vendredi et dimanche à 17:40
    var deux = new DirectProgramme { Jours = new() { 5, 0, 5, 9 }, Heure = "17h40", Duree = 150 };
    deux.Normaliser();
    Ok(deux.Jours.SequenceEqual(new[] { 0, 5 }), "jours : sans double ni jour impossible");
    Ok(deux.Quand == "Chaque vendredi et dimanche à 17:40" && deux.Fin == "arrêt après 2 h 30", "en mots : " + deux.Quand + ", " + deux.Fin);
    Ok(deux.Prochain(new DateTime(2026, 11, 20, 17, 0, 0)) == new DateTime(2026, 11, 20, 17, 40, 0), "vendredi 17:00 : à 17:40 le jour même");
    Ok(deux.Prochain(new DateTime(2026, 11, 20, 18, 0, 0)) == new DateTime(2026, 11, 22, 17, 40, 0), "vendredi 18:00 : dimanche à 17:40");
    Ok(new DirectProgramme { Jours = new() { 0, 1, 2, 3, 4, 5, 6 }, Heure = "9:00" }.Quand == "Tous les jours à 09:00", "tous les jours");
    Ok(new DirectProgramme { Jours = new() { 0, 1, 3 }, Heure = "19:30" }.Quand == "Chaque lundi, mercredi et dimanche à 19:30", "lundi d'abord, dimanche à la fin");
    Ok(new DirectProgramme { Jours = new() }.Prochain(dimanche) == null && new DirectProgramme { Jours = new() }.Quand == "Aucun jour choisi", "aucun jour : jamais");
    Ok(DirectProgramme.DureeTexte(45) == "45 min" && DirectProgramme.DureeTexte(120) == "2 h" && DirectProgramme.DureeTexte(90) == "1 h 30", "durées en mots");

    // le titre du jour
    Ok(DirectProgramme.DateLongue(dimanche) == "dimanche 15 novembre 2026", "date en français : " + DirectProgramme.DateLongue(dimanche));
    var p = DirectProgramme.Nouveau(new ModeleDirect { Description = "Culte de {jour} à {heure}", Confidentialite = "unlisted", Enfants = null, Latence = "low" });
    var m = p.ModelePour(dimanche.AddHours(10));
    Ok(p.Modele.Titre == "Culte du {date}" && m.Titre == "Culte du dimanche 15 novembre 2026" && m.Description == "Culte de dimanche à 10:00"
        && m.Confidentialite == "unlisted" && m.Enfants == false && m.Latence == "low", "« {date} » remplacé : « " + m.Titre + " »");
    p.Modele.Titre = new string('x', 120);
    Ok(p.ModelePour(dimanche).Titre.Length == 100, "titre coupé à 100 caractères (limite de YouTube)");
    p.Modele.Titre = "  ";
    Ok(p.ModelePour(dimanche).Titre == "Culte du dimanche 15 novembre 2026", "titre vide : « Culte du … »");
    Ok(DirectProgramme.Moment(dimanche.AddHours(10), dimanche.AddHours(8)) == "aujourd'hui à 10:00"
        && DirectProgramme.Moment(dimanche.AddDays(1).AddHours(10), dimanche) == "demain à 10:00"
        && DirectProgramme.Moment(new DateTime(2026, 11, 22, 10, 0, 0), dimanche) == "dimanche 22 novembre à 10:00", "moment en mots");

    // gardés dans les réglages
    var don = Donnees.Charger(Temp(), res);
    don.Reglages.Direct.Programmes.Add(deux);
    don.Reglages.Direct.Programmes.Add(new DirectProgramme { Id = deux.Id, Jours = new() { 6 }, Heure = "08:00", Qualite = "8k", Duree = -5 });
    don.SauverReglages();
    var relu = Donnees.Charger(don.Dossier, res).Reglages.Direct.Programmes;
    Ok(relu.Count == 2 && relu[0].Quand == deux.Quand && relu[0].Duree == 150 && relu[1].Id != relu[0].Id && relu[1].Qualite == "" && relu[1].Duree == 0,
        "directs automatiques gardés (identifiants séparés, réglages impossibles corrigés)");
    Ok(ReglagesDirect.TailleDe("2160p") == (3840, 2160, 30000) && ReglagesDirect.TailleDe("1080p") == (1920, 1080, 6000) && ReglagesDirect.TailleDe("") == (1280, 720, 4500),
        "qualités : 4K, 1080p, 720p");
}

Groupe("Caméra Sony par Wi-Fi (1.9.5)");
if (Actif()) MyVerse.Tests.CameraWifiTests.RunAsync(Ok).GetAwaiter().GetResult();

Groupe("Enregistrement");
if (Actif()) MyVerse.Tests.RecordingIntegrationTests.RunAsync(Ok).GetAwaiter().GetResult();

Groupe("Montage et publication YouTube (2.2.0)");
if (Actif()) MyVerse.Tests.MontageTests.RunAsync(Ok).GetAwaiter().GetResult();

Console.WriteLine($"\n{total - echecs}/{total} vérifications réussies" + (echecs > 0 ? $" — {echecs} ÉCHEC(S)" : " — tout est bon"));
return echecs == 0 ? 0 : 1;
