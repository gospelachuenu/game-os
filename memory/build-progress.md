---
name: build-progress
description: What has actually been built/tested so far in the Gaming OS codebase, project by project
metadata:
  type: project
---

Solution `c:\Gaming_OS\GamingOS.sln`, built incrementally per plan.md §10's phased roadmap but starting from the Supervisor core rather than strictly Phase 1 first (OS hardening steps in §1 are execute-only actions deferred to real hardware — see [[dev-environment-constraint]]).

**Why building this way:** user asked to "take it step by step" rather than scaffold all sections at once; each piece is built, unit-tested (via fakes/seams, never live execution), and confirmed building clean before moving to the next.

**Projects implemented so far:**

1. **`src/ConsoleSupervisor`** (net8.0-windows exe) — plan.md §2, the Supervisor daemon.
   - `ModuleWatchdog.cs` — polls every 500ms, restarts dead managed modules. Deliberately built behind an `IProcessLauncher`/`IProcessHandle` seam (see `IProcessLauncher.cs`, `Win32ProcessLauncher.cs`) so it's unit-testable with fakes instead of real `Process.Start` calls — this pattern should be reused for any future Supervisor logic that needs testing without live execution.
   - `FocusGuardian.cs` + `NativeMethods.cs` — `SetWinEventHook`-based focus reclaim (§2.2). Written but NOT executed/tested live (requires real window session) — validate this only on target hardware.
   - `ManagedModule.cs` — config record for each watched module (UI/Input/CEC/Monitor/Hardware). Paths currently point at placeholder `stubs/*.exe` — the real UI.exe frontend has not been built yet.
   - `Program.cs` — wires watchdog + focus guardian together; not run in this dev environment.

2. **`src/ConsoleSupervisor.Tests`** (xUnit, net8.0-windows target — must match since it references the -windows project) — 6 tests covering watchdog launch-once, no-restart-while-alive, restart-after-exit, event propagation, start-failure handling, per-module failure isolation. All using `FakeProcessLauncher`, no real processes.

