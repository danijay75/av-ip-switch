# Passation macOS — AV IP-Switch v1.2.2

Document de passation destiné à l'instance Antigravity sur **le Mac dédié** (le dossier `mac/` n'est jamais modifié depuis Windows, conformément à `AGENTS.md`).

> [!IMPORTANT]
> **Règle de périmètre** : depuis Windows, la version Windows **v1.2.2**, le dépôt GitHub, la release Windows `v1.2.2` et le site web ont déjà été mis à jour. Ce document récapitule exactement ce qui a été fait côté Windows/Web et ce qu'il reste à implémenter, tester, signer/packager et publier côté **macOS**.

---

## Prompt prêt à coller dans Antigravity sur le Mac

```text
Mets d'abord le dépôt Git à jour (`git remote set-url origin https://github.com/danijay75/av-ip-switch.git && git fetch origin && git pull --rebase origin main`), puis lis intégralement `docs/PASSATION-MACOS-1.2.2.md` et implémente toutes les étapes macOS pour AV IP-Switch v1.2.2, en mettant également à jour `mac/PLAN-ACTION-MACOS.md`, `mac/ETAT-DU-PROJET-MACOS.md`, `mac/README.md` et les tests Swift, avant de lancer `bash mac/scripts/test.sh` et `bash mac/scripts/build-mac.sh --universal`.
```

---

## 1. État actuel du projet (après Windows v1.2.2)

### Dépôt GitHub et artefacts Windows
- **Dépôt GitHub renommé** : `https://github.com/danijay75/av-ip-switch.git` (anciennement `danijay75/easy-ip-switch`).
- **Branche `main`** : à jour jusqu'au commit Windows `2edbdcc` (`feat(windows): masquer le bouton Creer un raccourci lorsqu'il est deja installe`).
- **Release GitHub publiée** : [`v1.2.2`](https://github.com/danijay75/av-ip-switch/releases/tag/v1.2.2) (actifs Windows uniquement) :
  - `av-ip-switch-1.2.2-windows.exe` (335 360 octets, SHA-256 `A4CF2EA18C282C72E56427EDDB28BA3C0C1F20418808D59172959D16E0CBD4B9`)
  - `av-ip-switch-1.2.2-windows.zip` (138 264 octets, SHA-256 `1EE313EBBAB1CDA487B280A8A6C6DFC0CA08B79CD8E2EB19143C1B42C5B02754`)
  - `SHA256SUMS.txt`
- **Tests Windows vérifiés** : `powershell -ExecutionPolicy Bypass -File .\tests\run.ps1` -> `AV IP-Switch tests passed (461 assertions).`

### Site web (`danijay75/angelivisions`)
- **Commit déployé en production** : `83d2792` sur `main`.
- **Pages localisées en ligne (HTTP 200)** :
  - `https://angelivisions.com/fr/av-ip-switch`
  - `https://angelivisions.com/en/av-ip-switch`
  - `https://angelivisions.com/de/av-ip-switch`
  - `https://angelivisions.com/es/av-ip-switch`
  - Redirections 301/308 configurées depuis `/easy-ip-switch*` et `/av-ip-switch*`, sitemap soumis à Google Search Console (`sc-domain:angelivisions.com`) et IndexNow.
