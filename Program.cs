using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;
using Windows.Media.Audio;

namespace BtSpeaker
{
    class Program
    {
        #region Var_
        // ── État global ───────────────────────────────────────────────────
        static readonly Dictionary<string, (string Name, AudioPlaybackConnection Conn)> _connected = [];
        static readonly SemaphoreSlim _lock = new(1, 1);
        static DeviceWatcher _watcher;
        private const string V = "avrt.dll";

        // ── Win32 P/Invoke pour optimisations audio ───────────────────────

        // Élève la priorité du thread audio dans le Multimedia Class Scheduler (MMCSS)
        // Réduit les interruptions OS
        [DllImport(V, SetLastError = true, CharSet = CharSet.Unicode)]
#pragma warning disable SYSLIB1054
        static extern IntPtr AvSetMmThreadCharacteristics(string TaskName, ref uint TaskIndex);
#pragma warning restore SYSLIB1054
        [DllImport(V, SetLastError = true)]
#pragma warning disable SYSLIB1054
        static extern bool AvRevertMmThreadCharacteristics(IntPtr AvrtHandle);
#pragma warning restore SYSLIB1054
        // Empêche le PC de passer en veille / économie d'énergie pendant le streaming
        [DllImport("kernel32.dll", SetLastError = true)]
#pragma warning disable SYSLIB1054
        static extern uint SetThreadExecutionState(uint esFlags);
#pragma warning restore SYSLIB1054
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_AWAYMODE_REQUIRED = 0x00000040;

        static IntPtr _mmcssHandle = IntPtr.Zero;
        #endregion

        static async Task Main(string[] args)
        {
            Assembly thisAssem = typeof(Program).Assembly;
            AssemblyName thisAssemName = thisAssem.GetName();
            Version ver = thisAssemName.Version;
            ArgumentNullException.ThrowIfNull(args);

            Console.Title = string.Format("Bluetooth A2DP Sink {0}", ver);
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Priorité temps-réel du processus entier
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

            // Empêche la mise en veille pendant le streaming
            _ = SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED);

            Error("╔══════════════════════════════════════════╗");
            Error("║       Bluetooth Speaker — A2DP Sink      ║");
            Error("║         Windows 10 2004+ / .NET 8        ║");
            Error("║            Author: ICAZA MEDIA           ║");
            Error("╚══════════════════════════════════════════╝");
            Print("");

            if (!ApiInformation.IsTypePresent("Windows.Media.Audio.AudioPlaybackConnection"))
            {
                Error("AudioPlaybackConnection Not supported. Required : Windows 10 build 19041+.");
                Console.ReadKey();
                return;
            }

            Ok(" API AudioPlaybackConnection available");
            PrintHelp();

            bool running = true;
            while (running)
            {
                Console.Write("> ");
                var key = Console.ReadKey(intercept: true);
                Console.WriteLine();

                switch (char.ToUpper(key.KeyChar))
                {
                    case 'L': await ListDevicesAsync(); break;
                    case 'C': await ConnectInteractiveAsync(); break;
                    case 'D': await DisconnectInteractiveAsync(); break;
                    case 'S': PrintStatus(); break;
                    case 'A': StartWatcher(); break;
                    case 'H': PrintHelp(); break;
                    case 'I': PrintLatencyTips(); break;
                    case 'Q': running = false; break;
                    default: Print("Unknown command — [H] for help."); break;
                }
            }

            await CleanupAsync();
        }

        // ── List of devices ──────────────────────────────────────────

        static async Task ListDevicesAsync()
        {
            Print("Searching for Bluetooth audio devices...");
            var devices = await DeviceInformation.FindAllAsync(
                AudioPlaybackConnection.GetDeviceSelector());

            if (devices.Count == 0)
            {
                Warn("No device found. Pair the device first via Settings → Bluetooth.");
                return;
            }

            Print($"\n{devices.Count} device(s) :\n");
            for (int i = 0; i < devices.Count; i++)
            {
                var connected = _connected.ContainsKey(devices[i].Id);
                Print($"  [{i + 1}] {devices[i].Name}  {(connected ? "🟢 Connected" : "⚪ Available")}");
            }
            Print("");
        }

        // ── Interactive connection ────────────────────────────────────────

