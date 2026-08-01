# Domain model

Cross-file invariants for the weekly/tournament data hierarchy. These are the parts that are *not* obvious from reading any single file.

## People

| Type | File | Identifying fields |
|---|---|---|
| `Member` | [Tsump/Models/Member.cs](../Tsump/Models/Member.cs) | `Guid Id`, `Name`, `IsActive`, `LeagueId`, `ExtraThreePlayerTableCount` |
| `TournamentParticipant` | [Tsump/Models/Tournament.cs](../Tsump/Models/Tournament.cs) | `Guid Id`, `Name`, `int Number`, `LeagueId` |

A player's display name differs between contexts: `Member.Name` for weekly, `"{Number}. {Name}"` for tournament (`TournamentDetail.GetParticipantDisplay`). Pass the appropriate `PlayerNameResolver` to `ScoreTable` and `TableShareActions`.

A `Member` and a `TournamentParticipant` are **not the same identity** — even a player who shows up in both has a different `Id` in each. Stored scores reference whichever Id was used when the session/tournament was created.

`Member.IsActive` exists primarily for soft-delete: a member who appears in any historical session is **deactivated** instead of deleted ([Members.razor:354-379](../Tsump/Pages/Members.razor)). Only members with zero history are hard-deleted.

## Sessions

Two parallel kinds, both containing `List<TableAssignment> Tables`:

| Kind | Container | File | Group by |
|---|---|---|---|
| Weekly | `Hanchan` | [Tsump.Shared/Models/Hanchan.cs](../Tsump.Shared/Models/Hanchan.cs) | `Date` (implicit — no parent "session" object; "hanchans on date" is `SessionService.GetAllAsync().Where(h => h.Date.Date == …)`) |
| Tournament | `TournamentSession` inside `Tournament` | [Tsump/Models/Tournament.cs](../Tsump/Models/Tournament.cs) | `Tournament.Sessions`, ordered by `SessionNumber` |

Both `Hanchan` and `TournamentSession` carry a `Guid Id`. The shared codec ([ScoringPayload.cs](../Tsump.Shared/Scoring/ScoringPayload.cs)) uses that Id as the `ContextId` in invites/results — the same payload shape works for both kinds.

**Weekly groupings have no parent object.** UI concepts like "1st hanchan of the day" are derived at view time by ordering `hanchansOnDate` by `StartTime`. There's no stored hanchan number.

`Hanchan.ExcludeFromOptimization`: per-hanchan flag, but **propagated across all hanchans on the same date** by `SaveHanchan` ([WeeklySessionPage.razor](../Tsump/Pages/WeeklySessionPage.razor)). Effectively per-date despite being stored per-hanchan.

`TournamentSession.Id` is back-filled when missing. Stored tournaments from before the field existed deserialise with `Guid.Empty`; `TournamentService.GetAllAsync` ([TournamentService.cs](../Tsump/Services/TournamentService.cs)) assigns new Guids and re-saves on first load.

## Table assignment (weekly)

`TableAssignmentService.AssignTables` ([TableAssignmentService.cs](../Tsump/Services/TableAssignmentService.cs)) seats present players at 4-player (preferred) and 3-player tables. Rules, in priority order:

1. **Hard constraint**: whoever sat at a 3-player table in their most recent attended session is not eligible for one now (unless there's no other way to fill the slots). Because same-date hanchans always count as history, this also blocks double 3-duty within one evening.
2. **3-player duty fairness**: candidates are ranked on the **post-assignment ratio** `(threeCount + 1) / (attended + 1)` — the ratio a player *would* have after taking duty. This must stay consistent with `ScoreThreePlayerFairness` and makes first-timers (attendance 0) the *least* attractive candidates (ratio 1.0). The pre-2026-07 code ranked on the historical ratio, which mapped "no history" to 0.0 and *guaranteed* newcomers a 3-player seat. `Member.ExtraThreePlayerTableCount` seeds the count for players who owe extra duty. Ties at the cutoff ratio are broken by attendance (highest first); when a tier of players tied on *both* ratio and attendance only partly fits the remaining slots, duty goes to the **subset with the lowest mutual meeting cost** — exhaustive over all C(n,k) subsets (`SelectTierSubset`; greedy fallback above a combination cap for pathological cases like a first-ever session), random among equal-best subsets. The earlier greedy one-at-a-time pick was effectively a lottery when everyone was tied: its first pick scored against an empty selection (all zeros), and since every tied subset has the same fairness score, the attempt loop's early exit meant only one candidate trio was ever evaluated.
3. **Meeting spread**: seating minimizes pair meeting costs (`met² / min(attendance)` — convex, so each further repeat gets quadratically more expensive; a linear cost let repeats pile up on the highest-attendance players), via greedy table formation followed by a swap hill-climb (`ImproveBySwaps` — swaps between same-size tables only, so duty fairness is untouched). Pairs that already met earlier the **same day** carry a flat penalty of 10 (`PairCostModel`), so a second hanchan of an evening avoids repeat opponents wherever the player count allows (with 8 or 12 players some repeats are mathematically forced). The exactly-21-players second hanchan keeps its dedicated solver (`TryBuildSpecialCase21SecondHanchan`) which *guarantees* zero repeats.

The `Simulation` console project is the offline test bench: walk-forward replay of a real export across algorithm variants (`dotnet run -- export.json`), plus `--regen out.json` to rewrite an export's assignments with the live algorithm for in-app comparison, and `--regen2 out.json` to additionally give every regenerated evening a second hanchan (same attendance, +90 min, new Guid) — the September two-hanchan format. Real club exports (`currentdata.json`, `*regenerated*.json`) are gitignored.

## Table assignment (tournament)

`TournamentAssignmentService.GenerateAllSessions` ([TournamentAssignmentService.cs](../Tsump/Services/TournamentAssignmentService.cs)) builds *all* hanchans at once from a fixed movement pattern (players split into remaining/+1/+2/+3 groups by their position in number order; phantoms stand in for the missing seats of 3-player tables), then post-processes: `BalanceThreePlayerDistribution`, `ReduceDuplicateMeetings`, `ApplyUniversalStartPositions`.

**Numbering contract.** `TournamentParticipant.Number` is the player's identity for the whole event — the club hands out numbered lots *before* generating, so a number must never move to another person. Generation therefore guarantees both halves:

- **Numbers survive generation.** `ApplyUniversalStartPositions` computes seat numbers 1..N from the session-1 table order and then **relabels the seats, not the people**: seat *k* is given to the participant ranked *k*-th by number. Substituting player ids across the whole schedule is a bijection, so the meeting spread and 3-player balance the earlier passes produced are unchanged (verified: the resulting meeting/3-player distributions are identical to the pre-2026-07 renumbering path for N=15…40 × 2 and 4 hanchans). Until 2026-07 the method instead wrote fresh numbers onto the participants, which reshuffled who held which number — with 30 players *all 30* came out with a different number than they went in with.
- **Session 1 seats numbers in order**: table 1 holds 1-4, table 2 holds 5-8, … and 3-player tables get the highest table *and* participant numbers. Later sessions reuse the same table numbering, ordered 4-player-first then by lowest player number.

The relabeling needs numbers that are a clean 1..N-style set: if any participant has `Number == 0` or two share a number, it falls back to the old behaviour (hand out numbers by seat). The organizer UI blocks generation while anyone is unnumbered (see [pages.md](pages.md#tournamentsrazor--tournamentparticipantsrazor--tournamentscoringsettingsrazor)), so in practice only the bench and legacy data reach the fallback.

## Tables and scores

`TableAssignment` ([Tsump.Shared/Models/Hanchan.cs](../Tsump.Shared/Models/Hanchan.cs)) holds `TableNumber`, `PlayerIds`, `AbsentPlayerIds`, and optional `TableScore`. Tables are 3 or 4 players.

**`PlayerIds` is the seating record; `ActivePlayerIds()` is who's playing.** A player who quits mid-event is added to `AbsentPlayerIds` — they stay in `PlayerIds` (so the seating, and the meeting matrix built from it, survive) but stop counting: `PlayerCount` is `PlayerIds.Count(id => !AbsentPlayerIds.Contains(id))`, so a 4-player table with one dropout becomes a 3-player table with Mr. X and 3-player Uma, with no other code needing to know. `ActivePlayerIds()` is a **method**, not a property, so System.Text.Json doesn't write it into every stored table.

Anything that means *"who is playing"* must use `ActivePlayerIds()`, not `PlayerIds` — in particular every **positionally paired** path, since a mismatch silently lands scores on the wrong people: `ScoreTable.InitializeScores`, `ScoreInviteService.BuildInviteUrl` (the QR carries three names), both `IScoreContextResolver` implementations' `PlayerNames`, `ScoreImportService.ByPosition`, the import-preview name columns, and the printed scoresheets. `PlayerIds` stays correct for the seating record: History, MeetingMatrix, the assignment generators, and the assignment exports.

Marking absent is **organizer-only and per table** — there is no cascade to the player's later hanchans, and the scoring app has no notion of it at all (it just receives an ordinary 3-player invite). The control is behind `ScoreTable`'s `AllowAbsence` parameter, which only [TournamentDetail.razor](../Tsump/Pages/TournamentDetail.razor) sets. It is offered only on a 4-player table: a second dropout would leave two real players plus Mr. X, which has no Uma set and isn't playable — that has to be fixed by reseating.

`TableAssignment.Scanned` is organizer-side bookkeeping: "this table's invite QR has been handed out / scanned". Set from the QR modal (Enter sets, never clears — a scanner's trailing key) and the per-table card checkbox (mouse toggles), persisted with the container. It's **not** on the scoring wire payload, and the whole scanned UI is gated on `ClubSettings.EnableExternalScoring`.

`TableScore.PlayerScores` order matters: real players in `ActivePlayerIds()` order, then any virtual players at the end. `ScoreTable.InitializeScores` enforces this ordering, drops rows for players who left the table (including dropouts), and adds the virtual Mr. X for 3-player tables.

**Scores are bound to the seat, not the person.** Nothing in `PlayerScores` records who a row belongs to once written, beyond `PlayerId` — so reseating a table that already has scores would re-label them. Both pages therefore refuse to swap any player at a table where `ScoreStatusHelper.HasAnyScore(table)`; see [pages.md](pages.md#tournamentdetailrazor--weeklysessionpagerazor).

### Mr. X (the 3-player virtual fourth)

A 3-player table gets a single `PlayerScore { IsVirtual = true, VirtualName = "Mr. X" }` appended. The virtual player's `EndPoints` is **always derived**, never user-entered:

```
realDiff = Σ (real.EndPoints - real.StartingPoints + real.Loan)
mrX.EndPoints = mrX.StartingPoints - realDiff
```

so the sum of differences across all four `PlayerScore` rows is zero. (Real players' `Penalty` is deliberately excluded — a penalty isn't a point transfer.)

**The derivation is never trusted from storage.** `ScoreTable.DeriveVirtualEnd` runs on every edit, at the tail of `InitializeScores`, and in `ScoreTable.OnParametersSet` — the last one matters because the `Table` parameter is re-pointed at freshly-deserialised objects after every save/import/reload, and a stored Mr. X value can predate a later edit. Deriving on parameter-set means a stale number can't reach the screen. `ScoreImportService.ApplyAsync` ([ScoreImportService.cs](../Tsump/Services/ScoreImportService.cs)) calls it too, so manual entry and imported results produce identical Mr. X values.

The *persistence* side of the same problem: `OnScoresComplete` only fires on a fully-filled, balanced table, so an edit that blanks or unbalances a score was never written and reload resurrected the old numbers. `ScoreTable.OnScoresChanged` fires after **every** edit and is wired to a plain save on both pages — on WeeklySessionPage deliberately to `PersistScoresOnly`, *not* `SaveScores`, because the latter re-reads storage and re-points `tables` at new objects, which mid-typing would churn the inputs under the caret.

### Uma

`ScoreTable.CalculateUma` ([ScoreTable.razor:561](../Tsump.Shared/Components/ScoreTable.razor)) only fires when all `EndPoints` are filled AND the differences sum to zero. Ranking is by `Difference` descending; **ties get averaged Uma** across the tied positions.

Weekly Uma is from `ClubSettings.WeeklyUma{3,4}Players`. Tournament Uma is from `Tournament.Uma{3,4}Players` if set, otherwise **falls back to weekly settings** — see [TournamentScoreContextResolver.cs](../Tsump/Services/TournamentScoreContextResolver.cs). Same fallback rule applies to `StartingPoints`.

### Score-status classification

`ScoreStatusHelper.TableIsComplete(table)` is the single-table predicate (all real `EndPoints` set + differences sum to 0); `TablesAreComplete` delegates to it. Used by the table-nav strip to colour a Scores-view button green per table (see [styles.md](styles.md#table-nav-strip)). `HasAnyScore(table)` is the single-table "any real `EndPoints` set" predicate — `TablesHaveAnyScore` delegates to it, and it doubles as the *don't reseat this table* test for the swap UI.

[ScoreStatusHelper.cs](../Tsump/Services/ScoreStatusHelper.cs) classifies a hanchan/session against its later siblings:

| Status | Rule | Tab class |
|---|---|---|
| `Normal` | No scores anywhere | (none) |
| `Partial` | This entry has some real `EndPoints` filled but not all | `bg-warning text-dark` |
| `Complete` | All tables have all `EndPoints` and sum-of-differences = 0 | `bg-success text-white` |
| `Stale` | This entry isn't Complete AND a later sibling has any score | `bg-danger text-white` |

**Stale beats Partial.** A hanchan with some scores but missing the rest, where a later hanchan already has scores, is Stale (red), not Partial (yellow). The rationale: a stale hanchan was likely abandoned mid-entry and needs attention before the user keeps going chronologically.

`ScoreStatusHelper.Aggregate` collapses a list of per-entry statuses for the "All Scores" tab: all-Complete → Complete; any-Stale → Stale; any-other-non-Normal → Partial; else Normal.

Tab badges show on both the per-hanchan/session tabs and the "Scores" tab. The question-mark explainer icon is on the Scores / All Scores tabs only — **never on per-hanchan tabs**.

Unselected tab fading: `ScoreStatusHelper.TabStyle(isActive)` returns `--bs-bg-opacity: 0.4` for inactive coloured tabs and empty for active. Bootstrap 5.3 honours that custom property on `bg-*` utilities so only the background dims, not the text.

## Storage

| Service | Storage key | What it persists |
|---|---|---|
| `MemberService` | `tsump_members` | `List<Member>` |
| `SessionService` | `tsump_sessions` | `List<Hanchan>` |
| `TournamentService` | `tsump_tournaments` | `List<Tournament>` |
| `SettingsService` | `tsump_settings` | single `ClubSettings` |
| `ScanLogService` | `tsump_scan_log` | `List<ScanLogEntry>` (import-capture diagnostics; newest-first, capped at 200, only written when `ClubSettings.EnableScanLogging`) |

All `GetAllAsync` calls deserialise fresh — see the **stale-reference** gotcha in [CLAUDE.md](../CLAUDE.md). All Save* calls re-read the full list, replace the entry by Id, write back.