3. **`src/ConsoleLibrary`** (net8.0 class library) — plan.md §4, library/data layer.
   - `GameEntry.cs` — data model matching §4.1's schema (GameId, GameName, Provider, AppId, InstallPath, Status, ImageUrl).
   - `GameLibraryDatabase.cs` — SQLite-backed `console_lib.db` via `Microsoft.Data.Sqlite` (pinned to 8.0.10 to match net8.0, NOT the default 10.x that `dotnet add package` resolves to). Connection pooling explicitly disabled (`Pooling = false`) — this fixed a real file-locking test failure AND is the right call for the actual kiosk deployment, since UWF's RAM-overlay + a process that may not shut down cleanly makes pooled handles a "database locked" risk.
   - `Steam/VdfParser.cs` — real recursive-descent parser for Valve's KeyValue/VDF format (handles nesting, escaped quotes, line comments, case-insensitive keys) — written from scratch, not regex.
   - `Steam/SteamManifestScanner.cs` — parses `appmanifest_*.acf` into `GameEntry`; StateFlags==4 maps to Installed, anything else maps to Unknown (deliberately does NOT try to guess "Downloading" from StateFlags — that's the FileSystemWatcher/GhostTileProgress's job per §4.3, not the manifest scanner's).
   - `Epic/EpicManifest.cs` + `EpicManifestScanner.cs` — parses Epic's `.item` JSON manifests (DisplayName, CatalogItemId, AppName, InstallLocation, bIsIncompleteInstall) into `GameEntry`.
   - `GhostTileProgress.cs` — pure function implementing §4.3's percentage formula, clamped 0-100, handles zero/negative expected-size gracefully.

4. **`src/ConsoleLibrary.Tests`** (xUnit, net8.0) — 32 tests covering VDF parsing edge cases, Steam/Epic manifest scanning (including corrupt-file tolerance via temp dirs), ghost-tile math, and full SQLite round-trip/upsert/status-update behavior.

5. **`src/Input`** (net8.0-windows exe, RootNamespace `InputDaemon`) — plan.md §5 + §9.4, the Input.exe daemon.
   - `XInputNative.cs` — P/Invoke surface for `xinput1_4.dll` (XInputGetState, XInputGetBatteryInformation), plus XInputButtons flags enum, BatteryType/BatteryLevel enums.
   - `IGamepadReader.cs` + `XInputGamepadReader.cs` — same fake-seam pattern as ConsoleSupervisor's IProcessLauncher: an interface wrapping the native calls so polling/chord logic is testable without a real controller attached.
   - `EmergencyKillChordDetector.cs` — §5.2's Guide+Start emergency kill chord. Edge-triggered (fires once per fresh press, not once per poll while held); ignores the chord if the controller is disconnected.
   - `BatteryTelemetryMonitor.cs` — §5.4. Classifies raw battery telemetry into a UI state (Full/Medium/Low/Critical/Charging/Unknown) and implements in-game suppression of the Critical toast (never shown while a game has focus).
   - `SleepAutomation.cs` — §5.5's inactivity ladder (dim at 10min idle, suspend at 20min), as a pure function of elapsed idle TimeSpan so it doesn't need a real Timer to test.
   - `MouseEmulationMapper.cs` — §9.4's Stray Window Navigator (holding LB remaps left stick to cursor movement, A/B to left/right click). Pure translation math with deadzone handling; actual SendInput/cursor calls deliberately left outside this class.
   - `Program.cs` — wires the poll loop together (~60Hz gamepad poll, 30s battery poll per §5.4); not run in this dev environment, same as ConsoleSupervisor's Program.cs.

6. **`src/Input.Tests`** (xUnit, net8.0-windows) — 33 tests covering kill-chord edge-triggering/disconnection handling, battery state classification and toast suppression, sleep-ladder thresholds (exact boundary values at 10/20 min), and mouse-emulation deadzone/axis-inversion/click-mapping.

7. **`src/GameLauncher`** (net8.0 console exe, RootNamespace `GameLauncher`, references ConsoleLibrary) — plan.md §3, game client automation.
   - `LaunchUriBuilder.cs` — builds `steam://run/{appid}` and `com.epicgames.launcher://apps/{appname}?action=launch&silent=true` URIs from a `GameEntry` (§3.2). Epic app IDs are URI-escaped.
   - `StoreModeGate.cs` — tracks `isStoreModeActive` (§3.3); a WMI-observed game process start (`OnGameProcessObserved`) always forces it back off regardless of what the UI thinks the current tab state is (§3.4), raising `StoreModeExited` only on an actual active→inactive transition.
   - `GameProcessMatcher.cs` — pure matching logic for §3.4's WMI `Win32_ProcessStartTrace` interception: given an observed process image name + the library + a GameId→executable-name map, finds which tracked game just started. The actual WMI event subscription is deliberately left outside this class.
   - `Program.cs` — placeholder entry point; the real consumer of this logic will be ConsoleSupervisor, not a standalone process. Not run in this dev environment.

8. **`src/GameLauncher.Tests`** (xUnit, net8.0) — 14 tests covering Steam/Epic URI construction (including escaping), Store Mode gate transitions and event-firing-only-on-real-transition, and WMI process-name matching (case-insensitivity, multi-game library, no-match/no-mapping cases).

9. **`src/HardwareManager`** (net8.0 class library) — plan.md §7, GPU tuning/reconciliation logic.
   - `GpuProfile.cs` — data model matching §7.1's JSON schema exactly (ProfileName, TargetGPU, DesiredState{CoreClockMHz,VoltageMv,PowerLimitPercent,FanTargetCelsius}, SafetyThresholds{MaxAllowedCelsius,MinAllowedVoltageMv}).
   - `GpuProfileParser.cs` — parses/loads the JSON profile files (e.g. `RX6600M_Balanced.json`).
   - `IGpuHardwareApi.cs` — seam over the real ADLX/vendor wrapper (§7.2's NativeHardwareAPI). The REAL ADLX P/Invoke implementation is NOT written yet — it needs the actual RX 6600M board (see [[project_overview]]'s ADLX spike note). `GpuState.Matches()` does exact-field comparison, deliberately not tolerance-banded, so drift is never masked.
   - `StabilityWatchdog.cs` — pure evaluation over a sequence of temperature samples vs. max-allowed-celsius; the real 30-second wall-clock sampling cadence lives outside this class so tests don't need to wait 30 seconds.
   - `ConsoleHardwareManager.cs` — the full §7.2 reconciliation state machine (already-matched / inject-and-lock-if-stable / rollback-on-driver-rejection / rollback-on-instability), built against `IGpuHardwareApi` and an injectable temperature-sampling function + optional logger callback.

10. **`src/HardwareManager.Tests`** (xUnit, net8.0) — 21 tests covering profile JSON round-trip (using plan.md's exact sample JSON), GpuState exact-match semantics (every field independently), stability watchdog boundary conditions (exactly-at-threshold, one-over, late-in-window exceedance), and all four reconciliation outcome branches via `FakeGpuHardwareApi`.

11. **`src/CecController`** (net8.0 class library) — plan.md §8 + §9.5, HDMI-CEC and audio routing decision logic.
    - `ICecClient.cs` — seam over `cec-client.exe` process invocation. The real Pulse-Eight adapter/process invocation is NOT implemented — needs a live TV+adapter to exercise.
    - `CecCommands.cs` — the exact command strings from plan.md §8.2/§8.3 (`-as` ActiveSource, `-p on`, `-p standby`), centralized so they're defined once.
    - `PowerStateSyncEnum.cs` — `PowerStateTransition` enum (ResumeFromSleep/EnteringSuspend), decoupled from the real `Microsoft.Win32.SystemEvents.PowerModeChanged` event args so the mapping is testable without a real OS power transition.
    - `CecPowerSyncController.cs` — §8.2/§8.3's logic: on resume, send PowerOn then ActiveSource (in that order — TV needs to be powered before it'll honor an input-switch pulse); on suspend, send standby only.
    - `SoundDeviceEnforcer.cs` + `IAudioEndpointRouter.cs` — §9.5's logic: when CEC reports the TV's HDMI audio endpoint connected, force-route default audio there. The real `IPolicyConfig` COM interop (undocumented Windows API) is NOT implemented — needs a live audio session to exercise.

12. **`src/CecController.Tests`** (xUnit, net8.0) — 7 tests covering CEC command sequencing/ordering per transition, that suspend never sends ActiveSource and resume never sends standby, multi-transition sequences, and audio endpoint routing (including re-routing on repeated connect events).

13. **`src/MaintenanceHub`** (net8.0 class library) — plan.md §6, System Maintenance & update UI pipeline logic.
    - `UpdateType.cs` — the three update categories as an enum backed by the exact integer state values from §6.2 (1=WindowsOsUpdate, 2=AmdDriverUpdate, 3=ConsoleSoftwarePatch).
    - `ProgressDisplayMode.cs` — LinearPercent / Indeterminate / HighSpeedProcessingBar.
    - `MaintenanceUiClassifier.cs` — maps UpdateType → (ProgressDisplayMode, warning text). AMD driver updates carry the exact mandatory warning text from §6.2 verbatim (flicker/black screen/audio cutout/do-not-interrupt); the other two states carry no warning.
    - `IWindowsUpdateTracker.cs` — seam over wuapi.dll's Windows Update Agent COM API (§6.2 State 1). Real implementation NOT written — needs a live WU session.
    - `SilentInstallCommand.cs` + `SilentInstallCommandBuilder.cs` — builds the exact §6.3 command lines (`AMD_Setup.exe -install -s -noreboot`, `pnputil.exe /add-driver *.inf /install /subdirs`) with CreateNoWindow/UseShellExecute always forced to mask the installer UI. Returns a value describing what to run rather than spawning it — actual process invocation stays unexecuted on this dev laptop per [[dev-environment-constraint]].

14. **`src/MaintenanceHub.Tests`** (xUnit, net8.0) — 12 tests covering UI-state classification for all three update types (including the exact mandatory AMD warning text content and that Windows/Console states carry no warning), the enum's backing integers matching plan.md's State 1/2/3 numbering, and both silent-install command builders (exact flags + masking always on).

**Total: 125 tests passing (6 Supervisor + 32 Library + 33 Input + 14 GameLauncher + 21 HardwareManager + 7 CecController + 12 MaintenanceHub), whole solution builds clean with 0 warnings.**

This completes all backend logic identified as feasible to build/test without live UI or target hardware — see [[backend-logic-complete]] for what's left and why.

**NOT yet built (all require either the UI.exe frontend or real target hardware):**
- The actual `UI.exe` frontend (dashboard/tile grid, virtual keyboard, guide menu overlay, maintenance hub modal rendering) — this is a separate, large piece of work not yet started or scoped. See [[ui-not-yet-built]].
- Live `FileSystemWatcher` wiring into `steamapps/downloading/` (the watcher itself is buildable/testable via temp-dir file events even on this laptop — just not done yet, could still be done backend-only if picked up later).
- §5's dynamic virtual keyboard overlay, §9.1 guide menu overlay, §9.2 resume splash, §9.3 display enforcer — all need a real rendering/window surface.
- §3.1's actual pass-through auth / credential manager reliance is manual, one-time user setup (sign into Steam/Epic/Xbox apps), not something to build in code.
- The real WMI `Win32_ProcessStartTrace` event subscription, ADLX P/Invoke, `cec-client.exe` invocation, `IPolicyConfig` COM interop, wuapi.dll COM tracking, and actual driver/patch process spawning — all need live hardware/OS sessions; only the decision/mapping logic around each is built/tested.
- Dev environment note: this laptop had no .NET SDK initially (only the runtime) — installed via `winget install Microsoft.DotNet.SDK.8`. Must run `export PATH="$PATH:/c/Program Files/dotnet"` in Bash tool sessions since it's not on PATH by default there.
- Recurring gotcha: any new xUnit test project that references a `net8.0-windows` project must itself target `net8.0-windows` (not plain `net8.0`), otherwise `dotnet add reference`/restore fails with an incompatible-target-framework error. Hit this twice now (ConsoleSupervisor.Tests, Input.Tests).
