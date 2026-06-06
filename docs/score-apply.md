# Score apply (service layer)

The organizer side of the score round-trip. Companion to [score-import-ui.md](score-import-ui.md) which covers the UI; this file is the service-layer flow.

## Payload codec

[Tsump.Shared/Scoring/ScoringPayload.cs](../Tsump.Shared/Scoring/ScoringPayload.cs)

```csharp
// Encoded to a compact binary form (see "Encoding" below) to keep QRs small. The
// [JsonPropertyName] attrs on the records are now only documentation, not the size lever.
record ScoringInvite(
    Guid ContextId,                    // c
    int TableNumber,                   // t
    List<string> PlayerNames,          // n
    int StartingPoints,                // p
    List<int> Uma,                     // u
    string? Title,                     // title — compact event short name only (or null); see below
    string? OrganizerUrl,              // o
    int SessionNumber = 1);            // sn

record ScoringResult(
    Guid ContextId,                    // c
    int TableNumber,                   // t
    List<int[]> Scores);               // s — each entry is [endPoints, loan, penalty]

// Use these to index entries readably:
const int ScoreEndPoints = 0;
const int ScoreLoan      = 1;
const int ScorePenalty   = 2;
```

- `ContextId` is the **only key used for lookup**. It carries either a `Hanchan.Id` (weekly) or a `TournamentSession.Id` (tournament); the organizer's resolvers disambiguate.
- **No PlayerIds in either payload.** Players are paired by position: the invite's `PlayerNames[i]` corresponds to `table.PlayerIds[i]` on the organizer; the result's `Scores[i]` applies back to the i-th non-virtual `PlayerScore` on that table. The scoring app generates per-session synthetic Guids internally so `ScoreTable` can keep wiring edits up by `PlayerScore.PlayerId`, but those Guids never reach the wire format.
- `SessionNumber` is display-only but now load-bearing for the heading: the scoring app **reconstructs** `"Hanchan {SessionNumber} — Table {TableNumber}"` itself (in its own language) rather than receiving a pre-formatted string — that pre-formatted title was a big chunk of the QR payload. Still never used for lookup.
- `Title` therefore carries **only an optional compact event name** — the tournament's `ShortName` (≤12 chars), or `null` for weekly sessions (which have no event name). Kept short on purpose: every payload byte raises the QR's module count. The scoring app shows it as a prefix to the reconstructed `Hanchan N` line. (Legacy invites that still hold a full title decode fine — it just shows as a longer prefix.)
- `Uma` is sent in the outbound invite (the scoring app shows it), but **omitted** from the inbound result — the organizer recomputes Uma from its own settings to avoid drift if Uma config changes between invite send and result return.
- `OrganizerUrl` carries `Nav.BaseUri` so the scoring app can build a return URL back to whichever organizer instance issued the invite.

### Encoding

`ScoringPayloadCodec` packs each record into a **compact binary buffer** — 16-byte `Guid`, zig-zag varints for the ints, length-prefixed UTF-8 for the strings — then **optionally Deflate-compresses** it (kept only when it actually shrinks the body), prepends a 1-byte header (low nibble = type, bit `0x10` = compressed body), and Base64Url-encodes the result into the URL fragment (`#p=…` outbound, `#r=…` inbound; the result QR is additionally wrapped in `[[ … ]]` scan-frame markers).

The outbound invite fragment additionally carries the organizer's primary language as a non-payload param: `#p=<payload>&l=<lang>` (built by `ScoreInviteService.BuildInviteUrl`). It is **not** part of the binary payload — `ScorePage` splits the fragment on `&`, decodes `p`, and calls `Lang.SetLanguage(l)` so the scoring app shows the same language as the organizer. (Coordinated change — the scoring app must parse `&l=` before the organizer starts emitting it.)

Binary packing is the main size win: the `Guid` drops from a 36-char string to 16 bytes and all JSON syntax/field-names vanish. Compression then squeezes the text-heavy **invite** (player names, title, organizer URL); the dense **result** (mostly Guid + small ints) is left raw because Deflate wouldn't shrink it (the header records which was used, so decode is unambiguous). There is no JSON on the wire any more — the `[JsonPropertyName]` attributes on the records are retained only as field documentation.

