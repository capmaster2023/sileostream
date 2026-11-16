<?php
/*
 * Siloé Stream — mises à jour servies par siloestream.tech
 *
 * Les versions sont publiées dans le dépôt GitHub privé (GitHub Actions, à chaque avance de « main »).
 * Ce fichier les lit avec un jeton GitHub en lecture seule (api/config.php, jamais envoyé aux visiteurs)
 * et les sert sous la même forme que l'API GitHub : le logiciel Windows et l'application Android
 * les lisent comme avant.
 *
 *   /api/releases/latest             dernière version (hors bêtas)
 *   /api/releases                    toutes les versions publiées (bêtas comprises)
 *   /telecharger/windows             installateur de la dernière version (aussi : portable, android)
 *   /telecharger/<tag>/<fichier>     un fichier d'une version précise
 *
 * Un téléchargement est renvoyé vers GitHub (lien signé, valable quelques minutes) :
 * les fichiers ne passent pas par l'hébergement.
 */
declare(strict_types=1);
ini_set('display_errors', '0');

const DUREE_CACHE = 60; // secondes : GitHub est relu au plus une fois par minute
const RACCOURCIS = [
    'windows'  => '/^SiloeStream-Setup-.+\.exe$/',
    'portable' => '/^SiloeStream-.+-Windows\.zip$/',
    'android'  => '/^SiloeStream\.apk$/',
];

$config = is_file(__DIR__ . '/config.php') ? (require __DIR__ . '/config.php') : [];
$config = is_array($config) ? $config : [];
define('JETON', trim((string) ($config['jeton'] ?? '')));
define('DEPOT', trim((string) ($config['depot'] ?? 'capmaster2023/sileostream')));
define('SITE', rtrim((string) ($config['site'] ?? 'https://siloestream.tech'), '/'));

$quoi = (string) ($_GET['quoi'] ?? '');
// les liens « Télécharger » sont ouverts par des personnes : leurs erreurs sont des pages, pas du JSON
define('HUMAIN', $quoi === 'raccourci');

// ------------------------------------------------------------------ réponses

function json_sortie(int $code, mixed $donnees, int $cache = 0): never
{
    http_response_code($code);
    header('Content-Type: application/json; charset=utf-8');
    header($cache > 0 ? "Cache-Control: public, max-age=$cache" : 'Cache-Control: no-store');
    header('X-Content-Type-Options: nosniff');
    echo json_encode($donnees, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT);
    exit;
}

function erreur(int $code, string $message): never
{
    if (!HUMAIN) {
        json_sortie($code, ['message' => $message]);
    }
    http_response_code($code);
    header('Content-Type: text/html; charset=utf-8');
    header('Cache-Control: no-store');
    echo '<!doctype html><html lang="fr"><meta charset="utf-8">'
        . '<meta name="viewport" content="width=device-width, initial-scale=1">'
        . '<title>Siloé Stream</title>'
        . '<body style="font-family:system-ui,sans-serif;max-width:36rem;margin:4rem auto;padding:0 1rem;line-height:1.5;color:#1e1a17;background:#fbf7f1">'
        . '<h1 style="font-size:1.4rem">Téléchargement indisponible</h1>'
        . '<p>' . htmlspecialchars($message, ENT_QUOTES) . '</p>'
        . '<p><a href="/" style="color:#a8141b">Retour à siloestream.tech</a></p></body></html>';
    exit;
}

// ------------------------------------------------------------------ GitHub

