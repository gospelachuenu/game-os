---
name: ui-not-yet-built
description: "Status of the real console UI.exe frontend vs. the windowed test build - don't conflate the two"
metadata: 
  node_type: memory
  type: project
  originSessionId: adc68f07-75f7-4330-99d9-866022aafc0b
  modified: 2026-07-18T17:51:44.588Z
---

As of 2026-07-18: a **windowed test build** of the dashboard exists at `src/UI` (see [[ui-build-progress]] for full detail — Terminal-style layout, crossfaded video background, theming, focus animation, physical battery icon). This runs as a normal desktop window on the user's dev laptop for visual iteration.

The **real console `UI.exe`** — fullscreen/kiosk, integrated with `ConsoleSupervisor`'s `FocusGuardian`, driven by real `Input.exe` XInput gamepad polling instead of arrow keys, running on the actual target hardware — has NOT been built. `ManagedModule` config in `ConsoleSupervisor` still just reserves the name/path `stubs/UI.exe` as a placeholder.

**Why this distinction matters:** don't say "the UI isn't built" (it partially is, as a test build) or conflate the windowed test build's arrow-key navigation/theming choices with final decisions for the real fullscreen console UI — the windowed build was explicitly meant for visual/design iteration only, per [[dev-environment-constraint]].

**How to apply:** When resuming, check whether the user wants to keep iterating on the windowed test build (more polish, real gamepad wiring via the existing `Input` project's `XInputGamepadReader`, wiring the launch action) or wants to start porting/adapting it into the real fullscreen `UI.exe` once target hardware is available.
