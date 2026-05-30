# MahjongIndeler (organizer) — Claude session notes

## What this repo is

Organizer-side Blazor WASM PWA for a Dutch Mahjong club. Manages members, weekly sessions, tournaments, and the round-trip with the separate scoring app. No backend; everything is offline-first in `localStorage`.

Deployed at: `https://steffens-bridgemate.github.io/MahjongIndeler/`

The paired scoring app lives in the [MahjongScoring](https://github.com/Steffens-Bridgemate/MahjongScoring) repo and has [its own `CLAUDE.md`](../MahjongScoring/CLAUDE.md). The two together describe the full system.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `Tsump` | Blazor WASM | The organizer app — members, weekly sessions, tournaments |
| `Tsump.Shared` | Razor Class Library | Models, codec, `ScoreTable`, `QrCodeModal`, `QrCodeRenderer`, `LanguageService`. Consumed by `Tsump` directly and by MahjongScoring via git submodule |
| `Simulation` | Console | Offline table-assignment optimisation analysis (not deployed) |
| `Tsump.Scoring` (in this tree) | — | Build-output / IDE-restored stub only. **Source lives in the MahjongScoring repo.** Don't edit files here |

## Two-repo relationship & deploy dance

`Tsump.Shared` is the source of truth for both apps. MahjongScoring includes this whole repo as a submodule at `external/MahjongIndeler` and references the same `Tsump.Shared.csproj` from there.

**Wire-format note.** `ScoringInvite` / `ScoringResult` ([Tsump.Shared/Scoring/ScoringPayload.cs](Tsump.Shared/Scoring/ScoringPayload.cs)) are minified for QR-size reasons: short field names (`c`/`t`/`n`/`p`/`u`/`o`/`sn` on the invite; `c`/`t`/`s` on the result). `ContextId` (Guid) carries either a `Hanchan.Id` or a `TournamentSession.Id`. **No PlayerIds in either payload** — players are paired by position (index in `PlayerNames` = index in `table.PlayerIds`). The result's `Scores` is `List<int[]>` where each entry is `[endPoints, loan, penalty]`; use the `ScoreEndPoints` / `ScoreLoan` / `ScorePenalty` index constants on `ScoringPayloadCodec`. Breaking changes to this codec require coordinated deploys.

Push to `master` in this repo auto-triggers `.github/workflows/deploy.yml`, which bumps `Tsump/AppVersion.cs`, publishes, and deploys to Pages.

### Deploying a shared-code change

After the organizer push lands at `origin/master`:

```powershell
cd c:\Users\aners\source\repos\MahjongScoring
git submodule update --remote external/MahjongIndeler
git submodule status               # verify pointer moved
# If any MahjongScoring callsite uses renamed payload fields / new classes,
# update Tsump.Scoring/Pages/ScorePage.razor in the same commit
git add external/MahjongIndeler [other-files]
git commit -m "Bump shared: …"
git push                           # auto-deploys the scoring app
```

For wire-format breaking changes deploy MahjongScoring **first** if any active scoring links exist in WhatsApp; otherwise order doesn't matter.

**Before the organizer push:** refresh the `docs/` files (`docs/domain.md`, `docs/score-apply.md`, `docs/score-import-ui.md`, `docs/pages.md`) to match the changes being deployed, and commit them in the same push.

**Never push without an explicit ask.** Push = deploy = live users.

## Coding practices

- **MUST READ before any UI/markup change: [docs/styles.md](docs/styles.md).** It is the house
  style (button colour semantics, sizing, flex action rows, `ExportButtonGroup`, inline-confirm,
  tabs, view-gating, auto-save). Match it rather than inventing new patterns; if a new pattern is
  truly needed, add it to that file in the same change.
- **Refactor to components and services to minimise duplication.** When the same logic appears in two pages, extract before adding a third. Recent precedents: [TableShareActions.razor](Tsump/Components/TableShareActions.razor), [ScoreImportPanel.razor](Tsump/Components/ScoreImportPanel.razor), [ScoreInviteService.cs](Tsump/Services/ScoreInviteService.cs), [IScoreContextResolver.cs](Tsump/Services/IScoreContextResolver.cs).
- **Use Segoe Fluent Icons / Bootstrap Icons** for button glyphs — not raw text or emoji.
- **Never bulk-rename strings** the user deliberately left at their current values. Ask if unsure.
- **Never commit or push without explicit approval.** Build locally first; then ask.
- **Comments are for *why*, not *what*.** Skip docstrings that restate the signature. Note non-obvious invariants, workarounds, surprising decisions.
- **`Tsump.Shared` cannot reference `Tsump`.** Components that need `TournamentService` etc. must live in `Tsump/Components/`. Pure Razor (no Tsump-side deps) can live in `Tsump.Shared/Components/`.
- **Dutch term for "session" is "Zitting"** (not "Sessie"). Use it in `nl` strings in [LanguageService.cs](Tsump.Shared/Services/LanguageService.cs).

## Gotchas

- **Stale-reference after Save.** `SessionService.GetAllAsync` / `TournamentService.GetAllAsync` return freshly-deserialised objects on every call. After a save + reload, the in-memory `currentHanchan` / `tournament` you mutated is *not* the same instance as `hanchansOnDate[i]` / the freshly-fetched tournament. Re-point local references via `.FirstOrDefault(h => h.Id == …)` so subsequent edits land on the live entry. See `SaveHanchan` / `SaveScores` in [WeeklySessionPage.razor](Tsump/Pages/WeeklySessionPage.razor) and `ReloadTournament` in [TournamentDetail.razor](Tsump/Pages/TournamentDetail.razor).
- **`Score == null` tables render nothing.** [ScoreTable.razor](Tsump.Shared/Components/ScoreTable.razor) early-returns when `Table.Score` is null. Any path that reloads a container from storage (e.g. after import) must call `ScoreTable.InitializeScores(tables, …)` on every table — otherwise tables that never had scores saved disappear from the Scores tab until the user flips tabs and back.
- **QRs are pristine — no on-QR overlay.** Earlier iterations painted a coloured badge over the QR's centre or lower-right quadrant to distinguish organizer from scoring side; phones decoded fine but USB HID 2D scanners refused even small off-centre badges. Final design: the QR itself is unadorned; the distinguishing badge lives in the modal header (rendered as inline SVG by [QrCodeModal.razor](Tsump.Shared/Components/QrCodeModal.razor)) and as a coloured title band in the PNG copied to clipboard (handled by `copyQrImageToClipboard` JS, called with `headerBg` / `headerFg` arguments). `Overlays.Organizer` (blue clipboard) and `Overlays.ScoringResult` (green check) records still exist in [QrCodeRenderer.cs](Tsump.Shared/Scoring/QrCodeRenderer.cs) but only supply the glyph and colour to those two consumers.
- **QR SVG mechanics**: ECC level **H**, `width`/`height` stripped and a `viewBox` added — without viewBox, canvas rasterisers default to 300×150 and clip larger QRs.
- **Both pages share the import/share components.** [TournamentDetail.razor](Tsump/Pages/TournamentDetail.razor) and [WeeklySessionPage.razor](Tsump/Pages/WeeklySessionPage.razor) both use `<ScoreImportPanel>` (with `OnApplied` reloading the page's data) and `<TableShareActions>`. There is no longer any inlined import/share code — change the components once and both pages get it. The two pages differ only in placement: Tournament puts the panel in a Regenerate+Import row gated by its inner tab; Weekly renders it at the top of the Scores tab.
- **`TournamentSession.Id` back-fill.** Stored tournaments from before the Id field existed deserialise with `Guid.Empty`. `TournamentService.GetAllAsync` assigns a new Guid and re-saves on first load. Side effect: opening Tournaments after upgrading triggers a silent storage write.
- **PWA service worker.** Both apps use Blazor's `service-worker.published.js` with cache-name keyed on `assetsManifest.version`. After deploying, the new SW installs but does not take over an open page; users must close all tabs of the PWA and reopen to pick up changes.

## Detailed docs

Loaded on demand. Read the one(s) relevant to the current task:

- [docs/styles.md](docs/styles.md) — **UI house style (must-read for any markup change)**: button colour semantics, sizing, flex action rows, `ExportButtonGroup`, inline confirmations, tabs, view-gating, auto-save.
- [docs/domain.md](docs/domain.md) — data model: `Hanchan` vs `TournamentSession`, `Member` vs `TournamentParticipant`, `TableAssignment`, Mr. X, Uma, score-status classification.
- [docs/score-apply.md](docs/score-apply.md) — payload codec, `IScoreContextResolver` strategy, `ScoreImportService.ApplyAsync` flow.
- [docs/score-import-ui.md](docs/score-import-ui.md) — `ScoreImportPanel` state machine, four input methods (clipboard / HID scanner / camera / file), inactivity timer, auto-return, QR overlays.
- [docs/pages.md](docs/pages.md) — page-specific behaviour worth knowing (only where non-obvious cross-file behaviour exists).
