# Siloé Stream 1.1.0 : la capture ASIO, comme dans OBS

**À installer sur le PC (SiloeStream-Setup-1.1.0.exe).**

**Testé automatiquement, pas encore avec la carte son de l'église : faites un essai avant un culte.**

## Nouvelle source : Capture ASIO

Dans le mélangeur, **+ Source audio › Capture ASIO…** ouvre la même fenêtre que le plugin ASIO d'OBS :

- **Périphérique** : le pilote ASIO de la carte son (Focusrite USB ASIO, UMC ASIO Driver, PreSonus…).
- **Format** : **Mono** ou **Stéréo**.
- **Canal OBS 1** (et **Canal OBS 2** en stéréo) : **Muet**, **Input 1**, **Input 2**…
- **Panneau de configuration** : le panneau du fabricant (fréquence, taille du tampon).
- **Valeurs par défaut**, **OK**, **Annuler**.

Exemple : CHANTEUR = Focusrite USB ASIO, Mono, Input 1.

Volume, filtres, monitoring, pistes 1 à 6 et propriétés audio avancées marchent comme pour les autres sources. MyVirtualSound et VB-CABLE ne changent pas.

## À savoir

- **Un seul pilote ASIO à la fois** : toutes les sources ASIO utilisent la même carte son.
- **Fermez OBS** s'il utilise déjà le pilote ASIO : sinon Siloé Stream ne peut pas l'ouvrir (le nom de la source passe en rouge avec l'explication).

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- NAudio 2.2.1 (MIT) : https://github.com/naudio/NAudio
