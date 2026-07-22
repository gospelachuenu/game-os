# Publishing a console update

The console checks for updates at boot by fetching a `version.json` manifest over HTTPS.
Pushing an update is publishing a new manifest and build — **no code change, no per-console
step.** Every console picks it up at its next boot.

## One-time setup per console

Point the console at your manifest by setting an environment variable before it launches:

```
GAMINGOS_UPDATE_MANIFEST=https://github.com/<user>/<repo>/releases/latest/download/version.json
```

Unset, the console uses the built-in simulated source — which is why the dev build still
works without a server.

## Publishing a release

1. **Build a self-contained copy:**

   ```
   dotnet publish src/UI/UI.csproj -c Release -r win-x64 --self-contained true -o out/
   ```

2. **Zip `out/`** as e.g. `UI-1.1.0.zip`.

3. **Write `version.json`:**

   ```json
   {
     "version": "1.1.0",
     "packageUrl": "https://github.com/<user>/<repo>/releases/download/v1.1.0/UI-1.1.0.zip",
     "sizeBytes": 71680000,
     "notes": [
       { "kind": "New", "text": "Music now plays in the background" },
       { "kind": "Fixed", "text": "Controller no longer drops on YouTube" },
       { "kind": "Image", "text": "The new app drawer", "image": "shots/drawer.png" }
     ]
   }
   ```

   - `version` MUST be higher than what is installed, compared numerically (1.10.0 beats
     1.9.0). Same or lower = the console ignores it.
   - `notes` become the patch-notes screen shown after the update. `kind` is
     `New`, `Improved`, `Fixed`, `Image`, or `Video` (case-insensitive). Unknown kinds are
     skipped, so a newer manifest never breaks an older console.
   - Image/Video `image` is a path **relative to the update package**, never a URL —
     patch-note images are untrusted content resolved strictly inside the package.

4. **Attach both** `UI-1.1.0.zip` and `version.json` to a GitHub Release tagged `v1.1.0`.
   The `latest/download/` URL always resolves to the newest release, so the manifest URL
   never has to change.

## What the console does with it

`GitHubUpdateSource` fetches the manifest (with a cache-buster, since GitHub's raw endpoint
caches hard), compares versions, and — if newer — hands a `SoftwareRelease` to the existing
check → download-in-background → install-next-boot → show-patch-notes flow. That flow was
already built and tested; this only replaced the stubbed "is there an update" source.

## What installs the update

When the manifest URL is set, the console uses the **real downloader** (`HttpUpdateDownloader`,
with resume) and the **real installer** (`UpdateInstaller`):

1. Boot check sees a newer version, downloads the zip in the background.
2. Next boot, the console offers to install. Accepting unpacks the zip to a staging folder
   (validated — it must contain an .exe, or it is rejected before any swap).
3. A small `.cmd` is launched and the console **exits**. The script waits for the process
   to close, renames the live install aside, moves the new build into place, and then
   **reboots the machine**. A crash mid-swap leaves either the old or the new build, never
   a broken mixture.
4. The console comes back on the new build after the restart. The swap happens inside the
   reboot's own black screen, so the user never sees the Windows shell — it reads as
   "restarting to update", like a real console.
5. On the real appliance the script toggles UWF around the swap; on the VM (no filter) that
   step is skipped automatically, so the same package works on both. UWF also only re-arms
   across a reboot, which is why rebooting to finish is required there anyway.

**Testing tip:** set `GAMINGOS_UPDATE_RELAUNCH=1` on the VM to relaunch the app in place
instead of rebooting, so you can test updates repeatedly without a full restart each time.
Leave it unset on the real console for the clean reboot.

With no manifest URL set, the windowed dev build simulates all of this — nothing is really
replaced — so the flow stays demonstrable on the laptop.

## Testing it on the VM

1. Publish a build and a `version.json` to a GitHub Release as above, with a version HIGHER
   than what the VM is running.
2. In the VM, set `GAMINGOS_UPDATE_MANIFEST` to the manifest URL before the console starts.
3. Boot the console. It checks, downloads in the background, and offers the install on the
   next boot. Accept it and confirm the VM comes back up on the new build.

This is the piece that could not be tested on the dev laptop — file swapping while the app
is not running — and is exactly what the VM exists to verify.
