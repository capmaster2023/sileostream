# Siloé Stream 1.6.5 — Caméras fluides (60 images par seconde)

- **Caméras USB et cartes de capture HDMI fluides.** Les cartes de capture USB (« USB Video ») ne donnent que 5 à 10 images par
  seconde en image non compressée : Siloé Stream demande maintenant le format compressé MJPG, à **60 images par seconde par défaut**,
  et mesure ce qui arrive vraiment. Si la carte n'y arrive pas en 1080p, il prend 720p à 60 ; sinon le meilleur réglage possible.
  Ce qui marche est retenu pour la suite.
- **Au choix : 60, 40 ou 30 images par seconde** : clic droit sur la caméra (Bibliothèque › Médias) › « Images par seconde ».
  En haut du menu : ce que la caméra donne en ce moment (par exemple « 1920 × 1080 · MJPG · 60 i/s »).
- **Taille de l'image** : clic droit › « Taille de l'image » : 1080p (conseillé), 4K (pour le direct en 4K, 30 images par seconde
  au plus) ou 720p (plus léger).
- Les images de la caméra sont montrées dès qu'elles arrivent (avant : une pause après chaque image, et une vérification toutes les 15 ms).
- Si la caméra donne moins d'images que demandé, Siloé Stream le dit une fois, avec quoi faire.
- « Reconnecter la source » : la caméra cherche de nouveau son meilleur réglage (après avoir changé la Sony en 60p, par exemple).

Pour une Sony α6100 en 60 images par seconde : molette en mode vidéo, « Format fichier » : XAVC S HD, « Réglage d'enreg. » : 60p
(en 4K, la Sony sort au plus 30 images par seconde par HDMI).
