# Siloé Stream 2.2.0 — Montage, publication directe sur YouTube, recherche vocale

## Montage (nouveau bouton « Montage » dans la barre de gauche)

Enregistrement 4K → montage dans Siloé Stream → publication sur YouTube, sans CapCut et sans attendre un fichier exporté.

- **Médias** : les enregistrements de Siloé Stream et ceux d'OBS (dossier Vidéos) sont listés tout seuls ; **+ Importer…** ajoute n'importe quelle vidéo ou musique. Double-clic : à la suite sur la piste vidéo. Les fichiers d'origine ne sont jamais modifiés ni supprimés.
- **Ligne de temps multipiste** : V1 (générique de début, séquences, générique de fin), T (titres), A1 à A4 (musique, micro enregistré à part…). Glisser une séquence change l'ordre ; glisser un bord la raccourcit ; l'**aimant** colle aux bords et à la tête de lecture ; Ctrl + molette pour zoomer.
- **Couper** (S), **marquer le début / la fin** (I / O), **supprimer** (Suppr), **annuler / rétablir** (Ctrl Z / Ctrl Y), image par image (← →, Maj : une seconde), Espace pour lire.
- **Fondus** entre deux séquences (enchaîné ou par le noir), **titres** (bandeau en bas, centre, haut) avec fondu, **logo de l'église** dans un coin, **génériques** (fond noir, logo, texte), **volume** par séquence et par son ajouté, fondus du son.
- **Aperçu** : les séquences à la suite avec les titres, le logo et les génériques à leur place (les fondus et les sons ajoutés ne sont joués que dans le rendu). Un format que Windows ne lit pas s'affiche image par image.
- **Sauvegarde automatique** du projet (fichier `.siloemontage` dans Vidéos › Siloé Stream › Montages) : on reprend le montage plus tard, là où on l'avait laissé.

## Qualité : copie sans perte, ou réencodage par la carte graphique

- **Copie sans réencodage** (FFmpeg « -c copy ») quand rien ne l'oblige à réencoder (pas de titre, logo, générique, fondu ni son changé, enregistrements du même format) : la 4K, le codec et le débit d'origine sont gardés, et c'est très rapide. Chaque coupe se cale alors sur l'image clé qui la précède (souvent moins de 2 s) ; pour couper à l'image près, choisir « Toujours réencoder ».
- **Réencodage** sinon, avec le meilleur encodeur qui marche vraiment sur le PC (essayé à la taille voulue) : **NVIDIA NVENC**, **Intel Quick Sync**, **AMD AMF**, ou x264 / x265 (processeur) en secours ; H.264 ou HEVC ; 1080p, 1440p, 4K ou la taille des enregistrements ; qualité constante réglable. Chaque séquence est décodée à son tour (deux à la fois pendant un fondu) : la mémoire reste bornée même pour des heures, et le son reste calé sur l'image à l'échantillon près, quel que soit le nombre de coupes.

## Publier sur YouTube (vidéo classique, pas un direct)

- **Connexion OAuth** (le même compte que pour le direct), **changer de chaîne**, titre, description, **miniature**, **playlist**, **tags**, catégorie, « pour les enfants », **visibilité** (publique, non répertoriée, privée) ou **date de publication**.
- La vidéo **part pendant qu'elle se fabrique** (YouTube Data API v3, « Resumable Upload ») : aucun fichier final à attendre sur le disque. Une coupure d'Internet ne perd rien : Siloé Stream demande à YouTube ce qu'il a reçu et reprend tout seul. Le dernier morceau ne part que si le rendu a réussi : une vidéo incomplète n'est jamais publiée.
- **Progression réelle** du rendu et de l'envoi (débit, temps restant). La fenêtre peut se fermer : la publication continue (suivi à droite dans la page Montage).
- **Copie sur le PC** (facultative) : le rendu va alors à pleine vitesse dans le fichier, l'envoi le suit, et **« Reprendre l'envoi »** continue même après avoir fermé Siloé Stream. La copie n'est jamais supprimée sans votre accord (bouton « Supprimer la copie »).
- **Exporter un fichier** (MP4, ou MKV) si on veut aussi la vidéo sur le disque ; la place libre est vérifiée avant, et un disque plein est dit clairement.

À savoir : YouTube garde en **privé** les vidéos envoyées par une application Google qui n'a pas passé sa vérification ; Siloé Stream le signale après l'envoi (il suffit alors de la rendre publique dans YouTube Studio).

## Recherche vocale (le petit micro dans la barre de recherche)

- Un clic : **« Chant ou Bible ? »**, puis Siloé Stream écoute l'assemblée, la chorale ou la prédication, reconnaît ce qui est dit **sur le PC** (Whisper, sans Internet après un téléchargement unique du modèle, 142 Mo ou 466 Mo, avec votre accord) et propose le chant ou le verset : **Y aller**, **Montrer**, **+** (programme).
- Bible : les références dites à voix haute (« Jean chapitre 3 verset 16 à 18 », « premier Jean 1, 9 », « le psaume 23 ») d'abord, puis les versets lus sans leur référence, dans toutes les Bibles installées (King James comprise).
- Chant : les paroles entendues retrouvent le chant même avec des mots « à l'oreille ». **Écoute continue** pour suivre la prédication. Micro, langue, durée et précision dans « Réglages de l'écoute ».

## Duo

- Les dossiers **Duo** ont maintenant leurs boutons **Église** et **Stream** dans la grille des Médias : ses deux éléments deviennent la source principale de l'église ou de l'antenne (MyStream : Zoom, YouTube). Un élément « Dans l'église seulement » ne part pas à l'antenne. Un dossier Duo peut aussi être ajouté au programme comme écran principal.
