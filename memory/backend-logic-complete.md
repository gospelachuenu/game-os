---
name: backend-logic-complete
description: All backend-only logic from plan.md has been built and tested (125 tests, 14 projects); next work requires either the UI.exe frontend or real target hardware
metadata:
  type: project
---

As of 2026-07-18, every piece of plan.md's spec that could be built and unit-tested without a live UI surface or the real target hardware has been implemented: §2 (Supervisor watchdog + focus guardian), §3 (game launch URIs, store mode gate, WMI process matching), §4 (SQLite schema, Steam/Epic manifest parsing, ghost-tile progress math), §5 (XInput polling seam, kill chord, battery telemetry, sleep automation, mouse emulation), §6 (maintenance UI state classification, silent install command builders), §7 (ADLX reconciliation state machine against a hardware seam), §8 (CEC power-sync command sequencing), §9.4/§9.5 (mouse emulation, sound device routing decision).

**Why this matters:** the user asked to "finish it off with the backend" — this is the natural stopping point for backend-only work. See [[build-progress]] for the full per-project breakdown (14 projects, 125 tests, 0 warnings).

**How to apply:** When resuming this project, don't default to inventing more backend-only slices — the remaining plan.md items genuinely need one of two things:
1. **The UI.exe frontend** (dashboard/tile grid, virtual keyboard, guide menu overlay, resume splash, maintenance hub modal rendering) — a substantial unstarted piece of work, see [[ui-not-yet-built]].
2. **Real target hardware** (the i5-10500T/RX 6600M box) — needed to actually implement and validate: ADLX P/Invoke calls, cec-client.exe/Pulse-Eight adapter integration, IPolicyConfig COM audio routing, wuapi.dll Windows Update tracking, WMI Win32_ProcessStartTrace subscription, FocusGuardian's SetWinEventHook behavior under a real window session, and all of §1's OS-hardening steps (shell replacement, UWF, Autologon, bcdedit).

Next session should ask the user which of these two tracks to pick up, rather than assuming.
