# Siloé Stream 2.5.1 — La caméra ne coupe plus sur Zoom et YouTube

## La caméra sur Zoom et YouTube

- **Zoom (MyVirtualCam)** : plus d'images noires au milieu de la caméra. Quand Siloé Stream écrit l'image suivante au même moment, ou prend un court retard (ordinateur chargé), Zoom garde la dernière image au lieu d'afficher du noir. Le noir ne vient plus que si Siloé Stream est fermé (ou ne répond plus depuis 10 secondes).
- **MyStream (Zoom et YouTube)** : une caméra qui manque d'images un instant (carte de capture qui hésite, caméra qui se rouvre) garde sa dernière image jusqu'à 2 secondes, au lieu d'un éclair noir.
- **Beaucoup moins de charge pour l'ordinateur** : la lecture des caméras et des cartes de capture (Cam Link 4K…) occupait un cœur du processeur à plein, en priorité haute, même entre deux images. Elle attend maintenant l'image suivante sans rien faire : Zoom, l'encodeur du direct YouTube et MyStream ont toute la place.
- MyStream (l'image envoyée sur Internet) passe en priorité haute, comme la caméra et l'envoi du direct. L'heure de MyVirtualCam est aussi rafraîchie à part : un instant de travail de MyStream ne fait plus passer Zoom au noir.
- **Caméra figée** (signal HDMI perdu ou changé, carte de capture qui ne donne plus rien) : après 3 secondes sans image, Siloé Stream rouvre la caméra tout seul, et cherche de nouveau le bon réglage si le signal a changé (par exemple la Sony passée du 4K au 1080p). La dernière image reste à l'écran en attendant.
- **Conseil 4K** : si la caméra arrive en 4K alors que le direct et l'écran sont en 1080p, Siloé Stream le dit une fois. Plus léger et plus stable : sur la Sony, Réglages HDMI › Résolution HDMI : 1080p, puis clic droit sur la source › Taille de l'image : 1080p.
- Au journal (Réglages › Journal) : les coupures d'images de la caméra et de MyStream, pour trouver la cause si ça recommence.
- Après la mise à jour, **fermez et rouvrez Zoom** : la caméra MyVirtualCam est chargée dans Zoom.

## Médias : ▲ ▼ dans la liste de gauche

- Chaque média de la liste de gauche a ses flèches **▲ ▼**, comme « Sources à l'écran » : ▲ le fait passer devant les autres sources à l'écran, ▼ derrière.
- Superposé, c'est son calque qui bouge (ses poignées apparaissent dans Médias) ; montré en grand, c'est l'écran principal. S'il n'est pas à l'écran, Siloé Stream le dit (⧉ pour le superposer).
