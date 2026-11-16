# Siloé Stream 1.3.2 : Zoom voit l'écran de l'église, même pendant que Zoom est projeté

**À installer sur le PC (SiloeStream-Setup-1.3.2.exe).** Vos chants, sources Display et réglages sont gardés.

**Testé automatiquement, pas encore avec Zoom dans l'église : faites un essai (Zoom projeté, MyVirtualCam comme caméra dans Zoom) avant un culte.**

## MyVirtualCam = l'écran de l'église

**MyVirtualCam** (la caméra de Siloé Stream dans Zoom) montre maintenant tout ce que montre l'écran de l'église :

- les versets, les chants, Display, le Duo… comme avant ;
- **l'écran par défaut** (le logo, l'image ou le fond choisi dans « Écran par défaut ») : avant, Zoom voyait un écran noir ;
- **pendant une capture de fenêtre « Dans l'église »** (Zoom projeté dans l'église) : Zoom voit **l'écran par défaut** à sa place, au lieu d'un écran noir. Zoom ne reçoit toujours pas sa propre fenêtre : pas d'effet miroir.

Avec l'écran par défaut **Noir**, Zoom voit du noir, comme l'église.

La page OBS (source navigateur) ne change pas : rien pendant l'écran par défaut ni pendant une source « Dans l'église », pour rester transparente par-dessus la caméra d'OBS.

## Fenêtre « Capture de fenêtre »

- Les explications sous **Dans l'église**, **En streaming** et **Les deux** passent à la ligne au lieu d'être coupées à droite.
- **Dans l'église** dit maintenant que Zoom voit l'écran principal à la place.

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 : GNU GPL version 3 — https://ffmpeg.org/releases/
- NAudio 2.2.1 (MIT) : https://github.com/naudio/NAudio
- OpenCvSharp (Apache 2.0) : https://github.com/shimat/opencvsharp
