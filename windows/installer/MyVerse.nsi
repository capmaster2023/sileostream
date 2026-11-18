; Installateur de Siloé Stream (logiciel Windows natif), compilé sur GitHub Actions.
; Le dossier « app » (copié en entier, sous-dossiers compris) contient aussi « obs » : libobs (OBS Studio 33.0.1, GNU GPL v2 ou ultérieure)
; et COPYING.txt, ainsi que « ffmpeg » : ffmpeg.exe 8.0.1 (gyan.dev, GNU GPL v3) et sa licence, pour le FLAC et la vidéo des enregistrements. VB-CABLE n'est jamais inclus : Siloé Stream le télécharge à la demande depuis le site officiel.
; Installation par utilisateur (pas besoin d'être administrateur), au même endroit que la 3.x : la mise à jour la remplace.
; Une mise à jour garde chants, réglages, Bibles importées et téléphones jumelés (%APPDATA%\SiloeStream) ; la désinstallation propose de les effacer.
Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "3.0.1"
!endif
!define APP "Siloé Stream"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\SiloeStream"

Name "${APP}"
OutFile "SiloeStream-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\SiloeStream"
InstallDirRegKey HKCU "Software\SiloeStream" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "myverse.ico"
!define MUI_UNICON "myverse.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\SiloeStream.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer Siloé Stream"

VIProductVersion "3.0.1.0"
VIAddVersionKey /LANG=1036 "ProductName" "Siloé Stream"
VIAddVersionKey /LANG=1036 "FileDescription" "Installateur de Siloé Stream"
VIAddVersionKey /LANG=1036 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1036 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1036 "CompanyName" "Siloé Stream"
VIAddVersionKey /LANG=1036 "LegalCopyright" "Siloé Stream"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "French"

Var ANCIENNE3

; mise à jour automatique (lancée par MyVerse avec /S) : on rouvre MyVerse à la fin
Function .onInstSuccess
  IfSilent 0 +2
  Exec '"$INSTDIR\SiloeStream.exe"'
FunctionEnd

Function FermerMyVerse
  nsExec::Exec 'taskkill /F /IM MyVerse.exe'
  nsExec::Exec 'taskkill /F /IM SiloeStream.exe'
  nsExec::Exec 'taskkill /F /IM MyChurch.exe'
  nsExec::Exec 'taskkill /F /IM verselight-bridge.exe'
  Sleep 1200
FunctionEnd

; Écarte un fichier du dossier d'installation (renommé en .old) pour que sa nouvelle version puisse s'écrire,
; même si un autre logiciel le garde ouvert. Si un ancien .old est encore utilisé, un autre nom libre est pris.
Function LibererFichier
  Exch $R0
  Push $R1
  IfFileExists "$INSTDIR\$R0" 0 liberer_fin
  ClearErrors
  Rename "$INSTDIR\$R0" "$INSTDIR\$R0.old"
  IfErrors 0 liberer_fin
  GetTempFileName $R1 "$INSTDIR"
  Delete "$R1"
  ClearErrors
  Rename "$INSTDIR\$R0" "$R1.old"
  liberer_fin:
  ClearErrors
  Pop $R1
  Pop $R0
FunctionEnd

Section "MyVerse"
  Call FermerMyVerse
  ; 1.9.0 : MyChurch devient Siloé Stream : l'ancienne installation est retirée (chants, Bibles et réglages sont gardés)
  StrCmp $INSTDIR "$LOCALAPPDATA\Programs\MyVerse" +3
  IfFileExists "$LOCALAPPDATA\Programs\MyVerse\MyChurch.exe" 0 +2
    RMDir /r "$LOCALAPPDATA\Programs\MyVerse"
  Delete "$DESKTOP\MyChurch.lnk"
  Delete "$SMPROGRAMS\MyChurch.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MyVerse"
  DeleteRegKey HKCU "Software\MyVerse"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MyChurch"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MyChurch Agent"
  Delete "$INSTDIR\MyVerse.exe"
  Delete "$INSTDIR\MyVerse.dll"
  Delete "$INSTDIR\MyVerse.deps.json"
  Delete "$INSTDIR\MyVerse.runtimeconfig.json"
  Delete "$DESKTOP\MyVerse.lnk"
  Delete "$SMPROGRAMS\MyVerse.lnk"

  ; MyVerse 3.x (HTML / Neutralino) : ses fichiers sont retirés, ses données restent dans %APPDATA%
  StrCpy $ANCIENNE3 "0"
  IfFileExists "$INSTDIR\resources.neu" 0 +2
    StrCpy $ANCIENNE3 "1"
  Delete "$INSTDIR\resources.neu"
  Delete "$INSTDIR\neutralinojs.log"
  Delete "$INSTDIR\WebView2Loader.dll"
  RMDir /r "$INSTDIR\extensions"
  RMDir /r "$INSTDIR\.tmp"
  RMDir /r "$INSTDIR\.storage"

  ; moteur audio OBS : remplacé en entier à chaque mise à jour (aucun ancien module ne reste),
  ; seulement dans un dossier de Siloé Stream (il est déjà fermé par FermerMyVerse)
  IfFileExists "$INSTDIR\SiloeStream.exe" 0 +2
    RMDir /r "$INSTDIR\obs"

  ; ffmpeg : un enregistrement interrompu peut laisser ffmpeg.exe ouvert un instant ; il est alors écarté (.old) comme MyVirtualCam
  Delete "$INSTDIR\ffmpeg\ffmpeg.exe.old"
  Push "ffmpeg\ffmpeg.exe"
  Call LibererFichier

  ; MyVirtualCam : quand un logiciel (OBS, Zoom…) utilise la caméra virtuelle, Windows garde ses deux fichiers ouverts
  ; et refuse de les remplacer (« Erreur lors de l'ouverture du fichier en écriture »). Un fichier ouvert peut par contre
  ; être renommé : les anciens passent en .old, les nouveaux s'écrivent à leur place, et les .old sont supprimés
  ; tout de suite s'ils sont libres, sinon au prochain redémarrage ou à la prochaine installation.
  Delete "$INSTDIR\MyVirtualCam.dll.old"
  Delete "$INSTDIR\MyVirtualCam32.dll.old"
  Delete "$INSTDIR\ns*.tmp.old"
  Push "MyVirtualCam.dll"
  Call LibererFichier
  Push "MyVirtualCam32.dll"
  Call LibererFichier

  SetOutPath "$INSTDIR"
  File /r "app\*.*"

  Delete /REBOOTOK "$INSTDIR\MyVirtualCam.dll.old"
  Delete /REBOOTOK "$INSTDIR\MyVirtualCam32.dll.old"
  Delete /REBOOTOK "$INSTDIR\ffmpeg\ffmpeg.exe.old"
  Delete "$INSTDIR\ns*.tmp.old"
  ; aucun redémarrage n'est demandé : le logiciel qui utilise la caméra garde l'ancienne version jusqu'à sa fermeture
  SetRebootFlag false

  ; pare-feu Windows (une seule autorisation) : pour que le téléphone puisse joindre le PC, même sur un Wi-Fi « public ».
  ; Mise à jour silencieuse : seulement en venant de la 3.x (le programme qui écoute a changé).
  IfSilent 0 parefeu
  StrCmp $ANCIENNE3 "1" parefeu fin_parefeu
  parefeu:
    ExecShellWait "runas" "$INSTDIR\SiloeStream.exe" "--parefeu" SW_HIDE
  fin_parefeu:

  ; Register the persistent virtual camera. Windows asks for elevation once.
  ExecShellWait "runas" "$INSTDIR\SiloeStream.exe" "--virtualcam-install" SW_HIDE

  CreateShortcut "$DESKTOP\Siloé Stream.lnk" "$INSTDIR\SiloeStream.exe" "" "$INSTDIR\SiloeStream.exe" 0
  CreateShortcut "$SMPROGRAMS\Siloé Stream.lnk" "$INSTDIR\SiloeStream.exe" "" "$INSTDIR\SiloeStream.exe" 0

  WriteUninstaller "$INSTDIR\Desinstaller.exe"
  WriteRegStr HKCU "Software\SiloeStream" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "Siloé Stream"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "Siloé Stream"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\SiloeStream.exe"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Desinstaller.exe"'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

; Désinstaller : retire le programme ; 2.4.0 : propose d'effacer aussi les données (chants, Bibles importées, photos, programmes,
; réglages, comptes, téléphones jumelés) et les anciens profils de MyVerse 3.x qui les faisaient revenir : une réinstallation
; repart alors de zéro. Les enregistrements, montages et miniatures (dossier Vidéos) ne sont jamais effacés.
; Sans fenêtre (/S : tests, scripts) : les données sont gardées.
Section "Uninstall"
  Call un.FermerMyVerse
  ExecShellWait "runas" "$INSTDIR\SiloeStream.exe" "--virtualcam-uninstall" SW_HIDE
  Delete "$DESKTOP\Siloé Stream.lnk"
  Delete "$SMPROGRAMS\Siloé Stream.lnk"
  ; par sécurité : on ne vide le dossier que si c'est bien celui de MyVerse
  IfFileExists "$INSTDIR\SiloeStream.exe" 0 +4
    RMDir /r "$INSTDIR\obs"
    RMDir /r "$INSTDIR\ffmpeg"
    RMDir /r "$INSTDIR"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\SiloeStream"
  ; 1.6.4 : « Ouvrir MyChurch au démarrage de Windows » (directs automatiques)
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MyChurch"
  ; 1.7.0 : l'agent « à distance »
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "MyChurch Agent"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Siloé Stream"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Siloé Stream Agent"
  MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON1 "Effacer aussi toutes les données de Siloé Stream ?$\r$\n$\r$\nChants, Bibles importées, photos et médias, programmes du culte, réglages, comptes et téléphones jumelés : une prochaine installation repartira de zéro.$\r$\n$\r$\nVos enregistrements, montages et miniatures (dossier Vidéos) ne sont jamais effacés.$\r$\n$\r$\nNon : les garder pour une réinstallation." /SD IDNO IDNO garder_donnees
    RMDir /r "$APPDATA\SiloeStream"
    RMDir /r "$APPDATA\VerseLight"
    RMDir /r "$APPDATA\MyVerse.exe"
    RMDir /r "$APPDATA\VerseLight.exe"
    RMDir /r "$LOCALAPPDATA\Programs\MyVerse\MyVerse.exe.WebView2"
    RMDir /r "$LOCALAPPDATA\Programs\MyVerse\VerseLight.exe.WebView2"
  garder_donnees:
SectionEnd

Function un.FermerMyVerse
  nsExec::Exec 'taskkill /F /IM MyVerse.exe'
  nsExec::Exec 'taskkill /F /IM SiloeStream.exe'
  nsExec::Exec 'taskkill /F /IM MyChurch.exe'
  Sleep 1000
FunctionEnd
