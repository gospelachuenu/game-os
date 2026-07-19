# MASTER CONSOLE ARCHITECTURE SPECIFICATION

## SYSTEM BLUEPRINT & SOFTWARE ROADMAP (PRODUCTION-READY V4.0)

This is the definitive, unified master technical specification for your bespoke console environment. It serves as your primary reference document to feed directly into an AI development workspace or use as an engineering roadmap to build a plug-and-play console appliance running on fixed hardware (**Intel i5-10500T, AMD Radeon RX 6600M, 16GB RAM, 1080p target TV**).

---

## 1. Operating System & Stealth Boot Mechanics

The goal of this phase is to strip the PC of its desktop identity completely so it behaves like a closed retail console appliance from the moment the physical power button is pressed.

* **1.1 Lean OS Foundation:** Deploy **Windows 10 IoT Enterprise LTSC**. This version provides a stripped-down corporate kernel completely free of consumer bloatware, pre-installed promotional games, heavy telemetry tracking, and disruptive consumer feature updates, preserving maximum CPU cycles and RAM overhead for game execution.
* **1.2 Custom Shell Replacement:** Modify the Windows registry by changing the default string inside `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`. Replace `explorer.exe` with the exact target file path of your custom executable `ConsoleSupervisor.exe`. The traditional Windows desktop environment, taskbar, start menu, system tray notifications, and desktop icons will never initialize.
* **1.3 Stealth Boot Sequence:** Suppress the native spinning Windows boot animation completely by running `bcdedit /set bootux disabled` via an administrative command prompt. Use the Windows Sysinternals `Autologon` utility to inject encrypted credentials into the local security authority, forcing the machine to bypass the login password lock screen instantly.
* **1.4 Silencing OS Failures:** Suppress standard Windows error pop-ups and "Application Has Stopped Working" dialog boxes by editing the registry key `HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\DontShowUI` to `1`. If an application crashes, Windows will swallow the exception silently out of the user's sight.
* **1.5 Zero-State Hardening via UWF:** Enable the native **Unified Write Filter (UWF)**. This forces the system partition (C:) into a permanent "Read-Only" state, redirecting all temporary system writes, application data dumps, and logs into a transient RAM overlay. Upon a system reboot, the RAM overlay is entirely flushed. Permanent OS data corruption becomes physically impossible.

---

## 2. The Supervisor Service Hierarchy & Focus Watchdog

The architecture isolates distinct hardware and tracking loops into specialized background workers managed by a single master service rather than running as a single, bloated process.

```
          [ Windows Boot ]
                 │
       [ ConsoleSupervisor.exe ]  <─── High-Privilege Watchdog
                 │
   ┌─────────────┼──────────────┬──────────────┬──────────────┐
   ▼             ▼              ▼              ▼              ▼
[UI.exe]   [Input.exe]    [CEC.exe]    [Monitor.exe]  [Hardware.exe]
(Frontend)  (Controller/  (HDMI-CEC/   (Manifest Scans/(Tuner, Audio,
             Keyboard)     TV Power)    Download Delta) Enforcer, Watchdog)

```

* **2.1 The Supervisor Daemon (`ConsoleSupervisor.exe`):** A high-privilege application running as a core background daemon immediately at system initialization. It executes a persistent `System.Timers.Timer` checking `Process.GetProcessesByName()` every 500ms for the decoupled core modules. If any module crashes, the supervisor invokes a fresh `Process.Start()` instance immediately.
* **2.2 The Win32 Focus Guardian:** To mitigate game window minimization or loss of focus from background anomalies, the supervisor instantiates a low-level Win32 hook (`SetWinEventHook`) watching for `EVENT_SYSTEM_FOREGROUND`. If the foreground window handle deviates from the active game handle or `UI.exe`, the service immediately calls `SetForegroundWindow` to restore exclusive, full-screen interactive priority.
* **2.3 Process Protection:** Security Access Control Lists (ACLs) are programmatically applied to the running Supervisor process, blocking manual process termination tasks from standard user-level commands.

---

## 3. Game Client Automation & Storefront Portals

Games from third-party launchers (Steam, Epic Games, Xbox) are executed using pass-through token patterns, hiding desktop launcher interfaces completely.

