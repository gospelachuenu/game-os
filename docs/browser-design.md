# Console Browser — Design

A WebView2-based browser as its own console screen, for three jobs: **YouTube**,
**light general browsing**, and **downloading launcher/game installers**.

Status: **design only, nothing built.**

---

## The governing rule: it follows the device mode

The browser is not "a locked-down kid's browser". Like the launch gate, its
restrictions are **dormant unless the console is a child device** — because the console
is distributed to several people, not all of them under restrictions.

| Device mode | Browser behaviour |
|---|---|
| **Unrestricted** (default) | A normal browser. No filtering, no allowlist, downloads work without a PIN. |
| **ChildDevice** | `BrowserPolicy` enforced: access level, allowlist, downloads PIN-gated. |

This is the same `ParentalControlsService.IsActive` gate the launch path already uses,
so there is one concept to understand rather than two.

`BrowserPolicy` already exists in `src/ParentalControls/ParentalPolicy.cs`:
`Off / AllowlistOnly / YouTubeOnly / Open`, a domain allowlist, and `BlockDownloads`.

---

## Downloads — the part that undermines everything else

Downloading installers is in direct tension with filtering: the whole point is that an
executable lands on disk and gets run. That is precisely what you would otherwise block
on a child's device, and no filter is perfect.

**Resolution: on a child device, ANY download requires the parent PIN.** The child can
browse (filtered); the moment something tries to download, the PIN is demanded. The
parent installs launchers themselves; the child cannot fetch executables at all.

On an unrestricted console downloads just work — no PIN, no prompt.

WebView2 supports this directly: `CoreWebView2.DownloadStarting` fires before a
download begins and can be cancelled, so the PIN prompt sits in that handler.

---

## Filtering (child mode only)

**Recommendation: a family-safe filtering DNS, not an in-app blocklist.**

Being honest about the alternatives:

- **An in-app blocklist would always lag.** Maintaining a list of bad domains is a
  full-time job for entire companies; a hand-rolled one gives the appearance of safety
  without the substance.
- **A strict allowlist breaks YouTube.** YouTube alone pulls from a dozen domains
  (`googlevideo.com`, `ytimg.com`, `ggpht.com`, …). An allowlist permissive enough to
  let it work is close to permissive enough to be pointless.
- **A filtering DNS is maintained by people whose job it is**, covers the whole device
  rather than just this browser, is free, and cannot be bypassed by the browser itself.

The console-side allowlist stays for the `AllowlistOnly` mode — useful for a very tight
"only these sites" setup — but should not be sold as the primary defence.

---

## Screen design

Its own console screen, opened from a rail button — the same overlay pattern as
Updates and Settings (ambient video keeps playing behind, `Opened`/`CloseRequested`,
`ContentGrid` hidden).

```
+--------------------------------------------------+
|  [<]  [>]  [reload]    example.com          [X]  |   <- chrome, controller-navigable
+--------------------------------------------------+
|                                                  |
|                  WebView2 content                |
|                                                  |
+--------------------------------------------------+
|  A Select   B Back   Y Address   MENU Home       |   <- button hints
+--------------------------------------------------+
```

**Controller navigation is the hard part of the UI.** A web page is not a console
screen: there is no fixed set of focusable tiles. Options, in order of preference:

1. **Cursor mode** — the left stick drives a pointer, A clicks. Universally works on
   any page, familiar from smart TVs and the Steam Deck. Recommended.
2. **Spatial focus** — D-pad moves between the page's focusable elements via injected
   JavaScript. Feels more native but breaks on pages that fight it.

A cursor is the honest choice: it always works, and a browser is inherently a
pointer medium.

---

## YouTube

`youtube.com/tv` is the TV-optimised interface and is the right target — it is designed
for D-pad navigation, so it sidesteps the cursor problem entirely for the main use case.

It requires a **spoofed user agent** (a TV/console UA) or it redirects to the desktop
site. WebView2 supports setting the UA per-navigation.

Whether to make this a separate tile or a bookmark inside the browser is open.

---

## Practical traps found while researching

**1. The WebView2 Runtime must be present.**
It is not part of Windows on all versions. The console build has to either ship the
**Evergreen Standalone (offline) Installer** or check for the runtime and install it at
setup. On a UWF-protected machine this must happen BEFORE the write filter is enabled
(the same ordering rule as everything else). Checking at runtime and failing gracefully
is required either way — a missing runtime must show "Browser unavailable", not crash.

**2. Cancelling `NavigationStarting` does NOT stop the fetch.**
A documented WebView2 issue: the resource is still downloaded even when navigation is
cancelled — it just is not displayed. So blocking navigation is **not** a privacy
guarantee, only a display guarantee. Worth knowing before claiming the allowlist
"prevents" anything at the network level. Another argument for DNS-level filtering.

**3. Subscribing to `DownloadStarting` changes SmartScreen behaviour.**
With SmartScreen disabled, potentially-harmful downloads get blocked with a "File was
blocked" message when the app subscribes to `DownloadStarting`. Needs testing on real
hardware with a real installer download.

---

## Open questions

- Separate YouTube tile, or a bookmark inside the browser?
- Which filtering DNS (there are several free family-safe resolvers) — and is it set
  console-wide at setup, or per-policy from the portal?
- Does the browser get a Home page / bookmarks, and are those parent-editable?
- History: kept and visible to a parent, or not kept at all?

---

## Build order

1. **The screen shell** — rail button, overlay, chrome, close. No web content yet.
2. **WebView2 hosted**, unrestricted only. Prove it renders and navigates.
3. **Cursor navigation** from the controller.
4. **Mode-dependent policy** — allowlist, access levels, wired to `BrowserPolicy`.
5. **PIN-gated downloads.**
6. **YouTube TV mode** with the spoofed UA.

Steps 1–3 need no parental work and are testable immediately; 4–5 build on the
parental service that already exists.
