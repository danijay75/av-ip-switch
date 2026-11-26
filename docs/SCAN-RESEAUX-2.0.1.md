# Scan réseaux — AV IP-Switch 2.0.1

Document de conception de l'onglet **Scan réseaux** et de l'agent
AV IP-Switch. Il sert de référence pour l'implémentation Windows et pour
l'implémentation macOS, qui doit être réalisée sur le Mac dédié.

## 1. Objectif

Ajouter à AV IP-Switch un onglet **Scan réseaux** permettant de :

- découvrir les machines du réseau local (IP, nom, MAC, système) ;
- afficher l'état DHCP, le serveur DHCP et le pare-feu des machines
  équipées de l'agent ;
- changer l'adresse IP d'une machine distante, avec retour arrière
  automatique ;
- identifier physiquement une machine en affichant sur son écran une
  fenêtre plein écran, d'une couleur unie, avec son adresse IP.

## 2. Décisions validées

| Sujet | Décision |
|---|---|
| Onglet | **Scan réseaux** |
| Version | **2.0.1** |
| Langues | FR, EN, DE, ES |
| Signature | Non signé pour l'instant ; à prévoir plus tard |
| Agent | Un agent privilégié par machine, qui fait tout |
| Interface agent | Service/daemon + helper d'affichage lancé à la demande, **sans tray permanent** |
| Appairage | Approbation dans la session utilisateur, ou jeton à l'installation pour les machines sans session |
| Scan | Sous-réseaux locaux complets + plages personnalisées + scan local des agents |
| IPv6 | Plus tard ; IPv4 d'abord |
| Tableau | `Nom`, `Adresse IP`, `DHCP`, `Serveur DHCP`, `Pare-feu`, `Agent`, `Système`, `Dernière réponse` |
| MAC | Dans le panneau Détails, pas dans le tableau principal |
| DHCP | `Oui` / `Non` avec l'agent ; `—` sans agent |
| Serveur DHCP | Adresse du serveur DHCP, ou `—` |
| Pare-feu | `Oui` / `Non` avec l'agent ; `—` sans agent |
| Système | Windows, macOS, imprimante, NAS, inconnu |

## 3. Vue d'ensemble

```text
+-----------------------------+          TLS / TCP 47821          +------------------------------+
|  AV IP-Switch (console)   | <-------------------------------> |  Agent AV IP-Switch        |
|  Onglet Scan réseaux        |                                 |  Service / daemon privilégié |
|  Windows ou macOS           |          UDP 47820 (découverte)  |  + helper d'affichage        |
+-----------------------------+ <-------------------------------> +------------------------------+
                                                                  |
                                                                  |  ARP / ICMP / inventaire
                                                                  v
                                                          Réseau local de la machine
```

L'application est la console : elle scanne, affiche et envoie des commandes.
L'agent est l'exécuteur local : il connaît sa machine, peut changer son IP,
afficher une fenêtre dans la session de l'utilisateur et scanner son propre
sous-réseau.

L'agent supprime le besoin de WinRM, WMI distant, SSH, SMB ou d'un compte
administrateur sur chaque cible au quotidien. Un seul port TCP est ouvert,
et uniquement vers le réseau local.

## 4. L'onglet Scan réseaux

### 4.1 Barre supérieure

- Sélecteur de carte réseau / sous-réseau : « Toutes les cartes » par défaut.
- Champ de plage : `192.168.1.0/24`, `192.168.1.10-50`, ou plusieurs plages
  séparées par des virgules.
- Bouton **Scanner** (principal) / **Arrêter** pendant le scan.
- Case **Inclure les agents distants** (cochée par défaut).
- Barre de progression et texte d'état : `Scan de 192.168.1.0/24… 128/254`.

### 4.2 Tableau

Colonnes exactes :

```text
Nom | Adresse IP | DHCP | Serveur DHCP | Pare-feu | Agent | Système | Dernière réponse
```

Détail des colonnes :

