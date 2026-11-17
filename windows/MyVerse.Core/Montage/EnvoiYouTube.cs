using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace MyVerse.Montages;

/// <summary>Ce que YouTube affiche de la vidéo envoyée (titre, description, tags, catégorie, visibilité, date de publication).</summary>
public sealed class InfosVideo
{
    public string Titre { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    /// <summary>Catégorie YouTube (29 : organisations à but non lucratif).</summary>
    public string Categorie { get; set; } = "29";
    /// <summary>« public », « unlisted » (non répertoriée) ou « private ».</summary>
    public string Confidentialite { get; set; } = "public";
    /// <summary>Publier plus tard : la vidéo reste privée jusqu'à cette date, puis YouTube la rend publique tout seul.</summary>
    public DateTimeOffset? PublierLe { get; set; }
    public bool Enfants { get; set; }

    /// <summary>La ressource « video » de l'API (snippet et status).</summary>
    public JsonObject Ressource()
    {
        var tags = new JsonArray();
        int longueur = 0;
        // YouTube : 500 caractères au plus pour l'ensemble des tags
        foreach (var t in Tags.Select(t => t.Trim().Replace("<", "").Replace(">", "")).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            longueur += t.Length + (t.Contains(' ') ? 2 : 0) + 1;
            if (longueur > 490) break;
            tags.Add(t);
        }
        var snippet = new JsonObject
        {
            ["title"] = Nettoyer(Titre, 100).Length > 0 ? Nettoyer(Titre, 100) : "Culte",
            ["description"] = Nettoyer(Description, 5000),
            ["categoryId"] = string.IsNullOrWhiteSpace(Categorie) ? "29" : Categorie,
            ["tags"] = tags,
        };
        var status = new JsonObject { ["selfDeclaredMadeForKids"] = Enfants, ["embeddable"] = true };
        if (PublierLe is { } d && d > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            status["privacyStatus"] = "private";
            status["publishAt"] = d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
        else status["privacyStatus"] = Confidentialite is "public" or "unlisted" or "private" ? Confidentialite : "public";
        return new JsonObject { ["snippet"] = snippet, ["status"] = status };
    }

    /// <summary>YouTube refuse « &lt; » et « &gt; » dans le titre et la description.</summary>
    static string Nettoyer(string? s, int max)
    {
        s = (s ?? "").Replace("<", "‹").Replace(">", "›").Trim();
        return s.Length > max ? s[..max] : s;
    }
}

/// <summary>Où en est une session d'envoi chez YouTube.</summary>
public sealed record EtatEnvoi(bool Fini, long Recus, JsonNode? Video);

/// <summary>
/// Siloé Stream 2.2.0 : l'envoi d'une vidéo sur YouTube (pas un direct) par la YouTube Data API v3, en « Resumable Upload ».
/// La vidéo part pendant qu'elle se fabrique : les octets arrivent d'un flux (la sortie de FFmpeg) dont on ne connaît pas encore
/// la taille, et partent par morceaux de 8 Mo (un multiple de 256 Kio, comme Google l'exige). La mémoire reste bornée : deux
/// morceaux en lecture d'avance et ce qui attend l'accord de YouTube. Une coupure d'Internet ne perd rien : Siloé Stream demande à
/// YouTube ce qu'il a reçu et renvoie la suite (le rendu attend pendant ce temps, rien ne s'accumule). Le dernier morceau, celui
/// qui termine la vidéo, ne part que si le rendu a réussi : une vidéo incomplète n'est jamais publiée.
/// </summary>
public sealed class EnvoiYouTube
{
    public const int Quantum = 256 * 1024;
    readonly YouTubeCompte compte;
    readonly HttpClient http;
    /// <summary>La taille d'un morceau (multiple de 256 Kio).</summary>
    public int TailleMorceau { get; init; } = 32 * Quantum;
    /// <summary>Le temps maximal d'une requête (un morceau de 8 Mo sur une connexion lente).</summary>
    public TimeSpan DelaiRequete { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>L'attente la plus longue entre deux essais quand Internet est coupé.</summary>
    public TimeSpan AttenteMax { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Combien de temps on réessaie sans aucun progrès avant d'abandonner (YouTube garde la session une semaine).</summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromHours(12);
    /// <summary>L'attente entre deux essais (remplacée par les tests).</summary>
    public Func<TimeSpan, CancellationToken, Task> Attendre { get; init; } = (t, c) => Task.Delay(t, c);
    /// <summary>Ce qui se passe, en mots simples (« Internet coupé : nouvel essai dans 8 s »).</summary>
    public event Action<string>? Etat;

    public EnvoiYouTube(YouTubeCompte compte, HttpClient? http = null)
    {
        this.compte = compte;
        this.http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        if (TailleMorceau % Quantum != 0) throw new ArgumentException("La taille d'un morceau doit être un multiple de 256 Kio.");
    }

    /// <summary>Ouvre la session d'envoi (la vidéo n'existe sur la chaîne qu'à la fin de l'envoi). Rend l'adresse de la session.</summary>
    public async Task<string> OuvrirSessionAsync(InfosVideo infos, string typeMime, long? taille = null, CancellationToken annuler = default)
    {
        for (int essai = 0; ; essai++)
        {
            using var m = new HttpRequestMessage(HttpMethod.Post, compte.AdresseEnvoi + "/videos?uploadType=resumable&part=snippet,status");
            m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await compte.JetonAsync(annuler));
            m.Headers.TryAddWithoutValidation("X-Upload-Content-Type", typeMime);
            if (taille is { } t) m.Headers.TryAddWithoutValidation("X-Upload-Content-Length", t.ToString(CultureInfo.InvariantCulture));
            m.Content = new StringContent(infos.Ressource().ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage r;
            try
            {
                using var delai = CancellationTokenSource.CreateLinkedTokenSource(annuler);
                delai.CancelAfter(TimeSpan.FromSeconds(60));
                r = await http.SendAsync(m, delai.Token);
            }
            catch (Exception e) when (!annuler.IsCancellationRequested && e is HttpRequestException or IOException or OperationCanceledException && essai < 5)
            {
                await AttendreAvantEssai(essai, "Connexion à YouTube impossible", annuler);
                continue;
            }
            using (r)
            {
                if (r.StatusCode == HttpStatusCode.Unauthorized && essai == 0) { compte.OublierJeton(); continue; }
                if ((int)r.StatusCode >= 500 && essai < 5) { await AttendreAvantEssai(essai, "YouTube ne répond pas", annuler); continue; }
                var texte = await r.Content.ReadAsStringAsync(annuler);
                if (!r.IsSuccessStatusCode) throw Erreur(texte, (int)r.StatusCode);
                var adresse = r.Headers.Location?.ToString();
                if (string.IsNullOrEmpty(adresse)) throw new ErreurYouTube("YouTube n'a pas ouvert l'envoi (pas d'adresse de session).");
                return adresse;
            }
        }
    }

    /// <summary>Demande à YouTube ce qu'il a déjà reçu (après une coupure, ou pour reprendre plus tard).</summary>
    public async Task<EtatEnvoi> EtatSessionAsync(string session, long? total = null, CancellationToken annuler = default)
    {
        using var r = await PutAsync(session, Array.Empty<byte>(), 0, 0, total is { } t ? $"bytes */{t}" : "bytes */*", annuler);
        return await LireReponse(r, annuler);
    }

    async Task<HttpResponseMessage> PutAsync(string session, byte[] tampon, int debut, int longueur, string plage, CancellationToken annuler)
    {
        using var m = new HttpRequestMessage(HttpMethod.Put, session);
        m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await compte.JetonAsync(annuler));
        var contenu = new ByteArrayContent(tampon, debut, longueur);
        contenu.Headers.TryAddWithoutValidation("Content-Range", plage);
        m.Content = contenu;
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annuler);
        delai.CancelAfter(DelaiRequete);
        return await http.SendAsync(m, HttpCompletionOption.ResponseContentRead, delai.Token);
    }

    async Task<EtatEnvoi> LireReponse(HttpResponseMessage r, CancellationToken annuler)
    {
        int code = (int)r.StatusCode;
        if (code is 200 or 201)
        {
            var texte = await r.Content.ReadAsStringAsync(annuler);
            JsonNode? n = null;
            try { n = JsonNode.Parse(texte); } catch (Exception) { }
            return new EtatEnvoi(true, -1, n);
        }
        if (code == 308)
        {
            long recus = 0;
            if (r.Headers.TryGetValues("Range", out var v) && v.FirstOrDefault() is { } plage)
            {
                // « bytes=0-12345 » : le prochain octet attendu est le 12346e
                int tiret = plage.LastIndexOf('-');
                if (tiret > 0 && long.TryParse(plage[(tiret + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dernier)) recus = dernier + 1;
            }
            return new EtatEnvoi(false, recus, null);
        }
        var corps = await r.Content.ReadAsStringAsync(annuler);
        throw code switch
        {
            401 => new ErreurYouTube("Accès refusé par YouTube.", "unauthorized", 401),
            404 or 410 => new ErreurYouTube("La session d'envoi a expiré chez YouTube : il faut recommencer l'envoi.", "sessionExpiree", code),
            _ when code >= 500 || code == 429 => new ErreurPassagere(code),
            _ => Erreur(corps, code),
        };
    }

    sealed class ErreurPassagere : Exception { public ErreurPassagere(int code) : base("YouTube : erreur " + code) { } }

    static ErreurYouTube Erreur(string texte, int code)
    {
        JsonNode? n = null;
        try { n = texte.Length > 0 ? JsonNode.Parse(texte) : null; } catch (Exception) { }
        var e = YouTubeCompte.Erreur(n, texte, code);
        return e.Raison switch
        {
            "quotaExceeded" or "rateLimitExceeded" or "userRateLimitExceeded" => new ErreurYouTube("YouTube refuse l'envoi pour aujourd'hui (quota de l'application atteint) : réessayez demain, ou envoyez le fichier exporté depuis YouTube Studio.", e.Raison, code),
            "uploadLimitExceeded" => new ErreurYouTube("La chaîne a atteint sa limite d'envois de vidéos pour aujourd'hui.", e.Raison, code),
            "youtubeSignupRequired" => new ErreurYouTube("Ce compte Google n'a pas de chaîne YouTube.", e.Raison, code),
            "invalidCategoryId" => new ErreurYouTube("Catégorie refusée par YouTube.", e.Raison, code),
            "invalidPublishAt" => new ErreurYouTube("Date de publication refusée : elle doit être dans le futur.", e.Raison, code),
            _ => e,
        };
    }

    async Task AttendreAvantEssai(int essai, string pourquoi, CancellationToken annuler)
    {
        var attente = TimeSpan.FromSeconds(Math.Min(AttenteMax.TotalSeconds, Math.Pow(2, Math.Min(essai, 10))));
        Etat?.Invoke($"{pourquoi} : nouvel essai dans {Math.Ceiling(attente.TotalSeconds):0} s…");
        await Attendre(attente, annuler);
    }

    /// <summary>
    /// Envoie un morceau ; en cas de coupure, attend, demande à YouTube ce qu'il a reçu et le rend (la suite est renvoyée par
    /// l'appelant). <paramref name="total"/> : la taille totale si c'est le dernier morceau.
    /// </summary>
    async Task<EtatEnvoi> EnvoyerMorceauAsync(string session, byte[] tampon, int longueur, long position, long? total, CancellationToken annuler)
    {
        var debutEssais = DateTime.UtcNow;
        bool interroger = false;
        for (int essai = 0; ; essai++)
        {
            annuler.ThrowIfCancellationRequested();
            try
            {
                if (interroger)
                {
                    var e = await EtatSessionAsync(session, total, annuler);
                    if (e.Fini || e.Recus != position) { Etat?.Invoke("Connexion rétablie : l'envoi reprend."); return e; }
                    interroger = false;
                }
                string plage = longueur == 0
                    ? $"bytes */{total ?? position}"
                    : $"bytes {position}-{position + longueur - 1}/{(total is { } t ? t.ToString(CultureInfo.InvariantCulture) : "*")}";
                using var r = await PutAsync(session, tampon, 0, longueur, plage, annuler);
                if (r.StatusCode == HttpStatusCode.Unauthorized) { compte.OublierJeton(); if (essai < 3) continue; }
                var etat = await LireReponse(r, annuler);
                if (essai > 0) Etat?.Invoke("Connexion rétablie : l'envoi reprend.");
                return etat;
            }
            catch (Exception e) when (!annuler.IsCancellationRequested && e is HttpRequestException or IOException or OperationCanceledException or ErreurPassagere)
            {
                if (DateTime.UtcNow - debutEssais > Patience) throw new ErreurYouTube($"Internet est coupé depuis trop longtemps ({e.Message}) : l'envoi est arrêté.", "patience");
                await AttendreAvantEssai(essai, e is ErreurPassagere ? "YouTube ne répond pas" : "Internet coupé ou trop lent", annuler);
                interroger = true;
            }
        }
    }

    /// <summary>
    /// Envoie tout <paramref name="source"/> dans la session (à partir de l'octet <paramref name="depart"/>, déjà reçu par YouTube).
    /// <paramref name="sourceComplete"/> : appelé quand la source est finie, avant le dernier morceau ; faux = le rendu a échoué, rien
    /// n'est publié. Rend la ressource « video » créée par YouTube.
    /// </summary>
    public async Task<JsonNode> EnvoyerAsync(string session, Stream source, long depart = 0, Func<Task<bool>>? sourceComplete = null,
        IProgress<long>? progres = null, CancellationToken annuler = default)
    {
        var canal = Channel.CreateBounded<(byte[] Tampon, int Longueur)>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        using var arret = CancellationTokenSource.CreateLinkedTokenSource(annuler);
        // la lecture d'avance : deux morceaux au plus en mémoire (FFmpeg attend quand l'envoi n'avance pas)
        var lecture = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var t = ArrayPool<byte>.Shared.Rent(TailleMorceau);
                    int n = 0;
                    while (n < TailleMorceau)
                    {
                        int r = await source.ReadAsync(t.AsMemory(n, TailleMorceau - n), arret.Token);
                        if (r <= 0) break;
                        n += r;
                    }
                    if (n == 0) { ArrayPool<byte>.Shared.Return(t); break; }
                    await canal.Writer.WriteAsync((t, n), arret.Token);
                    if (n < TailleMorceau) break;
                }
                canal.Writer.TryComplete();
            }
            catch (Exception e) { canal.Writer.TryComplete(e); }
        });

        var attente = new byte[TailleMorceau * 2];
        int lg = 0, sansProgres = 0;
        long position = depart;
        bool finSource = false;
        try
        {
            while (true)
            {
                while (!finSource && lg < TailleMorceau)
                {
                    if (!await canal.Reader.WaitToReadAsync(arret.Token)) { finSource = true; break; }
                    if (canal.Reader.TryRead(out var m))
                    {
                        Buffer.BlockCopy(m.Tampon, 0, attente, lg, m.Longueur);
                        lg += m.Longueur;
                        ArrayPool<byte>.Shared.Return(m.Tampon);
                    }
                }
                EtatEnvoi e;
                int envoye;
                if (finSource)
                {
                    // le dernier morceau termine la vidéo : seulement si le rendu est complet
                    if (sourceComplete != null && !await sourceComplete())
                        throw new IOException("Le rendu s'est arrêté avant la fin : l'envoi est abandonné, rien n'est publié.");
                    long total = position + lg;
                    if (total == 0) throw new IOException("Le rendu n'a rien produit.");
                    envoye = lg;
                    e = await EnvoyerMorceauAsync(session, attente, lg, position, total, annuler);
                }
                else
                {
                    // un morceau complet, multiple de 256 Kio ; au moins un octet reste pour le dernier morceau
                    envoye = (lg - 1) / Quantum * Quantum;
                    e = await EnvoyerMorceauAsync(session, attente, envoye, position, null, annuler);
                }
                if (e.Fini)
                {
                    progres?.Report(position + lg);
                    return e.Video ?? throw new ErreurYouTube("YouTube a terminé l'envoi sans décrire la vidéo.");
                }
                long consomme = e.Recus - position;
                if (consomme < 0 || consomme > lg)
                    throw new ErreurYouTube($"YouTube dit avoir reçu {e.Recus} octets alors que {position} étaient confirmés : l'envoi ne peut pas reprendre.", "plage");
                if (consomme > 0)
                {
                    Buffer.BlockCopy(attente, (int)consomme, attente, 0, lg - (int)consomme);
                    lg -= (int)consomme;
                    position = e.Recus;
                    sansProgres = 0;
                    progres?.Report(position);
                }
                else if (envoye > 0)
                {
                    // YouTube a répondu sans rien garder de ce morceau : on attend un peu et on le renvoie
                    if (++sansProgres > 12) throw new ErreurYouTube("YouTube n'accepte plus les données envoyées : l'envoi est arrêté.", "bloque");
                    await AttendreAvantEssai(sansProgres, "YouTube n'a rien gardé du dernier morceau", annuler);
                }
            }
        }
        finally
        {
            arret.Cancel();
            try { await lecture; } catch (Exception) { }
            while (canal.Reader.TryRead(out var m)) ArrayPool<byte>.Shared.Return(m.Tampon);
        }
    }
}
