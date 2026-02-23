[![EN](https://img.shields.io/badge/lang-EN-blue)](README.md)
[![FR](https://img.shields.io/badge/lang-FR-blue)](README.fr.md)

# 🔵 Bluetooth A2DP Sink pour Windows

Transformez votre PC Windows en enceinte Bluetooth. N'importe quel appareil appairé (téléphone, tablette, laptop) peut se connecter et diffuser de l'audio directement vers la sortie audio par défaut de votre PC.

```
Téléphone / Tablette  ──(Bluetooth A2DP)──►  PC Windows  ──►  Enceintes / Casque
```

---

## Fonctionnalités

- **A2DP Sink** — reçoit l'audio depuis n'importe quel appareil Bluetooth supportant le profil A2DP
- **Multi-appareils** — connectez plusieurs appareils simultanément
- **Reconnexion automatique** — le mode DeviceWatcher connecte automatiquement les appareils connus dès leur détection
- **Optimisations latence** — priorité thread MMCSS `Pro Audio`, priorité processus élevée, verrou anti-veille
- **Zéro configuration** — aucun pilote, aucun périphérique audio virtuel, aucun logiciel tiers
- **Léger** — un seul exécutable console, aucune surcharge d'interface graphique

---

## Prérequis

| Composant | Minimum |
|---|---|
| Windows | 10 version 2004 (build 19041) — *May 2020 Update* |
| SDK .NET | 8.0 ou supérieur |
| Architecture | x64 |
| Adaptateur Bluetooth | Tout adaptateur compatible Windows (BT 4.0+) |

> **Pourquoi Windows 10 2004 ?**
> L'API [`AudioPlaybackConnection`](https://learn.microsoft.com/fr-fr/uwp/api/windows.media.audio.audioplaybackconnection) a été introduite dans le SDK Windows 10 19041.
> Cette application l'utilise directement — aucune bibliothèque audio tierce requise.

---

## Démarrage rapide

### 1. Cloner et compiler

```bash
git clone https://github.com/icaza/bluetooth-a2dp-sink.git
cd bluetooth-a2dp-sink
dotnet build -p:Platform=x64
```

### 2. Appairer votre appareil

Avant de vous connecter, appairez votre téléphone ou tablette via **Paramètres Windows → Bluetooth et appareils → Ajouter un appareil**.

### 3. Lancer

```bash
dotnet run -p:Platform=x64
```

### 4. Connecter

Appuyez sur `L` pour lister les appareils disponibles, puis `C` pour vous connecter.

---

## Utilisation

```
╔══════════════════════════════════════════╗
║     Bluetooth Speaker — A2DP Sink             ║
║     Windows 10 2004+ / .NET 8                 ║
╚══════════════════════════════════════════╝

  [L] Lister les appareils Bluetooth audio disponibles
  [C] Connecter un appareil
  [D] Déconnecter un appareil
  [S] Statut des connexions actives
  [W] Activer la surveillance automatique (DeviceWatcher)
  [H] Aide
  [Q] Quitter
```

Une fois connecté, l'audio de votre téléphone est lu via la **sortie audio par défaut** de votre PC — aucune configuration supplémentaire nécessaire.

---

## Fonctionnement

Cette application utilise l'API Windows Runtime `AudioPlaybackConnection` introduite dans le SDK Windows 10 2004 :

```
AudioPlaybackConnection.TryCreateFromId(deviceId)
  └─► connection.StartAsync()
        └─► connection.OpenAsync()
              └─► Audio routé vers la sortie par défaut par l'OS
```

La stack Bluetooth Windows gère automatiquement toute la négociation de codec (SBC / AAC) et le routage PCM — aucune capture WASAPI manuelle n'est nécessaire.

### Optimisations de latence

| Optimisation | API | Effet |
|---|---|---|
| Priorité thread MMCSS `Pro Audio` | `avrt.dll / AvSetMmThreadCharacteristics` | Empêche l'OS d'interrompre le thread audio |
| Priorité processus élevée | `Process.PriorityClass = High` | Réduit les délais d'ordonnancement |
| Verrou d'alimentation | `SetThreadExecutionState` | Empêche l'économie d'énergie CPU/BT pendant le streaming |

---

## Réduire la latence et les grésillements

A2DP a une latence protocolaire inhérente de **100–300 ms** — c'est une contrainte du standard Bluetooth, pas une limitation logicielle. Les étapes suivantes permettent de la minimiser :

### Côté logiciel
- Passer le plan d'alimentation Windows en **Haute performance**
  *(Panneau de configuration → Options d'alimentation → Haute performance)*
- Fermer les applications en arrière-plan générant une charge CPU élevée

### Côté matériel

| Problème | Solution |
|---|---|
| Codec SBC par défaut (~200 ms) | Activer AAC dans Paramètres → Bluetooth → [appareil] → Avancé |
| Adaptateur BT intégré bas de gamme | Remplacer par un adaptateur USB BT 5.0 dédié (ex : ASUS BT-500) |
| Interférences WiFi 2.4 GHz | Passer le routeur en 5 GHz, ou éloigner l'adaptateur BT de la carte WiFi |
| Distance / obstacles | Rester à moins de 5 m avec une ligne de vue directe vers l'adaptateur |

---

## Structure du projet

```
bluetooth-a2dp-sink/
├── BtSpeaker.csproj   — Projet SDK-style, net8.0-windows10.0.26100.0, x64
└── Program.cs         — Toute la logique applicative (~250 lignes)
```

Aucun package NuGet requis. Toutes les APIs sont fournies par le TFM .NET 8 Windows.

---

## Dépannage

**"AudioPlaybackConnection non supporté"**
→ Votre version de Windows est inférieure à 10.0.19041. Mettez à jour via Windows Update.

**L'appareil n'apparaît pas après `[L]`**
→ L'appareil n'est pas appairé. Allez dans Paramètres → Bluetooth → Ajouter un appareil.

**`TryCreateFromId` retourne null**
→ L'appareil est appairé mais n'expose pas d'endpoint A2DP sink. Vérifiez qu'il supporte la diffusion audio A2DP (la plupart des téléphones et tablettes le supportent).

**La connexion expire**
→ Rapprochez-vous du PC. Désactivez les autres connexions Bluetooth actives sur le même adaptateur.

**Grésillements / coupures**
→ Consultez la section [Réduire la latence et les grésillements](#réduire-la-latence-et-les-grésillements) ci-dessus.

---

## Licence

MIT — voir [LICENSE](LICENSE) pour les détails.
