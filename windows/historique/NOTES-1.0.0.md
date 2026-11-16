# Siloé Stream 1.0.0 : la projection tient tout le culte, Échap n'efface plus le direct, l'audio comme OBS

**À installer sur le PC (SiloeStream-Setup-1.0.0.exe) et sur le téléphone (SiloeStream.apk).**

**Vous avez Siloé Stream 4.x ?** Siloé Stream a déménagé dans ce dépôt (l'historique et les anciennes versions restent dans l'ancien dépôt). Une 4.x ne voit pas la 1.0 dans ses mises à jour : installez-la **une fois à la main, par-dessus** — l'installateur sur le PC, l'APK sur le téléphone. Chants, Bibles, déroulés, réglages et téléphones jumelés sont gardés. Les mises à jour suivantes se font de nouveau depuis Siloé Stream.

**Testé automatiquement, pas encore avec le projecteur et la carte son de l'église : faites un essai avant un culte (débrancher puis rebrancher le projecteur, micros, enregistrement, Zoom).**

## Pas de mise en veille pendant la projection

Avant, rien n'empêchait Windows d'éteindre l'écran pendant 45 minutes de prédication sans toucher la souris.

- Tant que la projection est ouverte, Windows **n'éteint ni l'écran ni le PC**, et l'économiseur d'écran ne démarre pas.
- Pendant un enregistrement, le PC reste éveillé.
- Projection fermée : Windows reprend ses réglages d'alimentation habituels.

## Projecteur débranché, puis rebranché

Avant, Siloé Stream ne réagissait pas : la projection ne revenait pas sur le projecteur rebranché, et pouvait recouvrir l'écran de la régie.

- **Débranché** (câble tiré, projecteur éteint) : la projection se ferme tout de suite, sans recouvrir l'écran de la régie, et la barre du bas affiche **Projecteur débranché**.
- **Rebranché** : la projection se rouvre toute seule dessus, avec ce qui est en direct, même sur une autre prise ou dans une autre résolution.
- Le projecteur est reconnu par son nom (« EPSON PJ »), sinon par sa sortie de la carte graphique. **Un autre écran (écran de scène) n'est jamais pris pour lui.**
- Le projecteur change de résolution pendant la projection : elle le suit sans se fermer.
- Pour ne plus l'attendre : **Projection › Ne plus attendre le projecteur** (ou Réglages › Écrans).

## La projection se rouvre au démarrage

- La projection laissée ouverte à la fermeture de Siloé Stream **se rouvre toute seule sur le dernier écran**.
- Projecteur pas encore branché : Siloé Stream l'attend et ouvre la projection dès qu'il est branché.
- Jamais en plein écran sur l'écran de la régie. Après **Arrêter la projection** (ou la croix de la fenêtre de projection), elle ne se rouvre plus toute seule.

## Échap n'efface plus le verset pendant une recherche

Avant, Échap dans la recherche (Bible ou chants) annulait la recherche **et** retirait le verset en direct.

- Dans une recherche (Bible, chants, numéro de chant, Ctrl K) : **Échap annule la recherche, le direct ne bouge pas**. Un 2e Échap quitte le champ.
- Dans un autre champ (texte d'un display, réglages) : Échap quitte le champ sans rien effacer.
- En dehors d'un champ : Échap retire ce qui est à l'écran, comme avant. Une liste déroulante ou un menu ouvert se referme d'abord.

## L'audio, simplifié comme OBS

Il y avait trop de réglages. Siloé Stream garde ce qu'OBS montre, et presque rien d'autre.

- **+ Source audio**, comme le « + » des sources d'OBS : **Capture audio (entrée)**, **Capture audio (sortie)**, **Média**, ou le son de la vidéo projetée depuis Display.
- Une capture audio s'ouvre sur la fenêtre « Propriétés » d'OBS : **Périphérique** (« Par défaut » ou un périphérique) et **Utiliser l'horodatage du périphérique**, avec **Valeurs par défaut**, **OK** et **Annuler**. Une carte son à plusieurs entrées (Behringer, Focusrite…) propose aussi **Entrée** : toutes ensemble, ou une seule (un micro par source).
- **Mélangeur** : pour chaque source, son nom, son volume, ses vumètres, le fader, **M** (sourdine), le **casque** (monitoring) et **⋮** : Propriétés, Filtres, Renommer, Propriétés audio avancées, déplacer, Supprimer.
- **⚙ Propriétés audio avancées**, le même tableau que dans OBS : nom, état (**Actif** ou **Inactif**), volume (case **%**), mono, balance, décalage de synchronisation, monitoring audio et **pistes 1 à 6**, avec « Sources actives uniquement ».
- **Pistes**, comme dans OBS : chaque source coche ses pistes (une nouvelle source va dans les 6), et chaque enregistrement ou sortie MyVirtualSound choisit **sa piste**. Exemple : le pasteur seul sur la piste 2 pour l'enregistrement « Prédication ». Les mélanges déjà faits en 4.1 reçoivent chacun leur piste.
- **Retiré** : dans Réglages › Audio, tout sauf le **périphérique de monitoring audio** (casque de la régie) ; dans le mélangeur, épingler, masquer, disposition horizontale, pastilles sous les bandes ; « Couper les autres sources » ; le choix d'un autre ffmpeg.exe (celui de Siloé Stream est inclus). « Audio du bureau » et « Micro/Aux » deviennent des sources ordinaires : rien n'est perdu.
- **MyVirtualSound et VB-CABLE ne changent pas** : Réglages › MyVirtualSound › Installer VB-CABLE.

## Premier lancement : un logiciel vide

Une nouvelle installation démarre sans chant (les recueils ne sont plus livrés avec Siloé Stream), sans source audio et sans enregistrement. Seules deux Bibles libres de droits sont livrées : **Louis Segond 1910** (français) et **King James** (anglais). Les chants s'ajoutent avec **+ Importer chants ou recueil** ou **+ Nouveau chant**.

**Une mise à jour ne supprime rien** : chants, recueils, Bibles importées, déroulés et réglages restent.

## Licence et code source

- Siloé Stream : GNU GPL version 2 ou ultérieure (à cause de libobs) — https://github.com/capmaster2023/sileostream
- OBS Studio 33.0.1 (libobs) : https://github.com/obsproject/obs-studio/tree/33.0.1
- FFmpeg 8.0.1 : GNU GPL version 3 — https://ffmpeg.org/releases/