History: `HanchanId` / `HanchanNumber` were earlier names from before tournaments. Renamed to `ContextId` / `SessionNumber`. Field names were minified and `PlayerResultEntry` collapsed into a 3-int array in a later coordinated deploy. Later still, the JSON-text encoding was replaced wholesale by the binary codec above. Every one of these was a clean break with **no back-compat shim** — each relied on there being no outstanding scoring links in the wild at deploy time.

## Resolver strategy

[Tsump/Services/IScoreContextResolver.cs](../Tsump/Services/IScoreContextResolver.cs)

```csharp
interface IScoreContextResolver {
    Task<ResolveOutcome> FindAsync(Guid contextId, int tableNumber);
    Task SaveAsync(ResolvedContext context);
}

abstract record ResolveOutcome {
    record NotMine : ResolveOutcome;             // this resolver doesn't own contextId
    record ContainerOnly(string DisplayLabel);    // found container, table number absent
    record Found(ResolvedContext Context);        // hit
}

record ResolvedContext(
    object Container,            // pattern-match: Hanchan | Tournament
    string DisplayLabel,         // pre-built UI string ("Mon 18 May · 14:00 · Table 2")
    TableAssignment Table,
    int StartingPoints,
    List<int> Uma3Players,
    List<int> Uma4Players,
    List<string> PlayerNames);   // names in Table.PlayerIds order (members / participants)
```

`PlayerNames` lets a consumer render names without knowing the container kind — used by the scan-log decode preview. `HanchanScoreContextResolver` resolves them via `MemberService`; `TournamentScoreContextResolver` via `tournament.Participants`.

Two implementations, both DI-registered in [Program.cs](../Tsump/Program.cs):

- `HanchanScoreContextResolver` — `SessionService` lookup. Reads weekly settings from `SettingsService` for `StartingPoints` / Uma.
- `TournamentScoreContextResolver` — `TournamentService` lookup. Iterates `tournament.Sessions` to find the one whose `Id == contextId`. **Falls back to club weekly settings** when per-tournament overrides (`Tournament.StartingPoints` / `UmaXPlayers`) are null.

`ScoreImportService` ([ScoreImportService.cs](../Tsump/Services/ScoreImportService.cs)) takes `IEnumerable<IScoreContextResolver>` (plus `SettingsService`, for the synthetic-preview defaults below) and iterates the resolvers in order; first `Found` wins. Adding a new context kind = add a new resolver + register it. No caller changes.

### `ContainerOnly` vs `NotMine`

