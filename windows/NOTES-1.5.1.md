# Siloé Stream 1.5.1 : l'éditeur enregistre tout seul, la recherche t'emmène, plus de vitesse

**À installer sur le PC (SiloeStream-Setup-1.5.1.exe).** Vos chants, Bibles, cultes et réglages sont gardés.

## Éditeur de chant

- **Plus de bouton Enregistrer** : chaque changement (titre, paroles, lignes par diapositive, Ranger, un chant collé)
  s'enregistre tout seul. En bas, un petit point vert dit « Enregistré tout seul à … ».
- **« Retour en arrière »** (bouton bordeaux, ou Ctrl+Z) défait la dernière modification. Plusieurs clics remontent
  plusieurs étapes, jusqu'au chant du début. Un nouveau chant ramené à vide disparaît de la liste.
- **« Fermer »** remplace « Annuler » : tout est déjà enregistré.
- Pendant que l'éditeur est ouvert, la liste des chants et les vignettes du culte ne se refont plus à chaque touche
  (une seule fois, à la fermeture). Un chant à l'écran change tout de suite sur le projecteur.

## Recherche (en haut, Ctrl K)

- **Le numéro marche des deux côtés** : « 48 wok », « wok 48 », « 48wok », « wokovu48 ».
- **Entrée ou clic t'emmène à sa place, sans rien montrer à l'écran** : un chant s'ouvre dans Bibliothèque › Chants
  (dans son recueil), un verset est choisi dans Bibliothèque › Bible, au bon chapitre. Ensuite, Entrée ou
  « Montrer la sélection » le montre quand tu veux.
- **Maj+Entrée** (ou Maj+clic) montre tout de suite, comme avant. **Tab ou +** ajoute au programme.

## Plus de vitesse

- Les petits aperçus (vignettes du culte, Ensuite, la télécommande, les miniatures, la grille des médias) se dessinent
  sans l'ombre floue du texte, qu'on ne voit presque pas à cette taille et qui coûte le plus à dessiner.
- Les vignettes sont dessinées une fois puis gardées en image : le cadre « À l'écran » qui bouge, le défilement et
  le retour sur la page Culte ne les redessinent plus.
- Un chapitre de la Bible s'ouvre plus vite (le « + » devant chaque verset est plus léger).
- Sans carte graphique, le grand aperçu du culte se fait léger aussi. Le journal (Réglages › Journal) dit au démarrage
  comment l'écran est dessiné, et note toute action de plus de 300 ms.

## Correction

- MyVirtualCam (Zoom) : en passant des paroles à une image ou une caméra, Zoom pouvait garder la diapositive d'avant.
  L'image la plus récente gagne toujours.

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 : GNU GPL version 3 — https://ffmpeg.org/releases/
- NAudio 2.2.1 (MIT) : https://github.com/naudio/NAudio
- OpenCvSharp (Apache 2.0) : https://github.com/shimat/opencvsharp