| Colonne | Contenu |
|---|---|
| `Nom` | Nom NetBIOS/DNS, ou nom exact fourni par l'agent ; `—` si inconnu |
| `Adresse IP` | IPv4 principale ; badge `+N` si la machine a plusieurs adresses |
| `DHCP` | `Oui` / `Non` avec l'agent ; `—` sans agent |
| `Serveur DHCP` | Adresse du serveur DHCP ; `—` si statique ou sans agent |
| `Pare-feu` | `Oui` / `Non` avec l'agent ; `—` sans agent |
| `Agent` | Version et état (`v2.0.1`, `Appairé`, `Non appairé`, `—`) |
| `Système` | `Windows 11`, `macOS 15`, `Imprimante`, `NAS`, `Inconnu` |
| `Dernière réponse` | Heure relative (`il y a 2 s`) ; RTT dans l'infobulle |

Le tableau est triable, filtrable (`Tout`, `Avec agent`, `Sans agent`) et
sélectionnable. Un double-clic ouvre les détails.

### 4.3 Actions

Menu contextuel et boutons :

- **Identifier** : affiche la fenêtre colorée sur l'écran de la machine
  distante. Actif uniquement pour une machine avec agent.
- **Changer l'adresse IP…** : actif uniquement pour une machine avec agent.
- **Détails** : tous les adaptateurs, MAC, masque, passerelle, DNS, profils
  de pare-feu, version de l'agent, système, ports ouverts, source de
  découverte.
