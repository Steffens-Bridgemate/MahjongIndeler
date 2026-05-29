# Score import UI

User-facing import flow. Companion to [score-apply.md](score-apply.md) which covers the service-layer; this file is the UI state machine, input sources, scan logging, and the QR overlay system.

Primary file: [Tsump/Components/ScoreImportPanel.razor](../Tsump/Components/ScoreImportPanel.razor).

## Used by

- **[TournamentDetail.razor](../Tsump/Pages/TournamentDetail.razor)** — in a Regenerate+Import row at the top of the assignments card body; `TriggerDisabled` on the Assignments tab. `OnApplied="ReloadTournament"`.
- **[WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor)** — at the top of the Scores tab content. `OnApplied="OnHanchanScoreApplied"`.

Both also use `<TableShareActions>` for the per-table share/QR buttons. There is no inlined import/share code left in either page — the components are the single source of truth.

## Design: neutral-first

Opening the panel lands in a **neutral state** showing four explicit method buttons — it does **not** auto-read the clipboard. This is deliberate: an auto clipboard read traps the flow (a rejected clipboard result reappears every time you reopen, so camera/scanner stay out of reach). The user always picks a method explicitly, and the four buttons remain available on every error state, so all methods stay reachable.

## State machine

`ImportStatus` enum:

| State | Meaning | UI |
|---|---|---|
| `Neutral` | Resting; awaiting a method choice | Prompt + four method buttons |
| `HandScanning` | HID capture armed | QR icon + waiting/scanning bar + Cancel |
| `Scanning` | Camera scanner live | Live view + tips + Cancel |
| `FileDecoding` | Picked image, html5-qrcode decoding | Spinner |
| `Preview` | Decoded + table found, awaiting confirm | Preview table + Apply / Cancel |
| `AlreadyScored` | As `Preview`, but target already scored | Preview + overwrite warning |
| `Applied` | Success | Banner; continuation depends on method (below) |
| `NotResultLink` | Capture is an invite link (`#p=`/`?p=`), not a result | Warning + method buttons |
| `NoResult` | Empty / didn't decode to a result | Warning + method buttons |
| `NoMatchingTable` | Decoded but no resolver found the table | Warning + method buttons |
| `ScanError` | Camera failed to start | Error + method buttons |
| `FileScanFailed` | Image had no readable QR | Warning + method buttons |
| `ScanTruncated` | Closing `]]` seen without opening `[[` (focus arrived mid-scan) | Warning + method buttons |

`Neutral` plus all the warning/error states render the same four-button row (with a contextual banner above), so every method is one click away from anywhere.

Two helper enums drive continuation and focus:

- `InputMethod { None, Clipboard, Camera, HandScanner, Image }` — which method the user picked; decides what happens after a successful `Apply`.
- `FocusTarget { None, Next, ReadImage }` — which button to focus on the next render so the **Enter** key activates it natively.

## Four input methods

Each is an explicit button (in `Neutral` and error states). All decoded text funnels through `ProcessText(string?)`.

### 1. Check clipboard (`StartClipboard`, `bi-clipboard`)

`method = Clipboard`, reads `readClipboardText` (JS), then `ProcessText`. Empty/garbage → `NoResult`/`NotResultLink`.

### 2. Camera scan (`StartCamera` → `StartQrScan`, `bi-camera`)

State `Scanning` → JS `startQrScanner("qr-scanner-view", scannerRef)`. JS calls `[JSInvokable] OnQrScanned(decodedText)` on first decode → `ProcessText`. A background timer ticks `scanElapsedSeconds` so the "still looking" alert + laptop-camera tip appear after 8s. Cancel → `ReturnToNeutral` (stops the scanner).

### 3. Hand scanner (`StartHandScan` → `BeginHandScan`, `bi-upc-scan`)

State `HandScanning`. Arms the **JS-side capture** (below) and shows the QR icon + progress bar. This is the only state where the capture input exists. Cancel → `ReturnToNeutral`.

### 4. Read image (`StartImage` → `PickQrImage`, `bi-image`)

