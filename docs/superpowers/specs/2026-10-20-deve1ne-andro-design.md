# Deve1ne Andro — émulateur Android pour Windows (design)

Date : 2026-10-20 · Statut : validé

> « Andro » est un nom de travail (repris du dossier `Andro-stack`). Renommable.

## 1. Intention

Faire tourner des apps Android sur un PC Windows **simplement**, sans BlueStacks :
pas de pub, pas d'abonnement, pas de compte, gratuit. BlueStacks a été jugé trop
lourd ; le critère n°1 est la **simplicité** : installer une app depuis Google Play
ou en glissant une APK, sans menus compliqués.

L'app est **distribuée à d'autres personnes**, comme application de la suite
Deve1ne, installée et mise à jour par le **Deve1ne Launcher**
(`C:\dev\deve1ne-launcher`). Le launcher ne fait que rassembler les installeurs :
Andro est une app autonome, avec son propre dépôt et son propre MSI, au même titre
que Git UI.

## 2. Usages visés

1. **Apps du quotidien via Google Play** : ouvrir le Play Store, installer, utiliser.
2. **Apps F-Droid** : apps open source installées par APK.
3. **APK perso** : tester ses propres apps par glisser-déposer.

## 3. Objectifs, non-objectifs, anti-objectifs

**Objectifs (V1)**
- Assistant de premier lancement : il prépare tout, de zéro jusqu'à un téléphone Android démarré, sans connaissance technique.
- Écran Android **intégré dans la fenêtre Andro**, avec une barre d'outils : retour, accueil, apps récentes, rotation, volume, capture d'écran, marche/arrêt.
- Glisser-déposer d'une `.apk` → installation. Glisser-déposer d'un autre fichier → copie dans `Download/` du téléphone.
- Bouton « Installer F-Droid », qui installe le client F-Droid officiel dans le téléphone.
- Google Play présent dès le départ (image `google_apis_playstore`).
- Démarrage rapide via snapshot (Quick Boot).
- Réglages simples : RAM, nombre de cœurs, résolution, « Réinitialiser le téléphone ».

**Non-objectifs (V1)**
- Mapping clavier/manette pour les jeux.
- Plusieurs téléphones en même temps, ou choix libre de la version d'Android et du modèle (au plus un mode avancé plus tard).
- Catalogue F-Droid intégré dans l'interface Andro (option (b), écartée en V1).
- Format `.xapk` / `.apks` (bundles éclatés).
- macOS / Linux.
- Root, Magisk.

**Anti-objectifs : même si ça « marche », c'est un échec si…**
- il y a de la pub, de la télémétrie, un compte obligatoire ou un abonnement ;
- la licence Google est acceptée **en silence** à la place de l'utilisateur ;
- l'app tente de **contourner Play Integrity** (contraire aux CGU Google ; risque pour une app distribuée) ;
- l'utilisateur doit installer Android Studio, Java ou lancer des lignes de commande ;
- des droits administrateur sont demandés pour autre chose que l'activation unique de la fonctionnalité Windows de virtualisation.

## 4. Contraintes