- `NotMine` (default if `contextId` isn't found at all) means "try the next resolver".
- `ContainerOnly` is terminal: the container matched but the table number doesn't exist in it (e.g. assignments were regenerated after the link was shared). Surfaces to UI as `FailureReason.NoMatchingTable`.

`ScoreImportService.ApplyAsync` makes a second pass over the resolvers when the first pass returned nothing, looking specifically for `ContainerOnly`, to distinguish "no resolver knows this id" (`NoMatchingContainer`) from "we know the container but the table is gone" (`NoMatchingTable`).

## ApplyAsync flow

[ScoreImportService.cs](../Tsump/Services/ScoreImportService.cs) `ApplyAsync(ScoringResult result, Func<string,string> langGet, bool confirmOverwrite = false)`:

1. **Resolve.** Walk registered resolvers; first `Found` wins.
2. **Overwrite gate.** If `table.Score` already has any non-virtual `PlayerScore` with `EndPoints` set and `confirmOverwrite` is false → return `FailureReason.AlreadyScored` (UI re-prompts the user).
3. **Initialise.** `ScoreTable.InitializeScores(new List<TableAssignment> { table }, langGet, context.StartingPoints)` — adds `PlayerScore` rows for players in `table.PlayerIds`, removes stale ones, adds Mr. X if 3-player. **Note**: only the matching table gets initialised here — the rest of the container's tables keep whatever `Score` state they had in storage. See the score-null gotcha in [CLAUDE.md](../CLAUDE.md).
4. **Apply.** Pair `result.Scores[i]` to the i-th non-virtual `PlayerScore` on the table by **position**. A count mismatch (e.g. assignments regenerated since the link was issued) terminates with `FailureReason.NoMatchingTable`. Each entry's three slots are accessed via the `ScoreEndPoints` / `ScoreLoan` / `ScorePenalty` constants on `ScoringPayloadCodec`.
5. **Derive Mr. X.** `ScoreTable.DeriveVirtualEnd(table)`.
6. **Compute Uma.** `ScoreTable.CalculateUma(table, context.Uma3Players, context.Uma4Players)`.
7. **Persist.** `resolver.SaveAsync(context)` — Hanchan resolver calls `SessionService.SaveAsync`, Tournament resolver calls `TournamentService.SaveAsync` on the whole tournament.
8. **Return** `ApplyOutcome(Success, Reason, ResolvedContext?)`.

Steps 3–6 (initialise → apply by position → derive Mr. X → compute Uma) live in a private `WriteScoresIntoTable(context, result, langGet)` shared with `BuildPreviewAsync` (below), so a preview renders scores exactly as an apply would. It returns `false` on a player-count mismatch.

## What `ApplyAsync` does NOT do

- It doesn't refresh any UI state — callers reload data after a successful outcome (`ReloadTournament` in TournamentDetail; `OnHanchanScoreApplied` in WeeklySessionPage, both wired to `ScoreImportPanel.OnApplied`).
- It doesn't initialise `Score` on other tables in the container. UI re-loaders must call `ScoreTable.InitializeScores` on all tables themselves to keep them visible (the score-null gotcha).
- It can't validate that the player **identities** still match (no PlayerIds in the wire format). If the assignments were regenerated and the new table has the same player *count* but different players, the result silently writes onto whichever players sit at those positions now. Count mismatch is caught (`NoMatchingTable`); identity drift is not.

## Find / Preview / Describe (read-only)

Three read-only helpers, no mutation of stored data, no save:

- `FindAsync(ScoringResult)` — resolver lookup used by the import UI to preview before applying. Returns `Lookup?`.
- `DescribeAsync(contextId, tableNumber)` — used by the scan-log header. Returns `ScanDescription(Label, Recognized, TableFound)`: friendly label when any resolver recognises the id (from `Found` *or* `ContainerOnly`), and whether the table still exists.
- `BuildPreviewAsync(ScoringResult, langGet)` — renders a decoded result as a read-only `ScoreTable` for the scan-log "Decode" button. Returns `DecodePreview(Context, NameResolver, TableMismatch)`.
  - When a resolver recognises the id, it uses the real table (mutating only the transient, freshly-deserialised instance — never saved) and `Context.PlayerNames`.
  - When **no** resolver owns the id (foreign organizer, or a regenerated/deleted session), it fabricates a synthetic table from the result itself — one player id per score row (so 3-player tables still derive Mr. X), club-default starting points/Uma from `SettingsService`, and parenthesised positional names `(Player 1)`, `(Player 2)`… So any valid `ScoringResult` is inspectable, importable or not.

## Callers

| Caller | Path | What it does after `ApplyAsync` |
|---|---|---|
| [ImportScorePage.razor](../Tsump/Pages/ImportScorePage.razor) | `/import-score#r=…` deep link | Renders applied banner, link back to session |
| [WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor) `OnHanchanScoreApplied` | `<ScoreImportPanel>` on the Scores tab | Reloads `hanchansOnDate`, re-points `currentHanchan`, re-inits scores, bumps `scoreTableVersion` |
| [ScoreImportPanel.razor](../Tsump/Components/ScoreImportPanel.razor) `Apply` | the shared component | Invokes `OnApplied` (consumer e.g. `TournamentDetail.ReloadTournament` reloads + re-inits scores), then continues per input method — see [score-import-ui.md](score-import-ui.md) |

`BuildPreviewAsync` / `DescribeAsync` are also consumed by [ScanLog.razor](../Tsump/Pages/ScanLog.razor) (read-only; no apply).