- **Copier l'adresse IP**.
- **Ping** depuis la machine cible (via l'agent) ou depuis la console.
- **Installer l'agent** / **Appairer l'agent** si absent.

### 4.4 Détails

Le panneau Détails contient notamment :

- nom, FQDN, système et version ;
- utilisateur connecté et état de la session (active, verrouillée, aucune) ;
- pour chaque adaptateur : nom, description, MAC, IP, masque, passerelle,
  DNS, DHCP oui/non, serveur DHCP, état ;
- pare-feu : état global et profils Domaine / Privé / Public ;
- agent : version, protocole, date d'appairage, dernière communication ;
- découverte : source ARP, ICMP ou agent, dernière réponse, RTT.

### 4.5 États vides et erreurs

- « Aucune machine trouvée. Vérifiez la plage et le pare-feu. »
- « Agent non installé sur cette machine. »
- « L'agent n'est pas appairé. »
- « La session est verrouillée : la fenêtre d'identification ne peut pas
  s'afficher. »
- « Aucune session interactive sur cette machine. »
- « Adresse en conflit : changement annulé. »
- « Le changement a été appliqué mais la machine n'a pas répondu à la
  nouvelle adresse. Retour arrière dans X secondes. »

## 5. Moteur de scan

### 5.1 Découverte locale

1. Énumérer les interfaces IPv4 actives et leurs masques.
2. Calculer l'adresse réseau et l'adresse de diffusion.
3. Ignorer la boucle locale, les interfaces inactives et, par défaut,
   les adresses link-local `169.254.0.0/16`.
4. Balayer chaque sous-réseau avec ARP et ICMP.

### 5.2 ARP

- P/Invoke `SendARP` (`iphlpapi.dll`) avec l'IP source de la carte scannée.
- Fonctionne même si le pare-feu de la cible bloque ICMP.
- Donne l'IP et le MAC.
- Uniquement sur le segment local ; ne traverse pas un routeur.
- Concurrence : 64 à 128 requêtes ; timeout 300 à 500 ms.

### 5.3 ICMP

- `Ping.SendPingAsync` avec timeout 500 ms et concurrence 64.
- Donne le RTT et le TTL.
- Le TTL permet une estimation du système : 128 → Windows, 64 → macOS/Linux,
  255 → équipement réseau.
- Un ping bloqué n'empêche pas la découverte ARP.

### 5.4 Noms

- NetBIOS `NBSTAT` (UDP 137), timeout 200 ms : meilleur résultat sur Windows.
- DNS inverse (`Dns.GetHostEntry`) avec timeout, en arrière-plan.
- mDNS plus tard si nécessaire.
- Les noms s'affichent au fur et à mesure ; le scan n'attend pas les
  résolutions lentes.

### 5.5 Plages

- `192.168.1.0/24` : CIDR.
- `192.168.1.10-50` : plage.
- `192.168.1.42` : adresse seule.
- Plusieurs entrées séparées par des virgules.
- Limite par défaut : 4 096 adresses par plage. Au-delà, avertissement et
  confirmation.
- Mode rapide pour les grandes plages : ARP/ICMP seulement, sans noms ni
  ports.
- Progression, annulation et reprise possibles.

### 5.6 Agents

- L'application envoie `scan` à chaque agent appairé.
- L'agent scanne ses propres sous-réseaux locaux et renvoie les hôtes.
- L'application fusionne les résultats et déduplique par IP.
- Les informations de l'agent (nom, DHCP, pare-feu, système) sont
  prioritaires sur les estimations ARP/ICMP.
- Cela couvre les réseaux derrière un routeur sans scanner des millions
  d'adresses.

### 5.7 Limites

- « Tout le réseau possible » signifie : tous les sous-réseaux locaux, toutes
  les plages personnalisées, et tous les sous-réseaux des agents. Pas
  l'intégralité de l'espace privé (10/8 + 172.16/12 + 192.168/16).
- Pas de MAC pour les hôtes routés.
- IPv4 seulement dans un premier temps.
- Les réseaux Wi-Fi avec isolation client ne verront que la passerelle.

## 6. Agent

### 6.1 Principe

Un seul agent privilégié par machine :

- Windows : service Windows (`LocalSystem`) ;
- macOS : LaunchDaemon (`root`).

Le service/daemon :

- écoute un port TCP unique (47821 par défaut) ;
- répond à la découverte UDP (47820) ;
- fournit l'inventaire exact ;
- change l'adresse IP locale ;
- lance le helper d'affichage dans la session active ;
- scanne son propre sous-réseau ;
- journalise les actions.

Aucun tray permanent. Le helper d'affichage est un processus de courte durée,
lancé uniquement pour identifier la machine ou approuver un appairage.

### 6.2 Pourquoi un helper séparé

Un service Windows tourne dans la session 0, isolée du bureau. Une fenêtre
créée par le service ne serait pas visible. Le service lance donc le helper
dans la session interactive de l'utilisateur :

- Windows : `WTSQueryUserToken` + `DuplicateTokenEx` + `CreateProcessAsUser`,
  avec `winsta0\default` ;
- macOS : `launchctl asuser <uid> <helper>`.

Le helper s'exécute avec les droits de l'utilisateur, pas ceux de l'admin.
Il ne fait qu'afficher une fenêtre.

### 6.3 Modes de l'exécutable Windows

L'application et l'agent partagent le même exécutable :

| Argument | Rôle |
|---|---|
| *(aucun)* | Application normale |
| `--install-agent` | Installe le service, le pare-feu et les fichiers |
| `--uninstall-agent` | Désinstalle le service et les règles de pare-feu |
| `--service` | Mode service Windows |
| `--identify ...` | Helper d'affichage plein écran |
| `--pair ...` | Helper d'approbation d'appairage |

L'installation copie l'exécutable dans
`%ProgramFiles%\AV IP-Switch\EasyIpSwitch.exe`, crée le service
`EasyIpSwitchAgent`, ajoute les règles de pare-feu et démarre le service.

### 6.4 Découverte

- L'application diffuse `EASYIPS_DISCOVER_V1` en UDP 47820 sur chaque
  sous-réseau local.
- L'agent répond avec ses informations publiques :
  nom, IP, port, empreinte du certificat, version, protocole, état
  d'appairage, système.
- Aucun secret n'est transmis.
- L'application peut aussi tenter une connexion TCP 47821 sur les hôtes
  scannés si l'UDP est bloqué.

### 6.5 Appairage

1. L'application envoie `pair_request` avec son nom et son identifiant.
2. L'agent lance le helper `--pair` dans la session active.
3. L'utilisateur voit « AV IP-Switch (192.168.1.10) veut gérer cet
   ordinateur. Autoriser ? » et clique **Autoriser** ou **Refuser**.
4. En cas d'accord, l'agent génère un jeton aléatoire de 32 octets, stocke
   son empreinte SHA-256 et renvoie le jeton à l'application.
5. L'application stocke le jeton et l'empreinte du certificat de l'agent.
6. Chaque requête suivante contient le jeton ; le certificat de l'agent est
   épinglé.

Pour une machine sans session interactive (serveur), l'installateur accepte
`--pair-token <jeton>` ; l'application utilise le même jeton.

Réinitialisation : `--reset-pairing` sur l'agent, en administrateur.

### 6.6 Sécurité de l'agent

- TLS 1.2 minimum, certificat auto-signé généré à l'installation, épinglé
  par l'application.
- Jeton d'appairage à durée illimitée, révocable.
- Commandes fixes, pas de shell, pas d'exécution arbitraire.
- Taille maximale de message : 1 Mo.
- Timeouts de lecture/écriture.
- Limitation de débit (identification : 1 par 5 secondes).
- Écoute limitée au réseau local ; rejet des adresses sources publiques.
- Règles de pare-feu limitées aux sous-réseaux privés.
- Journal d'audit local :
  `%ProgramData%\AV IP-Switch\Agent\logs\agent-AAAAMMJJ.log`.
- Installation, désinstallation et réinitialisation de l'appairage
  nécessitent les droits administrateur.

## 7. Protocole agent

### 7.1 Transport

- TCP 47821.
- TLS 1.2+.
- Messages JSON précédés d'un entier 32 bits big-endian (longueur UTF-8).
- Une requête par connexion, avec un message `hello` implicite dans chaque
  requête.
- L'agent peut traiter plusieurs connexions simultanées.

### 7.2 Enveloppe

Requête :

```json
{
  "protocol": 1,
  "id": "3f1c9a52-...",
  "token": "base64...",
  "command": "inventory",
  "params": {}
}
```

Réponse :

```json
{
  "protocol": 1,
  "id": "3f1c9a52-...",
  "ok": true,
  "result": {},
  "error": null
}
```

Erreur :

```json
{
  "protocol": 1,
  "id": "3f1c9a52-...",
  "ok": false,
  "result": null,
  "error": {
    "code": "ip_conflict",
    "message": "L'adresse 192.168.1.50 est déjà utilisée."
  }
}
```

### 7.3 Commandes

| Commande | Auth | Rôle |
|---|---|---|
| `info` | non | Informations publiques de l'agent |
| `pair_request` | non | Demande d'appairage avec approbation locale |
| `inventory` | oui | Inventaire complet |
| `set_ip` | oui | Change la configuration IPv4 |
| `confirm_ip` | oui | Confirme le changement, annule le retour arrière |
| `rollback_ip` | oui | Force le retour arrière |
| `identify` | oui | Affiche la fenêtre colorée |
| `scan` | oui | Scanne un sous-réseau local |
| `ping` | oui | Ping depuis la machine cible |
| `get_log` | oui | Dernières entrées du journal |

### 7.4 Inventaire

```json
{
  "hostname": "DESKTOP-ABC",
  "fqdn": "desktop-abc.local",
  "os": {
    "name": "Windows 11 Pro",
    "version": "10.0.22631",
    "arch": "x64"
  },
  "agent": {
    "version": "2.0.1",
    "protocol": 1,
    "paired": true
  },
  "user": "DESKTOP-ABC\\daniel",
  "session": {
    "interactive": true,
    "locked": false,
    "console": true
  },
  "firewall": {
    "enabled": true,
    "profiles": {
      "domain": true,
      "private": true,
      "public": false
    }
  },
  "adapters": [
    {
      "id": "intel-ethernet",
      "name": "Ethernet",
      "description": "Intel(R) Ethernet Connection",
      "mac": "AA-BB-CC-DD-EE-FF",
      "status": "up",
      "ipv4": ["192.168.1.42"],
      "mask": "255.255.255.0",
      "gateway": "192.168.1.1",
      "dhcp": true,
      "dhcpServer": "192.168.1.1",
      "dns": ["192.168.1.1"],
      "speedMbps": 1000,
      "virtual": false
    }
  ]
}
```

### 7.5 Changement d'IP

```json
{
  "protocol": 1,
  "id": "...",
  "token": "...",
  "command": "set_ip",
  "params": {
    "adapterMac": "AA-BB-CC-DD-EE-FF",
    "mode": "static",
    "ip": "192.168.1.50",
    "mask": "255.255.255.0",
    "gateway": "192.168.1.1",
    "dns": ["192.168.1.1"],
    "rollbackSeconds": 300,
    "dryRun": false
  }
}
```

Réponse :

```json
{
  "ok": true,
  "result": {
    "changeId": "chg-...",
    "adapterMac": "AA-BB-CC-DD-EE-FF",
    "oldConfig": {
      "mode": "dhcp",
      "ip": "192.168.1.42",
      "mask": "255.255.255.0",
      "gateway": "192.168.1.1"
    },
    "newConfig": {
      "mode": "static",
      "ip": "192.168.1.50",
      "mask": "255.255.255.0",
      "gateway": "192.168.1.1"
    },
    "rollbackDeadline": "2026-11-20T18:05:00Z"
  }
}
```

### 7.6 Identification

```json
{
  "command": "identify",
  "params": {
    "color": "#E53935",
    "durationSeconds": 30,
    "message": "192.168.1.42",
    "screens": "all"
  }
}
```

Réponse :

```json
{
  "ok": true,
  "result": {
    "shown": true,
    "sessions": 1,
    "visible": true
  }
}
```

### 7.7 Codes d'erreur

| Code | Signification |
|---|---|
| `unauthorized` | Jeton absent ou invalide |
| `not_paired` | Agent non appairé |
| `pairing_denied` | Appairage refusé |
| `pairing_timeout` | Pas de réponse de l'utilisateur |
| `no_interactive_session` | Aucune session utilisateur |
| `session_locked` | Session verrouillée |
| `invalid_params` | Paramètres invalides |
| `adapter_not_found` | Adaptateur introuvable |
| `ip_conflict` | Adresse déjà utilisée |
| `apply_failed` | Échec de l'application |
| `rollback_failed` | Échec du retour arrière |
| `busy` | Opération déjà en cours |
| `unsupported` | Commande non supportée |
| `internal` | Erreur interne |

## 8. Changement d'adresse IP à distance

### 8.1 Séquence

1. L'application valide la saisie : IPv4, masque contigu, passerelle dans le
   sous-réseau, adresse différente de l'actuelle, pas la passerelle, pas le
   serveur DHCP.
2. Elle envoie `set_ip` à l'agent.
3. L'agent retrouve l'adaptateur par MAC.
4. Il sauvegarde l'ancienne configuration.
5. Il vérifie que l'adresse est libre (sonde ARP locale).
6. Il applique la nouvelle configuration.
7. Il démarre le retour arrière automatique.
8. Il répond avec `changeId` et l'échéance.
9. L'application se reconnecte à la nouvelle adresse et envoie
   `confirm_ip`.
10. Si la confirmation arrive, l'agent annule le retour arrière.
11. Sinon, à l'échéance, l'agent restaure l'ancienne configuration.

### 8.2 Retour arrière

- Délai par défaut : 300 secondes.
- Le changement en attente est écrit dans
  `%ProgramData%\AV IP-Switch\Agent\pending-change.json`.
- Au redémarrage du service, un changement expiré est restauré.
- L'application peut forcer `rollback_ip`.
- Si l'application ne peut pas joindre la nouvelle adresse (autre
  sous-réseau), le retour arrière se déclenche ; l'utilisateur est averti
  avant l'application.

