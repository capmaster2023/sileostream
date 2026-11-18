<?php
/*
 * Siloé Stream — l'identifiant Google du logiciel, gardé sur siloestream.tech (api/google-config.php).
 * Le logiciel Windows le lit au démarrage : il n'est plus dans le programme, et rien n'est à retaper après une réinstallation.
 * Réponse : {"id": "...", "secret": "..."} ; 404 tant que google-config.php n'est pas rempli.
 */
declare(strict_types=1);
ini_set('display_errors', '0');
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
header('X-Robots-Tag: noindex');

// seulement pour Siloé Stream (un visiteur qui ouvre l'adresse ne voit rien)
if (($_SERVER['HTTP_X_SILOE_STREAM'] ?? '') !== '1') {
    http_response_code(404);
    echo '{"erreur":"introuvable"}';
    exit;
}
$config = is_file(__DIR__ . '/google-config.php') ? (require __DIR__ . '/google-config.php') : [];
$id = trim((string) (is_array($config) ? ($config['google_id'] ?? '') : ''));
$secret = trim((string) (is_array($config) ? ($config['google_secret'] ?? '') : ''));
if ($id === '' || $secret === '' || str_starts_with($id, 'COLLER_') || str_starts_with($secret, 'COLLER_')) {
    http_response_code(404);
    echo '{"erreur":"identifiant Google pas encore rempli dans api/google-config.php"}';
    exit;
}
echo json_encode(['id' => $id, 'secret' => $secret], JSON_UNESCAPED_SLASHES);