JS `pickQrImage("qrFileInput", "qr-file-decode-tmp", scannerRef)` opens the hidden `<input type="file">`. Callbacks: `OnQrFileSelected` (file picked → `FileDecoding`), `OnQrScanned` (success → `ProcessText`), `OnQrFileScanFailed` (→ `FileScanFailed`). We don't transition from `PickQrImage` itself — cancelling the OS picker fires no callback, so we'd otherwise be stuck.

## HID capture is JS-side (the important part)

A USB HID scanner is a keyboard wedge that types the whole URL in a sub-second burst. Doing per-keystroke work in Blazor (a controlled `<textarea value=@buffer @oninput>`) breaks under a slow event loop — **debug builds and sluggish phones** — because the re-render reverts the textarea mid-burst and drops characters. (Symptom: imports that work flawlessly on the deployed Release build but truncate when run from Visual Studio, where the WASM debugging proxy slows every event. See the build-vs-run note in [CLAUDE.md](../CLAUDE.md).)

So capture lives in JS ([Tsump/wwwroot/index.html](../Tsump/wwwroot/index.html)):

- `attachScanCapture(el, dotNetRef)` — binds native `input`/`keydown` listeners to the (uncontrolled) textarea. It accumulates keystrokes in a plain JS string and hands the **complete** scan to .NET in a single call. `el.value` is only cleared at commit time, never mid-burst.
  - Framed scan `[[ … ]]`: commits the instant the closing `]]` arrives (deterministic, no timer).
  - Closing `]]` without opening `[[`: front-truncated (focus arrived late) → `OnScanTruncated`.
  - Unframed (paste/typing): commits on Enter or after a 500 ms idle.
- `detachScanCapture(el)` / `focusAndClearScanInput(el, force)` — lifecycle + focus. `force` is needed because auto-focus is **skipped on touch devices** (coarse pointer) so a phone using the camera path doesn't pop the on-screen keyboard; an explicit tap on the QR indicator passes `force = true`.

`[JSInvokable]` callbacks on the panel:

- `OnScanComplete(text)` → `ProcessText`.
- `OnScanTruncated()` → `ScanTruncated`.
- `OnScanProgress(bool)` — fired **only on the two burst transitions** (start / end), never per keystroke, so a burst costs at most two round-trips. Toggles `isScanning`, which switches the progress bar between its two animations:
  - **waiting** — indeterminate Bootstrap striped sweep (`progress-bar-striped progress-bar-animated`).
  - **scanning** — a fill that repeatedly grows left→right, ~1s/cycle (`.scan-fill` + `@keyframes scan-fill-grow`).

