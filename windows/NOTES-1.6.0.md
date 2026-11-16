# Siloé Stream 1.6.0 : MyStream et le direct sur YouTube

**À installer sur le PC (SiloeStream-Setup-1.6.0.exe).** Vos chants, Bibles, cultes et réglages sont gardés.

## Deux écrans, à droite

- **Église** : ce que montre le projecteur.
- **MyStream** : ce qui part sur Internet. Zoom (MyVirtualCam) et YouTube montrent la même image.

## MyStream

- **Source principale** (bouton « Source » sous MyStream) : une caméra, MyCam, une fenêtre, une vidéo ou une image.
  Elle reste sur MyStream comme l'écran de repos de l'église : quand l'église ne montre rien, montre son écran de repos
  ou une source « Dans l'église », MyStream montre la source principale.
- Les **paroles et les versets** passent par-dessus la source principale, **en bandeau** (ou en plein écran, au choix dans « Source »).
- Une image, une vidéo ou une caméra montrées à l'église passent en grand sur MyStream.
- **« Source principale » (touche M)** : MyStream revient sur la source principale, l'église garde ce qu'elle montre.
  La diapositive suivante repart aussi sur MyStream.
- Une source peut aller **« Sur MyStream »** seulement (avant : « En streaming ») ou « Dans l'église » seulement.
- Sans source principale, MyStream montre la même chose que l'église (comme Zoom avant).

## Le direct sur YouTube (pastille « YouTube », en haut)

- **Mon compte YouTube** (conseillé) : Siloé Stream crée le direct comme OBS : titre, description, confidentialité, catégorie,
  « conçue pour les enfants ? », miniature, latence, DVR, 360°, programmer pour plus tard. Ou « Direct déjà prévu ».
  « Arrêter le direct » le termine aussi sur YouTube.
- **Clé de stream** : la clé de YouTube Studio › Créer › Passer au direct, et c'est parti.
- **Autre service** : Facebook, Vimeo… (une adresse rtmps:// et une clé).
- **Image et son du direct** : 720p (conseillé) ou 1080p ; tout le son, ou seulement les sons choisis.
- **Si Internet coupe, Siloé Stream se reconnecte tout seul.** La pastille dit « En direct 0:42:13 » (rouge) ou « Reconnexion… ».
- La carte graphique encode l'image quand elle le peut (NVIDIA, Intel, AMD), sinon le processeur.
- La clé de stream et l'accès au compte sont chiffrés pour cet utilisateur de Windows.

## « Connecter le compte YouTube » : à faire une seule fois

Google demande un identifiant propre à chaque logiciel. En attendant, **la clé de stream marche tout de suite**.

1. Sur console.cloud.google.com, créez un projet (par exemple « Siloé Stream »).
2. API et services › Bibliothèque : activez **YouTube Data API v3**.
3. Écran de consentement OAuth : type **Externe**, nom « Siloé Stream », votre adresse e-mail ;
   accès aux données : ajoutez le champ d'application `https://www.googleapis.com/auth/youtube`.
4. Audience : **Publier l'application** (« En production »). Sinon, Google coupe l'accès tous les 7 jours.
   Pas besoin de validation pour une église : à la connexion, Google dira « application non validée » :
   « Paramètres avancés », puis « Accéder à Siloé Stream ».
5. Clients : **Créer un client OAuth**, type **Application de bureau**. Gardez l'identifiant et le secret.
6. Collez-les dans la fenêtre du direct (« Identifiant client », « Secret du client », puis « Garder »).
   Ou, pour toutes les prochaines versions : dépôt GitHub › Settings › Secrets and variables › Actions :
   `YOUTUBE_CLIENT_ID` et `YOUTUBE_CLIENT_SECRET`.

## Aussi

- Zoom (MyVirtualCam) est dessiné par MyStream, hors du fil de l'interface. Un téléphone MyCam tenu à la verticale
  a maintenant le fond flou sur les côtés, comme dans l'église.

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 : GNU GPL version 3 — https://ffmpeg.org/releases/
- NAudio 2.2.1 (MIT) : https://github.com/naudio/NAudio
- OpenCvSharp (Apache 2.0) : https://github.com/shimat/opencvsharp