### 8.3 Cas particuliers

- **Carte de gestion** : si l'adaptateur utilisé pour joindre l'agent est
  modifié, la connexion tombe. Le retour arrière est obligatoire.
- **Plusieurs adaptateurs** : identification par MAC, jamais par index.
- **DHCP → statique** : l'agent sauvegarde le mode DHCP ; le retour arrière
  réactive DHCP.
- **Statique → DHCP** : l'agent sauvegarde l'ancienne configuration
  statique ; le retour arrière la restaure.
- **DNS** : fourni avec la configuration statique ; `registerdns` sur
  Windows, `dscacheutil -flushcache` sur macOS si nécessaire.
- **Conflit** : si l'adresse est déjà utilisée, l'agent refuse et
  n'applique rien.

## 9. Identification d'une machine

### 9.1 Fenêtre

- Plein écran, sans bordure, toujours au premier plan.
- Couleur unie choisie par l'utilisateur (palette + couleur personnalisée).
- Adresse IP affichée en très grand.
- Nom de la machine et message optionnel.
- Texte noir ou blanc selon la luminosité de la couleur.
- Compte à rebours et bouton **Fermer** ; `Échap` ferme aussi.
- Fermeture automatique à la fin de la durée (15, 30, 60 ou 120 secondes).
- Affichage sur tous les écrans ou sur l'écran principal.

