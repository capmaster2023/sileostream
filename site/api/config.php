<?php
// Siloé Stream — réglages du site siloestream.tech (ce fichier n'est jamais envoyé aux visiteurs).
//
// jeton : jeton GitHub « fine-grained », en LECTURE SEULE, limité au dépôt sileostream :
//   github.com › Settings › Developer settings › Personal access tokens › Fine-grained tokens › Generate new token
//   Repository access : Only select repositories › sileostream
//   Permissions › Repository permissions › Contents : Read-only
// Collez-le à la place de COLLER_LE_JETON_ICI, sur le serveur seulement (jamais dans GitHub).
return [
    'jeton' => 'COLLER_LE_JETON_ICI',
    'depot' => 'capmaster2023/sileostream',
];