- **Stack de la suite Deve1ne** : .NET 8, Avalonia 12.1.2, CommunityToolkit.Mvvm 8.4.2, xUnit. Découpage en couches comme Git UI (`Domain` / `Application` / `Infrastructure` / `Presentation`). MSI WiX comme Git UI (`git-ui/packaging/windows/GitUi.wxs`). Release GitHub sur un tag `v*`, avec `SHA256SUMS.txt` (le launcher refuse d'installer sans ce fichier).
- **Licence** : la suite est sous GPL-3.0-or-later. Andro **ne redistribue pas** le SDK Android ni les images système (licence Android SDK). Tout est téléchargé chez l'utilisateur, depuis les serveurs Google, au premier lancement. Andro pilote l'émulateur comme un **processus séparé** (pas de liaison de code), ce qui est compatible avec la GPL.
- **Licences à afficher** : le texte de licence Google (fourni dans le manifeste du dépôt SDK) est affiché à l'utilisateur et doit être accepté explicitement avant tout téléchargement.
- **Machine cible** : Windows 10/11 x64, virtualisation CPU active dans le BIOS, 8 Go de RAM minimum, ~15 Go de disque libre.
- **Pas d'admin** pour le fonctionnement courant. Données stockées dans `%LocalAppData%\Deve1ne\Andro\` (`sdk\`, `avd\`, `settings.json`, `logs\`).
- **Désinstallation** : le MSI **ne touche pas** à `%LocalAppData%\Deve1ne\Andro\`, ni à la désinstallation ni à une mise à jour. Les réglages d'Andro proposent un bouton « Supprimer toutes les données Android » (avec confirmation, émulateur arrêté), qui libère les ~15 Go.
- **Play Integrity** : certaines apps (banques, paiement, streaming DRM, parfois messageries) refuseront de tourner. Il faut le **dire clairement** dans l'app (page d'aide ou message au premier lancement), pas le cacher.

## 5. Le comment : décisions prises

### 5.1 Moteur
Moteur : l'**Android Emulator officiel de Google** (QEMU), accéléré par **WHPX**
(Windows Hypervisor Platform). Andro ne réimplémente rien de la virtualisation.

### 5.2 Affichage intégré
Andro lance l'émulateur **sans fenêtre** (`-no-window`) avec son interface **gRPC**
activée (`-grpc <port>`, jeton d'accès). L'écran est reçu par flux
(`streamScreenshot`) et dessiné dans un contrôle Avalonia. Clics, gestes, clavier
et boutons physiques sont renvoyés via gRPC (`sendTouch`, `sendKey`,
`sendMouse`, `setPhysicalModel`…). Le `.proto` est livré avec l'émulateur
(`emulator/lib/emulator_controller.proto`).

> **Spike obligatoire en premier** (voir §7) : valider latence et fluidité du flux
> gRPC dans Avalonia. *(Mon choix)* Seuils de réussite, à la résolution par
> défaut : **≥ 30 images/s** en défilement, **< 100 ms** entre le clic et le pixel,
> CPU d'Andro raisonnable. Premier repli autorisé : transport mémoire partagée de
> l'émulateur, avec le même client gRPC. Si ça ne suffit toujours pas, **on s'arrête
> et on en reparle** avant de partir sur scrcpy, qui changerait l'architecture
> (décodage H.264, binaire tiers à livrer).

### 5.3 Installation de l'environnement Android : sans Java *(mon choix)*
`sdkmanager` et `avdmanager` exigent un JDK, ce qui viole l'anti-objectif
« pas de Java ». Andro implémente donc lui-même le strict nécessaire :
- lire le manifeste du dépôt SDK Google (`repository2-*.xml`, et `sys-img/google_apis_playstore/sys-img2-*.xml`) ;
- choisir l'image `google_apis_playstore` `x86_64` **la plus récente qui inclut la traduction ARM** *(mon choix : la compatibilité des apps Play passe avant le dernier niveau d'API ; le spike le vérifie en installant une app ARM-only)* ;
- **versions figées** *(mon choix)* : chaque release d'Andro déclare la version de l'émulateur testée (celle dont le `.proto` a servi à générer le client gRPC) et le niveau d'API de l'image. Le parseur de manifeste sélectionne ces versions-là, pas « la dernière ». Mettre à jour l'émulateur = nouvelle release d'Andro. L'image d'un téléphone existant **n'est jamais changée** automatiquement en V1 (ça effacerait apps et compte Google) ; une nouvelle image ne s'applique qu'après « Réinitialiser le téléphone », avec avertissement ;
- télécharger `emulator`, `platform-tools` et l'image système, en vérifiant la somme de contrôle fournie par le manifeste, avec progression et reprise ;
- extraire les archives dans `%LocalAppData%\Deve1ne\Andro\sdk\` en respectant l'arborescence attendue par l'émulateur ;
- créer l'AVD en écrivant soi-même `avd\<nom>.ini` et `config.ini` (format documenté), avec un profil de type Pixel.

### 5.4 Vérifications de prérequis (assistant)
- Virtualisation et WHPX : `emulator -accel-check`.
- Si la fonctionnalité Windows `HypervisorPlatform` est absente : expliquer, proposer de l'activer via **une seule** commande élevée (UAC), puis demander un redémarrage. L'assistant reprend là où il s'était arrêté au lancement suivant.
- Si la virtualisation est désactivée dans le BIOS : message clair. Andro ne peut rien faire de plus.
- Espace disque et RAM : avertir sous les seuils.

### 5.5 adb
`adb` (`platform-tools`) sert à installer les APK, pousser des fichiers et vérifier
que le téléphone a fini de démarrer (`sys.boot_completed`).

### 5.6 F-Droid
Le bouton télécharge `https://f-droid.org/F-Droid.apk` et l'installe via adb.
F-Droid gère ensuite lui-même ses mises à jour et son catalogue.

### 5.7 Cycle de vie
- Fermer Andro → sauvegarde d'un snapshot Quick Boot, puis arrêt propre de l'émulateur. **Andro ne quitte qu'après la fin du processus émulateur**, sinon le launcher pourrait mettre à jour pendant que l'émulateur écrit encore dans l'AVD.
- *(Mon choix, sans toucher au launcher)* : le launcher laisse 15 s à une app pour se fermer (`AppInstallers.cs`, `CloseRunningAsync`). Si la sauvegarde du snapshot dépasse ~10 s, Andro arrête l'émulateur sans snapshot : le prochain démarrage sera un démarrage à froid, mais rien n'est perdu.
- Un seul processus émulateur par instance d'Andro.
- En cas de plantage de l'émulateur : message et bouton « Redémarrer », avec les logs dans `logs\`.

### 5.8 Intégration au Deve1ne Launcher
Ajout d'une entrée `SuiteApp` dans `Core/SuiteCatalog.cs` (InstallKind `Msi`,
dépôt `Deve1ne/andro`, motif `Andro-{version}-win-x64.msi`, UpgradeCode dédié),
plus l'icône `Assets/apps/andro.png`, comme décrit dans le README « Add an app ».

