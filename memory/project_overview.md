---
name: project-overview
description: What the Gaming OS console appliance project is, its spec source, and overall status
metadata:
  type: project
---

The user is building a custom console-appliance software stack on fixed hardware (Intel i5-10500T, AMD Radeon RX 6600M, 16GB RAM, 1080p TV target), turning a Windows PC into a closed retail-console-like experience. The full spec lives at `c:\Gaming_OS\plan.md` — "MASTER CONSOLE ARCHITECTURE SPECIFICATION" (10 sections: OS/stealth boot, Supervisor watchdog, game client automation, library management, input/telemetry, system maintenance UI, GPU tuning, HDMI-CEC, couch-play QoL, implementation roadmap).

**Why:** Repurposing a PC as a dedicated console — no desktop chrome, controller-only navigation, auto-launches games, silent updates.

**How to apply:** Treat plan.md as the living spec. When starting a new session on this project, read plan.md fresh (it may have been edited) rather than relying on memory of its contents. See [[dev-environment-constraint]] for the critical constraint on how work happens, and [[build-progress]] for what's implemented so far.

Target OS is Windows 11 IoT Enterprise LTSC — user confirmed they can obtain this license easily, so that risk is resolved (was originally flagged as the main non-technical blocker).

The one open technical unknown flagged in the spec is §7 (ADLX GPU overclocking layer) — the SDK/interop path is proven and has prior art (AMD's official ADLX SDK C# samples, JamesCJ60/ADLX-SDK-Wrapper), but whether the specific RX 6600M board allows programmatic voltage/clock writes (vs. read-only telemetry) depends on the board vendor's vBIOS lock. User confirmed the 6600M is a standalone/custom board, not a laptop OEM part — this rules out the worst case (laptop vendor locking the write path entirely in firmware), but board-level voltage locks independent of "laptop vs desktop" are still possible and untested. Recommended approach: a small throwaway ADLX spike (test read/write on the actual board) once hardware is in hand, before investing in the full §7.2 reconciliation-loop architecture — this has NOT been done yet.

Note: this memory bank is mirrored in two locations — `C:\Users\Gospel A\.claude\projects\c--Gaming-OS\memory\` (Claude Code's own persistent store) and `c:\Gaming_OS\memory\` (inside the project folder itself, at the user's request, so it's visible alongside the code). Keep both in sync when updating.