* **3.1 Pass-Through Authentication:** During initial console assembly, sign into the official Steam, Epic Games, and Xbox desktop apps manually once, selecting "Remember Me" and "Auto-Sign In." These encrypted authentication tokens are saved natively within the secure local credential manager.
* **3.2 Direct URI Launch Execution Protocols:** The main user interface triggers games directly via URI parameter strings, completely bypassing the visual launcher storefront navigation paths:
* *Steam:* `steam://run/[GameID]`
* *Epic Games:* `com.epicgames.launcher://apps/[AppName]?action=launch&silent=true`


* **3.3 "Store Mode" Gatekeeping:** When the user enters the "Store" tab on the frontend UI, the frontend issues an IPC event setting `isStoreModeActive = true` within the Supervisor. This instruction temporarily suspends the Focus Guardian watchdog, allowing unrestricted window focus inside the standard launcher store interface.
* **3.4 WMI Process Hijack Interception:** A background WMI Event Watcher utilizing `Win32_ProcessStartTrace` actively listens for newly created game processes. The instant a game executable initializes (e.g., `Fortnite.exe`), the service forces `isStoreModeActive = false`, terminates the background launcher store page window, and forces the primary game window to assume full-screen priority.

---

## 4. Library Management & Local File-Watcher Deltas

The console dashboard grid is populated dynamically by parsing local manifest files and updating real-time download pipelines via recursive directory indexing.

