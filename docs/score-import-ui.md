# Score import UI

User-facing import flow. Companion to [score-apply.md](score-apply.md) which covers the service-layer; this file is the UI state machine, input sources, and the QR overlay system.

Primary file: [Tsump/Components/ScoreImportPanel.razor](../Tsump/Components/ScoreImportPanel.razor).

## Used by

- `<ScoreImportPanel>` is wired in **[TournamentDetail.razor](../Tsump/Pages/TournamentDetail.razor)** above the Tables/Scores/AllScores tab nav (page-level, one panel per tournament).
- **[WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor)** has its own inlined copy of this flow with the same state machine and same calls into `ScoreImportService`, but its own UI markup. Phase B will migrate it to the component. Behaviour changes that should land on both sides need to be mirrored.

## State machine

`ImportStatus` enum:

| State | Description | UI |
|---|---|---|
| `Reading` | Just-opened, awaiting input | Hint text + scan input |
| `NotResultLink` | Decoded payload looks like an invite (`#p=` / `?p=`) | Warning + actions |
| `NoResult` | Clipboard empty (only on explicit check), or text didn't decode | Warning + actions |
| `NoMatchingTable` | Decoded fine but no resolver found a matching table | Warning + actions |
| `Scanning` | Camera scanner is live | Live view + tips |
| `ScanError` | Camera failed to start (permission / no camera) | Error + actions |
| `FileDecoding` | User picked an image, html5-qrcode is decoding | Spinner |
| `FileScanFailed` | Decoded but no QR found in image | Warning + actions |
| `Preview` | Decoded + table found, awaiting confirm | Preview table + Apply/Cancel |
| `AlreadyScored` | Decoded + found, but target already has scores | Preview + overwrite warning |
| `Applied` | Success | Banner + auto-return after 2s (or buttons) |

Transitions are linear except for the four input methods which all feed `ProcessText`.

## Four input methods

All converge on `ProcessText(string?)`, which decodes via `ScoringPayloadCodec`, then `ScoreImport.FindAsync`, then sets status.

### 1. Always-on text input (covers HID scanners, manual paste, manual typing)

Top of the panel, rendered in every state **except** `Scanning` and `FileDecoding`. Auto-focused on every transition into a "waiting" state (`Reading`, `NoResult`, `NotResultLink`, `NoMatchingTable`, `ScanError`, `FileScanFailed`, `Applied`) — see `OnAfterRenderAsync` + `lastAutoFocusedStatus`. Not auto-focused during `Preview` / `AlreadyScored` so it doesn't fight the user reviewing the import.

USB HID scanner behaviour: scanners emulate a keyboard, typing the decoded text fast then (almost always) Enter. The panel handles both terminators:

- `OnScanKeyDown` — if `Enter`, cancel the debounce and submit immediately.
- `OnScanInput` — `ScanInputDebounceMs = 500ms` after the last keystroke, treat as scan complete and submit. Covers scanners configured without an Enter terminator.

Manual paste (Ctrl+V) lands as a single `oninput` event with the full text; the 500ms debounce fires once and submits. Manual typing also works (Enter or wait 500ms).

### 2. Clipboard

- **On panel open**: `OpenPanel` calls `TryClipboard(isExplicitCheck: false)` — if the clipboard is empty/whitespace, the panel **silently stays in `Reading`**. No "no result link" alert the user didn't ask for.
- **Explicit re-check**: the **"Check clipboard"** button (renamed from "Retry"; key `CheckClipboard`, icon `bi-clipboard`) calls `TryClipboard()` (default `isExplicitCheck: true`) — empty clipboard now *does* surface `NoResult`.
- **Auto-return after Apply**: silent re-check, same as on open.

### 3. Camera scan

`StartQrScan` → state `Scanning` → JS `startQrScanner("qr-scanner-view", scannerRef)`. JS calls back into `[JSInvokable] OnQrScanned(decodedText)` on first decode; that routes through `ProcessText`. A background timer ticks `scanElapsedSeconds` so the "still looking" alert and the laptop-camera tip appear after 8s.

`CancelQrScan` and `ClosePanel` both call `StopQrScannerIfRunning` (idempotent, swallows "nothing to stop").

### 4. QR image file picker