        static async Task ConnectInteractiveAsync()
        {
            var devices = await DeviceInformation.FindAllAsync(
                AudioPlaybackConnection.GetDeviceSelector());

            if (devices.Count == 0) { Warn("No devices available."); return; }

            for (int i = 0; i < devices.Count; i++)
                Print($"  [{i + 1}] {devices[i].Name}");

            Print("");
            Console.Write("Number : ");
            if (int.TryParse(Console.ReadLine(), out int idx) && idx >= 1 && idx <= devices.Count)
                await ConnectAsync(devices[idx - 1].Id, devices[idx - 1].Name);
            else
                Warn("Invalid number.");
        }

        // ── Heart of the connection ─────────────────────────────────────────

        static async Task ConnectAsync(string deviceId, string deviceName)
        {
            await _lock.WaitAsync();
            try
            {
                if (_connected.ContainsKey(deviceId))
                {
                    Warn($"{deviceName} is already connected.");
                    return;
                }

                Print($"Login to {deviceName}...");
                Print("");

                var conn = AudioPlaybackConnection.TryCreateFromId(deviceId);
                if (conn == null)
                {
                    Error($"Unable to create the connection for {deviceName}.");
                    Error("The device must be paired and support A2DP..");
                    return;
                }

                conn.StateChanged += (sender, _) =>
                {
                    if (sender.State == AudioPlaybackConnectionState.Closed)
                        _ = HandleDisconnectedAsync(sender.DeviceId);
                };

                // Élève la priorité MMCSS sur le thread courant avant StartAsync
                // pour que la négociation initiale se fasse avec les meilleures priorités
                BoostThreadPriority();

                await conn.StartAsync();
                var result = await conn.OpenAsync();

                switch (result.Status)
                {
                    case AudioPlaybackConnectionOpenResultStatus.Success:
                        _connected[deviceId] = (deviceName, conn);
                        Ok($">> {deviceName} connected — audio to default output.");
                        //PrintLatencyTips();
                        break;

                    case AudioPlaybackConnectionOpenResultStatus.RequestTimedOut:
                        conn.Dispose();
                        Error($"{deviceName} : waiting time exceeded.");
                        break;

                    case AudioPlaybackConnectionOpenResultStatus.DeniedBySystem:
                        conn.Dispose();
                        Error($"{deviceName} : Connection refused by the system.");
                        break;

                    case AudioPlaybackConnectionOpenResultStatus.UnknownFailure:
                        conn.Dispose();
                        Error($"{deviceName} : failure 0x{result.ExtendedError?.HResult ?? -1:X8}.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Error($"Exception : {ex.Message} (0x{ex.HResult:X8})");
            }
            finally
            {
                _lock.Release();
            }
        }

        // ── Interactive logout ──────────────────────────────────────

        static async Task DisconnectInteractiveAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_connected.Count == 0) { Warn("No devices connected."); return; }

                var list = new List<(string Id, string Name)>();
                foreach (var kv in _connected)
                    list.Add((kv.Key, kv.Value.Name));

                for (int i = 0; i < list.Count; i++)
                    Print($"  [{i + 1}] {list[i].Name}");

                Console.Write("Number to disconnect: ");
                if (int.TryParse(Console.ReadLine(), out int idx) && idx >= 1 && idx <= list.Count)
                {
                    var (id, name) = list[idx - 1];
                    if (_connected.TryGetValue(id, out var entry))
                    {
                        try { entry.Conn.Dispose(); } catch { }
                        _connected.Remove(id);
                        Ok($"{name} déconnecté.");
                    }
                }
                else Warn("Invalid number.");
            }
            finally { _lock.Release(); }
        }

        // ── Remote disconnection ─────────────────────────────────────────

        static async Task HandleDisconnectedAsync(string deviceId)
        {
            await _lock.WaitAsync();
            try
            {
                if (_connected.TryGetValue(deviceId, out var entry))
                {
                    try { entry.Conn.Dispose(); } catch { }
                    _connected.Remove(deviceId);
                    Warn($"\n!!  {entry.Name} disconnected.");
                    Console.Write("> ");
                }
            }
            finally { _lock.Release(); }
        }

        // ── DeviceWatcher ────────────────────────────────────────────────

        static void StartWatcher()
        {
            if (_watcher != null) { Warn("Surveillance already active."); return; }

            _watcher = DeviceInformation.CreateWatcher(
                AudioPlaybackConnection.GetDeviceSelector());

            _watcher.Added += async (_, info) =>
            {
                Print($"\n[Watcher] Device detected : {info.Name}");
                await Task.Delay(1200); // Laisse le stack BT s'initialiser
                await ConnectAsync(info.Id, info.Name);
                Console.Write("> ");
            };

            _watcher.Removed += (_, info) =>
            {
                Print($"\n[Watcher] Device removed.");
                Console.Write("> ");
            };

            _watcher.Start();
            Ok("Surveillance active.");
        }

        // ── Status ───────────────────────────────────────────────────────

        static void PrintStatus()
        {
            if (_connected.Count == 0) { Warn("No active connection."); return; }
            Print($"\n{_connected.Count} active connection(s):");
            foreach (var kv in _connected)
                Print($"  >> {kv.Value.Name}  [{kv.Value.Conn.State}]");
            Print("");
        }

        // ── Latency/audio quality optimizations ────────────────────────

        /// <summary>
        /// Élève la priorité du thread via MMCSS (Multimedia Class Scheduler Service).
        /// Windows utilise MMCSS pour garantir que les threads audio ne sont pas
        /// interrompus par d'autres processus, réduisant ainsi les glitches.
        /// Catégorie "Pro Audio" = priorité maximale pour l'audio.
        /// </summary>
        static void BoostThreadPriority()
        {
            if (_mmcssHandle != IntPtr.Zero) return;
            try
            {
                uint taskIndex = 0;
                _mmcssHandle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
                if (_mmcssHandle != IntPtr.Zero)
                    Ok("[MMCSS] High thread priority 'Pro Audio'.");
                else
                    Warn("[MMCSS] Unable to raise priority (error ignored).");
            }
            catch (Exception ex)
            {
                Warn($"[MMCSS] {ex.Message}");
            }
        }

        static void PrintLatencyTips()
        {
            Print("");
            Warn("── Tips to Reduce Latency and Crackling ────────────────");
            Print("");
            Infos(" • Codec: Windows negotiates SBC by default (~200ms).");
            Infos(" If your device supports AAC, disable SBC in");
            Infos(" Settings → Bluetooth → [device] → Advanced properties.");
            Infos(" • Distance: Stay within 5m of the PC, avoid obstacles.");
            Infos(" • Interference: Stay away from 2.4GHz WiFi networks,");
            Infos(" microwaves, other active BT devices.");
            Infos(" • BT Adapter: A dedicated USB BT 5.0 adapter");
            Infos(" (e.g., ASUS BT500) significantly reduces latency");
            Infos(" vs. Bluetooth integrated into the motherboard.");
            Infos(" • Windows power plan: switch to 'High performance'");
            Infos(" (Control Panel → Power Options).");
            Print("");
            Warn("────────────────────────────────────────────────────────");
            Print("");
        }

        // ── Clean ────────────────────────────────────────────────────

        static async Task CleanupAsync()
        {
            _watcher?.Stop();

            // Libère la priorité MMCSS
            if (_mmcssHandle != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(_mmcssHandle);
                _mmcssHandle = IntPtr.Zero;
            }

            // Restaure la gestion d'alimentation normale
            _ = SetThreadExecutionState(ES_CONTINUOUS);

            await _lock.WaitAsync();
            foreach (var (name, conn) in _connected.Values)
            {
                try { conn.Dispose(); } catch { }
                Print($"Disconnected : {name}");
            }
            _connected.Clear();
            _lock.Release();
        }

        // ── Help ─────────────────────────────────────────────────────────

        static void PrintHelp()
        {
            Print("");
            Warn("── Commands ────────────────────────────────");
            Print("");
            Print("  [L] List of Bluetooth audio devices");
            Print("  [C] Connect a device");
            Print("  [D] Disconnect a device");
            Print("  [S] Status of active connections");
            Print("  [A] Automatic monitoring (DeviceWatcher)");
            Print("  [H] Help");
            Print("  [I] Latency Tips");
            Print("  [Q] Quit");
            Print("");
            Warn("────────────────────────────────────────────");
            Print("");
        }

        // ── Console helpers ──────────────────────────────────────────────

        static void Print(string msg) => Console.WriteLine(msg);
        static void Ok(string msg) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(msg); Console.ResetColor(); }
        static void Warn(string msg) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine(msg); Console.ResetColor(); }
        static void Infos(string msg) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(msg); Console.ResetColor(); }
        static void Error(string msg) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(msg); Console.ResetColor(); }
    }
}
