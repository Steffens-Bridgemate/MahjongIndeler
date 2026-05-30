# Export: Copy / Print / Download — notes to self

How the "share an export" pattern works across the organizer app, so I don't have to
re-derive it. Covers rankings, scoresheets, hanchan assignments, and guidesheets.

## The three actions, and the JS that backs them

All export JS lives in **[Tsump/wwwroot/index.html](../Tsump/wwwroot/index.html)** as
`window.*` functions (called via `IJSRuntime.InvokeVoidAsync`). The relevant ones:

| JS function | Signature | Used for |
|---|---|---|
| `copyToClipboard` | `(html, plainText)` | **Copy** — writes a `ClipboardItem` with both `text/html` and `text/plain`. Rich apps (email) take the HTML; WhatsApp etc. take the plain text. |
| `copyTextToClipboard` | `(plainText)` | plain-text-only copy |
| `openHtmlInNewTab` | `(html)` | **Print** — opens an `text/html` blob in a new tab; the user prints from there |
| `downloadHtmlFile` | `(filename, html)` | **Download** — saves an `.html` blob |
| `downloadFile` | `(filename, base64)` | base64 (used for JSON data export, not HTML) |
| `readClipboardText` | `()` → string? | clipboard read (score import auto-fill) |
| `copyQrImageToClipboard` | `(svg, size, title, headerBg, headerFg)` | QR PNG copy |

Key point: **there is no "downloadHtmlAsImage" / "openPrintWindow" / "copyHtmlToClipboard".**
I have hallucinated those names before — always check `index.html` for the real names.

## The standard call shape

```csharp
// Copy: rich HTML + plain-text fallback
var (html, text) = BuildXxxContent();
await JS.InvokeVoidAsync("copyToClipboard", html, text);

// Print: wrap fragment in a full doc WITH the on-screen Print button
await JS.InvokeVoidAsync("openHtmlInNewTab", WrapAsHtmlDocument(title, html, includePrintButton: true));

// Download: same doc but WITHOUT the Print button (a stray button in a saved file is wrong)
await JS.InvokeVoidAsync("downloadHtmlFile", filename, WrapAsHtmlDocument(title, html));
```

**Print vs Download differ only in `includePrintButton`.** Builders that emit a full
document must gate the print/header chrome behind a flag (default `false`) so the
downloaded file is clean. See `BuildGuidesheetHtml(bool includePrintButton)` in
[TournamentGuidesheets.razor](../Tsump/Pages/TournamentGuidesheets.razor).

## ExportButtonGroup

[Tsump/Components/ExportButtonGroup.razor](../Tsump/Components/ExportButtonGroup.razor) is the
shared trigger. Collapsed = one button; expanded = trigger shrinks to an icon + Copy/Print/Download.
Params: `TriggerLabel` + `TriggerIcon` (required), `TriggerClass` (default `btn-outline-primary`),
`Small`, `@bind-Expanded`, `OnCopy` (optional — omit for groups with no clipboard variant),
`Copied`, `OnPrint`, `OnDownload`. The caller owns row layout (e.g. a `w-100` break to push the
expanded options onto their own line). The `Copied` flag is driven by a transient bool reset on a
~2s `Task.Delay`.

## Rankings export is shared (RankingTable)

The rankings Copy/Print/Download content is built **once** in
[Tsump/Components/RankingTable.razor](../Tsump/Components/RankingTable.razor) as public statics,
consumed by both the weekly Rankings tab and the tournament Rankings page:

- `BuildExportContent(List<RankingEntry> entries, string title, LanguageService lang)` → `(html, text)`.
  - HTML: bordered table with Rank / Name / Total / one column per hanchan.
  - Plain text: **fixed-width columns wrapped in ` ``` ` (Markdown/WhatsApp monospace fences)** so
    columns stay aligned on phones. Names are clipped to 20 chars.
- `WrapAsHtmlDocument(title, body, includePrintButton = false)` → standalone doc for Print/Download.
- `BuildRankings(...)` builds the `RankingEntry` list; `RankingEntry`/`HanchanScore` records also live here.

### Name column alignment
`nameResolver` returns names as `"<number>. <real name>"` (number 1–99, e.g. from
`GetParticipantDisplay`/`GetPlayerName`). In the fixed-width text variant, `AlignName` right-aligns
the number to two columns (`" 1. Ann"` / `"12. Ann"`) so the real names line up. It only touches
strings that start with a 1–2 digit number + `". "`; anything else is left alone. HTML output isn't
padded (table cells don't need it).

## Gotcha: WeeklySessionPage has its OWN WrapAsHtmlDocument

[WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor) keeps a private
`WrapAsHtmlDocument` with **scoresheet-specific CSS** (sticky print bar, responsive
`.table-block` float rules) used by the single/all-hanchan scoresheet exports — and currently its
rankings Open/Download still use that local one too. It is *not* the same template as
`RankingTable.WrapAsHtmlDocument` (which is the simpler rankings wrapper), so don't blindly merge
them. If unifying later, note the rankings call sites (`WrapAsHtmlDocument(title, html…)`) are not
textually unique vs the scoresheet ones.

## Lang keys that already exist
`ExportRankings`, `Print`, `Download`, `Copy`, `Copied`, `Rank`, `Name`, `TotalScore`,
`PrintGuidesheets`, `DownloadGuidesheets`. (nl + en in
[LanguageService.cs](../Tsump.Shared/Services/LanguageService.cs).)
