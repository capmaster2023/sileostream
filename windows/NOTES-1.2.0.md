# Siloé Stream 1.2.0 : MyCam fluide dans les enregistrements, synchro du son automatique

**À installer sur le PC (SiloeStream-Setup-1.2.0.exe) et sur le téléphone (SiloeStream.apk) :** la synchro automatique de MyCam a besoin des deux.

**Testé automatiquement, pas encore avec la carte son et les téléphones de l'église : faites un essai d'enregistrement avant un culte.**

## MyCam fluide dans les enregistrements vidéo

Avant, une vidéo enregistrée avec MyCam n'avait que 10 à 13 images différentes par seconde : chaque image passait par l'interface de Siloé Stream, qui ne suivait pas.

- La vidéo des enregistrements est composée **à part, à cadence exacte** (30 images par seconde), comme le rendu d'OBS.
- MyCam, les caméras USB et les vidéos sont posées à chaque image ; le texte, les versets, les chants et le Duo sont dessinés quand ils changent.
- L'interface et la projection ne ralentissent plus pendant un enregistrement.

## La voix sur les lèvres, sans réglage

Avant, la voix arrivait en retard sur l'image, et il fallait corriger à la main avec « Synchro du son ».

- **« Synchro du son » est retiré** : la synchro est automatique, comme dans OBS.
- Chaque bloc de son garde l'instant où OBS l'a capté, et chaque image est placée en face du son du même instant. Le retard du mélange d'OBS ne décale plus la voix.
- **Le retard de MyCam est mesuré** : le téléphone dit l'âge de chaque image et le PC mesure le trajet sur le Wi-Fi. Siloé Stream choisit pour chaque instant l'image prise à ce moment-là.
- Un téléphone pas encore mis à jour marche toujours : son retard est estimé (environ 100 ms).

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 : GNU GPL version 3 — https://ffmpeg.org/releases/
- NAudio 2.2.1 (MIT) : https://github.com/naudio/NAudio
- OpenCvSharp (Apache 2.0) : https://github.com/shimat/opencvsharp