- **Versions affichées sur le site (`app/av-ip-switch-page/release.json`)** :
  - `windowsVersion`: `"1.2.2"` (téléchargements dans `public/downloads/av-ip-switch/`)
  - `macVersion`: `"1.1.3"` (anciens DMG macOS 1.1.3 conservés dans `public/downloads/easy-ip-switch/` tant que la version macOS 1.2.2 n'est pas compilée et publiée).

### Dossier `mac/`
- Aucune modification effectuée depuis Windows (`Easy IP-Switch` encore en version `1.1.3`).

---

## 2. Synchronisation Git à exécuter sur le Mac

Avant toute lecture ou édition sur le Mac, mettre à jour l'URL du remote (car le dépôt a été renommé) et récupérer `main` :

```bash
git remote set-url origin https://github.com/danijay75/av-ip-switch.git
git fetch origin
git pull --rebase origin main
```

---

## 3. Périmètre complet du travail macOS (v1.2.2)

### A. Renommage complet `Easy IP-Switch` -> `AV IP-Switch` et passage en `1.2.2`

Inspecter chaque fichier ci-dessous avant modification afin de mettre à jour toutes les références utilisateur, identifiants et métadonnées :

1. **`mac/Info.plist`** :
   - `CFBundleDisplayName` -> `AV IP-Switch`
   - `CFBundleName` -> `AV IP-Switch`
   - `CFBundleShortVersionString` -> `1.2.2`
   - `CFBundleVersion` -> `1.2.2`
   - `CFBundleIdentifier` -> évaluer le passage vers `com.angelivisions.avipswitch` (en tenant compte de la migration éventuelle des préférences utilisateur, voir point B)
   - `CFBundleExecutable` -> conserver `EasyIPSwitch` si `Package.swift` garde ce nom de cible, ou aligner sur `AVIPSwitch` si la cible SwiftPM est renommée.
   - `NSLocalNetworkUsageDescription` -> remplacer `Easy IP-Switch` par `AV IP-Switch`.
   - **Ajouter** `NSDesktopFolderUsageDescription` (requis pour TCC lors de l'accès à `~/Desktop` pour la création/vérification de l'alias sur le Bureau).

2. **`mac/Package.swift`** (optionnel si renommage technique du binaire/module) :
   - Actuellement package/exécutable/cible `EasyIPSwitch`, test `EasyIPSwitchTests`, `.macOS(.v12)`. Si la cible est renommée en `AVIPSwitch`, mettre à jour `Info.plist`, `scripts/build-mac.sh` et les imports dans `Tests/`. Sinon, conserver les noms de cible internes et mettre à jour uniquement le nom d'affichage/bundle `AV IP-Switch` (plus simple et sans risque de casser les imports).

3. **`mac/Sources/EasyIPSwitch/AppInfo.swift`** :
   - `productName = "AV IP-Switch"`
   - `version = "1.2.2"`
   - Ajouter un helper `websiteURL(for language: AppLanguage)` retournant :
     - `fr` -> `https://www.angelivisions.com/fr/av-ip-switch`
     - `en` -> `https://www.angelivisions.com/en/av-ip-switch`
     - `de` -> `https://www.angelivisions.com/de/av-ip-switch`
     - `es` -> `https://www.angelivisions.com/es/av-ip-switch`
   - Conserver `websiteURL` par défaut cohérent (ex. `https://www.angelivisions.com/fr/av-ip-switch` ou fallback selon usage) et `donationURL`.
   - S'inspirer de l'implémentation Windows dans `src/AppInfo.cs`.

4. **`mac/Sources/EasyIPSwitch/Views/ContentView.swift`** :
   - Dans `FooterView` (ligne ~283) : remplacer `Link("angelivisions.com", destination: AppInfo.websiteURL)` par `AppInfo.websiteURL(for: language)` afin que le lien du pied de page pointe vers `www.angelivisions.com/[lang]/av-ip-switch`.
   - Dans `TabBarView` (lignes ~120-167) : ajouter le bouton `Créer un raccourci` à gauche du bouton `Réglages Réseau` (détails en section 4).

5. **Chaînes UI et mentions `Easy IP-Switch` à passer en `AV IP-Switch`** :
   - `mac/Sources/EasyIPSwitch/EasyIPSwitchApp.swift` (lignes ~152, ~168)
   - `mac/Sources/EasyIPSwitch/QuitDialog.swift` (ligne ~101 : `"Soutenir AV IP-Switch"`)
   - `mac/Sources/EasyIPSwitch/TermsView.swift` (ligne ~16 : `"Veuillez lire et accepter les présentes conditions pour utiliser AV IP-Switch."`)
   - `mac/Sources/EasyIPSwitch/TermsContent.swift` (4 langues : `french`, `english`, `german`, `spanish`)
   - `mac/Sources/EasyIPSwitch/LocalizationCatalog.swift` (lignes ~15, 23, 32-34, 43, 100 + nouvelles clés pour le raccourci)
   - `mac/Sources/EasyIPSwitch/LocalizationExtras.swift` (ligne ~55 + nouvelles clés pour le raccourci)
   - `mac/Sources/EasyIPSwitch/NetworkReader.swift` (lignes ~12-13 : `"AV IP-Switch" as CFString`)

6. **`mac/scripts/build-mac.sh`** :
   - `APP_BASENAME="AV IP-Switch"`
   - Version par défaut -> aligner (ne plus laisser `'1.1.1'` en fallback)
   - Nommage DMG / AppleScript -> `/Applications/AV IP-Switch.app` et lien Desktop `"AV IP-Switch"`
   - Vérifier la cohérence des noms d'artefacts produits (`av-ip-switch-1.2.2-macos-...dmg`).

### B. Migration des préférences `UserDefaults` (sans perte pour les utilisateurs existants)
Clés actuellement présentes :
- `TermsConsent.swift` (`line 11`): `"EasyIPSwitch.termsAccepted"` -> `"AVIPSwitch.termsAccepted"`
- `AppTheme.swift` (`line 81`): `"EasyIPSwitch.isDark"` -> `"AVIPSwitch.isDark"`
- `Localization.swift` (`line 139`): `"EasyIPSwitch.language"` -> `"AVIPSwitch.language"`

Si ces clés sont migrées vers `AVIPSwitch.*`, implémenter une migration one-shot au démarrage (si la nouvelle clé `AVIPSwitch.*` n'existe pas encore, recopier l'ancienne valeur `EasyIPSwitch.*` puis écrire dans la nouvelle clé) pour que l'utilisateur n'ait pas à réaccepter les conditions ou reconfigurer sa langue/thème.

---

## 4. Spécification complète de la fonction « Créer un raccourci » (v1.2.2)

S'appuyer sur le comportement de référence Windows implémenté dans `src/AppShortcut.cs`, `src/MainForm.cs`, `src/Localization.cs` et `tests/AppShortcutTests.cs`, avec les adaptations validées pour macOS.

### Décisions produit validées avec l'utilisateur
1. **Emplacement UI** : bouton `Créer un raccourci` placé **à gauche** du bouton `Réglages Réseau` dans `TabBarView` (`ContentView.swift`, `.secondary`).
2. **Dossier de copie sur macOS** : **`/Applications/AV IP-Switch.app`** (nom fixe, sans numéro de version dans le nom du `.app`).
3. **Comportement en cas de copie/raccourci existant** :
   - Si `/Applications/AV IP-Switch.app` existe avec une version différente (ou si l'app courante est plus récente / différente), mettre à jour la copie dans `/Applications` sans écraser de fichiers tiers non liés.
   - Créer ou mettre à jour l'alias sur le Bureau sans créer de doublons (`AV IP-Switch 2`, etc.).
4. **Type de raccourci sur le Bureau** : **Alias Finder natif** (`~/Desktop/AV IP-Switch`) créé via `URL.bookmarkData(options: .suitableForBookmarkFile, ...)` puis `URL.writeBookmarkData(_:to:options:)`.
5. **Comportement après clic** : rester dans l'application ouverte et afficher un message de confirmation avec le(s) chemin(s) créé(s)/mis à jour dans la barre de statut (`StatusBarView`).
6. **Disparition automatique du bouton + règle fail-open** :
   - Le bouton `Créer un raccourci` doit **disparaître automatiquement** si `/Applications/AV IP-Switch.app` existe déjà à la version courante (`1.2.2`) **ET** qu'un alias/raccourci valide vers `/Applications/AV IP-Switch.app` existe déjà sur `~/Desktop`.
   - **Règle de repli (*fail-open*)** : si l'application n'a pas accès au Bureau/Applications ou ne peut pas vérifier (ex. erreur TCC/EPERM), **laisser le bouton affiché par défaut**.

### Contraintes techniques macOS et solutions recommandées

Lire d'abord `mac/Sources/EasyIPSwitch/AppModel.swift` en entier avant de concevoir l'état (`@Published var showShortcutButton: Bool`, action, messages de statut). Créer un module dédié `mac/Sources/EasyIPSwitch/AppShortcutInstaller.swift` avec protocoles/dépendances injectables pour les tests unitaires :

1. **Résolution du bundle source & App Translocation** :
   - Si l'app est lancée depuis un DMG ou un téléchargement non déplacé, macOS peut monter l'app en lecture seule via App Translocation (`/private/var/folders/.../AppTranslocation/...`).
   - Utiliser `Bundle.main.bundleURL` (en vérifiant si le bundle source correspond déjà à `/Applications/AV IP-Switch.app` via `standardizedFileURL.resolvingSymlinksInPath()`).
   - Copier le `.app` complet avec `/usr/bin/ditto` (préserve les permissions exécutables, symlinks internes, ressources et signature de code), contrairement à un simple `FileManager.copyItem` parfois capricieux sur les bundles signés.
   - Si `Bundle.main.bundleURL` pointe déjà sur `/Applications/AV IP-Switch.app` (même chemin standardisé), sauter la copie et créer/mettre à jour uniquement l'alias Bureau.

2. **Droits d'écriture dans `/Applications`** :
   - Si l'utilisateur courant est administrateur et que `/Applications` est accessible en écriture (cas standard), copier directement (via `ditto` dans un répertoire temporaire puis remplacement atomique ou `ditto` après suppression de l'ancienne copie `AV IP-Switch.app` uniquement).
   - Si l'utilisateur n'a pas la permission d'écrire dans `/Applications` (compte standard ou bundle appartenant à `root`), proposer un fallback `NSAppleScript` / `osascript -e 'do shell script "..." with administrator privileges'` pour exécuter le `ditto` avec élévation macOS standard. En cas d'annulation utilisateur, afficher un message d'état explicite sans planter.

3. **Protection TCC de `~/Desktop` (précaution critique au démarrage)** :
   - Sur macOS, lire le contenu de `~/Desktop` ou appeler `FileManager.default.fileExists(atPath:)` / `URL.resourceValues` sur `~/Desktop/AV IP-Switch` peut déclencher la boîte de dialogue système TCC *« AV IP-Switch souhaite accéder aux fichiers du dossier Bureau »*.
   - **Ne pas déclencher cette popup TCC de manière intempestive au premier lancement** si la copie `/Applications/AV IP-Switch.app` n'existe même pas encore ou n'est pas à la version courante (`1.2.2`) !
   - Ordre de vérification recommandé au démarrage (et au retour au premier plan) :
     1. Vérifier d'abord `/Applications/AV IP-Switch.app/Contents/Info.plist` (`CFBundleShortVersionString == AppInfo.version`). S'il est absent ou à une autre version -> retourner `false` (bouton **visible**) **sans toucher à `~/Desktop`**.
     2. Optionnellement, mémoriser un drapeau/cache léger ou vérifier `~/Desktop/AV IP-Switch` : si un accès à `~/Desktop` échoue pour permission (`NSFileReadNoPermissionError` / `EPERM` / TCC refusé) -> **fail-open** (`showShortcutButton = true`), ne jamais bloquer le démarrage.
     3. Vérifier que l'élément sur `~/Desktop/AV IP-Switch` (ou un alias valide détecté) est bien un alias/bookmark (ou symlink) qui résout vers `/Applications/AV IP-Switch.app` via `URL(resolvingAliasFileAt:)` / `URL(resolvingBookmarkData:...)`.
   - Déclarer `NSDesktopFolderUsageDescription` dans `mac/Info.plist` pour que la demande d'autorisation macOS soit claire si elle apparaît lors du clic ou de la vérification.
   - Remarque sécurité sur l'écrasement : ne jamais écraser un fichier/dossier utilisateur arbitraire sur le Bureau s'il ne s'agit pas d'un alias/symlink (ou demander/préserver et signaler clairement dans la barre de statut si un fichier non-raccourci portant exactement ce nom existe déjà).

4. **Localisation (FR / EN / DE / ES)** :
   Ajouter dans `LocalizationCatalog.swift` et `LocalizationExtras.swift` les traductions cohérentes avec `src/Localization.cs` (Windows), adaptées à macOS (`/Applications` au lieu de `Documents`) :
   - Bouton :
     - FR : `"Créer un raccourci"`
     - EN : `"Create shortcut"`
     - DE : `"Verknüpfung erstellen"`
     - ES : `"Crear acceso directo"`
   - Confirmation barre de statut (succès) :
     - FR : `"Copie installée dans /Applications et raccourci créé sur le Bureau."` (adapter si déjà dans `/Applications` -> raccourci créé/mis à jour)
     - EN : `"Copy installed in /Applications and shortcut created on the Desktop."`
     - DE : `"Kopie in /Applications installiert und Verknüpfung auf dem Schreibtisch erstellt."`
     - ES : `"Copia instalada en /Applications y acceso directo creado en el Escritorio."`
   - Erreurs explicites (annulation admin, erreur TCC Bureau, erreur copie).

---

## 5. Point d'attention optionnel : parité « Scan réseaux »

Avant de publier `1.2.2` sur macOS, vérifier si l'onglet **Scan réseaux** (Lot 1 : découverte locale sans agent) doit également être porté ou non sur macOS. Pour référence côté Windows :
- Spécification : `docs/SCAN-RESEAUX-2.0.1.md`
- Implémentation Windows : `src/ScanView.cs`, `src/NetworkScanner.cs`, `src/ScanRangeParser.cs`.

*(Dans l'immédiat, l'objectif prioritaire de cette passation est le renommage `AV IP-Switch`, le lien pied de page, et la fonction `Créer un raccourci` en `v1.2.2`.)*

---

## 6. Documentation, tests, build et publication après implémentation sur Mac

### 1. Documentation macOS à mettre à jour sur le Mac
- `mac/PLAN-ACTION-MACOS.md`
- `mac/ETAT-DU-PROJET-MACOS.md`
- `mac/README.md`

### 2. Tests Swift (`mac/Tests/EasyIPSwitchTests/`)
- Mettre à jour `AppInfoTests.swift` (`"AV IP-Switch"`, `"1.2.2"`, URLs localisées `www.angelivisions.com/[lang]/av-ip-switch`).
- Mettre à jour `TermsConsentTests.swift` (mention `"AV IP-Switch"` et clé migrée).
- Créer `AppShortcutInstallerTests.swift` couvrant :
  - copie/mise à jour dans un dossier Applications simulé (`TempDirectory`),
  - création et mise à jour d'alias sans doublon,
  - détection « déjà installé » -> bouton masqué quand copie `1.2.2` + alias valide sont présents,
  - cas version différente -> bouton affiché,
  - cas erreur permission / échec vérification -> **fail-open** (bouton affiché par défaut),
  - refus d'écraser un fichier utilisateur non-alias déjà présent.

### 3. Build et vérifications sur Mac
```bash
bash mac/scripts/test.sh
bash mac/scripts/build-mac.sh --universal
```
- Vérifier visuellement dans l'app lancée :
  - le titre/nom `AV IP-Switch` et la version `1.2.2`,
  - le bouton `Créer un raccourci` à gauche de `Réglages Réseau`,
  - le comportement après clic (copie `/Applications/AV IP-Switch.app` + alias `~/Desktop/AV IP-Switch` + message barre de statut + disparition du bouton),
  - le lien pied de page ouvrant `https://www.angelivisions.com/[lang]/av-ip-switch`.

### 4. Publication GitHub & Site web
1. Commiter et pousser les changements `mac/` sur `danijay75/av-ip-switch` (`main`).
2. Publier la release GitHub **`v1.2.2-macos`** (ou compléter `v1.2.2` selon la convention choisie) avec le `.dmg` Universal et `SHA256SUMS.txt`.
3. Mettre à jour le site `danijay75/angelivisions` :
   - copier les nouveaux `.dmg` dans `public/downloads/av-ip-switch/` et mettre à jour `SHA256SUMS.txt`,
   - passer `macVersion` à `"1.2.2"` dans `app/av-ip-switch-page/release.json` (et vérifier `av-ip-switch-landing.tsx`, `seo.ts`, `content.ts`),
   - redéployer le site en production (depuis Windows avec `.\.agent\deploy.ps1`).
