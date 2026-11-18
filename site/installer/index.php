<?php
/*
 * Siloé Stream — installateur du site siloestream.tech.
 * Ouvrez https://siloestream.tech/installer/ juste après avoir extrait le zip dans public_html :
 * vous entrez le jeton GitHub et l'identifiant Google une seule fois, et ils sont enregistrés sur Hostinger
 * (api/config.php et api/google-config.php, jamais lisibles depuis le Web).
 * Une fois fini, l'installateur se ferme tout seul (api/installation.lock). Pour le rouvrir : supprimez ce fichier
 * dans le Gestionnaire de fichiers de Hostinger.
 */
declare(strict_types=1);
ini_set('display_errors', '0');
header('Cache-Control: no-store');
header('X-Robots-Tag: noindex');
session_start();

const API = __DIR__ . '/../api';
$verrou = API . '/installation.lock';

function lire(string $fichier): array
{
    if (!is_file($fichier)) return [];
    $c = require $fichier;
    return is_array($c) ? $c : [];
}
function rempli(string $v): bool { return $v !== '' && !str_starts_with($v, 'COLLER_'); }
function ecrire(string $fichier, string $entete, array $valeurs): bool
{
    $php = "<?php\n" . $entete . "return " . var_export($valeurs, true) . ";\n";
    return file_put_contents($fichier, $php, LOCK_EX) !== false;
}
function h(string $t): string { return htmlspecialchars($t, ENT_QUOTES, 'UTF-8'); }

function jetonValide(string $jeton, string $depot): array
{
    $ch = curl_init('https://api.github.com/repos/' . $depot);
    curl_setopt_array($ch, [
        CURLOPT_RETURNTRANSFER => true, CURLOPT_TIMEOUT => 15,
        CURLOPT_HTTPHEADER => ['Authorization: Bearer ' . $jeton, 'User-Agent: siloestream-installateur', 'Accept: application/vnd.github+json'],
    ]);
    curl_exec($ch);
    $code = (int) curl_getinfo($ch, CURLINFO_HTTP_CODE);
    curl_close($ch);
    return [$code === 200, $code];
}

$config = lire(API . '/config.php');
$google = lire(API . '/google-config.php');
$depot = (string) ($config['depot'] ?? 'capmaster2023/sileostream');
$aJeton = rempli((string) ($config['jeton'] ?? ''));
$aGoogle = rempli((string) ($google['google_id'] ?? '')) && rempli((string) ($google['google_secret'] ?? ''));

$messages = [];
$fini = is_file($verrou);

