# Siloé Stream 1.6.7 — MyStream ne reste plus noir

- **Correction : MyStream (Zoom, YouTube) restait noir** avec « Caméra occupée ou indisponible » alors que l'écran de l'église
  montrait bien la caméra. Si la première ouverture de la caméra ratait (pilote qui hésite, appareil pas encore libéré), MyStream
  ne réessayait jamais. Maintenant, une caméra qui ne s'ouvre pas est réessayée toutes les 2 secondes, partout (église, MyStream,
  Duo) : elle revient toute seule dès qu'elle est libre.
- L'ouverture des caméras USB est plus patiente : trois essais espacés avant de dire « occupée », et une courte pause entre deux
  essais de réglage (certaines cartes de capture refusent d'être rouvertes aussitôt).
- Si une autre application se sert vraiment de la caméra (Zoom, OBS réglés sur « USB Video »), le message le dit : choisissez
  MyVirtualCam dans Zoom et OBS, et Siloé Stream reprend la caméra tout seul.
