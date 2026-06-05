# Pages — non-obvious bits

Sections appear only for pages with cross-file behaviour worth recording. Pages with self-evident behaviour (read the .razor) are intentionally absent.

## Members.razor

[Tsump/Pages/Members.razor](../Tsump/Pages/Members.razor)

- **Soft-delete via `IsActive`**: deleting a member who appears in any historical session sets `IsActive = false` instead of removing the record — historical data keeps a valid `PlayerId` reference. Only members with zero history are hard-deleted. See `DeleteClicked` (~line 354).
- Active flag drives default attendance pre-selection in `WeeklySessionPage` and the player list in `MeetingMatrix`.
- `MembersPinnedColumns` lives on `ClubSettings`, not in component state — column visibility is club-wide, not per-user.

## Settings.razor

[Tsump/Pages/Settings.razor](../Tsump/Pages/Settings.razor)

- **Three nested gate flags** on `ClubSettings`:
  - `EnableScoreEntry` — master gate. Reveals all scoring config (weekly + tournament uma/starting points + the external-scoring flag).
  - `EnableExternalScoring` — child gate. Reveals the per-table "Share scoring link" + QR + Import buttons across `WeeklySessionPage` and `TournamentDetail`. **Forced off** when `EnableScoreEntry` is toggled off.
  - `EnableScanLogging` — grandchild gate (under external scoring). Turns on capture logging + the **Log** nav item (see §ScanLog.razor). **Forced off** when `EnableExternalScoring` is toggled off.
- **`SettingsService.OnChanged`** fires after every save. `NavMenu` subscribes so the gated **Log** item appears/disappears live (no reload).
- **Dual scoring config**: weekly defaults (`WeeklyStartingPoints` / `WeeklyUma{3,4}Players`) and tournament defaults (`TournamentStartingPoints` / `TournamentUma{3,4}Players`) are independent. A tournament can additionally have its own per-tournament overrides — those fall back to the tournament-defaults from settings only via `TournamentScoreContextResolver`'s null-coalesce. The Settings page only edits the club-wide defaults, not per-tournament overrides.
- **Scoring app URL is compile-time**: `Tsump.Scoring.ScoringAppConfig.DeployedUrl` ([Tsump.Shared/Scoring/ScoringAppConfig.cs](../Tsump.Shared/Scoring/ScoringAppConfig.cs)) is a `const string`. Not user-configurable — PWA-installed instances hide the address bar so users can't be expected to type a URL. Pointing at a different scoring app deployment requires editing the constant and redeploying.
- **Schedule** drives `SettingsService.GetNextScheduledSlotAsync` — used by the workflow page to pre-fill the next club-night's date/time.
- **Four tabs** (`activeTab`): **Club event** (`SettingsTabClubEvent` — "Club bijeenkomst", *not* "Clubavond": a club can play in the afternoon), **Tournament**, **Style**, **Language**. Each tab is a `<div>` toggled with `d-none`, not a routed page. Competition period sits under Club event.

### Language (primary + always-English secondary)