### 9.2 Sessions

- Windows : session console active (`WTSGetActiveConsoleSessionId`).
- macOS : session graphique active (`stat -f%Su /dev/console`).
- Si aucune session : erreur `no_interactive_session`.
- Si la session est verrouillée : erreur `session_locked` ; l'application
  invite à déverrouiller la machine.
- Plusieurs sessions : console par défaut ; option « tous les écrans » pour
  les sessions actives.

### 9.3 Sécurité

- Réservé aux applications appairées.
- Limitation de débit.
- Le helper ne reçoit que la couleur, la durée, l'IP et le message.
- Aucun accès fichier ou shell.

## 10. Sécurité générale

- L'agent n'est pas un backdoor : commandes fermées, entrées validées,
  sorties JSON.
- TLS + jeton + certificat épinglé.
- Jamais de mot de passe stocké en clair.
- Jetons stockés hachés côté agent, chiffrés côté application
  (DPAPI / Credential Manager sous Windows, Keychain sous macOS).
- Journal d'audit local, sans secret.
- Pare-feu limité aux réseaux privés.
- L'application ne doit pas exposer l'agent sur Internet.
- Une confirmation explicite est demandée pour changer une IP.
- Les actions sont tracées : date, application, machine, ancienne et
  nouvelle configuration, résultat.

