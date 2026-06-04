---
name: verify-scoring-app-visually
description: How the user wants the scoring app launched for a visual/manual check of the score-entry UI
metadata:
  type: feedback
---

To visually check the scoring app's score-entry form, add a **Visual Studio launch profile**
(in `MahjongScoring/Tsump.Scoring/Properties/launchSettings.json`) with `launchBrowser: true`
and `launchUrl: "score#p=<payload>"`, so the user can F5 it from VS in a real browser. Do **not**
build elaborate headless-Chrome / CDP automation for this.

**Why:** The user runs from Visual Studio and wants to see/drive the real app themselves. The
`/score` page needs a real wire-format invite payload in the URL fragment (mint one with
`ScoringPayloadCodec.EncodeInvite`, which is pure and copyable into a throwaway console). The
transposed score-entry layout only appears below 500px (width or height), so they narrow the
window / use the browser device toolbar.

**How to apply:** When asked to "launch"/"see"/"verify" a scoring-app UI change, add (or reuse)
named demo launch profiles with sample 4-player and 3-player invite URLs and tell them which to
pick. Free port 5151 first if a dev server is already holding it. See [[run-skill-note]].

The user wants these demo profiles **kept and committed** (named "Demo: 4-player table" and
"Demo: 3-player table (Mr. X)") — they find them very useful. Do not propose dropping them before
a commit.
