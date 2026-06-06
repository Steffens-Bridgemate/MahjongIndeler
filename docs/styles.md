# UI style pattern — MUST READ before touching any page's markup

This is the house style for the organizer app, distilled from the redesigned
[WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor). It is the reference every
page should converge on. When you add or change UI, match these patterns rather than inventing
new ones. If a genuinely new pattern is needed, add it here in the same change.

Bootstrap 5 + Bootstrap Icons (`bi bi-*`) only. No raw-text or emoji glyphs.

## 1. Button colour = meaning (semantic, not decorative)

| Intent | Class | Examples |
|---|---|---|
| The one primary generative action in a view | `btn btn-primary` (solid) | Generate Tables |
| A primary *workflow step* the user is expected to take next | `btn btn-outline-primary` | Export Assignments, Export Scoresheets |
| Neutral / secondary action | `btn btn-outline-secondary` | Copy from history, global/secondary exports |
| Additive / positive | `btn btn-outline-success` | Add Hanchan, All Present |
| Destructive (trigger) | `btn btn-outline-danger` | Delete Hanchan |
| Destructive / caution **confirmation** step | solid `btn btn-danger` / `btn btn-warning` | Yes-delete, Yes-regenerate |

- **Reserve yellow (`warning` / `bg-warning`) for genuine caution**, not ordinary actions. On this
  page yellow already means "3-player table" and "are you sure?" — don't dilute it (a plain Swap is
  `btn-primary`, not `btn-warning`).
- **Don't stack two `primary`-coloured buttons** next to each other; only the single most important
  action in a row should read as primary.

## 2. Button size & prominence

- Full-size `btn` for prominent / primary actions and the main workflow steps.
- `btn btn-sm` for secondary toolbars (export toolbars, attendance toggles) and for **icon-only**
  buttons.
- **De-emphasise secondary actions to icon-only `btn btn-sm` buttons** with a `title` tooltip and a
  single `<i class="bi …">` (no label). Precedent: Regenerate (`bi-arrow-repeat`) and Delete
  (`bi-trash`) on the action row. Destructive icon buttons still expand to a full inline confirm
  (see §5), so the icon never deletes on a single click.

## 3. Action rows = flex, not margins

Group buttons in a flex container, never per-button `me-2`/`ms-2`:

```razor
<div class="d-flex align-items-center flex-wrap gap-1 mb-3"> … </div>
```

- `align-items-center` so mixed full-size and `btn-sm` icon buttons line up vertically.
- `flex-wrap gap-1` for uniform spacing and graceful wrapping on narrow screens.
- Force a deliberate row break with `<div class="w-100"></div>` (e.g. so an expanded export group
  and the Regenerate/Delete icons each get their own row).

## 4. Export buttons → `ExportButtonGroup`

Use the shared [ExportButtonGroup.razor](../Tsump/Components/ExportButtonGroup.razor) for every
copy/print/download export. Never hand-roll the expand/collapse trio again.

- Collapsed: one trigger button. Expanded: the trigger shrinks to its icon (click to collapse),
  followed by **Copy / Print / Download** (short labels — `Lang.Get("Copy"|"Print"|"Download")`,
  not the long `CopyToClipboard`/`OpenPrint`/`DownloadHtml` strings).
- `OnCopy` is optional (omit for export groups with no clipboard variant, e.g. scoresheets).
- `TriggerClass` carries the §1 colour (`btn-outline-primary` for a workflow step,
  `btn-outline-secondary` for a secondary/global export); `Small="true"` for `btn-sm` toolbars.
- Distinguish scope by icon: `bi-table` (one hanchan) vs `bi-stack` (all hanchans),
  `bi-card-checklist` (scoresheets), `bi-list-ol` (rankings).
- Opening one of two sibling groups should collapse the other (one option set visible at a time).

## 5. Confirmation = inline alert, not modal

Destructive / regenerate actions reveal an inline confirm in place of the trigger:

```razor
<span class="alert alert-danger d-inline-block mb-0 py-1">
    @Lang.Get("DeleteHanchanConfirm")
    <button class="btn btn-sm btn-danger ms-2 me-1" @onclick="ConfirmDelete">@Lang.Get("YesDelete")</button>
    <button class="btn btn-sm btn-secondary" @onclick="CancelDelete">@Lang.Get("Cancel")</button>
</span>
```

`alert-warning` for regenerate/attendance-reset, `alert-danger` for delete. `alert-info` for
neutral explanations (e.g. status help, unassigned-player notices).

## 5b. Collapsible sections → `CollapsibleCard`

Use the shared [CollapsibleCard.razor](../Tsump/Components/CollapsibleCard.razor) for any card whose
body collapses behind a clickable header (attendance, assignments, 3-player distribution, meeting
matrix, scoring overrides, …). It renders the header, the chevron (`bi-chevron-up/down` — never a
`▲/▼` text glyph), and the body. `Title` is the header text, `@bind-Expanded` owns the open flag,
`TitleClass` overrides the heading size (default `h5 mb-0`), `CssClass` the card margins, and the
optional `Id` puts a DOM id on the card root so it can be a scroll/anchor target.