## 11. Windows et macOS

### 11.1 Windows

- Application WinForms C# 5, .NET Framework 4.x.
- Agent dans le même exécutable, modes `--service`, `--identify`, `--pair`,
  `--install-agent`.
- Service `LocalSystem`.
- Inventaire : `NetworkInterface`, WMI local
  (`Win32_NetworkAdapterConfiguration`, `MSFT_NetFirewallProfile`),
  `Win32_ComputerSystem`.
- Changement d'IP : `netsh` / WMI local, comme l'application actuelle.
- Helper : `CreateProcessAsUser` dans la session console.
- Pare-feu : règle TCP 47821 et UDP 47820, limitée aux réseaux privés.
- Installation : auto-installation par l'exécutable, sans installateur tiers.

### 11.2 macOS

- Application SwiftUI existante, à modifier sur le Mac.
- Agent Swift : LaunchDaemon root + helper d'affichage.
- Même protocole, mêmes commandes, mêmes colonnes.
- Inventaire : `SystemConfiguration`, `networksetup`, `ipconfig getpacket`,
  `socketfilterfw`, `sw_vers`, `scutil`.
- Changement d'IP : `networksetup -setmanual` / `-setdhcp`,
  `-setdnsservers`.
- Helper : `launchctl asuser <uid>`.
- Installation : paquet `.pkg` signé et notarié.
- **Sans signature/notarisation, Gatekeeper bloque l'installation simple.**
  Le développement et les tests locaux restent possibles, mais la diffusion
  macOS exige la signature.
- Le dossier `mac/` n'est pas modifié depuis Windows ; l'implémentation
  macOS se fait sur le Mac.

### 11.3 Compatibilité

- L'application Windows peut gérer les agents Windows et macOS.
- L'application macOS peut gérer les agents Windows et macOS.
- Le protocole est le contrat commun ; toute évolution doit être
  rétrocompatible ou versionnée.

## 12. Version 2.0.1

Fichiers à mettre à jour côté Windows :

