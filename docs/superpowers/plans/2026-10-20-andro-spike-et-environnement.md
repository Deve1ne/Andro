# Andro — Spike d'affichage + installation de l'environnement Android : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** valider (ou invalider) l'affichage intégré par gRPC, puis livrer le sous-système qui installe l'environnement Android sans Java (manifeste Google → téléchargement vérifié → extraction → AVD) et démarre un téléphone, piloté par une petite CLI de dev.

**Architecture :** couches comme Git UI. `Andro.Domain` : modèles purs (manifeste SDK, sélection des paquets, génération de l'AVD), sans E/S. `Andro.Application` : orchestration (`SetupService`) contre des interfaces. `Andro.Infrastructure` : HTTP, zip, processus, détection des prérequis. La CLI `tools/Andro.DevCli` branche le tout. L'UI Avalonia, l'écran intégré, le cycle de vie, le glisser-déposer, F-Droid, les réglages, le MSI et l'entrée dans le launcher feront l'objet d'un **second plan**, écrit après le verdict du spike (Task 1).

**Tech Stack :** .NET 8 (`net8.0`, ImplicitUsings, Nullable), xUnit 2.5.3, NSubstitute 6.2.0, Microsoft.Extensions.DependencyInjection, `System.Xml.Linq`, `System.IO.Compression`. Spike : Avalonia 12.1.2, Grpc.Net.Client + Grpc.Tools.

**Spec :** `docs/superpowers/specs/2026-10-20-deve1ne-andro-design.md`

## Global Constraints

- Cible `net8.0`, Windows x64 uniquement (`[assembly: SupportedOSPlatform("windows")]` dans les projets exécutables).
- Licence GPL-3.0-or-later (fichier `LICENSE` copié depuis `C:\dev\git-ui\LICENSE`).
- **Aucun binaire ni image Google dans le dépôt ou le MSI**. Tout se télécharge depuis `https://dl.google.com/android/repository/`.
- **Pas de Java** : ni `sdkmanager` ni `avdmanager` dans le code livré (le spike seul peut s'en servir).
- Aucune licence Google acceptée sans action explicite de l'utilisateur. Rien n'est téléchargé avant cette acceptation.
- Données : `%LocalAppData%\Deve1ne\Andro\` avec `sdk\`, `avd\`, `downloads\`, `logs\`, `settings.json`. Rien n'est écrit dans `%USERPROFILE%\.android` (variable `ANDROID_USER_HOME` → `%LocalAppData%\Deve1ne\Andro\user`).
- Image : `google_apis_playstore`, `x86_64`, **avec traduction ARM**, au niveau d'API **figé** dans `PinnedComponents`. Émulateur à la révision **figée**. Jamais « la dernière ».
- Aucun droit admin, sauf l'activation unique de `HypervisorPlatform` (hors périmètre de ce plan : ici, on se contente de la détecter).
- Pas de télémétrie.
- Fin de chaque message de commit : `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Téléchargement interrompu** (coupure réseau, app fermée pendant 3 Go) → au relancement, la reprise part de l'octet atteint, et une installation partielle n'est jamais considérée comme terminée. Tests : Task 5 (`Resumes_from_partial_file`) et Task 8 (`Interrupted_component_is_not_marked_installed`).
2. **Somme de contrôle fausse** (miroir corrompu, proxy d'entreprise qui réécrit) → fichier supprimé, erreur claire, aucune extraction. Test : Task 5 (`Checksum_mismatch_deletes_file_and_throws`).
3. **Nom d'utilisateur Windows avec espace ou accent** (`C:\Users\Jérôme Dupont`) → l'AVD et l'émulateur fonctionnent. Tests : Task 4 (`Config_paths_survive_spaces_and_accents`), Task 9 (vérification manuelle avec un `--data-root` contenant `é` et un espace).
4. **Archive malveillante ou mal formée** (entrée `..\..\x`) → refusée, rien n'est écrit hors de la cible. Test : Task 6 (`Rejects_entries_escaping_target`).
5. **Licence refusée, ou nouvelle licence lors d'une version figée plus récente** → rien n'est téléchargé, et la question est reposée. Tests : Task 8 (`Nothing_downloaded_without_acceptance`, `New_license_requires_new_acceptance`).

---

### Task 1 : Spike d'affichage gRPC (jetable, **porte de décision**)

Pas de TDD : c'est un spike. Le code va dans `spikes/DisplaySpike/` et ne sera jamais réutilisé tel quel.

**Files :**
- Create : `spikes/DisplaySpike/DisplaySpike.csproj`, `spikes/DisplaySpike/Program.cs`, `spikes/DisplaySpike/MainWindow.axaml(.cs)`, `spikes/DisplaySpike/Protos/emulator_controller.proto` (copié depuis l'émulateur installé)
- Create : `docs/superpowers/spikes/2026-10-20-display-spike.md` (le rapport)

**Interfaces :**
- Produces : le rapport, qui fixe (a) la **révision de l'émulateur** testée, (b) le **niveau d'API** de l'image retenue, (c) le **mécanisme d'authentification gRPC** observé (jeton via `-grpc-use-token` lu dans le fichier de découverte `running\pid_<pid>.ini`, ou JWT), (d) le **transport retenu** (flux d'images simple ou mémoire partagée), (e) les **mesures**, et (f) si une app ARM-only s'installe et tourne. Les Tasks 3 et 9 recopient (a), (b) et (c).

- [ ] **Step 1 : installer un environnement à la main**, dans `C:\AndroSpike\sdk` (sdkmanager + JDK autorisés ici seulement) : `platform-tools`, `emulator`, et l'image `google_apis_playstore;x86_64` la plus récente dont le paquet référence la licence `android-sdk-arm-dbt-license`. Créer un AVD Pixel (1080×2400, 420 dpi, 4 Go de RAM, 4 cœurs).
- [ ] **Step 2 : lancer** `emulator -avd Spike -no-window -grpc 8554 -grpc-use-token -gpu host -no-boot-anim`. Noter où le jeton apparaît.
- [ ] **Step 3 : coder la fenêtre Avalonia**, qui s'abonne à `streamScreenshot` (format RGBA8888, largeur cible 540 px), dessine dans un `WriteableBitmap`, et renvoie appui/relâchement/déplacement de la souris via `sendTouch` (en convertissant les coordonnées vers la résolution de l'appareil) et les touches via `sendKey`. Ajouter un compteur d'images par seconde dans le titre.
- [ ] **Step 4 : mesurer.** Images/s moyennes pendant 20 s de défilement dans Paramètres. Latence clic→pixel : filmer à 240 im/s ou utiliser l'horodatage d'image du flux, sur 10 essais, et prendre la médiane. CPU d'`DisplaySpike.exe` dans le Gestionnaire des tâches.
- [ ] **Step 5 : si < 30 im/s ou > 100 ms**, refaire le Step 4 avec le transport mémoire partagée (`ImageTransport` MMAP du `.proto`).
- [ ] **Step 6 : tester ARM.** Se connecter au Play Store sur l'AVD, puis installer et ouvrir une app native ARM-only (par exemple une app dont l'APK ne contient que `lib/arm64-v8a`). Noter le résultat.
- [ ] **Step 7 : écrire le rapport** `docs/superpowers/spikes/2026-10-20-display-spike.md` avec les points (a) à (f), et le verdict **GO gRPC** / **GO mémoire partagée** / **STOP**.
- [ ] **Step 8 : PORTE.** Présenter le rapport à l'utilisateur. **STOP → on s'arrête là**, et on revient à la conception (spec §5.2). GO → on continue. Les Tasks 2 à 9 ne dépendent pas du transport, mais elles dépendent de (a), (b) et (c).
- [ ] **Step 9 : commit** du rapport et du dossier `spikes/` (après la Task 2, qui initialise le dépôt, ou en initialisant le dépôt ici avec le `.gitignore` de la Task 2).

---

### Task 2 : Solution + parseur du manifeste SDK

**Files :**
- Create : `.gitignore` (copié depuis `C:\dev\git-ui\.gitignore`), `LICENSE`, `README.md` (3 lignes : quoi, licence, statut), `Andro.sln`
- Create : `src/Andro.Domain/Andro.Domain.csproj` (même contenu que `GitUi.Domain.csproj`), `src/Andro.Application/…csproj` (référence Domain), `src/Andro.Infrastructure/…csproj` (référence Application + Domain, avec `Microsoft.Extensions.DependencyInjection` 10.0.12)
- Create : `tests/Andro.Domain.Tests/`, `tests/Andro.Application.Tests/`, `tests/Andro.Infrastructure.Tests/` (même csproj que `GitUi.Infrastructure.Tests.csproj`, avec la référence adaptée)
- Create : `src/Andro.Domain/Sdk/SdkManifest.cs`, `src/Andro.Domain/Sdk/SdkManifestParser.cs`
- Test : `tests/Andro.Domain.Tests/Sdk/SdkManifestParserTests.cs`, avec des fixtures XML minimales écrites à la main dans `tests/Andro.Domain.Tests/Fixtures/`

**Interfaces :**
- Produces :
  - `record SdkLicense(string Id, string Text)`
  - `record SdkArchive(Uri Url, long Size, string ChecksumType, string Checksum, string? HostOs)` (`ChecksumType` vaut `"sha256"` ou `"sha1"`, `Url` est absolue)
  - `record SdkRevision(int Major, int Minor, int Micro) : IComparable<SdkRevision>`, avec `Parse("35.1.4")`
  - `record SdkPackage(string Path, SdkRevision Revision, string Channel, IReadOnlyList<string> LicenseRefs, IReadOnlyList<SdkArchive> Archives)`
  - `record SdkManifest(IReadOnlyList<SdkPackage> Packages, IReadOnlyDictionary<string, SdkLicense> Licenses)`
  - `static SdkManifest SdkManifestParser.Parse(XDocument xml, Uri manifestUrl)` : les URL relatives sont résolues par rapport au dossier de `manifestUrl`. Le canal vient de `channelRef/@ref` (`channel-0` = stable).

- [ ] **Step 1 : `git init`**, créer la solution et les 6 projets, `dotnet build` → succès.
- [ ] **Step 2 : écrire les tests qui échouent**
  - `Parses_package_path_revision_channel_and_licenses` : fixture `repository-min.xml`, avec un paquet `emulator` 35.1.4 sur `channel-0` et `uses-license ref="android-sdk-license"` → toutes les valeurs sont retrouvées.
  - `Resolves_relative_archive_url_against_manifest_folder` : manifeste `https://dl.google.com/android/repository/sys-img/google_apis_playstore/sys-img2-3.xml` et URL `x86_64-34_r14.zip` → `https://dl.google.com/android/repository/sys-img/google_apis_playstore/x86_64-34_r14.zip`.
  - `Prefers_sha256_when_both_checksums_present`.
  - `Keeps_archives_per_host_os` : 3 archives (`windows`, `linux`, sans host-os) → 3 entrées, `HostOs` respectivement `"windows"`, `"linux"`, `null`.
  - `Reads_license_text_verbatim`.
- [ ] **Step 3 : lancer** `dotnet test tests/Andro.Domain.Tests` → FAIL (types absents).
- [ ] **Step 4 : implémenter** `SdkManifestParser.Parse` avec `XDocument`, en comparant les noms locaux (ignorer les espaces de noms `sdk:`, `common:`… qui changent selon la version du manifeste).
- [ ] **Step 5 : relancer** les tests → PASS.
- [ ] **Step 6 : commit** `feat: solution skeleton and SDK manifest parser`.

---

### Task 3 : Sélection des composants figés

**Files :**
- Create : `src/Andro.Domain/Sdk/PinnedComponents.cs`, `src/Andro.Domain/Sdk/ComponentSelector.cs`
- Test : `tests/Andro.Domain.Tests/Sdk/ComponentSelectorTests.cs`

**Interfaces :**
- Consumes : `SdkManifest`, `SdkPackage`, `SdkArchive`, `SdkRevision` (Task 2).
- Produces :
  - `static class PinnedComponents { EmulatorRevision; ApiLevel; const string ArmTranslationLicense = "android-sdk-arm-dbt-license"; }`. Valeurs recopiées du rapport de spike, points (a) et (b).
  - `record SetupComponent(string PackagePath, string InstallSubdirectory, SdkArchive Archive, IReadOnlyList<SdkLicense> Licenses)`
  - `static IReadOnlyList<SetupComponent> ComponentSelector.Select(SdkManifest repository, SdkManifest playStoreImages)`, qui renvoie dans l'ordre `platform-tools`, `emulator`, puis l'image système.
  - `class ComponentSelectionException(string message) : Exception`

Règles : `platform-tools` = dernière révision `channel-0` ; `emulator` = exactement `PinnedComponents.EmulatorRevision` ; image = `system-images;android-{ApiLevel};google_apis_playstore;x86_64`, dernière révision, qui **doit** référencer `ArmTranslationLicense`. Archive : `HostOs == "windows"` (sinon `null`). `InstallSubdirectory` = chemin du paquet avec `;` remplacés par `\` (`system-images\android-34\google_apis_playstore\x86_64`).

- [ ] **Step 1 : écrire les tests qui échouent**
  - `Selects_pinned_emulator_not_latest` : émulateur 35.1.4 (figé) et 36.0.0 présents → c'est 35.1.4 qui est retenu.
  - `Selects_latest_stable_platform_tools` : 35.0.1 (`channel-0`) et 36.0.0-rc (`channel-1`) → 35.0.1.
  - `Rejects_image_without_arm_translation` : image sans la licence arm-dbt → `ComponentSelectionException` dont le message contient `"traduction ARM"`.
  - `Missing_pinned_emulator_throws`.
  - `Picks_windows_archive_and_maps_install_subdirectory`.
  - `Collects_all_referenced_licenses` : l'image référence `android-sdk-license` + arm-dbt → les deux textes sont présents.
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `ComponentSelector.Select`.
- [ ] **Step 4 : relancer** les tests → PASS.
- [ ] **Step 5 : commit** `feat: pinned component selection with ARM translation check`.

---

### Task 4 : Génération de l'AVD

**Files :**
- Create : `src/Andro.Domain/Avd/AvdDefinition.cs`, `src/Andro.Domain/Avd/AvdFiles.cs`
- Test : `tests/Andro.Domain.Tests/Avd/AvdFilesTests.cs`

**Interfaces :**
- Produces :
  - `record AvdDefinition(string Name, int ApiLevel, string SystemImageSubdirectory, int RamMb, int Cores, int Width, int Height, int Density, string DataPartitionSize)`, avec `static AvdDefinition Default(int apiLevel, string sysImgSubdir)` → `"Andro"`, 4096 Mo, 4 cœurs, 1080×2400, 420 dpi, `"8G"`.
  - `static IReadOnlyDictionary<string, string> AvdFiles.Render(AvdDefinition avd, string avdHome)`, avec des clés relatives à `avdHome` (`"Andro.ini"`, `"Andro.avd\\config.ini"`) et le contenu en valeur. Le fichier est écrit en UTF-8 sans BOM, par la Task 8.

Contenu de `Andro.ini` : `avd.ini.encoding=UTF-8`, `path=<avdHome>\Andro.avd`, `path.rel=avd\Andro.avd`, `target=android-<api>`.
Contenu de `config.ini` (une clé par ligne, `clé=valeur`) : `AvdId`, `avd.ini.displayname`, `avd.ini.encoding=UTF-8`, `PlayStore.enabled=true`, `abi.type=x86_64`, `hw.cpu.arch=x86_64`, `hw.cpu.ncore`, `hw.ramSize`, `hw.lcd.width`, `hw.lcd.height`, `hw.lcd.density`, `hw.keyboard=yes`, `hw.gpu.enabled=yes`, `hw.gpu.mode=auto`, `image.sysdir.1=<SystemImageSubdirectory>\`, `tag.id=google_apis_playstore`, `tag.display=Google Play`, `disk.dataPartition.size`, `fastboot.forceColdBoot=no`, `showDeviceFrame=no`.

- [ ] **Step 1 : écrire les tests qui échouent**
  - `Renders_ini_pointing_to_avd_folder`.
  - `Config_enables_play_store_and_x86_64`.
  - `Config_uses_relative_sysdir_with_trailing_backslash` → `image.sysdir.1=system-images\android-34\google_apis_playstore\x86_64\`.
  - `Config_paths_survive_spaces_and_accents` : `avdHome = @"C:\Users\Jérôme Dupont\AppData\Local\Deve1ne\Andro\avd"` → `path=` contient ce chemin exact, sans guillemets ni échappement.
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `AvdFiles.Render`.
- [ ] **Step 4 : relancer** les tests → PASS.
- [ ] **Step 5 : commit** `feat: render AVD ini and config.ini`.

---

### Task 5 : Chemins + téléchargeur vérifié avec reprise

**Files :**
- Create : `src/Andro.Application/Abstractions/IDownloader.cs`, `src/Andro.Infrastructure/Paths/AndroPaths.cs`, `src/Andro.Infrastructure/Downloads/HttpDownloader.cs`
- Test : `tests/Andro.Infrastructure.Tests/Downloads/HttpDownloaderTests.cs`, avec un faux `HttpMessageHandler` qui sert un tableau d'octets et gère `Range`

**Interfaces :**
- Consumes : `SdkArchive` (Task 2).
- Produces :
  - `record DownloadProgress(long Received, long Total)`
  - `interface IDownloader { Task DownloadAsync(SdkArchive archive, string destinationFile, IProgress<DownloadProgress>? progress, CancellationToken ct); }`
  - `class ChecksumMismatchException : Exception`
  - `record AndroPaths(string Root)`, avec les propriétés `Sdk`, `AvdHome`, `UserHome`, `Downloads`, `Logs`, `SettingsFile`, et `static AndroPaths Default()` → `%LocalAppData%\Deve1ne\Andro`.
  - `HttpDownloader(HttpClient http) : IDownloader`

Comportement : écrire dans `<destinationFile>.part`. Si ce fichier existe, envoyer `Range: bytes=<taille>-` ; si le serveur répond 200 au lieu de 206, repartir de zéro. Le hachage (SHA-256 ou SHA-1 selon `ChecksumType`) porte sur le fichier complet, une fois terminé. S'il correspond : renommer en `destinationFile`. Sinon : supprimer le `.part` et lever `ChecksumMismatchException`.

- [ ] **Step 1 : écrire les tests qui échouent**
  - `Downloads_and_verifies_sha256`.
  - `Verifies_sha1_when_only_sha1_given`.
  - `Checksum_mismatch_deletes_file_and_throws` : ni `.part` ni fichier final ne restent.
  - `Resumes_from_partial_file` : un `.part` contient déjà les 40 premiers octets → la requête porte `Range: bytes=40-`, et le résultat est le fichier complet et valide.
  - `Restarts_when_server_ignores_range` : 200 renvoyé → le fichier final est correct (pas de duplication).
  - `Reports_progress_up_to_total`.
  - `Cancellation_keeps_part_file` : après `OperationCanceledException`, le `.part` existe toujours.
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `AndroPaths` et `HttpDownloader`.
- [ ] **Step 4 : relancer** les tests → PASS.
- [ ] **Step 5 : commit** `feat: resumable, checksum-verified downloader`.

---

### Task 6 : Extraction des archives

**Files :**
- Create : `src/Andro.Application/Abstractions/IArchiveExtractor.cs`, `src/Andro.Infrastructure/Archives/ZipArchiveExtractor.cs`
- Test : `tests/Andro.Infrastructure.Tests/Archives/ZipArchiveExtractorTests.cs` (zips construits en mémoire dans le test)

**Interfaces :**
- Produces : `interface IArchiveExtractor { Task ExtractAsync(string zipFile, string targetDirectory, CancellationToken ct); }`

Comportement : les zips Google contiennent un unique dossier racine (`emulator/`, `platform-tools/`, `x86_64/`). Ce dossier est retiré : son **contenu** va dans `targetDirectory`. On extrait d'abord dans `<targetDirectory>.tmp`, puis on remplace `targetDirectory` de façon atomique (en supprimant l'ancien dossier s'il existe). Toute entrée dont le chemin résolu sort de la cible fait lever `InvalidDataException`.

- [ ] **Step 1 : écrire les tests qui échouent**
  - `Strips_single_top_level_folder` : `emulator/emulator.exe` → `<target>\emulator.exe`.
  - `Keeps_layout_when_no_single_root`.
  - `Rejects_entries_escaping_target` : une entrée `../evil.txt` → exception, aucun fichier `evil.txt` hors de la cible, aucune cible créée.
  - `Replaces_existing_target_atomically` : l'ancienne cible contient `old.txt` → il n'y est plus après l'extraction.
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `ZipArchiveExtractor`.
- [ ] **Step 4 : relancer** les tests → PASS.
- [ ] **Step 5 : commit** `feat: zip extraction with root stripping and zip-slip guard`.

---

### Task 7 : Détection des prérequis

**Files :**
- Create : `src/Andro.Domain/Prerequisites/PrerequisiteReport.cs`, `src/Andro.Application/Abstractions/IProcessRunner.cs` (même signature que celui de Git UI, cité ci-dessous), `src/Andro.Application/Abstractions/IPrerequisiteChecker.cs`, `src/Andro.Infrastructure/Processes/ProcessRunner.cs`, `src/Andro.Infrastructure/Prerequisites/PrerequisiteChecker.cs`
- Test : `tests/Andro.Infrastructure.Tests/Prerequisites/PrerequisiteCheckerTests.cs` (avec `IProcessRunner` substitué via NSubstitute)

**Interfaces :**
- Produces :
  - `interface IProcessRunner { Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default); }`, avec `record ProcessResult(int ExitCode, string StandardOutput, string StandardError)`.
  - `enum Acceleration { Usable, HypervisorPlatformDisabled, VirtualizationDisabledInBios, Unknown }`
  - `record PrerequisiteReport(Acceleration Acceleration, string AccelerationDetail, long FreeDiskBytes, long TotalRamBytes)`, avec `bool DiskOk => FreeDiskBytes >= 15L * 1024 * 1024 * 1024` et `bool RamOk => TotalRamBytes >= 8L * 1024 * 1024 * 1024`.
  - `interface IPrerequisiteChecker { Task<PrerequisiteReport> CheckAsync(AndroPaths paths, CancellationToken ct); }`

Comportement : si l'émulateur n'est pas encore installé, l'accélération vaut `Unknown` (elle sera revérifiée après l'installation). Sinon, lancer `<sdk>\emulator\emulator.exe -accel-check` : un code de sortie 0 donne `Usable`. Si la sortie contient `WHPX` et « not installed », « disabled » ou « not enabled » → `HypervisorPlatformDisabled`. Si elle contient « VT-x », « SVM » ou « virtualization » et « disabled » → `VirtualizationDisabledInBios`. Sinon → `Unknown`. `AccelerationDetail` = la sortie brute. Disque : `DriveInfo` du lecteur de `paths.Root`. RAM : `GlobalMemoryStatusEx` (P/Invoke).

- [ ] **Step 1 : écrire les tests qui échouent**, un par branche : `Exit_zero_is_usable`, `Whpx_not_installed_maps_to_hypervisor_platform_disabled`, `Vtx_disabled_maps_to_bios`, `Missing_emulator_is_unknown`, `Unrecognised_output_is_unknown_with_raw_detail`.
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `ProcessRunner` (`ArgumentList`, lecture asynchrone de stdout et stderr, kill à l'expiration du délai) et `PrerequisiteChecker`.
- [ ] **Step 4 : relancer** les tests → PASS.
- [ ] **Step 5 : vérifier à la main** : sur ce PC, une fois la Task 9 faite, `andro-dev check` affiche `Usable`.
- [ ] **Step 6 : commit** `feat: prerequisite detection (acceleration, disk, RAM)`.

---

### Task 8 : `SetupService` : orchestration, licences, reprise

**Files :**
- Create : `src/Andro.Application/Setup/SetupService.cs`, `src/Andro.Application/Setup/SetupState.cs`, `src/Andro.Application/Abstractions/IManifestSource.cs`, `src/Andro.Application/Abstractions/ISetupStore.cs`, `src/Andro.Infrastructure/Setup/HttpManifestSource.cs`, `src/Andro.Infrastructure/Setup/JsonSetupStore.cs`, `src/Andro.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test : `tests/Andro.Application.Tests/Setup/SetupServiceTests.cs`

**Interfaces :**
- Consumes : `ComponentSelector.Select` (Task 3), `AvdDefinition` et `AvdFiles.Render` (Task 4), `IDownloader` et `AndroPaths` (Task 5), `IArchiveExtractor` (Task 6).
- Produces :
  - `interface IManifestSource { Task<(SdkManifest Repository, SdkManifest PlayStoreImages)> FetchAsync(CancellationToken ct); }`. L'implémentation HTTP lit `https://dl.google.com/android/repository/repository2-3.xml` et `https://dl.google.com/android/repository/sys-img/google_apis_playstore/sys-img2-3.xml`.
  - `record SetupState(IReadOnlyDictionary<string, string> AcceptedLicenseHashes, IReadOnlyDictionary<string, string> InstalledComponents, bool AvdCreated)`. Clés de `InstalledComponents` : `PackagePath` ; valeurs : la révision. Les licences acceptées sont stockées par id, avec le SHA-256 de leur texte.
  - `interface ISetupStore { Task<SetupState> LoadAsync(); Task SaveAsync(SetupState state); }`. Implémentation : `<Root>\setup-state.json`.
  - `record SetupPlan(IReadOnlyList<SetupComponent> Pending, IReadOnlyList<SdkLicense> LicensesToAccept, long DownloadBytes)`
  - `class SetupService` :
    - `Task<SetupPlan> PlanAsync(CancellationToken ct)`
    - `Task AcceptLicensesAsync(IReadOnlyList<SdkLicense> licenses)`
    - `Task RunAsync(SetupPlan plan, IProgress<SetupProgress>? progress, CancellationToken ct)`, avec `record SetupProgress(string PackagePath, string Phase, long Received, long Total)` (`Phase` ∈ `"download"`, `"extract"`, `"avd"`).
  - `class LicenseNotAcceptedException : Exception`
  - `IServiceCollection AddAndro(this IServiceCollection services, AndroPaths paths)`

Comportement : `PlanAsync` → composants dont la révision installée diffère, et licences dont l'id est absent **ou dont le hachage du texte diffère**. `RunAsync` lève `LicenseNotAcceptedException` **avant tout téléchargement** si une licence de `plan.LicensesToAccept` n'est pas acceptée. Pour chaque composant : téléchargement dans `Downloads\<nom de l'archive>`, extraction dans `Sdk\<InstallSubdirectory>`, puis **seulement ensuite** enregistrement dans `InstalledComponents` et sauvegarde de l'état, et suppression de l'archive. Après les composants : création de `Sdk\platforms\` (vide ; l'émulateur considère sinon le SDK invalide), écriture des fichiers de `AvdFiles.Render` sous `AvdHome` si `!AvdCreated`, puis `AvdCreated = true`.

- [ ] **Step 1 : écrire les tests qui échouent** (substituts pour toutes les abstractions)
  - `Plan_lists_all_components_and_licenses_on_first_run`.
  - `Plan_skips_installed_components_at_same_revision`.
  - `Nothing_downloaded_without_acceptance` : `RunAsync` sans acceptation → `LicenseNotAcceptedException`, et `IDownloader.DownloadAsync` jamais appelé.
  - `New_license_requires_new_acceptance` : même id, texte modifié → la licence revient dans `LicensesToAccept`.
  - `Interrupted_component_is_not_marked_installed` : l'extracteur lève une `IOException` (disque plein) → `InstalledComponents` ne contient pas ce paquet, et un nouveau `PlanAsync` le propose à nouveau.
  - `Creates_platforms_folder_and_avd_once` : deux `RunAsync` successifs → fichiers AVD écrits une seule fois.
  - `Reports_phases_in_order` : download → extract pour chaque composant, puis avd.
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `SetupService`, `HttpManifestSource`, `JsonSetupStore` (`System.Text.Json` ; écriture atomique via fichier temporaire puis `File.Move(overwrite: true)`) et `AddAndro`.
- [ ] **Step 4 : relancer** `dotnet test` (toute la solution) → PASS.
- [ ] **Step 5 : commit** `feat: setup orchestration with license gating and resumable state`.

---

### Task 9 : CLI de dev + lancement de l'émulateur (vérification de bout en bout)

**Files :**
- Create : `src/Andro.Application/Abstractions/IEmulatorLauncher.cs`, `src/Andro.Infrastructure/Emulator/EmulatorLauncher.cs`, `tools/Andro.DevCli/Andro.DevCli.csproj` (Exe, référence Infrastructure), `tools/Andro.DevCli/Program.cs`
- Test : `tests/Andro.Infrastructure.Tests/Emulator/EmulatorLauncherTests.cs`

**Interfaces :**
- Consumes : `AddAndro`, `SetupService` (Task 8), `IPrerequisiteChecker` (Task 7), `AndroPaths` (Task 5).
- Produces (pour le second plan) :
  - `record EmulatorSession(int ProcessId, int GrpcPort, string GrpcToken)`
  - `interface IEmulatorLauncher { Task<EmulatorSession> StartAsync(string avdName, CancellationToken ct); }`
  - `static ProcessStartInfo EmulatorLauncher.BuildStartInfo(AndroPaths paths, string avdName, int grpcPort)` (public pour les tests)

`BuildStartInfo` : `FileName = <Sdk>\emulator\emulator.exe`, arguments `-avd <name> -no-window -grpc <port> -grpc-use-token -gpu host -no-boot-anim` (adapter `-grpc-use-token` selon le point (c) du rapport de spike), variables d'environnement `ANDROID_SDK_ROOT` = `ANDROID_HOME` = `Sdk`, `ANDROID_AVD_HOME` = `AvdHome`, `ANDROID_USER_HOME` = `UserHome`. `StartAsync` choisit un port TCP libre, démarre le processus, attend le fichier de découverte et y lit le jeton (point (c)), puis attend que `adb -s emulator-<consolePort> shell getprop sys.boot_completed` renvoie `1` (délai max : 180 s).

- [ ] **Step 1 : écrire les tests qui échouent**
  - `Start_info_sets_sdk_avd_and_user_home_env` : les 4 variables pointent vers `AndroPaths`, et aucune vers `%USERPROFILE%\.android`.
  - `Start_info_runs_headless_with_grpc_port`.
  - `Start_info_handles_paths_with_spaces` : `Root = @"C:\Users\Jérôme Dupont\…"` → `FileName` exact, arguments passés via `ArgumentList` (pas de concaténation de chaîne).
- [ ] **Step 2 : lancer** les tests → FAIL.
- [ ] **Step 3 : implémenter** `EmulatorLauncher` et la CLI, avec les commandes `check`, `setup` et `run`, et l'option `--data-root <dir>` (par défaut `AndroPaths.Default()`). `setup` affiche chaque licence en entier et demande de taper `oui` avant d'appeler `AcceptLicensesAsync`. `run` affiche le port et le jeton, puis attend `Ctrl+C`, qui envoie `adb emu kill`.
- [ ] **Step 4 : relancer** les tests → PASS.
- [ ] **Step 5 : vérifier de bout en bout à la main.** `dotnet run --project tools/Andro.DevCli -- setup --data-root "C:\Temp\Andro Jérôme"` → licences affichées, `oui` → téléchargement avec progression. Couper le Wi-Fi au milieu, relancer → la reprise part de l'octet atteint. Puis `run` → `sys.boot_completed=1` en moins de 180 s, et `adb -s emulator-5554 shell pm list packages | findstr vending` montre le Play Store. Aucun fichier créé dans `%USERPROFILE%\.android`. Consigner les résultats dans `docs/superpowers/spikes/2026-10-20-display-spike.md`, section « Validation environnement ».
- [ ] **Step 6 : commit** `feat: dev CLI and headless emulator launcher`.

---

## Après ce plan

Rédiger le **plan 2** (app Avalonia : assistant de premier lancement, écran intégré selon le verdict du spike, barre d'outils, cycle de vie avec la règle des ~10 s, glisser-déposer, APK/fichiers, F-Droid, réglages et « Supprimer toutes les données Android », message Play Integrity, MSI WiX, release GitHub avec `SHA256SUMS.txt`, entrée `SuiteCatalog` du launcher).
