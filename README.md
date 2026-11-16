# Siloé Stream 2.1.0

**Commencer le culte** (2.1.0) : un clic vérifie le projecteur, YouTube, le son et les caméras, puis lance l'enregistrement dans le dossier choisi (Réglages › Son). Médias : « + Ajouter un média » et, à part, « + Ajouter un périphérique vidéo » (MyVirtualCam, webcams, cartes de capture, MyCam, Sony).

Design « Conducteur » (2.0.2) : **À L'ÉCRAN** (l'église, en or), **ENVOYER À L'ANTENNE** (touche S), **À L'ANTENNE** (YouTube, Zoom, MyStream, en rouge) ; chaque écran a sa source principale et son bouton **Revenir** (Échap pour l'écran, M pour l'antenne). Thème sombre chaud par défaut, clair au choix. Les couleurs sont dans `windows/MyVerse/Palette.cs`.

Logiciel Windows autonome de projection pour l'église, avec télécommande Android. Bible, chants et sources Display partent directement sur l'écran choisi dans **Projection**. Le son passe par le moteur audio d'OBS Studio (libobs), dans une page **Son** faite pour être comprise par tout le monde.

## Premier lancement

Siloé Stream démarre **vide** : aucun chant, aucun micro (seulement le son des vidéos), aucun enregistrement. Seules deux Bibles libres de droits sont livrées : **Louis Segond 1910** (français) et **King James** (anglais). Les chants s'ajoutent avec **+ Importer chants ou recueil** ou **+ Nouveau chant**, les autres Bibles avec **+ Importer une Bible**.