if (!$fini && $_SERVER['REQUEST_METHOD'] === 'POST') {
    if (!hash_equals($_SESSION['installateur'] ?? '', (string) ($_POST['jeton_page'] ?? ''))) {
        $messages[] = ['erreur', 'La page a expiré : rechargez-la et recommencez.'];
    } else {
        $jeton = trim((string) ($_POST['jeton'] ?? ''));
        $gid = trim((string) ($_POST['google_id'] ?? ''));
        $gsecret = trim((string) ($_POST['google_secret'] ?? ''));
        $ok = true;

        if ($jeton !== '') {
            [$valide, $code] = jetonValide($jeton, $depot);
            if (!$valide) { $ok = false; $messages[] = ['erreur', "Le jeton GitHub ne donne pas accès au dépôt $depot (réponse $code). Vérifiez-le."]; }
        } elseif (!$aJeton) { $ok = false; $messages[] = ['erreur', 'Le jeton GitHub est nécessaire (les téléchargements passent par lui).']; }

        if ($gid !== '' || $gsecret !== '') {
            if (!str_ends_with($gid, '.apps.googleusercontent.com')) { $ok = false; $messages[] = ['erreur', "L'identifiant client Google doit finir par .apps.googleusercontent.com."]; }
            if (strlen($gsecret) < 10) { $ok = false; $messages[] = ['erreur', 'Le secret du client Google semble incomplet.']; }
        } elseif (!$aGoogle) { $ok = false; $messages[] = ['erreur', "L'identifiant et le secret Google sont nécessaires (connexion YouTube du logiciel)."]; }

        if ($ok) {
            $ecrit = true;
            if ($jeton !== '') {
                $config['jeton'] = $jeton;
                $config['depot'] = $depot;
                $ecrit = $ecrit && ecrire(API . '/config.php', "// Siloé Stream — réglages du site (écrit par l'installateur ; jamais lisible depuis le Web).\n", $config);
            }
            if ($gid !== '') {
                $ecrit = $ecrit && ecrire(API . '/google-config.php', "// Siloé Stream — identifiant Google du logiciel (écrit par l'installateur ; jamais lisible depuis le Web).\n",
                    ['google_id' => $gid, 'google_secret' => $gsecret]);
            }
            if ($ecrit && file_put_contents($verrou, date('c') . "\n") !== false) {
                $fini = true;
                $messages[] = ['ok', 'Installation terminée : tout est enregistré sur Hostinger. Siloé Stream prend l\'identifiant Google tout seul, sur tous les PC.'];
            } else {
                $messages[] = ['erreur', "Impossible d'écrire dans public_html/api (droits du dossier ?)."];
            }
        }
    }
}
$_SESSION['installateur'] = bin2hex(random_bytes(16));
?><!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Installer Siloé Stream</title>
<style>
  :root { --fond:#121010; --carte:#1d1a19; --trait:#3a3433; --texte:#f4ede4; --brume:#a89f97; --or:#f0b43c; --rouge:#e1251b; --vert:#3cc47c; }
  * { box-sizing: border-box; }
  body { margin:0; background:var(--fond); color:var(--texte); font:16px/1.5 system-ui, "Segoe UI", sans-serif; padding:32px 16px; }
  main { max-width:620px; margin:0 auto; }
  h1 { font-size:26px; margin:0 0 6px; }
  p.sous { color:var(--brume); margin:0 0 24px; }
  form, .carte { background:var(--carte); border:1px solid var(--trait); border-radius:14px; padding:22px; }
  label { display:block; font-weight:600; margin:16px 0 6px; }
  label:first-child { margin-top:0; }
  small { display:block; color:var(--brume); font-size:13px; margin-top:4px; }
  input { width:100%; padding:11px 12px; border-radius:9px; border:1px solid var(--trait); background:#0f0d0d; color:var(--texte); font-size:15px; }
  input:focus { outline:2px solid var(--or); border-color:transparent; }
  button { margin-top:22px; width:100%; padding:13px; border:0; border-radius:10px; background:var(--texte); color:#121010; font-weight:700; font-size:16px; cursor:pointer; }
  .msg { border-radius:10px; padding:12px 14px; margin-bottom:16px; }
  .erreur { background:rgba(225,37,27,.15); border:1px solid var(--rouge); }
  .ok { background:rgba(60,196,124,.14); border:1px solid var(--vert); }
  .deja { color:var(--vert); font-weight:600; font-size:13px; }
</style>
</head>
<body>
<main>
  <h1>Installer Siloé Stream</h1>
  <p class="sous">Une seule fois : les clés sont enregistrées sur Hostinger, jamais dans le logiciel ni dans GitHub.</p>
  <?php foreach ($messages as [$genre, $texte]): ?>
    <div class="msg <?= $genre ?>"><?= h($texte) ?></div>
  <?php endforeach; ?>
  <?php if ($fini): ?>
    <div class="carte">
      <p><strong>L'installation est terminée.</strong> Cette page est maintenant fermée, pour que personne d'autre ne puisse changer vos clés.</p>
      <p style="color:var(--brume)">Pour changer une clé plus tard : Hostinger › Gestionnaire de fichiers › <code>public_html/api/installation.lock</code> › Supprimer, puis rouvrez cette page.</p>
    </div>
  <?php else: ?>
    <form method="post" autocomplete="off">
      <input type="hidden" name="jeton_page" value="<?= h($_SESSION['installateur']) ?>">
      <label for="jeton">Jeton GitHub (lecture seule)</label>
      <input id="jeton" name="jeton" type="password" placeholder="<?= $aJeton ? 'Déjà enregistré : laisser vide pour le garder' : 'github_pat_…' ?>">
      <small>github.com › Settings › Developer settings › Fine-grained tokens. Dépôt <?= h($depot) ?>, Contents : Read-only.</small>
      <?php if ($aJeton): ?><span class="deja">✓ déjà enregistré</span><?php endif; ?>

      <label for="google_id">Identifiant client Google</label>
      <input id="google_id" name="google_id" type="text" placeholder="<?= $aGoogle ? 'Déjà enregistré : laisser vide pour le garder' : '…apps.googleusercontent.com' ?>">
      <small>Google Cloud › mychurchapp › API et services › Identifiants › client « Application de bureau ».</small>

      <label for="google_secret">Secret du client Google</label>
      <input id="google_secret" name="google_secret" type="password" placeholder="<?= $aGoogle ? 'Déjà enregistré : laisser vide pour le garder' : 'GOCSPX-…' ?>">
      <?php if ($aGoogle): ?><span class="deja">✓ déjà enregistré</span><?php endif; ?>

      <button type="submit">Enregistrer sur Hostinger</button>
    </form>
  <?php endif; ?>
</main>
</body>
</html>
