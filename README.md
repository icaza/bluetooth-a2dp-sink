[![EN](https://img.shields.io/badge/lang-EN-blue)](README.md)
[![FR](https://img.shields.io/badge/lang-FR-blue)](README.fr.md)

# 🔵 Bluetooth A2DP Sink for Windows

Turn your Windows PC into a Bluetooth speaker. Any paired Bluetooth device (phone, tablet, laptop) can connect and stream audio directly to your PC's default audio output.

```
Phone / Tablet  ──(Bluetooth A2DP)──►  Windows PC  ──►  Speakers / Headphones
```

---

## Features

- **A2DP Sink** — receives audio from any Bluetooth device supporting the A2DP profile
- **Multi-device** — connect multiple devices simultaneously
- **Auto-reconnect** — DeviceWatcher mode automatically connects known devices when they appear
- **Low-latency optimizations** — MMCSS `Pro Audio` thread priority, high process priority, anti-sleep power lock
- **Zero configuration** — no drivers, no virtual audio devices, no third-party software
- **Lightweight** — single console executable, no UI overhead

---

## Requirements

| Component | Minimum |
|---|---|
| Windows | 10 version 2004 (build 19041) — *May 2020 Update* |
| .NET SDK | 8.0 or later |
| Architecture | x64 |
| Bluetooth adapter | Any Windows-compatible adapter (BT 4.0+) |

> **Why Windows 10 2004?**
> The [`AudioPlaybackConnection`](https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.audioplaybackconnection) API was introduced in Windows 10 SDK 19041.
> This app uses it directly — no third-party audio library required.

---

## Getting Started

### 1. Clone and build

```bash
git clone https://github.com/icaza/bluetooth-a2dp-sink.git
cd bluetooth-a2dp-sink
dotnet build -p:Platform=x64
```

### 2. Pair your device

Before connecting, pair your phone or tablet via **Windows Settings → Bluetooth & devices → Add device**.

### 3. Run

```bash
dotnet run -p:Platform=x64
```

### 4. Connect

Press `L` to list available devices, then `C` to connect.

---

## Usage

```
╔══════════════════════════════════════════╗
║     Bluetooth Speaker — A2DP Sink             ║
║     Windows 10 2004+ / .NET 8                 ║
╚══════════════════════════════════════════╝

  [L] List available Bluetooth audio devices
  [C] Connect a device
  [D] Disconnect a device
  [S] Show active connections status
  [W] Start automatic watcher (auto-connect on device detection)
  [H] Help
  [Q] Quit
```

Once connected, audio from your phone plays through your PC's **default audio output** — no additional configuration needed.

---

## How It Works

This app uses the Windows Runtime `AudioPlaybackConnection` API introduced in Windows 10 SDK 2004:

```
AudioPlaybackConnection.TryCreateFromId(deviceId)
  └─► connection.StartAsync()
        └─► connection.OpenAsync()
              └─► Audio routed to default output by the OS
```

The OS Bluetooth stack handles all codec negotiation (SBC / AAC) and PCM routing automatically — no manual audio capture or WASAPI wiring is needed.

### Latency optimizations

| Optimization | API | Effect |
|---|---|---|
| MMCSS `Pro Audio` thread priority | `avrt.dll / AvSetMmThreadCharacteristics` | Prevents OS from preempting the audio thread |
| High process priority | `Process.PriorityClass = High` | Reduces scheduling delays |
| Power lock | `SetThreadExecutionState` | Prevents CPU/BT power-saving during streaming |

---

## Reducing Latency & Crackling

A2DP has an inherent protocol latency of **100–300 ms** — this is a Bluetooth standard constraint, not a software limitation. The following steps can minimize it:

### Software side
- Set Windows power plan to **High Performance**
  *(Control Panel → Power Options → High Performance)*
- Close background applications that generate high CPU load

### Hardware side

| Issue | Fix |
|---|---|
| Default SBC codec (~200 ms) | Enable AAC in Settings → Bluetooth → [device] → Advanced |
| Onboard BT adapter | Replace with a dedicated USB BT 5.0 adapter (e.g. ASUS BT-500) |
| 2.4 GHz WiFi interference | Switch your router to 5 GHz, or move the BT adapter away from the WiFi card |
| Distance / obstacles | Stay within 5 m with clear line-of-sight to the adapter |

---

## Project Structure

```
bluetooth-a2dp-sink/
├── BtSpeaker.csproj   — SDK-style project, net8.0-windows10.0.26100.0, x64
└── Program.cs         — All application logic (~900 lines)
```

No NuGet packages required. All APIs are provided by the .NET 8 Windows TFM.

---

## Troubleshooting

**"AudioPlaybackConnection not supported"**
→ Your Windows version is below 10.0.19041. Update via Windows Update.

**Device not listed after `[L]`**
→ The device is not paired. Go to Settings → Bluetooth → Add device first.

**`TryCreateFromId` returns null**
→ The device is paired but does not expose an A2DP sink endpoint. Verify it supports A2DP audio output (most phones and tablets do).

**Connection times out**
→ Move closer to the PC. Disable other active Bluetooth connections on the same adapter.

**Crackling / dropouts**
→ See the [Reducing Latency & Crackling](#reducing-latency--crackling) section above.

---

## License

MIT — see [LICENSE](LICENSE) for details.
