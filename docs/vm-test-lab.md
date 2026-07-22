# VM Test Lab

A VirtualBox machine for testing the system-level parts of the console that must
never be run on a personal development laptop: shell replacement, kiosk lockdown,
the Unified Write Filter, and the stealth boot chain.

Guest OS is **Windows 10 IoT Enterprise LTSC** — the same edition plan.md §1.1
specifies for the real console, so this is testing what actually ships, not an
approximation.

---

## What this VM can and cannot test

| Can test | Cannot test |
|---|---|
| Shell replacement (`explorer.exe` → UI.exe) | GPU / ADLX — no real Radeon |
| Kiosk lockdown (Task Manager, Alt+Tab, Win key) | Controller input — XInput passthrough is unreliable |
| UWF: overlay behaviour, write-through exclusions | Ambient video performance — no GPU acceleration |
| Stealth boot: `bootux`, auto-login, boot ordering | HDMI-CEC — no HDMI |
| Self-update through UWF (the three-boot dance) | Real launcher/game installs — impractical |
| Startup ordering and crash/recovery behaviour | Anything timing-sensitive to real hardware |

The rule of thumb: **this VM tests the OS behaving like an appliance, not the
hardware.** Video will judder and controllers may not work — neither tells you
anything about the real console.

---

## 1. Create the VM

VirtualBox → New. Expert Mode is easier here; the guided wizard hides half of these.

| Setting | Value | Why |
|---|---|---|
| Name | `GamingOS-Lab` | |
| Type / Version | Microsoft Windows / **Windows 10 (64-bit)** | IoT LTSC is Windows 10 underneath |
| RAM | **4096 MB** minimum, 8192 if the host allows | UWF's overlay lives in RAM — too little and it fills fast |
| CPUs | **2–4** | 1 makes the install painfully slow |
| Disk | **60 GB**, dynamically allocated | LTSC installs small, but updates and the overlay need headroom |
| EFI | **Enabled** | Matches the real machine's boot path |

After creating it, open **Settings** and change these — the defaults are wrong for
this use case:

- **Display → Video Memory: 128 MB** (the maximum). The UI is WPF and full-screen;
  the default 16 MB will crawl.
- **Display → Graphics Controller: VBoxSVGA**. VMSVGA has broken WPF rendering in
  some VirtualBox versions.
- **Display → Enable 3D Acceleration: ON**. Helps WPF compositing a little. It is
  *not* real GPU acceleration and the ambient video will still be poor.
- **System → Processor → Enable PAE/NX: ON**.
- **Storage**: attach the Windows 10 IoT Enterprise LTSC ISO to the optical drive.
- **Network**: leave NAT. The console needs internet for the update check; NAT is
  sufficient and needs no configuration.

> **Snapshot discipline is the entire point of this VM.** Shell replacement *will*
> lock you out at some point — no Start menu, no Task Manager, black screen. A
> snapshot is a ten-second rollback; without one it is a full reinstall. Take one
> before every step marked ⚠ below.

---

## 2. Install Windows

Standard install. Two things to get right:

- Choose **"I don't have internet"** / skip the Microsoft account. A local account
  is what the console uses, and a linked MS account complicates auto-login.
- Name the account something predictable — `console` — because the auto-login and
  shell-replacement steps both reference it.

Once at the desktop:

1. Install **VirtualBox Guest Additions** (Devices → Insert Guest Additions CD).
   Without them the display is stuck at a low resolution.
2. Set the display to **1920×1080** to match the target TV.
3. Install the **.NET 8 Desktop Runtime** — the UI build needs it. (Not the SDK;
   the runtime is enough to run a published build.)

### 📸 Snapshot: `01-clean-install`

---

## 3. Get the console UI into the VM

Publish a self-contained build on the host so the VM needs no SDK:

```powershell
# On the HOST, in the repo root
dotnet publish src/UI/UI.csproj -c Release -r win-x64 --self-contained true -o publish/
```

Move `publish/` into the VM. Easiest routes, in order of preference:

- **Shared folder** (Devices → Shared Folders → add the repo folder, auto-mount)
- **Drag and drop** (needs Guest Additions, must be enabled bidirectionally)
- A **zip on a USB** passed through