Siloé Stream est un logiciel libre sous licence **GNU GPL version 2 ou ultérieure** (voir [Licence et code source](#licence-et-code-source)).

## Interface (3.2, design « Pupitre »)

- **Barre de gauche** : **Culte**, **Bible**, **Chants**, **Display**, **Duo**, **Son**, puis en bas **Sombre / Clair** et **Réglages**.
- **Page Culte** : le déroulé en cartes à gauche (un trait de couleur par type, « ● DIRECT » sur l'élément à l'écran) et, au centre, **toutes les diapositives du culte en vignettes**, dessinées comme à l'écran et groupées par élément. La vignette à l'écran est encadrée de rouge (« À L'ÉCRAN »), la suivante d'ambre (« SUIVANT »). Un clic affiche une vignette.
- **Colonne de contrôle à droite, sur toutes les pages** : un petit écran **EN DIRECT**, la diapositive **SUIVANT** en plus petit, **◀ / Suivant ▶**, **Noir / Retirer de l'écran**. Duo et habillage sont dessous.
- **Barre du bas** : l'écran de projection, les téléphones, l'écran de scène et l'heure.
- Thème **clair** par défaut (crème), **sombre** au choix d'un clic. Police Manrope.

## Duo : deux contenus à l'écran

L'onglet **Duo** prépare des dossiers de 2 éléments, chacun à sa place et à sa taille : **Choisir › Chant…** (recueil, puis numéro ou titre), **Verset…** ou une source Display. Dans la grille de Display, la vignette d'un dossier montre ses deux éléments comme sur l'écran principal.

Pour épingler à la volée :

1. Affichez le premier contenu.
2. Dans la carte **DUO**, cliquez **Épingler le contenu en direct**, ou appuyez sur **P**.
3. Affichez le second contenu : le premier reste dans un coin.
4. Choisissez un coin ou **Côte à côte**. La taille d'un coin se règle entre 20 et 50 %.

**Retirer de l'écran** ne vide que le contenu principal. **Retirer le duo** enlève le contenu épinglé. La touche **B** masque les deux sans les perdre.

Textes, versets, chants, images, caméras et fenêtres peuvent être épinglés. Une source vidéo épinglée continue de fonctionner pendant l'affichage de l'autre contenu. Le Duo se retrouve dans la projection, l'aperçu et **MyVirtualCam**.

## Display

- **+ Ajouter une source** ouvre un menu : Nouveau texte, Images, Périphérique de capture vidéo, Capture de fenêtre et MyVirtualCam.
- **+** à côté d'une source l'ajoute à la grille ; le bouton devient **✓**, et un clic sur ✓ l'en retire. **Modifier** est dans le clic droit.
- Un clic sur une vignette l'envoie immédiatement sur l'écran choisi.
- Clic droit : Afficher puis épingler, Modifier, Retirer de la grille, Ajouter au déroulé du culte ou Supprimer.

### Capture de fenêtre (1.3.1)

La fenêtre **Capture de fenêtre** ressemble à celle d'OBS : l'image de la fenêtre choisie **en direct** en haut, la liste **« [Zoom.exe] : Zoom Meeting »** (seulement les vraies fenêtres, rangées par programme), **Actualiser**, **Zone client seulement** (sans la barre de titre ni les bords, coché d'office) et le nom dans Display. Elle demande aussi **où va la fenêtre** :

- **Dans l'église** : seulement sur l'écran de projection. MyVirtualCam (Zoom) montre **l'écran par défaut** à la place (le logo, une image, le fond seul…), OBS ne reçoit rien. C'est le choix pour **montrer Zoom dans l'église** : Zoom ne reçoit plus sa propre fenêtre, plus d'effet miroir à l'infini. Siloé Stream le propose d'office pour Zoom, Teams, Webex…
- **En streaming** : seulement vers MyVirtualCam et OBS. L'écran de projection garde l'écran par défaut (noir, fond seul ou écran principal).
- **Les deux** : partout, comme les autres sources.

La tuile de la grille l'indique (**FENÊTRE · ÉGLISE**, **FENÊTRE · STREAMING**), l'écran en direct aussi (**Église seulement**, **Streaming seulement**). Clic droit › **Où va cette fenêtre** change le choix en un clic, **Modifier…** rouvre la fenêtre de capture. Une fenêtre fermée puis rouverte (Zoom qui change de réunion) est reprise toute seule : même programme et même titre, sinon même type de fenêtre ; une fenêtre réduite revient dès qu'elle est restaurée. Les enregistrements gardent ce qui est en direct.

## Son (Siloé Stream 1.3 : simple pour tous)

Tout le son est dans la page **Son** (barre de gauche). On n'y voit jamais de piste, de monitoring ni de filtre : Siloé Stream les règle. Le moteur est **libobs**, la bibliothèque d'OBS Studio 33.0.1, livrée dans le dossier `obs` à côté de `SiloeStream.exe` (48 kHz, stéréo).

**Ce qu'on entend** : une carte par son, avec sa barre qui bouge, un mot simple (**Bon niveau**, **Un peu faible**, **Trop fort !**, **Coupé**, **Débranché**), son **volume en %** (100 % = normal, double-clic pour y revenir), **Couper** (rouge : le son est coupé partout) et **Régler**.

- **+ Ajouter un son**, en trois questions : *Qu'est-ce que c'est ?* (micro, instrument, table de mixage, son de l'ordinateur, musique) ; *Où est-il branché ?* (on parle dans le micro et on clique la barre qui bouge ; une carte son à plusieurs entrées montre une barre par entrée ; cartes ASIO comprises) ; *Comment l'appelle-t-on ?*
- **Réglé tout seul** selon ce que c'est : une voix a le bruit enlevé et un volume régulier ; la musique reste telle quelle ; une musique s'entend dans l'église et attend **Lire** (Pause, Arrêter, en boucle au choix).
- Le **son des vidéos de Display** est toujours là : il suit la vidéo à l'écran et s'entend dans l'église.
- **Régler** : le nom, ce que c'est, où il est branché, et trois interrupteurs expliqués en une phrase : **Dans l'église**, **Enlever le bruit**, **Volume régulier**. **Retirer ce son** est là aussi.
- Un appareil débranché fait dire **Débranché** à son son, qui revient tout seul quand on le rebranche ; rien n'est ajouté ni retiré tout seul.

**Où va le son** :

- **Dans l'église** : les haut-parleurs de l'ordinateur, et les sons qu'on y entend (les micros, qui passent par la table de mixage, demandent une confirmation : risque de larsen).
- **Enregistrer** : **Enregistrer le culte** d'un clic (le son en MP3, prêt à partager, dans Vidéos › Siloé Stream). D'autres enregistrements au choix (5 au plus, en même temps) : le son seul (MP3, ou WAV qualité studio) ou **la vidéo et le son** (MKV : ce qui est à l'écran, des caméras, MyCam…, la synchro est automatique). FFmpeg 8.0.1 est inclus (dossier `ffmpeg`).
- **Zoom et en ligne** : **Envoyer le son à Zoom** d'un clic ; dans Zoom, OBS ou Teams, choisir le micro « CABLE Output ». Le câble audio virtuel (VB-CABLE, VB-Audio Software, www.vb-cable.com, donationware) n'est pas inclus : **Installer le câble** le télécharge depuis le site officiel, seulement sur clic. 4 envois au plus.
- **Casque** : écouter tout le son, comme il part à Zoom et dans l'enregistrement (sa sortie doit être différente de celle de l'église).
- Chaque destination reçoit **tout le son**, ou seulement les sons choisis. Siloé Stream donne les pistes d'OBS tout seul et ne renvoie jamais le son de l'ordinateur vers la sortie d'où il vient. Une protection anti-saturation garde l'enregistrement, Zoom et le casque propres.
- **Réglages › Son** : le moteur du son (**Réparer** s'il manque : réinstallation, données gardées), les haut-parleurs de l'église, le câble pour Zoom, le dossier des enregistrements.
- Les réglages des versions précédentes sont repris : les sources deviennent des sons, les sorties MyVirtualSound des envois, le monitoring « Dans l'église ».

## Projection et caméra virtuelle

Le bouton **Projection** (en bas à droite) propose les moniteurs détectés, leurs dimensions et une sortie en fenêtre. Siloé Stream projette directement : aucun logiciel externe ni connexion WebSocket n'est nécessaire.

- **Pas de veille** : tant que la projection est ouverte, Windows n'éteint ni l'écran ni le PC (`SetThreadExecutionState`) et l'économiseur d'écran ne démarre pas. Pendant un enregistrement, le PC reste éveillé.
- **Projecteur débranché** : la projection se ferme tout de suite (elle ne recouvre pas l'écran de la régie) et l'état **Projecteur débranché** s'affiche. **Rebranché** : elle se rouvre toute seule dessus, même sur une autre prise ou dans une autre résolution. Le projecteur est reconnu par son nom (« EPSON PJ »), sinon par sa sortie ; un autre écran (écran de scène) n'est jamais pris pour lui.
- **Au démarrage**, la projection laissée ouverte se rouvre sur le dernier écran, jamais en plein écran sur l'écran de la régie. **Arrêter la projection** : elle ne se rouvre plus toute seule.
- **Échap** retire ce qui est à l'écran, sauf dans un champ de saisie : dans une recherche (Bible, chants, numéro, Ctrl K), Échap annule la recherche sans toucher au direct, et un 2e Échap quitte le champ.

**MyVirtualCam** reçoit automatiquement la sortie en 720p30 : tout ce que montre l'écran de l'église, Duo et écran par défaut compris (1.3.2), sauf une capture de fenêtre « Dans l'église », remplacée par l'écran par défaut. Elle s'enregistre une fois avec l'autorisation Windows et reste disponible, avec une image noire, quand Siloé Stream est fermé. Elle vise les applications compatibles DirectShow et ne transporte pas d'audio.

La capture de fenêtre nécessite une fenêtre ouverte et non réduite (elle attend qu'on la rouvre ou la restaure). Certains contenus protégés ne permettent pas la capture. Les périphériques vidéo physiques nécessitent les pilotes et permissions Windows appropriés.

## Installation et mises à jour

Téléchargements : **https://siloestream.tech** (installateur Windows, version portable, application Android).

- `SiloeStream-Setup-<version>.exe` : installateur Windows.
- `SiloeStream-<version>-Windows.zip` : version portable.
- `SiloeStream.apk` : télécommande Android.

Les versions sont publiées dans les *Releases* de ce dépôt privé par GitHub Actions. Le site siloestream.tech (dossier `site`, voir `site/LISEZ-MOI.md`) les lit avec un jeton GitHub en lecture seule gardé sur le serveur et les sert sous la même forme que l'API GitHub : le logiciel et l'application y cherchent leurs mises à jour depuis la 1.9.2. Aucun jeton GitHub n'est intégré dans l'application. Une 1.9.1 ou plus ancienne cherche encore ses mises à jour dans l'ancien dépôt : installez la 1.9.2 une fois à la main, par-dessus ; les données sont gardées.

L'installation conserve les chants, Bibles, réglages et téléphones jumelés. Une DLL MyVirtualCam déjà utilisée est renommée avant la copie de sa nouvelle version; le logiciel qui l'utilise charge la nouvelle caméra à sa prochaine ouverture.

Sur le téléphone, installez l'APK par-dessus l'application existante. Le package Android et la signature existante restent conservés. Le PC et le téléphone doivent être sur le même réseau pour la télécommande.

## Caméra HDMI par le téléphone (1.9.6)

Dans l'application Android, **MyCam › Caméra HDMI** fait du téléphone un pont sans fil pour une vraie caméra en 1080p :
caméra → HDMI → carte de capture HDMI-USB → adaptateur USB-C → téléphone → Wi-Fi → Siloé Stream (source MyCam).
Pilote UVC intégré (`android/app/src/main/cpp/uvc.c`) : isochrone ou bulk, YUY2 / NV12 / MJPEG.

## Caméra Sony par Wi-Fi (1.9.5)

**Display › + Ajouter une source › Caméra Sony par Wi-Fi (sans câble)** reçoit l'image en direct d'une caméra Sony (α6100…) par son propre Wi-Fi, comme Imaging Edge Mobile.
La caméra est en **Ctrl w/ Smartphone** ; Siloé Stream connecte le PC à son Wi-Fi (DIRECT-xxxx, avec `netsh`) et la reçoit en PTP/IP (extension Sony SDIO, objet 0xFFFFC002) ou par l'API Web des anciens Sony (`liveviewstream`).
Les images entrent dans le registre MyCam (`windows/MyVerse.Core/CameraWifi.cs`, identifiant `sony-wifi`) : c'est une source « mycam » comme un téléphone.
Qualité de l'image de contrôle (environ 1024 × 576). Pour garder Internet : câble réseau ou 2e carte Wi-Fi.

## Comptes Google (1.9.3)

Le logiciel se connecte avec un compte Google par la page **siloestream.tech/connexion** (bouton **Compte** de la barre de gauche) : la page renvoie le jeton de Google au logiciel, sur `127.0.0.1`, et le relais le vérifie. Les rôles sont ceux de l'église (relais du VPS) : **Admin** (tout), **Versets** (la Bible), **Chants**, **Affichage** (Culte, Médias, Duo : l'église et Zoom) et **Caméraman** (MyCam, sur le téléphone). L'admin gère les membres dans **Réglages › Église et comptes** et peut y rendre la connexion obligatoire.

## Licence et code source

Siloé Stream est un logiciel libre : vous pouvez le redistribuer et le modifier selon les termes de la **GNU General Public License, version 2 ou (à votre choix) toute version ultérieure**, publiée par la Free Software Foundation. Cette licence s'applique parce que Siloé Stream utilise libobs (OBS Studio), elle-même sous GPL v2 ou ultérieure. Le texte complet est dans [`COPYING`](COPYING), installé avec Siloé Stream sous le nom `COPYING.txt` (**Réglages › Licences › Lire la licence GNU GPL**).

- Code source de Siloé Stream : https://github.com/capmaster2023/sileostream
- Code source d'OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 (`ffmpeg.exe`, build « essentials » de gyan.dev) : programme séparé sous **GNU GPL version 3**, licence dans `ffmpeg\LICENSE.txt`, sources sur https://ffmpeg.org/releases/

Le dossier `obs` livré avec Siloé Stream contient les bibliothèques du paquet officiel OBS Studio 33.0.1 pour Windows (sans l'interface d'OBS), avec le runtime Microsoft Visual C++ dont elles ont besoin. Les polices de l'interface (Inter, Space Grotesk, Literata, Atkinson Hyperlegible Next, Unbounded) sont sous licence SIL Open Font License 1.1.

## Développement et validation

- `windows/MyVerse.Core` : cœur .NET 8, sans WPF.
- `windows/MyVerse` : interface Windows WPF.
- `windows/MyVerse.Tests` : tests du cœur et du protocole téléphone, dont Duo, et du mélangeur sur la vraie libobs (groupe « Audio OBS »).
- `windows/MyVirtualCam` : composant vidéo DirectShow 32 et 64 bits.
- `android` : télécommande Kotlin / Compose.
- `windows/NOTES-<version>.md` : notes de version (la dernière : `NOTES-1.0.0.md`). Les notes des anciennes versions sont dans `windows/historique`.

Tests locaux : `dotnet run --project windows/MyVerse.Tests -c Release -- windows/MyVerse/Ressources [groupe]`. Les essais de libobs utilisent le dossier OBS donné par la variable `SILOE_OBS` sous Windows, ou le paquet `obs-studio` sous Linux (avec `xvfb`). Les essais FLAC et MKV (dont la synchro son/image mesurée) utilisent le `ffmpeg` donné par `SILOE_FFMPEG`.

GitHub Actions **Build Siloé Stream** télécharge le paquet officiel d'OBS 33.0.1 et FFmpeg 8.0.1 (SHA-256 vérifiés), ne garde d'OBS que libobs et trois modules (win-wasapi, obs-filters, obs-ffmpeg), vérifie leurs dépendances, enregistre de vrais fichiers WAV, FLAC et MKV, compile et teste WPF sur Windows, vérifie les images de projection, les pixels de MyVirtualCam, l'export navigateur local et l'installation, puis construit les APK. La branche `dev` sert à valider. Une avance de `main` publie la version. Les comptes rendus et captures sont copiés dans `ci-logs`.