/** Appel à l'API GitHub. $limite : octets de réponse gardés au plus (au-delà, l'appel s'arrête). */
function github(string $chemin, string $accept = 'application/vnd.github+json', int $limite = 8 << 20): array
{
    $entetes = [];
    $corps = '';
    $ch = curl_init('https://api.github.com' . $chemin);
    curl_setopt_array($ch, [
        CURLOPT_FOLLOWLOCATION => false,
        CURLOPT_CONNECTTIMEOUT => 10,
        CURLOPT_TIMEOUT => 25,
        CURLOPT_HTTPHEADER => [
            'Accept: ' . $accept,
            'Authorization: Bearer ' . JETON,
            'X-GitHub-Api-Version: 2022-11-28',
            'User-Agent: siloestream.tech',
        ],
        CURLOPT_HEADERFUNCTION => function ($ch, string $ligne) use (&$entetes): int {
            $p = strpos($ligne, ':');
            if ($p !== false) {
                $entetes[strtolower(trim(substr($ligne, 0, $p)))] = trim(substr($ligne, $p + 1));
            }
            return strlen($ligne);
        },
        CURLOPT_WRITEFUNCTION => function ($ch, string $morceau) use (&$corps, $limite): int {
            if (strlen($corps) + strlen($morceau) > $limite) {
                return 0; // trop gros : on s'arrête là
            }
            $corps .= $morceau;
            return strlen($morceau);
        },
    ]);
    curl_exec($ch);
    $code = (int) curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
    return [$code, $entetes, $corps];
}

/** Numéros d'une version « v1.9.2 » ou « 1.10.0-beta » : [1, 9, 2] (comme le logiciel). */
function parties(string $v): array
{
    $morceaux = explode('.', ltrim(trim($v), 'vV'), 3);
    $p = [0, 0, 0];
    foreach (array_slice($morceaux, 0, 3) as $i => $m) {
        $p[$i] = preg_match('/\d+/', $m, $x) ? (int) $x[0] : 0;
    }
    return $p;
}

/** $a est plus récente que $b (une version finale passe devant la bêta du même numéro). */
function plus_recente(string $a, string $b): bool
{
    $pa = parties($a);
    $pb = parties($b);
    for ($i = 0; $i < 3; $i++) {
        if ($pa[$i] !== $pb[$i]) {
            return $pa[$i] > $pb[$i];
        }
    }
    return !str_contains($a, '-') && str_contains($b, '-');
}

function lire_cache(string $fichier): ?array
{
    $v = is_file($fichier) ? json_decode((string) file_get_contents($fichier), true) : null;
    return is_array($v) ? $v : null;
}

/** Les versions publiées, la plus récente d'abord. $forcer : relire GitHub (au plus toutes les 10 s). */
function versions(bool $forcer = false): array
{
    $dossier = __DIR__ . '/cache';
    $fichier = $dossier . '/versions.json';
    $age = is_file($fichier) ? time() - (int) filemtime($fichier) : PHP_INT_MAX;
    if ($age < ($forcer ? 10 : DUREE_CACHE) && ($gardee = lire_cache($fichier)) !== null) {
        return $gardee;
    }
    if (JETON === '' || str_contains(JETON, 'COLLER')) {
        erreur(503, "Le site n'est pas encore relié à GitHub : mettez le jeton dans api/config.php.");
    }

    [$code, , $corps] = github('/repos/' . DEPOT . '/releases?per_page=50');
    $liste = $code === 200 ? json_decode($corps, true) : null;
    if (!is_array($liste)) {
        // GitHub injoignable ou jeton refusé : la dernière liste connue sert encore
        if (($gardee = lire_cache($fichier)) !== null) {
            return $gardee;
        }
        erreur(502, in_array($code, [401, 403, 404], true)
            ? "GitHub refuse l'accès (code $code) : vérifiez le jeton et le dépôt dans api/config.php."
            : "GitHub ne répond pas (code $code). Réessayez dans une minute.");
    }

    $versions = [];
    foreach ($liste as $r) {
        if (!is_array($r) || !empty($r['draft']) || empty($r['tag_name'])) {
            continue; // brouillon : la version n'est pas encore prête (la compilation Windows n'a pas fini)
        }
        $fichiers = [];
        foreach ($r['assets'] ?? [] as $a) {
            if (($a['state'] ?? 'uploaded') !== 'uploaded') {
                continue;
            }
            $fichiers[] = [
                'id' => (int) $a['id'],
                'name' => (string) $a['name'],
                'size' => (int) $a['size'],
                'content_type' => (string) ($a['content_type'] ?? 'application/octet-stream'),
                'browser_download_url' => SITE . '/telecharger/' . rawurlencode((string) $r['tag_name']) . '/' . rawurlencode((string) $a['name']),
            ];
        }
        $versions[] = [
            'tag_name' => (string) $r['tag_name'],
            'name' => (string) ($r['name'] ?: $r['tag_name']),
            'body' => (string) ($r['body'] ?? ''),
            'draft' => false,
            'prerelease' => (bool) ($r['prerelease'] ?? false),
            'published_at' => (string) ($r['published_at'] ?? ''),
            'assets' => $fichiers,
        ];
    }
    usort($versions, fn(array $a, array $b): int =>
        plus_recente($a['tag_name'], $b['tag_name']) ? -1 : (plus_recente($b['tag_name'], $a['tag_name']) ? 1 : 0));

    if (!is_dir($dossier)) {
        @mkdir($dossier, 0750, true);
    }
    $tmp = $fichier . '.' . getmypid();
    if (@file_put_contents($tmp, json_encode($versions, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE)) !== false) {
        @rename($tmp, $fichier);
    }
    return $versions;
}

