# Score apply (service layer)

The organizer side of the score round-trip. Companion to [score-import-ui.md](score-import-ui.md) which covers the UI; this file is the service-layer flow.

## Payload codec

[Tsump.Shared/Scoring/ScoringPayload.cs](../Tsump.Shared/Scoring/ScoringPayload.cs)

```csharp
record ScoringInvite(
    Guid ContextId, int TableNumber, List<string> PlayerNames, List<Guid> PlayerIds,
    int StartingPoints, List<int> Uma, string? Title, string? OrganizerUrl,
    int SessionNumber = 1);

record ScoringResult(Guid ContextId, int TableNumber, List<PlayerResultEntry> Scores);
record PlayerResultEntry(Guid PlayerId, int EndPoints, int Loan, int Penalty);
```

- `ContextId` is the **only key used for lookup**. It carries either a `Hanchan.Id` (weekly) or a `TournamentSession.Id` (tournament); the organizer's resolvers disambiguate.
- `SessionNumber` is display-only (used to build the "Hanchan N" subtitle on the scoring side); never used for lookup.
- `Uma` is sent in the outbound invite (the scoring app shows it), but **omitted** from the inbound result — the organizer recomputes Uma from its own settings to avoid drift if Uma config changes between invite send and result return.
- `OrganizerUrl` carries `Nav.BaseUri` so the scoring app can build a return URL back to whichever organizer instance issued the invite.

JSON via `System.Text.Json` with `DefaultIgnoreCondition = WhenWritingNull`, base64url-encoded into the URL fragment (`#p=…` outbound, `#r=…` inbound).

`HanchanId` / `HanchanNumber` are historical names from before tournaments existed — renamed to `ContextId` / `SessionNumber` in a coordinated cross-repo deploy. No back-compat shim; we relied on there being no outstanding scoring links at rename time.

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
4. **Apply.** For each `PlayerResultEntry`, set `EndPoints` / `Loan` / `Penalty` on the matching `PlayerScore`.
5. **Derive Mr. X.** `ScoreTable.DeriveVirtualEnd(table)`.
6. **Compute Uma.** `ScoreTable.CalculateUma(table, context.Uma3Players, context.Uma4Players)`.
7. **Persist.** `resolver.SaveAsync(context)` — Hanchan resolver calls `SessionService.SaveAsync`, Tournament resolver calls `TournamentService.SaveAsync` on the whole tournament.
8. **Return** `ApplyOutcome(Success, Reason, ResolvedContext?)`.

## What `ApplyAsync` does NOT do

- It doesn't refresh any UI state — callers reload data after a successful outcome (`ReloadTournament` in TournamentDetail; `ApplyImportPreview` in WeeklySessionPage).
- It doesn't initialise `Score` on other tables in the container. UI re-loaders must call `ScoreTable.InitializeScores` on all tables themselves to keep them visible (the score-null gotcha).
- It doesn't validate that the result's player Ids are members of the matching table. If a stale link is applied to a regenerated table with different players, the loop in step 4 silently skips entries whose `PlayerId` doesn't match — the table ends up partially filled.

## Find vs Apply

`ScoreImportService.FindAsync(ScoringResult)` is a read-only lookup used by the import UI to preview before applying. Same resolver iteration as `ApplyAsync` but no mutation, no save.

## Callers

| Caller | Path | What it does after `ApplyAsync` |
|---|---|---|
| [ImportScorePage.razor](../Tsump/Pages/ImportScorePage.razor) | `/import-score#r=…` deep link | Renders applied banner, link back to session |
| [WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor) `ApplyImportPreview` | inline import panel (Phase A holdover) | Reloads `hanchansOnDate`, re-points `currentHanchan`, re-inits scores, bumps `scoreTableVersion` |
| [ScoreImportPanel.razor](../Tsump/Components/ScoreImportPanel.razor) `Apply` | the new component | Invokes `OnApplied` callback; consumer ( `TournamentDetail.ReloadTournament`) reloads and re-inits scores |
