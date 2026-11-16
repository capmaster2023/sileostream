# Siloé Stream 1.9.6 — La caméra en 1080p sans câble jusqu'au PC : le téléphone comme pont

- **Application Android, MyCam › Caméra HDMI** : une vraie caméra (Sony α6100, toute caméra avec une sortie HDMI) branchée au
  téléphone par une carte de capture HDMI → USB (Elgato Cam Link 4K si le téléphone a un port USB 3, ou une carte « HDMI to USB »)
  et un adaptateur USB-C. Le téléphone envoie l'image au PC par le Wi-Fi en H.264, en 720p ou 1080p (réglages de MyCam), avec la
  même connexion, le même témoin rouge / orange et la même synchro du son que MyCam.
- La carte est lue directement par Siloé Stream (pilote UVC intégré) : pas besoin que le téléphone gère les webcams USB.
  Formats YUY2 et NV12 (USB 3) et MJPEG (USB 2) ; les couleurs HDMI (BT.709) sont converties pour le PC.
- Brancher la carte propose d'ouvrir Siloé Stream ; Android demande l'autorisation une fois. Débranchée puis rebranchée, la caméra reprend toute seule.
- Logiciel Windows : rien ne change (Display › Ajouter › Téléphone MyCam).
- Sony α6100 : MENU › Réglage › Réglages HDMI › Affichage infos HDMI : Non, pour une image sans les indications de l'écran.
