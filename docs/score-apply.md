# Score apply (service layer)

The organizer side of the score round-trip. Companion to [score-import-ui.md](score-import-ui.md) which covers the UI; this file is the service-layer flow.

## Payload codec

[Tsump.Shared/Scoring/ScoringPayload.cs](../Tsump.Shared/Scoring/ScoringPayload.cs)

```csharp
// Field names are minified ([JsonPropertyName("c")] etc.) to keep encoded QRs small.
record ScoringInvite(
    Guid ContextId,                    // c
    int TableNumber,                   // t
    List<string> PlayerNames,          // n
    int StartingPoints,                // p
    List<int> Uma,                     // u
    string? Title,                     // title
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
- `SessionNumber` is display-only (used to build the "Hanchan N" subtitle on the scoring side); never used for lookup.
- `Uma` is sent in the outbound invite (the scoring app shows it), but **omitted** from the inbound result — the organizer recomputes Uma from its own settings to avoid drift if Uma config changes between invite send and result return.
- `OrganizerUrl` carries `Nav.BaseUri` so the scoring app can build a return URL back to whichever organizer instance issued the invite.

JSON via `System.Text.Json` with `DefaultIgnoreCondition = WhenWritingNull`, base64url-encoded into the URL fragment (`#p=…` outbound, `#r=…` inbound). Minified field names + array-form score entries cut the result URL to ~35% of its prior length, which materially helps hardware 2D scanners decode the QR.

History: `HanchanId` / `HanchanNumber` were earlier names from before tournaments. Renamed to `ContextId` / `SessionNumber`. Field names minified and `PlayerResultEntry` collapsed into a 3-int array in a later coordinated deploy. No back-compat shim — relied on no outstanding scoring links in the wild.

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
    List<int> Uma4Players);
```

Two implementations, both DI-registered in [Program.cs](../Tsump/Program.cs):

- `HanchanScoreContextResolver` — `SessionService` lookup. Reads weekly settings from `SettingsService` for `StartingPoints` / Uma.
- `TournamentScoreContextResolver` — `TournamentService` lookup. Iterates `tournament.Sessions` to find the one whose `Id == contextId`. **Falls back to club weekly settings** when per-tournament overrides (`Tournament.StartingPoints` / `UmaXPlayers`) are null.

`ScoreImportService` ([ScoreImportService.cs](../Tsump/Services/ScoreImportService.cs)) takes `IEnumerable<IScoreContextResolver>` and iterates them in order; first `Found` wins. Adding a new context kind = add a new resolver + register it. No caller changes.

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

## What `ApplyAsync` does NOT do

- It doesn't refresh any UI state — callers reload data after a successful outcome (`ReloadTournament` in TournamentDetail; `ApplyImportPreview` in WeeklySessionPage).
- It doesn't initialise `Score` on other tables in the container. UI re-loaders must call `ScoreTable.InitializeScores` on all tables themselves to keep them visible (the score-null gotcha).
- It can't validate that the player **identities** still match (no PlayerIds in the wire format). If the assignments were regenerated and the new table has the same player *count* but different players, the result silently writes onto whichever players sit at those positions now. Count mismatch is caught (`NoMatchingTable`); identity drift is not.

## Find vs Apply

`ScoreImportService.FindAsync(ScoringResult)` is a read-only lookup used by the import UI to preview before applying. Same resolver iteration as `ApplyAsync` but no mutation, no save.

## Callers

| Caller | Path | What it does after `ApplyAsync` |
|---|---|---|
| [ImportScorePage.razor](../Tsump/Pages/ImportScorePage.razor) | `/import-score#r=…` deep link | Renders applied banner, link back to session |
| [WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor) `ApplyImportPreview` | inline import panel (Phase A holdover) | Reloads `hanchansOnDate`, re-points `currentHanchan`, re-inits scores, bumps `scoreTableVersion` |
| [ScoreImportPanel.razor](../Tsump/Components/ScoreImportPanel.razor) `Apply` | the new component | Invokes `OnApplied` callback; consumer ( `TournamentDetail.ReloadTournament`) reloads and re-inits scores |
