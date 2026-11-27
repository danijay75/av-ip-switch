# AV IP-Switch

Application de changement rapide d’adresse IP pour **Windows** et **macOS**.

Version Windows actuelle : **1.4** (macOS publié : 1.1.3)

Site officiel : <https://angelivisions.com/fr/av-ip-switch>

## Fonctionnalités

- Cartes réseau Ethernet, Wi-Fi et interfaces virtuelles.
- Passage DHCP / adresse fixe.
- Validation IPv4 : format, masque contigu, passerelle dans le sous-réseau.
- Passerelle proposée automatiquement depuis l’adresse et le masque.
- Activation et désactivation des cartes.
- Test de connectivité et onglet de ping.
- Onglet **Scan réseaux** : sous-réseaux locaux et plages personnalisées, découverte ARP/ICMP, noms NetBIOS/DNS, tableau et détails.
- Bouton **Créer un raccourci** : copie l’application dans `Documents` et crée ou met à jour le raccourci sur le Bureau.
- Interface responsive et thèmes clair/sombre.
- Interface en **français, anglais, allemand et espagnol**, changeable sans redémarrage.
- Acceptation en un clic des conditions d’utilisation et de la notice RGPD au lancement (mémorisée après acceptation).
- Fermeture avec rappel de don et QR code PayPal.

## Structure

```text
src/                  Application Windows (C# / WinForms)
tests/                Tests Windows
tools/                Générateur d’icône
assets/               Icône, QR code et drapeaux
mac/                  Portage macOS (SwiftUI / Swift Package)
build.ps1             Compilation Windows
```

Les dossiers `dist/` et `mac/dist/` contiennent des binaires générés et ne sont
pas versionnés. Les versions publiées sont disponibles depuis la page officielle.

## Compiler sous Windows

Prérequis : Windows et le compilateur C# du .NET Framework (`csc.exe`).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Le binaire est produit dans :

```text
dist/AV IP-Switch v1.4.exe
```

## Signature et paquet Windows

La version 1.4 est encore non signée : SmartScreen peut afficher un
avertissement. Le pipeline de signature est prêt dans `sign.ps1` et
`package-windows.ps1`.

Signature Azure Artifact Signing (recommandée) :

```powershell
.\build.ps1 -Sign -SignMode Azure `
  -AzureDlib "C:\outils\Azure.CodeSigning.Dlib.dll" `
  -AzureMetadata ".\packaging\windows\artifact-signing-metadata.json" `
  -Package
```

Le paquet `dist/av-ip-switch-1.4-windows.zip` contient l'exécutable,
le README quadrilingue, la licence et les empreintes SHA-256. Le modèle
de README est choisi automatiquement selon la signature : version
« non signée » ou version « signée Angeli Visions ». Les détails de
mise en place et la procédure de remplacement sont dans
[`docs/SIGNATURE-WINDOWS.md`](docs/SIGNATURE-WINDOWS.md).

## Tests Windows

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\run.ps1
```

## macOS

Le portage macOS se compile exclusivement sur un Mac avec Xcode ou les
Command Line Tools. Consulter :

- `mac/README.md`
- `mac/ETAT-DU-PROJET-MACOS.md`

## Site web

La page <https://angelivisions.com/fr/av-ip-switch> (et ses déclinaisons `/en/av-ip-switch`, `/de/av-ip-switch`, `/es/av-ip-switch`) est servie depuis le dépôt
`danijay75/angelivisions` (Next.js).

### Déploiement depuis Windows

```powershell
cd "C:\Users\danij\Projets IA\angeli-visions-website"
git fetch origin
git reset --hard origin/main
.\.agent\deploy.ps1
```

## Soutenir le projet

<https://www.paypal.com/donate/?hosted_button_id=WZBCQXPEHHDDC>

## Licence

Distribué sous licence **MIT**. Voir [`LICENSE`](LICENSE).

---

© 2026 Angeli Visions.