- **Four languages**: `LanguageService.SupportedLanguages` = nl/en/fr/de, each a full dictionary in [LanguageService.cs](../Tsump.Shared/Services/LanguageService.cs). `Get` looks up the active dict, then **falls back to English**, then the raw key — so a missing translation degrades gracefully.
- **Primary vs active**: the club's **primary** language is `ClubSettings.PrimaryLanguage` (default `nl`); the **secondary is always English**. `Lang.Configure(primary)` runs on MainLayout's first render and sets the active display language to the primary. The top-row toggle (`SetLanguage`) flips the *active* language between primary and English and is **session-only** (resets to primary on reload — not persisted).
- **Top-row buttons** show the primary + English as flag buttons ([FlagIcon.razor](../Tsump.Shared/Components/FlagIcon.razor), inline SVG — Windows can't render emoji flags). When the primary **is** English, a single selected EN button remains as a reminder that the language is configurable.
- **Language tab** picks the primary via flag buttons; selecting one auto-saves `ClubSettings.PrimaryLanguage` and calls `Lang.SetPrimaryLanguage` (switches the whole app immediately). The "secondary is always English" note is shown as an `alert-info`.
- **Shared with the scoring app**: `LanguageService` lives in `Tsump.Shared`; the new members are additive (the scoring app keeps working unchanged), but the fr/de strings only go live there after MahjongScoring redeploys.

### Style theming (Club branding)

- **`ClubSettings.Style` (`ClubStyle`)** holds the club's colours, brand text, website link, logo and banner. **Defaults are the Tsumo! values** so the live deployment is unchanged out of the box; `ClubStyle.Neutral()` is the desaturated "blank slate" the **Reset to neutral** button applies.
- **Colours apply via CSS custom properties, not by rewriting CSS.** [app.css](../Tsump/wwwroot/css/app.css) and [NavMenu.razor.css](../Tsump/Layout/NavMenu.razor.css) reference `var(--club-primary, <hex>)` etc. — the literal Tsumo hex is kept as the **fallback** so nothing flashes before JS runs. `window.applyClubStyle(primary, link)` ([index.html](../Tsump/wwwroot/index.html)) sets the `--club-*` properties (and `--bs-primary`/`--bs-primary-rgb`, so Bootstrap `text-primary`/`border-primary` follow) on `<html>` from just two hex values; hover and translucent variants are **derived in JS**.
- **Who applies/reads it:** `MainLayout` applies colours on first render + on `SettingsService.OnChanged`, and renders the upper-right website link (hidden when `WebsiteUrl` is blank). `NavMenu` renders the upper-left logo image or `BrandText`. `Home` renders the banner. The Style tab additionally calls `applyClubStyle` directly on each picker change for instant whole-app preview.
- **Images** accept a remote URL **or** a downscaled `data:` URL produced by `window.readImageAsDataUrl(inputId, maxWidth, asJpeg)` (logo ≈256px PNG; banner ≈1200px JPEG q0.85, kept small for the localStorage blob).
- **PWA top-bar colour is a fixed neutral** (darkslategray `#2f4f4f`), **not** themed per club. An installed standalone PWA's title bar is locked to the cached `manifest.webmanifest` `theme_color` (can't change without the OS re-reading the manifest, ≈reinstall), so a per-club colour can't apply there anyway. The `<meta name="theme-color">` in `index.html` matches it and `applyClubStyle` deliberately does **not** touch it — keeping the browser-tab bar and the installed-app title bar consistent. The two values (manifest + meta) must stay in sync.
- **Explicit commit — the Style tab does NOT autosave** (unlike the rest of Settings). Edits mutate an in-memory `style` + flip `styleDirty`; colour edits also live-preview via `applyClubStyle` (CSS variables only, *not* persistence). The persist button is labelled **Commit** / *Vastleggen* (not the generic Save — `CommitStyle` key) and is gated by an inline `alert-warning` confirm (`CommitStyleConfirm`) warning that committing overwrites the saved style irrecoverably unless it was Exported first. `SaveStyle` is the **only** writer to localStorage and is reached only via `ConfirmCommit`. `savedStyle` is the last-persisted snapshot: **Discard** restores it, and `Dispose` re-applies its colours if the user navigates away while `styleDirty` (so an un-consented preview doesn't leak to the rest of the app). **Reset to neutral** and **Import** only *stage* (`MarkDirty`), they don't persist; any edit also cancels a pending commit confirmation.
- **Export/Import a style**: Export serializes the **in-memory** `ClubStyle` (the tentative edits, not the saved snapshot) to `<brand>-style.json` via `downloadFile` — so a club can export an unsaved style without persisting it. Import reads a `<InputFile>` JSON into the editor (validated, then staged dirty — Save still required). This is the style-only sibling of DataManagement's full export/import.
- **Live preview panel** at the top of the Style tab renders a mock top-bar (logo/brand + version + website link) + banner + sample button/link/badge, all driven by the in-memory `style`. Colours show through because they're already applied to the global `--club-*` vars; brand/logo/banner/link are previewed here rather than wired live into NavMenu/MainLayout (avoids an un-consented brand/image leak into the real chrome). So the user sees every choice immediately without saving.
- **Colour-from-image eyedropper**: the Colours card lets the user **upload or paste (Ctrl+V) a reference image** (e.g. a Snipping Tool capture of the club site) and click a pixel to sample its colour into Primary or Link (target chosen by a toggle). `window.clubPicker` (in [index.html](../Tsump/wwwroot/index.html)) draws the image to a `<canvas>` downscaled to ≤900px, registers a global `paste` listener (only consumes the event when the clipboard actually holds an image, so text paste elsewhere is unaffected), and `getImageData`-samples the clicked pixel back to `[JSInvokable] OnColorPicked`. The **reference image is transient — never written to `ClubStyle`/localStorage**. Picked colours are staged like any other edit (no autosave). The picker's paste listener + `DotNetObjectReference` are torn down in `Dispose`.

## ScanLog.razor

[Tsump/Pages/ScanLog.razor](../Tsump/Pages/ScanLog.razor) — route `/scan-log`. Diagnostic log of import captures, gated by `ClubSettings.EnableScanLogging`.

- **Nav item** is the **last** entry in [NavMenu.razor](../Tsump/Layout/NavMenu.razor) (both Weekly and Tournament modes), shown only when logging is on. `NavMenu` reads the flag on first render and re-reads it on `SettingsService.OnChanged`.
- **Capture source**: `ScoreImportPanel.ProcessText` calls `ScanLogService.AddAsync(raw)` for **every** capture (valid or not). `ScanLogService` ([ScanLogService.cs](../Tsump/Services/ScanLogService.cs)) stores `List<ScanLogEntry>` (raw string + UTC timestamp) at `tsump_scan_log`, newest-first, capped at 200, and no-ops when logging is off — so the panel can call it unconditionally.
- **Per-entry header colour** (three tiers):
  - **red** (`bg-danger-subtle`) — couldn't be decoded at all (invite link / garbage).
  - **amber** (`bg-warning-subtle`) — decoded but **not importable** (unknown session, or table regenerated/gone). The header also shows the **full `ContextId` Guid** in a copyable `code` block for emergency recovery.
  - **plain** — decoded and importable into a live local table.
- **Decode** button (any decodable result) → expands a read-only `<ScoreTable ReadOnly="true">` via `ScoreImport.BuildPreviewAsync`. Real names when the table resolves; synthetic `(Player N)` names otherwise (see [score-apply.md](score-apply.md)). `ReadOnly` (added for this) locks all inputs and hides the trash icon.
- **Copy to clipboard** button (every entry) copies the raw captured string. **Clear log** wipes the list.
- Decode/labels reuse `ScanResultParser` + `ScoreImportService.DescribeAsync`/`BuildPreviewAsync`, so the page and the import panel interpret captures identically.

## MeetingMatrix.razor

[Tsump/Pages/MeetingMatrix.razor](../Tsump/Pages/MeetingMatrix.razor)

- Pure statistical view over `SessionService.GetAllAsync()` — counts pair-meetings across all stored hanchans. Active members only.
- Drives no logic (read-only). The matrix is informational; the **actual** optimisation metric used when generating assignments lives in `TableAssignmentService`. Both consume the same underlying meeting counts but the matrix page doesn't share code with the assignment generator.
- Distribution table (right of the matrix) is clickable: clicking a meeting count dims all matrix cells except those with that count — for spotting which pairs are over/under-met.

## TournamentDetail.razor / WeeklySessionPage.razor

Cross-cutting concerns covered in [domain.md](domain.md) (status classification, Mr. X, Uma) and [score-import-ui.md](score-import-ui.md) (import panel, share actions, QR overlays). The pages themselves are mostly straightforward wiring around those.

Both pages now host the **table-nav strip** and a **contextual sidebar nav** — both documented in [styles.md](styles.md#11-table-nav-strip-jump-to-table). In short: the top-level tab strips (tournament's Participants/Tables/Guidesheets/Rankings; weekly's hanchans/All-scores/Rankings + Add hanchan + Back-to-workflow) moved into the sidebar, so on a tournament/session route the sidebar *is* that page's nav. Weekly publishes its entries via `ContextNavService` and clears them on `Dispose`; tournament is URL-driven. The tournament score tabs are now **Scores per hanchan** / **Scores per table** (`ScoresPerHanchan`/`ScoresPerTable` keys) matching how each groups its `ScoreTable`s.

Two non-obvious things specific to WeeklySessionPage worth flagging:

- `scoreTableVersion` is bumped on `SaveScores`, `SaveHanchan`, and `OnHanchanScoreApplied` (the `<ScoreImportPanel>` callback). It's part of every `ScoreTable`'s `@key` tuple so Blazor disposes/recreates each `ScoreTable` after a save — needed because the underlying `TableAssignment` references are replaced when `hanchansOnDate` is reloaded.
- `CurrentHanchanNumber` derives the 1-based "Hanchan N of the day" by ordering `hanchansOnDate` by `StartTime`. There's no stored hanchan number (see [domain.md](domain.md)).
- The sidebar's weekly nav rebuilds only on a visible-signature change (`PublishNav`), and hanchan-click handlers resolve the live `Hanchan` by id at click time to dodge the stale-reference gotcha after a save/reload.

## Workflow.razor

[Tsump/Pages/Workflow.razor](../Tsump/Pages/Workflow.razor)

- Step-by-step "guided session" UI for less-experienced organizers. Calls into the same `SessionService` / `TableAssignmentService` as `WeeklySessionPage`, but presents one step at a time with explicit Back/Next.
- Uses `SettingsService.GetNextScheduledSlotAsync` to pre-fill date/time from the club schedule.
- Passes `workflowStepKey` to `WeeklySessionPage` via query string so the back-to-workflow button on that page knows where to return.