* **4.1 Schema Core:** A local SQLite database (`console_lib.db`) acts as the single source of truth, tracking columns for `GameID (PK)`, `GameName`, `Provider`, `AppID`, `InstallPath`, `Status`, and `ImageURL`.
* **4.2 Manifest Scanners:** A background worker routine scans local storage volumes. For Steam, it parses structural `.acf` text files using Valve's KeyValue/VDF schema to map game titles and AppIDs. For Epic, it reads the JSON arrays located in `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests`.
* **4.3 "Ghost" Tile Progress Calculations:** Rather than relying on unreliable storefront network APIs for tracking installation metrics, the system calculates file sizing changes locally:
1. A `FileSystemWatcher` hooks into temporary staging folders (e.g., `\steamapps\downloading\`).
2. The instant a folder corresponding to a recognized game AppID is initialized, the database inserts a card with `Status = 'Downloading'`.
3. On every file modification step, download metrics are computed directly:

$$\text{Download Progress \%} = \left( \frac{\text{Current Size of Staging Folder}}{\text{Total Expected Size from Manifest}} \right) \times 100$$


4. The value is passed to the UI thread via a thread-safe `Dispatcher.Invoke` method. When the staging directory is cleaned (indicating verification is complete and the game has migrated to its permanent folder), the database switches the status to `Installed`, updating the UI to show a "Play" button.



---

## 5. Input Hooks, Virtual Keyboard, Telemetry & Power Loops

The system maps physical controller actions directly to hardware interrupts and OS functions, removing any reliance on a mouse or keyboard.

* **5.1 Low-Level Controller Daemon:** The isolated `Input.exe` process continuously polls gamepads via a persistent asynchronous loop using `XInputGetState` or the `Raw Input` API, translating analog stick and D-pad movements directly into UI layout navigation.
* **5.2 Global Hardware Interrupt Interceptor:** If the user holds a specific hardware fallback chord (`Guide/Home + Start` simultaneously), the daemon intercepts this action at a low device level. It skips standard OS navigation and passes an immediate `Process.Kill()` command to the active game process handle, cleaning the process tree and forcing focus safely back to the launcher dashboard.
* **5.3 Dynamic Virtual Keyboard:** A controller-navigable QWERTY panel overlays the dashboard screen the moment a search engine or text string field is selected. The D-Pad navigates the key grid, mapping the controller's `A` button to type characters.
* **5.4 XInput Battery Telemetry Subsystem:** The `Input.exe` process automatically checks connected gamepad battery telemetry via native Win32 `XInputGetBatteryInformation` calls.
* *Visual UI Integration:* Displays a multi-segmented status icon (Full/Medium/Low) in the dashboard corner. If the status hits Critical, it fires a toast overlay: `⚠️ CONTROLLER BATTERY LOW`. If it is plugged in via USB, it transitions to a charging/wired icon.
* *Polling Rate:* Executed once every 30 seconds to maintain zero processing overhead during gameplay loop execution.
* *In-Game Suppression:* Battery alerts are automatically queued and muted if a game binary window handle is actively targeted by the Focus Guardian, preventing performance notifications from interrupting gameplay.


* **5.5 Sleep Automation Loop:** An inactivity timer tracks physical XInput telemetry. If no inputs register for 10 minutes, the software dims the display panel. If it hits 20 minutes without an interrupt, it fires a native Win32 power command (`Application.SetSuspendState`), dropping the machine into an `S3` low-power sleep state that wakes up instantly when the power button or controller is tapped.

---

## 6. System Maintenance & Custom UI Pipeline

To maintain the look and feel of a console appliance, updates are split into three distinct categories on the frontend. The system manages background installers invisibly while keeping the user clearly informed of hardware behaviors.

### 6.1 The Maintenance Hub UI Layout

The update system lives natively inside a clean **"Settings > System Maintenance"** page inside your UI. When an update is executing, user controls are locked, the background is darkened, and a full-screen modal overlays the screen:

```
┌────────────────────────────────────────────────────────┐
│                   SYSTEM MAINTENANCE                   │
├────────────────────────────────────────────────────────┤
│  [!] UPDATE DETECTED                                   │
│                                                        │
│  TYPE:    [ Graphics Driver Update ]                   │
│  TARGET:  AMD Radeon RX 6600M (v26.4.1)                │
│  NOTES:   Optimizations for upcoming titles.           │
│                                                        │
│  ⚠️ ATTENTION: During installation, the display        │
│     WILL flicker and black screen temporarily.         │
│     This is completely normal.                         │
│                                                        │
│                 [ A: INSTALL NOW ]                     │
└────────────────────────────────────────────────────────┘

```

### 6.2 The Three Component States

The frontend updates its rendering states based on an integer variable sent by the background tracking layer:

* **Windows OS Updates (State 1):** Mapped from `wuapi.dll` state tracking. Progress displays a clean $0\%$ to $100\%$ linear string with a minimalist loading ring.
* **AMD Driver Updates (State 2):** Triggers an indeterminate, looping progress track (because silent AMD packages do not report linear execution steps). Displays the mandatory warning text: *"WARNING: The screen will flicker, flash black, and audio may cut out briefly while the graphics engine restarts. Do not interrupt this process."*
* **Console Software Patches (State 3):** High-speed processing bar showing internal data validation steps for your custom launcher updates.

### 6.3 Masking Window Spawning

To ensure the raw desktop and application installer environments remain hidden, you must use precise window flag arguments when spawning the install processes from your C# code:

```csharp
Process driverProcess = new Process();
driverProcess.StartInfo.FileName = @"C:\ConsoleDrivers\AMD_Setup.exe";

// The mandatory silent, unattended switches that block native AMD UI popups
driverProcess.StartInfo.Arguments = "-install -s -noreboot"; 

driverProcess.StartInfo.CreateNoWindow = true;
driverProcess.StartInfo.UseShellExecute = false;
driverProcess.Start();

```

*For legacy driver packages or system updates, the service invokes the deployment tool utility silently via `pnputil.exe /add-driver *.inf /install /subdirs` to bypass standard device manager confirmation wizard boxes.*

---

## 7. Fixed Hardware Optimization & Desired-State Tuning

Because this console uses a dedicated, unchanging hardware foundation, the software does not need to dynamically calculate specifications. It instead treats hardware profiles as a declarative policy.

```
┌────────────────────────────────────────────────────────┐
│               DESIRED STATE PROFILE (JSON)             │
│               "Target: 2300MHz @ 950mV"                │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
               ┌─────────────────────────┐
               │    PerformanceTuner     │ ◄──── Telemetry Loops
               │  (State Reconciliation) │       (ADLX / Hardware APIs)
               └────────────┬────────────┘
                            │
            ┌───────────────┴───────────────┐
            ▼                               ▼
     [ State Matches ]             [ State Mismatch ]
            │                               │
            ▼                               ▼
    (Maintain / Log)                (Attempt Inject)
                                            │
                            ┌───────────────┴───────────────┐
                            ▼                               ▼
                   [ Driver Accepts ]              [ Driver Rejects ]
                            │                               │
                            ▼                               ▼
                  (Run 30s Watchdog)               (Log Exception)
                            │                               │
                 ┌──────────┴──────────┐                    │
                 ▼                     ▼                    ▼
            [ Stable ]            [ Instability ]   ┌───────────────┐
                 │                     │            │ SAFE FALLBACK │
                 ▼                     ▼            │ (Load Stock)  │
           (Lock State)         (Rollback State)    └───────────────┘

```

### 7.1 The Desired-State Data Schema

Tuning properties are managed via isolated JSON policies inside `C:\ConsoleSupervisor\GPU_Profiles\`. The system does not assume it controls hardware forever; it constantly reconciles actual hardware telemetry against this policy.

**`RX6600M_Balanced.json`:**

```json
{
  "ProfileName": "RX6600M_Balanced",
  "TargetGPU": "AMD Radeon RX 6600M",
  "DesiredState": {
    "CoreClockMHz": 2300,
    "VoltageMv": 950,
    "PowerLimitPercent": -10,
    "FanTargetCelsius": 75
  },
  "SafetyThresholds": {
    "MaxAllowedCelsius": 85,
    "MinAllowedVoltageMv": 900
  }
}

```

### 7.2 Hardware Reconciliation Logic

The background execution task interfaces with low-level vendor wrapper libraries (like the AMD Display Library / ADLX API) using native P/Invoke calls. Every 60 seconds or immediately upon a system boot post-driver update, it assesses the driver state:

```csharp
public class ConsoleHardwareManager
{
    private readonly string profilePath = @"C:\ConsoleSupervisor\GPU_Profiles\RX6600M_Balanced.json";
    private readonly string stockPath = @"C:\ConsoleSupervisor\GPU_Profiles\RX6600M_Stock.json";

    public void ReconcileHardwareState()
    {
        var targetProfile = ProfileParser.Load(profilePath);
        var currentState = NativeHardwareAPI.GetGPUState(targetProfile.TargetGPU);

        // Check if actual system hardware matches our declared target configuration
        if (!currentState.Matches(targetProfile.DesiredState))
        {
            Logger.Write("Hardware mismatch caught. Attempting state reconciliation...");
            bool injectionAccepted = NativeHardwareAPI.ApplyDesiredState(targetProfile.DesiredState);

            if (injectionAccepted)
            {
                VerifyStateStability(targetProfile);
            }
            else
            {
                Logger.Write("Driver rejected profile configuration. Enforcing safe factory defaults.");
                EnforceSafeFallback();
            }
        }
    }

    private void VerifyStateStability(HardwareProfile profile)
    {
        // Execute a 30-second telemetry monitoring window via low-level stability watchdogs
        bool isStateStable = StabilityWatchdog.EvaluateTelemetry(30, profile.SafetyThresholds.MaxAllowedCelsius);

        if (isStateStable)
        {
            Logger.Write($"Desired State [{profile.ProfileName}] successfully locked.");
        }
        else
        {
            Logger.Write("Instability caught during verification loop! Tripping rollback system.");
            EnforceSafeFallback();
        }
    }

    private void EnforceSafeFallback()
    {
        var stockProfile = ProfileParser.Load(stockPath);
        NativeHardwareAPI.ApplyDesiredState(stockProfile.DesiredState);
    }
}

```

### 7.3 In-Game Performance Assistant Overlay

For games without automatic profile optimizations, the UI leverages the open-source **LibreHardwareMonitorLib** package via NuGet to read hardware diagnostic threads. It renders a subtle, borderless transparent overlay window on top of running games, providing the user with real-time optimization guidance derived directly from VRAM capacity metrics (e.g., *"VRAM close to limit: Reduce texture settings to High"*).

---

## 8. HDMI-CEC Integration

* **8.1 Hardware Requirement:** Pulse-Eight USB-to-HDMI CEC Adapter connected between the GPU and the primary television.
* **8.2 Active Source Selection:** When the console wakes from an `S3` sleep cycle, `ConsoleSupervisor.exe` triggers `cec-client.exe` programmatically to broadcast an `ActiveSource` pulse command. This forces the television to immediately switch its active input channel to the console's HDMI port.
* **8.3 Power State Synchronization:** By hooking into the `Microsoft.Win32.SystemEvents.PowerModeChanged` event framework, the supervisor maps power states bidirectionally:
* `ResumeAutomatic` system events broadcast a `cec-client -p on` command to power the television panel on.
* System idle timeout sleep transitions (`Suspend`) broadcast a `cec-client -p standby` command, putting the connected television into standby mode automatically.



---

## 9. Couch-Play Quality of Life (QoL) Layers

These front-facing user experience rules smooth out typical Windows friction points to preserve the clean, closed-box environment.

### 9.1 The Quick-Access Side Menu ("Guide Menu")

* Pressing the physical **Guide/Home Button** on the gamepad triggers a slide-out overlay pane onto the right side of the screen *on top of the active game*.
* **Available Telemetry & Mapping Hooks:**
* *Volume Slider:* Communicates with master Windows Endpoint Audio Volume APIs.
* *Display Backlight:* Sends DDC/CI monitor control commands to alter hardware TV backlight properties directly.
* *Overlay Toggle:* Activates/deactivates the live performance tracking overlay.
* *System Status Card:* Live system clock time, wireless pad battery cell readout, and network connection state icons.



### 9.2 Smart Boot Splash "Resume-State"

* When the machine wakes up instantly from an automatic `S3` sleep loop via an XInput signal, `UI.exe` handles layout transitions smoothly.
* If a game process was active prior to sleep, the UI renders a localized fullscreen loading asset (`assets/resume_splash.png`) using the game's cached library art with a visual caption string: *"Resuming game play state..."* for 2–3 seconds. This masks the window focus initialization window and driver wake delays seamlessly.

### 9.3 Display Enforcer Engine

* PC software often drops resolution bounds when a TV handshake lags. To guarantee perfect viewport boxing, the `ConsoleHardwareManager` implements a strict initialization intercept loop.
* The instant a game process handle transitions to full focus, the background service forces a strict structural call via Win32 API `ChangeDisplaySettings`, enforcing an explicit `1920x1080 @ 60Hz` configuration directly on the primary viewport space, completely neutralizing window configuration glitches.

### 9.4 Controller Navigation Fallback (Stray Window Navigator)

* PC game environments occasionally leak unmapped modal windows (e.g., EA app sync conflicts, crash reporter text boxes) that completely break gamepad tracking.
* To solve this from the couch without a mouse, the `Input.exe` service handles an override chord step: **Holding the Left Bumper (LB)** shifts the gamepad inputs into an emulation map state:
* *Left Analog Stick:* Maps to smooth system mouse cursor movement vectors.
* *A Button:* Issues a native left mouse click signal.
* *B Button:* Issues a native right mouse click signal.


* Releasing `LB` instantly restores the standard controller configuration profile the moment the prompt is clicked past.

### 9.5 Seamless Sound Device Enforcer

* Windows frequently loses track of HDMI audio routing when a TV or audio receiver powers cycle via CEC commands, leaving the console completely muted until a system restart.
* The service listens for standard hardware interface events. The millisecond the Pulse-Eight adapter initializes the television display device connection via CEC, the software triggers the native Windows CoreAudio engine APIs to explicitly force the active sound routing channel onto the TV's HDMI interface string, blocking Windows from routing sound back to basic motherboard aux outputs.

---

## 10. Production-Grade Implementation Roadmap

To maintain stability during development, follow this strict assembly sequence:

1. **Phase 1 (OS Hardening):** Image the hardware with Windows 10 IoT Enterprise LTSC. Apply registry overrides to substitute `explorer.exe` with your initial test shell, implement stealth boot sequences, and lock down error dialog blocks. Keep UWF disabled for now.
2. **Phase 2 (The Supervisor Core):** Develop `ConsoleSupervisor.exe` with its background polling timer and focus interception hooks. Verify its ability to gracefully restart dead target applications and snatch window focus back to full screen.
3. **Phase 3 (Database & Library Parsers):** Construct the local SQLite database layer. Write the manifest scrapers to parse Steam `.acf` text entries and Epic JSON items into the library grid.
4. **Phase 4 (Input, Battery & Automation Engine):** Build out the XInput polling daemon, integrate the controller battery check loops, and implement the global `Guide + Start` emergency process killer shortcut.
5. **Phase 5 (The Hardware Management Layer):** Implement the declarative JSON profile parser and write the `ConsoleHardwareManager` state checking routines with the 30-second stability test fallback.
6. **Phase 6 (System Freeze):** Enable the Unified Write Filter (UWF) to secure the final operating parameters permanently.