Put it at `C:\GamingOS\` in the VM — the shell registry value needs an absolute
path, and a short stable one is easier to type when you are recovering from a
broken shell.

**Run it windowed first and confirm it works** before touching the shell. If the
build cannot start normally, it certainly will not start as the shell, and you
will be debugging two problems at once.

### 📸 Snapshot: `02-ui-runs`

---

## 4. Auto-login and stealth boot

Per plan.md §1.3.

**Auto-login** — use Sysinternals `Autologon.exe` rather than editing the registry
by hand; it stores the password encrypted in the LSA rather than as plaintext in
`DefaultPassword`:

```
Autologon.exe console <domain-or-machine-name> <password>
```

**Suppress the boot animation** (admin prompt):

```
bcdedit /set bootux disabled
bcdedit /set quietboot on
```

**Verify by rebooting.** You should go from POST to desktop with no spinner and no
login prompt.

### 📸 Snapshot: `03-autologin`

---

## 5. ⚠ Shell replacement

**This is the step that locks you out. Snapshot first.**

Per plan.md §1.2, but note the plan says `HKCU`. Test **per-user first** — it is far
easier to recover from than the machine-wide `HKLM` equivalent.

```
Registry: HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon
Value:    Shell   (REG_SZ)
Data:     C:\GamingOS\UI.exe
```

If the value does not exist, create it. Reboot.

### Recovery — read this BEFORE rebooting

When the shell is replaced and your app fails, there is no desktop. Options, in
order of preference:

1. **Roll back the snapshot.** Ten seconds. This is why they exist.
2. **Ctrl+Shift+Esc** — Task Manager sometimes still opens. File → Run new task →
   `explorer.exe` gets a desktop back temporarily; `regedit` lets you undo the value.
3. **Safe Mode** — Safe Mode ignores the custom shell and boots to a normal desktop.
   VirtualBox makes reaching the boot menu awkward; enable it in advance while you
   still have a working desktop:
   ```
   bcdedit /set {bootmgr} displaybootmenu yes
   bcdedit /set {bootmgr} timeout 5
   ```
   Do this **now**, not after you are locked out.

### 📸 Snapshot: `04-shell-replaced` (only once it boots successfully)

---

## 6. Kiosk lockdown

Only after the shell is stable. Each of these is independently reversible, so do
them one at a time and reboot between:

- **Task Manager**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System`
  → `DisableTaskMgr` = 1
- **Ctrl+Alt+Del options**: same key → `DisableLockWorkstation`, `DisableChangePassword`
- **Alt+Tab / Win key**: these need a low-level keyboard hook in the app itself
  rather than a registry setting — the `FocusGuardian` piece in ConsoleSupervisor
- **Error dialogs / auto-restart**: `HKLM\SYSTEM\CurrentControlSet\Control\Windows`
  → `ErrorMode` = 2

Keep Task Manager **enabled** until everything else is proven. It is your last
recovery route short of a snapshot rollback.

### 📸 Snapshot: `05-locked-down`

---

## 7. ⚠ UWF — the one that unblocks three features

This is the most valuable thing this VM can tell you, because three separate
features are waiting on the same answer: the parent PIN, the setup-complete marker,
and downloaded-update state all need to survive a reboot under the write filter.

**Install** (IoT Enterprise only — this is why the edition matters):

```powershell
dism /online /enable-feature /featurename:Client-UnifiedWriteFilter /all
```

Reboot, then:

```powershell
uwfmgr filter enable
shutdown /r /t 0
```

**Confirm it is doing something:** create a file on the desktop, reboot, watch it
vanish. That is the overlay working as designed — and exactly why the console's
persistent state needs handling.

**Then work out the write-through exclusions**, which is the actual goal:

```powershell
uwfmgr file add-exclusion C:\GamingOS\State
uwfmgr volume set-writethrough C:
uwfmgr get-config
```

Test by writing to that path, rebooting, and confirming the file survived.

**What to find out and bring back:**

- Does a file-level exclusion survive reboot reliably?
- How large does the RAM overlay get during normal running? (`uwfmgr overlay get-consumption`)
- What happens when the overlay fills — graceful, or does it fall over?
- Does the console's own settings path need excluding, or just the state folder?

### 📸 Snapshot: `06-uwf-enabled`

---

## 8. Self-update through UWF

Only once §7 is understood. The three-boot dance:

1. Disable the filter (`uwfmgr filter disable`), reboot
2. Apply the update, reboot
3. Re-enable the filter (`uwfmgr filter enable`), reboot

The thing to test here is **failure**, not success: pull the VM's power (Machine →
Reset) partway through step 2 and see what state the machine comes back in. A kid
switching the console off mid-update is not a hypothetical, and the console must
survive it.

---

## Quick reference

```powershell
uwfmgr get-config                      # everything, current and pending
uwfmgr filter enable | disable         # takes effect after reboot
uwfmgr overlay get-consumption         # how full the RAM overlay is
uwfmgr file add-exclusion <path>       # survive reboots
uwfmgr file remove-exclusion <path>

bcdedit /set bootux disabled           # no boot animation
bcdedit /set {bootmgr} displaybootmenu yes   # rescue route — set this EARLY
```

**Shell registry value:**
`HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon` → `Shell`

---

## Snapshot summary

| Snapshot | Take it after |
|---|---|
| `01-clean-install` | Windows + Guest Additions + .NET runtime |
| `02-ui-runs` | UI.exe confirmed working windowed |
| `03-autologin` | Auto-login and stealth boot verified |
| `04-shell-replaced` | Shell replacement boots successfully |
| `05-locked-down` | Kiosk restrictions applied |
| `06-uwf-enabled` | UWF on and exclusions understood |
