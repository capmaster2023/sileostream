# siloestream.tech

Le site de Siloé Stream : la page de téléchargement et les mises à jour du logiciel et de l'application Android.

## Comment ça marche

1. Une avance de `main` (ou un tag `v*`) lance **Build Siloé Stream** sur GitHub : les APK puis Windows sont compilés et testés, puis la version est publiée dans les *Releases* du dépôt privé `sileostream`. Tant que Windows n'a pas fini, la version reste un brouillon, invisible pour le site.
2. Le site lit ces versions avec un jeton GitHub en lecture seule, gardé sur le serveur (`api/config.php`). Il relit GitHub au plus une fois par minute.
3. Le logiciel Windows et l'application Android demandent les mises à jour au site, plus à GitHub.
4. Un téléchargement passe par le site, qui renvoie vers un lien GitHub signé, valable quelques minutes : les fichiers ne passent pas par l'hébergement et le dépôt reste privé.

| Adresse | Contenu |
|---|---|
| `/` | page de présentation et de téléchargement |
| `/api/releases/latest` | dernière version, même forme que l'API GitHub |
| `/api/releases` | toutes les versions publiées, bêtas comprises |
| `/telecharger/windows` | installateur de la dernière version |
| `/telecharger/portable` | version portable (.zip) |
| `/telecharger/android` | application Android (.apk) |
| `/telecharger/<tag>/<fichier>` | un fichier d'une version précise (liens donnés au logiciel) |
| `/connexion/` | connexion Google du logiciel (1.9.3) : ouverte par Siloé Stream avec `?port=…&etat=…` |

## Mise en ligne (Hostinger)

1. **Jeton GitHub** : github.com › Settings › Developer settings › Personal access tokens › *Fine-grained tokens* › *Generate new token*. Nom : `siloestream.tech`. *Repository access* : *Only select repositories* › `sileostream`. *Permissions* › *Repository permissions* › *Contents* : *Read-only*. Copiez le jeton.
2. **Fichiers** : hPanel › Sites web › siloestream.tech › Gestionnaire de fichiers › `public_html`. Supprimez la page « Coming soon » (`default.php` ou `index.php`), envoyez `siloestream-site.zip` et choisissez *Extraire*. `.htaccess`, `index.html`, `logo.png`, `favicon.ico` et le dossier `api` doivent être directement dans `public_html`.
3. **Relier à GitHub** : ouvrez `public_html/api/config.php`, remplacez `COLLER_LE_JETON_ICI` par le jeton, enregistrez. Ne mettez jamais le jeton dans GitHub.
4. **Vérifier** : https://siloestream.tech/api/releases/latest doit afficher la dernière version.

La page `/connexion/` utilise l'identifiant Google de Siloé Stream : dans Google Cloud (projet mychurchapp › API et services › Identifiants › le client Web), **https://siloestream.tech** doit être dans les *Origines JavaScript autorisées*.

Le jeton GitHub expire (date choisie à sa création) : avant cette date, créez-en un nouveau et remplacez-le dans `api/config.php`.

Si vous renvoyez le site plus tard, ne remplacez pas `api/config.php` : il contient le jeton.

## Installateur (2.4.2)

Après avoir extrait `siloestream-site.zip` dans `public_html`, ouvrez **https://siloestream.tech/installer/** : entrez le jeton GitHub, l'identifiant client et le secret Google, puis « Enregistrer sur Hostinger ». L'installateur vérifie le jeton auprès de GitHub, écrit `api/config.php` et `api/google-config.php` (jamais lisibles depuis le Web) et se ferme tout seul (`api/installation.lock`).

Le logiciel lit alors l'identifiant Google sur `https://siloestream.tech/api/google.php` : rien à taper sur les PC, même après une réinstallation. Le zip ne contient jamais `config.php` ni `google-config.php` : renvoyer le site n'efface pas vos clés. Pour changer une clé : supprimez `api/installation.lock` et rouvrez l'installateur.