## 6. Tabs

Standard Bootstrap `nav nav-tabs`. **Active state is driven by `.active` only** — never hard-code
`bg-primary text-white` on a tab (that makes it read as selected when it isn't). Status colouring
goes through `ScoreStatusHelper.TabClass` / `TabStyle`.

## 7. Show only actions relevant to the current view

Gate actions to the active tab/view. Assignment-only actions (Regenerate, Delete, Export
Assignments, the global all-hanchans export) appear only on the assignment view
(`activeTab == "tables"`), not on Scores/Rankings. Feature-gated actions follow `ClubSettings`
(e.g. scoresheets behind `EnableScoreEntry`, share/QR behind `EnableExternalScoring`).

## 8. Don't over-explain to the user

- No persistent manual **Save** button — persist on change (auto-save). See the auto-save handlers
  in `WeeklySessionPage` and CLAUDE.md's stale-reference gotcha.
- Don't spell out constraints the UI already enforces. A disabled button + tooltip beats a sentence
  ("need at least 3 players", "save first" were removed). Use `disabled="@(…)"` + `title`.

## 9. Cards (table assignments)

Per-table cards encode player count by colour: 4-player = `border-success` + `bg-success text-white`
header; 3-player = `border-warning` + `bg-warning text-dark`. Headers omit the "(N players)" suffix on
the assignment view; the Scores view keeps it (rendered by `ScoreTable`).

**Multi-column flow — don't use a fixed Bootstrap `col-md-4` grid.** The per-table cards and the
`ScoreTable` cards both flow through CSS-grid wrappers in [app.css](../Tsump/wwwroot/css/app.css) so
wide screens show as many columns as fit and narrow screens collapse to one (no horizontal
overflow): `.table-assign-grid` (14rem track) for the assignment cards, `.score-table-grid`
(36rem track) for the score views.
Both are `grid-template-columns: repeat(auto-fill, minmax(min(100%, <track>), 1fr))` — the
`min(100%, …)` is what makes a single full-width column appear once the viewport is below one track.
Row spacing comes from each card's own `mb-3` (the grid sets `row-gap: 0`), so keep `mb-3` on the
card wrapper when moving a card into one of these grids.

**Player names are clipped to ~15 chars** with `.player-name-clip` (ellipsis, full value in `title`)
wherever a name sits in a width-constrained cell — the assignment list items and the `ScoreTable`
wide-layout column headers. This keeps the grid track widths above from blowing out on one unusually
long name.

## 9b. Club theming — never hardcode brand colours

Brand colours are **user-configurable** (Settings → Style). Don't write the Tsumo hex
(`#004b8d`, `#0088cc`) into new CSS — reference the custom property with the hex as fallback:
`color: var(--club-primary, #004b8d);` (variants: `--club-primary-hover`,
`--club-primary-soft`/`-softer` for the translucent nav backgrounds, `--club-link`,
`--club-link-hover`). They're set on `<html>` by `applyClubStyle` from two stored hex values;
Bootstrap `text-primary`/`border-primary` also follow (via `--bs-primary`). The upper-left brand,
upper-right link and landing banner come from `ClubSettings.Style` — see [pages.md](pages.md#style-theming-club-branding).

What the two colours reach:
- **Primary** — solid buttons, nav brand/active, loading spinner, **and checked checkboxes/radios**
  (Bootstrap 5.3 hardcodes `#0d6efd` on `.form-check-input:checked`, so [app.css](../Tsump/wwwroot/css/app.css)
  overrides it explicitly). The tick/dot glyph **auto-flips white↔black** for contrast: `applyClubStyle`
  computes YIQ brightness of the primary and sets `--club-check-image`/`--club-radio-image` accordingly.
- **Link** — hyperlinks (`a`) **and inactive `nav-tabs` tab headers**. Status-coloured tabs
  (`ScoreStatusHelper.TabClass`, `text-white`/`text-dark` `!important`) and the active tab
  (higher-specificity `.active`) deliberately keep their own colour.

## 10. i18n

All user-facing text via `Lang.Get(...)` with keys in both `nl` and `en` blocks of
[LanguageService.cs](../Tsump.Shared/Services/LanguageService.cs). Dutch "session" = **Zitting**.
Prefer reusing an existing short key (`Copy`, `Print`, `Download`, `Assignments`) over adding a
near-duplicate. Don't bulk-rename strings the user deliberately set.

## 11. Table-nav strip (jump-to-table)

[TableNavStrip.razor](../Tsump/Components/TableNavStrip.razor) is the floating, sticky strip of
finger-sized table-number buttons shown above the table cards on the weekly + tournament
assignment/score views. It's page-agnostic: the caller passes `Tables`, an `IsGreen` predicate, an
`AnchorId` mapper (the DOM id to smooth-scroll to), and optional `Leading` content.

- A button turns **green** (`btn-success`) when "done": *scanned* on the assignment view (gated on
  external scoring), *result-complete* (`ScoreStatusHelper.TableIsComplete`) on the score views.
- Clicking scrolls the matching `id` into view via `window.scrollToElement`, which measures the
  sticky strip's real height (it wraps to several rows) and offsets by it; scroll targets carry the
  `.table-scroll-anchor` class.
- The weekly assignment view adds a leading **mark/unmark-all-scanned** checkbox (external-scoring
  only). The tournament adds one strip per hanchan (Scores/Assignments) and one per table-number
  group on the per-table view.

## 12. Contextual sidebar nav

Some pages replace the normal [NavMenu](../Tsump/Layout/NavMenu.razor) menu with their own sub-nav
("Back to …" at the top), to declutter wide in-page tab strips. NavMenu picks, in priority order:
`ContextNavService.Entries` (entry-based, in-page state) → else the URL-based tournament branch →
else the default menu.

- **Weekly session** (entry-based): [ContextNavService](../Tsump/Services/ContextNavService.cs) lets
  the page publish a flat list of `ContextNavEntry` (label, icon or status-dot, active, disabled,
  `Indent`, click or href, optional "?" explainer). NavMenu renders them (so they get sidebar
  styling); the page rebuilds on a signature change and **clears on `Dispose`**. The hanchan
  designators (with a `.nav-status-dot` mirroring the tab status colour), Add hanchan, All scores,
  Rankings and Back-to-workflow live here. The top item is a generic **"Back"** (home icon) that
  escapes to the general menu — it navigates to History.
- **Tournament** (entry-based once generated; URL-based before): a *generated* tournament's
  [TournamentDetail](../Tsump/Pages/TournamentDetail.razor) page owns its whole sidebar via
  `ContextNavService` — back-to-Tournaments + Participants, the three **view selectors**
  (Assignments / Scores per Hanchan / Scores per Table) that drive the in-page `tournamentActiveTab`
  (there is **no** in-page tab strip), then jumps to the two bottom info cards (3-player
  distribution, Meetings — they expand the card and scroll to it), then Guidesheets, Rankings and
  finally **Settings** (the tournament's scoring-settings page, gear icon — always last). The
  active selector is followed by an **indented (`Indent = 1`) jump list** — its hanchans (Assignments
  / Scores per Hanchan) or its table numbers (Scores per Table); each entry switches the view,
  expands the target, and smooth-scrolls to it (`pendingScrollId` / `pendingScrollFn` consumed in
  `OnAfterRenderAsync`: `scrollToElement` reserves a sticky strip's height, `scrollToAnchor` is a
  plain scroll for targets with no strip above them). Before generation, and
  on the other `/tournament/{id}/…` sub-pages (which don't publish entries), NavMenu falls back to
  its URL-based branch: the tournament destinations as links (Participants, Assignments,
  Guidesheets, Rankings, then **Settings** last — the scoring-settings page) + a top **"Back"**
  item, with [TournamentNavContext](../Tsump/Services/TournamentNavContext.cs) carrying `IsGenerated`
  so Guidesheets/Rankings stay disabled until generated (Settings/Participants stay enabled). Both the entry-based and URL-based back items
  use the **home icon** and the generic `Back` label, and navigate to the Tournaments list.

Indented sub-items use `.nav-subitem` (tighter, smaller) in
[NavMenu.razor.css](../Tsump/Layout/NavMenu.razor.css). The per-granularity in-page
[`TableNavStrip`](#11-table-nav-strip-jump-to-table) strips stay (table-within-a-hanchan jumps); the
sidebar adds the coarser hanchan / table-number jumps.

On narrow screens the menu button is pinned top-right (fixed) and the menu drops in as a floating
panel — see the `max-width` block in [NavMenu.razor.css](../Tsump/Layout/NavMenu.razor.css).

## Exceptions

Deliberate departures from the rules above — don't "fix" these:

- **Record-edit forms keep an explicit Save button.** The "no manual Save / auto-save" rule (§8)
  is *session-scoped* (hanchan/tournament editing). A form that edits a discrete record — e.g. the
  member form in [Members.razor](../Tsump/Pages/Members.razor) — legitimately uses an explicit
  Save (`btn-success`); auto-saving every keystroke there would be wrong.
- **Confirmation-step buttons may be solid `btn-warning`/`btn-danger`.** §1 reserves *warning* for
  caution, but the "Yes, continue / Yes, delete" button inside an inline confirm (§5) is correctly
  a solid warning/danger button. That's not the "warning for an ordinary action" misuse.
- **[ImportScorePage.razor](../Tsump/Pages/ImportScorePage.razor) has no in-app navigation link.**
  It's a deep-link landing page that decodes a scoring result from the URL fragment
  (`/import-score#r=…`) when a phone opens a scanned result link. No nav reference is expected — it
  is *not* dead code.
