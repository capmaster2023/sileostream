# Siloé Stream 1.3.1 : Zoom dans l'église, sans effet miroir

**À installer sur le PC (SiloeStream-Setup-1.3.1.exe).** Vos chants, sources Display et réglages sont gardés.

**Testé automatiquement, pas encore avec Zoom dans l'église : faites un essai (Zoom projeté, MyVirtualCam comme caméra dans Zoom) avant un culte.**

## Nouvelle fenêtre « Capture de fenêtre », comme dans OBS

Display › **+ Ajouter une source › Capture de fenêtre** :

- **l'image de la fenêtre en direct**, en haut, pour voir tout de suite si c'est la bonne ;
- la liste **« [Zoom.exe] : Zoom Meeting »** : seulement les vraies fenêtres (plus de « Program Manager », « ZPToolBarParentWnd » ni de fenêtres cachées), rangées par programme. **Actualiser** relit la liste ; elle se relit aussi quand on revient dans la fenêtre ;
- **Zone client seulement** (cochée) : sans la barre de titre ni les bords de la fenêtre ;
- le **nom** dans Display ;
- **Où va cette fenêtre ?** (voir plus bas).

La nouvelle source va directement dans la grille : un clic sur sa tuile l'envoie en direct.

## Où va la fenêtre : dans l'église, en streaming ou les deux

- **Dans l'église** : seulement sur l'écran de projection. MyVirtualCam et OBS ne la reçoivent pas (image noire). C'est le choix pour **projeter Zoom dans l'église** : Zoom ne reçoit plus sa propre fenêtre par MyVirtualCam, donc plus d'effet miroir à l'infini. Siloé Stream le coche d'office pour Zoom, Teams, Webex, Skype, Discord et WhatsApp ; un autre choix est signalé en rouge.
- **En streaming** : seulement vers MyVirtualCam et OBS (Zoom, YouTube, Facebook). L'écran de projection garde l'écran par défaut (noir, fond seul ou l'écran principal choisi).
- **Les deux** : partout, comme avant.

Le choix se voit partout :

- sur la tuile de la grille : **FENÊTRE · ÉGLISE** ou **FENÊTRE · STREAMING** ; dans la bibliothèque : « Capture de fenêtre · Dans l'église seulement » ;
- à côté d'**EN DIRECT** : **Église seulement**, ou **Streaming seulement** (en or).

Pour changer d'avis : clic droit sur la source › **Où va cette fenêtre** (un clic), ou **Modifier…**, qui rouvre la fenêtre de capture avec ses réglages. Une fenêtre épinglée (Duo) suit la même règle.

Les enregistrements gardent ce qui est en direct, quel que soit le choix.

## La fenêtre se retrouve toute seule

- **Fermée puis rouverte** (Zoom qui change de réunion, un programme relancé) : Siloé Stream la reprend tout seul, avec le même programme et le même titre, sinon le même type de fenêtre (comme « Faire correspondre le titre, sinon trouver une fenêtre du même type » d'OBS). En attendant, l'écran dit « La fenêtre a été fermée… ».
- **Réduite** : la source attend et revient dès que la fenêtre est restaurée (avant, il fallait la réafficher).

Les fenêtres ajoutées avant la 1.3.1 marchent comme avant (les deux, fenêtre entière). Pour la zone client et la fenêtre retrouvée toute seule : **Modifier…** une fois, puis **Enregistrer**.

## Zoom dans l'église, pas à pas

1. Ouvrez la réunion Zoom sur le PC.
2. Display › **+ Ajouter une source › Capture de fenêtre** › **[Zoom.exe] : Zoom Meeting** : « Dans l'église » est déjà coché. **Ajouter la source**.
3. Dans Zoom, la caméra peut rester **MyVirtualCam** : pendant que Zoom est à l'écran de l'église, elle envoie une image noire au lieu de la fenêtre de Zoom.

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 : GNU GPL version 3 — https://ffmpeg.org/releases/
- NAudio 2.2.1 (MIT) : https://github.com/naudio/NAudio
- OpenCvSharp (Apache 2.0) : https://github.com/shimat/opencvsharp
