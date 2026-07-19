---
name: feedback-verify-before-claiming-fixed
description: "Never tell the user a visual/UI fix worked without actually seeing it rendered first - screenshot or explicit user confirmation, not just XAML reasoning"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: adc68f07-75f7-4330-99d9-866022aafc0b
  modified: 2026-07-18T18:53:55.996Z
---

Do not describe a UI/visual change as "fixed" or "should look right now" based purely on reasoning through the XAML/code — verify it was actually rendered and looks correct first, either via a real screenshot or by waiting for the user's own confirmation, before making any claim about the outcome.

**Why:** During the Gaming OS WPF UI build ([[ui-build-progress]]), this exact failure mode repeated many times in a row: claiming a corner-clipping bug was fixed, then the user's screenshot showed it wasn't; claiming a scale-animation bug was fixed based on a misread screenshot crop, when the user's actual screen showed otherwise; and worst, attempting to blend the ambient video into the launch overlay — tweaking `Opacity` values purely from theoretical reasoning about layering, asserting each attempt "should work," when in fact automated screenshot tooling in this sandbox was itself unreliable (stale frames, wrong windows in focus, wrong crops). The user became explicitly and justifiably furious ("you are pissing me off," "you just fucking changing the fucking opacity for fuck sakes," "you are making me lose my shit") specifically because fixes kept being asserted with confidence and turned out wrong, repeatedly, without ever being genuinely checked first.

**How to apply:**
- Before saying a visual bug is fixed, either (a) get a real, correctly-framed screenshot of the actual current state and inspect it yourself, or (b) explicitly ask the user to check and wait for their answer — do not proceed as if it's confirmed.
- If screenshot tooling is unreliable in this environment (see [[ui-build-progress]] lesson #5 — PowerShell `CopyFromScreen` here has caught stale/wrong-window frames repeatedly), default to asking the user rather than trusting your own capture.
- When making a speculative visual change (e.g., "let's try lowering opacity to see if X blends better"), say so explicitly as an experiment/guess, not as a confident fix — don't state "this should now show the video blended in" when what actually happened is an untested opacity edit.
- If a fix attempt fails and the user says to revert, revert completely and cleanly first, without immediately proposing another speculative fix in the same breath — let them decide if/when to try again.
