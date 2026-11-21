# Siloé Stream 3.0.0 — Chants swahili traduits, karaoké, et la caméra ne coupe plus sur Zoom et YouTube

## Traduction française des chants swahili (Wokovu)

- Les chants du recueil **Wokovu** sont traduits en français **tout seuls**, en arrière-plan : les chants du programme d'abord, puis le reste du recueil, petit à petit. Le chant montré passe devant les autres.
- La traduction est **gardée dans le chant** : une fois traduit, plus besoin d'Internet.
- Elle ne s'affiche **que si on le veut** : à droite, **Traduction française** › « Montrer le français sous les chants swahili », ou la **touche T** pour montrer ou cacher. Elle apparaît alors sous les paroles, en italique (écran de l'église, aperçus, Zoom, YouTube, OBS), et suit chaque ligne quelle que soit la découpe (karaoké compris).
- **Corriger à la main** : dans l'éditeur du chant, **Traduction…** montre chaque ligne et sa traduction. Une case effacée est refaite toute seule.
- Service gratuit sans compte (MyMemory) : 5 000 caractères par jour, ou **50 000 avec un courriel** (case « Courriel » dans Traduction française). La limite atteinte, la traduction attend l'heure de reprise ; les chants déjà traduits restent.
- On peut arrêter la traduction automatique (« Traduire tout seul »).

## Karaoké : 2 lignes et 1 ligne par diapositive

- Éditeur du chant › **Lignes par diapositive** : **2 lignes (karaoké)** et **1 ligne (karaoké)**, en plus de Auto, 3, 4 et 5.
- On peut passer du karaoké à 4 lignes (et revenir) sans perdre les couplets : les morceaux d'une même strophe sont recollés avant d'être rangés de nouveau.

## La caméra ne coupe plus sur Zoom et YouTube

- **Zoom (MyVirtualCam)** : plus d'images noires au milieu de la caméra. Quand Siloé Stream écrit l'image suivante au même moment, ou prend un court retard (ordinateur chargé), Zoom garde la dernière image. Le noir ne vient plus que si Siloé Stream est fermé (ou ne répond plus depuis 10 secondes).
- **MyStream (Zoom et YouTube)** : une caméra qui manque d'images un instant (carte de capture qui hésite, caméra qui se rouvre) garde sa dernière image jusqu'à 2 secondes, au lieu d'un éclair noir.
- **Beaucoup moins de charge pour l'ordinateur** : la lecture des caméras et des cartes de capture (Cam Link 4K…) occupait un cœur du processeur à plein, en priorité haute, même entre deux images. Elle attend maintenant l'image suivante sans rien faire : Zoom, l'encodeur du direct YouTube et MyStream ont toute la place.
- MyStream passe en priorité haute, comme la caméra et l'envoi du direct ; l'heure de MyVirtualCam est rafraîchie à part.
- **Caméra figée** (signal HDMI perdu ou changé) : après 3 secondes sans image, Siloé Stream rouvre la caméra tout seul, et cherche de nouveau le bon réglage si le signal a changé (la Sony passée du 4K au 1080p…). La dernière image reste en attendant.
- **Conseil 4K** : si la caméra arrive en 4K alors que le direct et l'écran sont en 1080p, Siloé Stream le dit une fois. Plus léger et plus stable : sur la Sony, Réglages HDMI › Résolution HDMI : 1080p, puis clic droit sur la source › Taille de l'image : 1080p.
- Au journal (Réglages › Journal) : les coupures d'images de la caméra et de MyStream.
- Après la mise à jour, **fermez et rouvrez Zoom** : la caméra MyVirtualCam est chargée dans Zoom.

## Médias : ▲ ▼ dans la liste de gauche

- Chaque média de la liste de gauche a ses flèches **▲ ▼**, comme « Sources à l'écran » : ▲ le fait passer devant les autres sources à l'écran, ▼ derrière.
- Superposé, c'est son calque qui bouge (ses poignées apparaissent dans Médias) ; montré en grand, c'est l'écran principal. S'il n'est pas à l'écran, Siloé Stream le dit (⧉ pour le superposer).