/** La dernière version finale (les bêtas ne sont proposées qu'aux bêtas). */
function derniere(array $versions): ?array
{
    foreach ($versions as $r) {
        if (!$r['prerelease']) {
            return $r;
        }
    }
    return null;
}

/** Ce qui est montré : tout sauf le numéro interne des fichiers sur GitHub. */
function publique(array $r): array
{
    $r['assets'] = array_map(fn(array $a): array => array_diff_key($a, ['id' => 0]), $r['assets']);
    return $r;
}

/** Renvoie le visiteur vers le lien signé de GitHub (valable quelques minutes, jamais gardé en cache). */
function envoyer_fichier(array $f): never
{
    [$code, $entetes] = github('/repos/' . DEPOT . '/releases/assets/' . $f['id'], 'application/octet-stream', 1 << 16);
    $lien = $entetes['location'] ?? '';
    if ($code >= 300 && $code < 400 && str_starts_with($lien, 'https://')) {
        header('Cache-Control: no-store');
        header('Location: ' . $lien, true, 302);
        exit;
    }
    erreur(502, "Le fichier {$f['name']} n'a pas pu être obtenu (GitHub : code $code). Réessayez dans une minute.");
}

// ------------------------------------------------------------------ adresses

switch ($quoi) {
    case 'derniere':
        $r = derniere(versions());
        if ($r === null) {
            erreur(404, 'Aucune version publiée pour le moment.');
        }
        json_sortie(200, publique($r), DUREE_CACHE);

    case 'liste':
        json_sortie(200, array_map('publique', versions()), DUREE_CACHE);

    case 'raccourci':
        $motif = RACCOURCIS[(string) ($_GET['type'] ?? '')] ?? null;
        if ($motif === null) {
            erreur(404, 'Ce téléchargement n’existe pas.');
        }
        foreach (derniere(versions())['assets'] ?? [] as $f) {
            if (preg_match($motif, $f['name'])) {
                envoyer_fichier($f);
            }
        }
        erreur(404, 'Ce fichier n’est pas encore publié. Réessayez un peu plus tard.');

    case 'fichier':
        $tag = (string) ($_GET['tag'] ?? '');
        $nom = (string) ($_GET['nom'] ?? '');
        foreach ([false, true] as $forcer) { // une version toute neuve peut manquer à la liste gardée
            foreach (versions($forcer) as $r) {
                if ($r['tag_name'] !== $tag) {
                    continue;
                }
                foreach ($r['assets'] as $f) {
                    if ($f['name'] === $nom) {
                        envoyer_fichier($f);
                    }
                }
            }
        }
        erreur(404, 'Fichier introuvable.');

    default:
        erreur(404, 'Adresse inconnue.');
}