- `src/AppInfo.cs` : `Version = "2.0.1"`.
- `src/AssemblyInfo.cs` : `AssemblyVersion` et `AssemblyFileVersion`
  `2.0.1.0`.
- `build.ps1` : version de secours `2.0.1`.
- `README.md` : version et fonctionnalités.
- `tests/HeaderVersionTests.cs` : 2.0.1.
- `tests/LocalizationTests.cs` : 3 onglets.
- `src/Controls/TabBar.cs` : largeur adaptée à 3 onglets.
- `src/Localization.cs` : nouvelles clés FR/EN/DE/ES.
- `src/MainForm.cs` : troisième onglet et vue.
- `src/ScanView.cs` : nouvel onglet.
- `src/NetworkScanner.cs` : moteur de scan.
- `src/AgentClient.cs` : client du protocole.
- `src/Agent/` : service, helper, installation, protocole.
- `docs/AGENT-PROTOCOL.md` : spécification du protocole.

Le dossier `mac/` reste intact. La version macOS 2.0.1 sera gérée sur le
Mac.

## 13. Découpage

### Lot 1 — Scan sans agent (Windows)

- Onglet **Scan réseaux**.
- Tableau avec les colonnes validées.
- Détection des sous-réseaux locaux.
- Plages personnalisées.
- ARP + ICMP + noms.
- Détails.
- Version 2.0.1, traductions, tests.

### Lot 2 — Agent Windows

- Service, installation, désinstallation.
- Découverte UDP.
- TLS, appairage, jeton.
- Inventaire exact (DHCP, serveur DHCP, pare-feu, système).
- Scan local de l'agent.
- Colonnes exactes dans le tableau.

### Lot 3 — Actions distantes

- Changement d'IP avec validation et retour arrière.
- Fenêtre d'identification.
- Journal d'audit.
- Tests de sécurité.

### Lot 4 — macOS

- Spécification du protocole (fournie).
- Onglet Scan réseaux dans l'application SwiftUI.
- Agent Swift.
- Signature/notarisation.
- À réaliser sur le Mac.

## 14. Tests

### Unitaires

- Analyse des plages (`CIDR`, `a-b`, adresse seule, virgules).
- Calcul réseau/diffusion.
- Validation IPv4, masque, passerelle.
- Fusion et déduplication des résultats.
- Formatage du tableau et des états.
- Traductions.
- Sérialisation du protocole.
- Hachage du jeton.
- État de retour arrière.

### Intégration

- Serveur de test en boucle locale pour le protocole.
- Authentification, mauvais jeton, messages malformés, timeouts.
- `set_ip` en mode `dryRun`.
- Scan d'un sous-réseau de test avec ARP/ICMP simulés.
- Service Windows : installation, démarrage, arrêt, désinstallation.

### Manuels

- Windows 10/11, macOS 13/14/15.
- Workgroup et domaine.
- Ethernet et Wi-Fi.
- Plusieurs adaptateurs.
- Session verrouillée, aucune session.
- Pare-feu actif/inactif.
- DHCP/statique.
- Conflit d'IP.
- Retour arrière après perte de connexion.

## 15. Risques et points ouverts

- **Déploiement de l'agent** : installation manuelle sur chaque machine,
  ou GPO/MDM, ou déploiement depuis l'application avec identifiants.
  Décision à prendre pour la 2.0.1.
- **Signature** : SmartScreen sous Windows ; Gatekeeper sous macOS.
  La diffusion macOS simple exige la signature/notarisation.
- **Antivirus** : un service qui lance une fenêtre dans la session peut
  être signalé ; tester avec Defender et signer.
- **Session verrouillée** : la fenêtre d'identification n'est pas visible ;
  prévoir un message clair.
- **Grandes plages** : performance et mémoire ; limite et mode rapide.
- **Réseaux routés** : pas de MAC ; utiliser les agents.
- **Mises à jour de l'agent** : manuelles tant que la signature n'est pas
  en place.
- **Attentes utilisateur** : l'agent doit être installé une fois par
  machine ; sans agent, pas de changement d'IP ni d'identification
  distante.
