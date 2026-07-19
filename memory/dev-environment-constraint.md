---
name: dev-environment-constraint
description: Critical constraint - this dev machine is the user's personal laptop/test system, not the target console hardware; never execute/run anything system-level or destructive here
metadata:
  type: feedback
---

Development for this project currently happens on the user's personal daily-driver laptop, NOT the target console hardware. The target console hardware (fixed spec: i5-10500T, RX 6600M, 16GB RAM) has not arrived/been assembled yet.

**Why:** The user interrupted and explicitly stopped a tool call when they realized I was about to launch/run a stub executable (copying notepad.exe as a stand-in "UI.exe" and running the Supervisor against it) on their personal laptop. They clarified this is their own laptop/test system, not the console box, and asked to keep things to "just a regular windowed exe file" until the real target machine is available. They separately re-emphasized "don't do anything destructive in here just a test system that is all" when a project-reference edit was made.

**How to apply:** On this laptop, restrict all work to writing and compiling code only:
- `dotnet build` and `dotnet test` are fine (compiler + in-process test assertions, no visible/system side effects).
- Do NOT `dotnet run` the ConsoleSupervisor or any component that installs Win32 hooks (SetWinEventHook focus guardian), steals foreground/focus, spawns visible windows, or otherwise touches live session/window state.
- Do NOT touch registry, do Autologon setup, run bcdedit, enable UWF, or execute any of plan.md §1's stealth-boot/shell-replacement steps for real — those are code/scripts to write and review, not to execute here.
- Do NOT run destructive operations (deleting/overwriting files outside scratch/temp, force-pushes, resets) beyond normal reversible dev work (creating projects, editing source, running the test suite).
- For code that needs a live window/focus/session to meaningfully exercise (FocusGuardian, CEC, XInput daemon, UI rendering), validate by careful code reading and reasoning, or via unit tests against fakes/seams — not by running the real thing. See [[build-progress]] for the IProcessLauncher/IGamepadReader seam pattern already used for this.
- This constraint lifts once the user has the actual target hardware — re-confirm with the user before assuming it's safe to start running things, don't assume based on elapsed time alone.
