# Spike d'affichage gRPC — rapport

Date : 2026-10-21 · Machine : Intel Core Ultra 7 155H, 64 Go, Intel Arc + RTX 1000 Ada, Windows 11 26200, Hyper-V actif.
Code jetable : `spikes/DisplaySpike/` (Avalonia 12.1.2 + Grpc.Net.Client, proto copié de l'émulateur).

## Verdict : **GO gRPC** (transport par défaut, sans mémoire partagée)

| Mesure | Résultat | Seuil |
|---|---|---|
| Images/s en défilement (Paramètres, 540 px de large, RGBA8888) | 34–53, ~40 en moyenne | ≥ 30 |
| Latence tap→première image (15 essais, médiane) | **32 ms** (échantillons : 2–224 ms, une valeur aberrante à 801 ms) | < 100 ms |
| CPU du client | ~40 % d'un cœur pendant le flux | « raisonnable » |
| Mémoire du client | ~280 Mo | — |

Le repli mémoire partagée n'a pas été nécessaire. Pistes d'optimisation CPU pour le plan 2 : supprimer la double copie (flux → tampon → WriteableBitmap), ou passer en MMAP.

## Valeurs figées

- **(a) Émulateur** : `37.2.12` (build 16428233, `channel-0`), archive `emulator-windows_x64-16428233.zip`.
- **(b) Image** : `system-images;android-37.0;google_apis_playstore;x86_64`, révision 6 (`x86_64-37.0_r06.zip`, 2,3 Go). Niveau d'API sous forme de **chaîne** : `"37.0"`.
- `platform-tools` : 37.0.1 (dernière stable, non figée).
- **(c) Authentification gRPC** : `-grpc <port> -grpc-use-token`. Le jeton se trouve dans `%LOCALAPPDATA%\Temp\avd\running\pid_<pid emulator>.ini`, clé `grpc.token`, avec aussi `grpc.port`, `port.serial`, `avd.name`. En-tête : `authorization: Bearer <jeton>`. Noms des RPC tels quels en C# (`streamScreenshot`, `sendMouse`, `sendKey`, `getStatus`).
- **(d) Transport** : flux d'images RGBA8888 par défaut.
- **(f) Traduction ARM** : `ro.product.cpu.abilist = x86_64,arm64-v8a`, `ro.dalvik.vm.native.bridge = libndk_translation.so`. Le Play Store (`com.android.vending`) est présent. **Non fait** : installation d'une app ARM-only depuis le Play Store, qui demande un compte Google → à vérifier à la main par l'utilisateur.

## Constats pour la suite

1. **La licence `android-sdk-arm-dbt-license` ne signale plus la traduction ARM.** Elle n'apparaît que sur API 30/31. Les images 33+ utilisent `android-sdk-license` et embarquent quand même `libndk_translation`. → Le sélecteur exige le chemin exact figé, et la traduction ARM est garantie par le choix de version vérifié ici.
2. **Les manifestes ne fournissent que du SHA-1** (pas de SHA-256).
3. Le chemin de l'image utilise `android-37.0` (avec point). Il existe aussi des variantes `_ps16k`, `-extNN`, `-beta`, `canary` qu'il faut ignorer.
4. Les archives d'image n'ont **pas** de `host-os` (sauf pour API 30/32). Le sélecteur accepte `windows` ou `null`.
5. **`ANDROID_USER_HOME` ne suffit pas** : l'émulateur 37 écrit dans `%USERPROFILE%\.android` tant que `ANDROID_EMULATOR_HOME` n'est pas défini aussi. Avec les deux, plus rien n'y est écrit. (adb crée sa clé `adbkey` dans le dossier utilisateur si on le lance sans ces variables.)
6. **Premier démarrage** : démarrage à froid en 213 s, puis invité saturé ~8 min (load 40 : mises à jour du Play Store, dexopt). Un dialogue « System UI isn't responding » est apparu et s'est retrouvé **figé dans le snapshot**. → Plan 2 : écran « Première préparation » qui attend que la charge retombe avant de rendre la main, et pas de snapshot pendant cette phase.
7. Démarrage suivant via snapshot : **15 s**.
8. `-accel-check` : code de sortie 0, sortie `WHPX(10.0.26200) is installed and usable.`
9. `-no-window` et `-qt-hide-window` donnent tous deux un flux correct. Le blocage observé au début venait du dialogue ANR, pas du mode fenêtre. On garde `-no-window`.

## Validation environnement

(À compléter en Task 9.)