The capture `<textarea>` is offscreen-but-focusable (`.scan-capture-hidden`: `position:fixed; 1×1px; opacity:0; pointer-events:none`). A focused invisible element still receives keystrokes; the raw URL is no longer shown on screen (it's recorded to the Log instead — below). `OnAfterRenderAsync` attaches the listeners once per `HandScanning` entry and focuses once (`handScanFocused` guard), so `OnScanProgress`'s mid-burst re-render never re-focuses or clears the in-flight capture.

## Pipeline: `ProcessText`

1. `ScanLog.AddAsync(text)` — log the raw capture verbatim (no-op when logging is disabled; see [pages.md](pages.md) §ScanLog).
2. Empty → `NoResult`.
3. `ScanResultParser.TryParse(text, out looksLikeInvite)` — the shared decode (strip `[[ ]]` frame, URL-unescape, detect `#p=`/`?p=` invite, extract `r=<payload>` then a garbled-prefix fallback `=<long-payload>`, then `DecodeResult`, validate 3–4 score rows). Null + invite → `NotResultLink`; null otherwise → `NoResult`. This same parser backs the scan-log page so both interpret captures identically.
4. `ScoreImport.FindAsync(parsed)`. Null → `NoMatchingTable`. Otherwise existing-scores check → `AlreadyScored` or `Preview`.

## Apply + per-method continuation

`Apply(confirmOverwrite: true)` (the user has already seen the overwrite warning by the time they click). On success: state → `Applied`, fire `OnApplied` (parent reloads). Then continuation depends on `method`:

| Method | After successful Apply | Cancel (preview / mid-method) |
|---|---|---|
| **Clipboard** | Banner + **Next** button (repeats the clipboard read); Next is focused so **Enter** triggers it | → neutral |
| **Camera** | Banner ~1.2s → **auto-restarts the camera** | → neutral |
| **HandScanner** | Banner ~1.2s → **auto re-arms** the capture (`BeginHandScan`) | → neutral |
| **Image** | Banner ~1.2s → **neutral**, with the Read-image button focused (Enter repeats) | → neutral |

`ScheduleAutoContinue` runs the ~1.2s (`AutoContinueAfterApplyMs`) timer for camera/hand-scanner/image; clipboard has no timer (waits for the user). Any cancel/close cancels the timer (`autoContinueCts`).

**Enter binding** is native: `pendingFocus` is set when entering a state with a "primary" button (clipboard `Applied` → `Next`; post-image neutral → `Read image`); `OnAfterRenderAsync` calls `FocusAsync()` on the matching `ElementReference`, and a focused `<button>` activates on Enter.

`ReturnToNeutral` is the universal in-body cancel: stops the camera if live, resets to `Neutral`/`None`, clears pending result. The header button is **Close** (`ClosePanel`) which collapses the whole panel.

## Scan logging

When `ClubSettings.EnableScanLogging` is on, `ProcessText` records **every** capture (valid or not, from any method) via `ScanLogService`. Inspected/decoded on the **Log** page — see [pages.md](pages.md) §ScanLog.razor.

## Inactivity timer

`InactivityTimeoutMs = 5 minutes`, reset on any user activity (`ResetInactivityTimer` is called from the method entry points, `ProcessText`, `Apply`, and `OnScanProgress(true)`). On expiry → `ClosePanel`. Disposed with the panel.

## QR badges (off the QR, in the header)

[Tsump.Shared/Scoring/QrCodeRenderer.cs](../Tsump.Shared/Scoring/QrCodeRenderer.cs)

```csharp
record CenterOverlay(string GlyphSvg, string BackgroundColor);

static class Overlays {
    static readonly CenterOverlay Organizer;       // blue clipboard
    static readonly CenterOverlay ScoringResult;   // green check
}

static string ToSvg(string url, int pixelsPerModule = 4);   // pristine — no overlay arg
```

**The QR itself is pristine.** Earlier attempts painted a badge on the QR (centre, then small centre, then off-centre); phones decoded all of them but USB HID 2D scanners refused even the smallest off-centre version. The badge now lives in two places, both *outside* the QR's module grid:

1. **Modal header** ([QrCodeModal.razor](../Tsump.Shared/Components/QrCodeModal.razor)) — small inline-SVG coloured circle + white glyph next to the title. `QrCodeModal` takes an `Overlay`; when non-null it draws the badge.
2. **PNG title band** — the auto-clipboard PNG fills its title band with the overlay's `BackgroundColor` and draws the title in white. Callers pass `headerBg`/`headerFg` to `copyQrImageToClipboard` ([index.html](../Tsump/wwwroot/index.html)). Pasted into WhatsApp, the recipient sees a coloured banner above an unadorned QR — colour alone differentiates organizer (blue) from scoring result (green).

QR SVG mechanics: ECC level H, `width`/`height` stripped and replaced with a `viewBox` so the canvas rasteriser knows the coordinate space.

## Hardware (USB HID) scanner notes

A USB 2D scanner behaves as a keyboard wedge: scanned text arrives as keystrokes into the focused element. No browser API, no permission prompt. The target is the offscreen capture `<textarea>`, which only exists in the **Hand scanner** state — so arm it first (the QR icon + progress bar is the "ready" cue). Capture/accumulation is JS-side (see above), which is what makes it robust regardless of event-loop speed.

The MahjongScoring repo also ships a tiny WinForms helper (`Tsump.QrScanner`) for scanning result QRs from a laptop with a connected USB scanner without opening the browser app — outside this repo's scope but relevant context. See [MahjongScoring/CLAUDE.md](../../MahjongScoring/CLAUDE.md).