### 5.9 Visuel
Reprise de la palette et des polices « papier et encre » de la suite (voir
`deve1ne-launcher/src/Deve1ne.Launcher/App.axaml`) pour une cohérence visuelle.

## 6. Laissé à l'appréciation du développeur

- Le nom définitif et l'icône.
- Le nom du dépôt GitHub (`Deve1ne/andro` est une proposition).
- Le découpage fin des classes à l'intérieur des couches.
- Le choix du port gRPC (port libre dynamique conseillé).
- Les seuils exacts de RAM et de disque, et les valeurs par défaut de l'AVD (RAM, cœurs, résolution).
- La présentation exacte de l'assistant et des écrans, tant que la simplicité prime.
- La stratégie de tests. Le Domain et l'Application doivent être testés comme dans Git UI ; l'Infrastructure (téléchargements, processus) l'est si possible par des fakes.

## 7. Ordre suggéré

1. **Spike d'affichage** (jetable) : émulateur installé à la main + fenêtre Avalonia qui affiche le flux gRPC et renvoie les touches. Mesurer la latence et le nombre d'images par seconde. Décider ensuite gRPC ou repli.
2. Installation de l'environnement sans Java (§5.3) et vérification des prérequis (§5.4).
3. Écran intégré, barre d'outils et cycle de vie.
4. Glisser-déposer, APK et F-Droid.
5. Réglages, message sur Play Integrity.
6. MSI, release GitHub, entrée dans le launcher.