`PickQrImage` → JS `pickQrImage("qrFileInput", "qr-file-decode-tmp", scannerRef)` opens the hidden `<input type="file">`. JS callbacks:

- `OnQrFileSelected` — file was actually picked → state `FileDecoding`.
- `OnQrScanned` (success path) → same `ProcessText` as camera.
- `OnQrFileScanFailed` → state `FileScanFailed`.

We deliberately don't transition state from `PickQrImage` itself — if the user cancels the OS picker, no JS callback fires and we'd be stuck.

## Pipeline: `ProcessText`

1. Trim, URL-decode.
2. If empty → `NoResult`.
3. If decoded text contains `#p=` or `?p=` → `NotResultLink` (this is the invite link, not the result).
4. Extract payload: regex `r=([A-Za-z0-9_-]+)` or fall back to the whole string.
5. `ScoringPayloadCodec.DecodeResult(payload)`; must produce 3–4 player scores, else `NoResult`.
6. `ScoreImport.FindAsync(parsed)`. Null → `NoMatchingTable`. Otherwise check existing scores → `AlreadyScored` or `Preview`.

## Apply

Always passes `confirmOverwrite: true` (the user has already seen the overwrite warning in `AlreadyScored` state by the time they click Apply). On success:

1. State → `Applied`.
2. `OnApplied` EventCallback fires — parent (TournamentDetail) reloads its data.
3. `ScheduleAutoReturnToReading` kicks off a 2s timer (`AutoReturnAfterApplyMs`) that flips back to `Reading` + silent clipboard re-check. Any user button click in the 2s window cancels via `autoReturnCts.Cancel()`.

## Inactivity timer

`InactivityTimeoutMs = 5 minutes`. Resets on every user activity (any of):

- `OnScanInput` (keystroke into the scan input)
- `OpenPanel` / `TryClipboard` / `StartQrScan` / `PickQrImage` / `Apply` (button clicks)
- `ProcessText` (input arrival from any source, valid or invalid)

On expiry: `ClosePanel`. Disposed alongside the panel.

## QR overlays

[Tsump.Shared/Scoring/QrCodeRenderer.cs](../Tsump.Shared/Scoring/QrCodeRenderer.cs)

```csharp
record CenterOverlay(string GlyphSvg, string BackgroundColor, double RelativeSize = 0.22);

static class Overlays {
    static readonly CenterOverlay Organizer;       // blue clipboard
    static readonly CenterOverlay ScoringResult;   // green check
}

static string ToSvg(string url, int pixelsPerModule = 4, CenterOverlay? overlay = null);
```

- **ECC level H** (not M) so the centre badge (~22% of QR width) leaves enough redundant data for reliable decode.
- **viewBox is added** (`width`/`height` stripped). Without viewBox, canvas rasterisers fall back to 300×150 and clip larger QRs — the result QR clipping bug.
- **Glyphs must be filled-only**, never stroked at sharp angles. Stroked paths with default miter joins spike beyond the badge into surrounding modules and break decode (the bi-check stroke bug). Both pre-built overlays use filled Bootstrap Icons paths.
- The white ring around the coloured circle (radius = `bgRadius + qrPixelSize * 0.012`) gives visual separation from the dense QR modules.

`QrCodeModal` ([Tsump.Shared/Components/QrCodeModal.razor](../Tsump.Shared/Components/QrCodeModal.razor)) takes an optional `Overlay` parameter and forwards to `QrCodeRenderer.ToSvg`. Callers (`TableShareActions`, `WeeklySessionPage`, MahjongScoring's `ScorePage`) pass the same overlay to both `QrCodeModal` AND the auto-PNG-to-clipboard JS path, so what the user sees in the modal matches what gets pasted into WhatsApp.

## Hardware (USB HID) scanner notes

A USB 2D scanner behaves as a keyboard wedge: scanned text arrives as keystrokes into whatever input is focused. No browser API; no permission prompt. The always-on text input at the top of the panel is the target. See the always-on text input section above.

The MahjongScoring repo also ships a tiny WinForms helper (`Tsump.QrScanner`) for scanning result QRs from a laptop with a connected USB scanner without opening the browser app — outside the scope of this repo but relevant context. See [MahjongScoring/CLAUDE.md](../../MahjongScoring/CLAUDE.md).
