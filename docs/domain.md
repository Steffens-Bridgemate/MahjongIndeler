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

## Tables and scores

`TableAssignment` ([Tsump.Shared/Models/Hanchan.cs](../Tsump.Shared/Models/Hanchan.cs)) holds `TableNumber`, `PlayerIds`, and optional `TableScore`. `PlayerCount` is `PlayerIds.Count`. Tables are 3 or 4 players.

`TableScore.PlayerScores` order matters: real players in `PlayerIds` order, then any virtual players at the end. `ScoreTable.InitializeScores` enforces this ordering and adds the virtual Mr. X for 3-player tables.

### Mr. X (the 3-player virtual fourth)

A 3-player table gets a single `PlayerScore { IsVirtual = true, VirtualName = "Mr. X" }` appended. The virtual player's `EndPoints` is **always derived**, never user-entered:

```
realDiff = Σ (real.EndPoints - real.StartingPoints + real.Loan)
mrX.EndPoints = mrX.StartingPoints - realDiff
```

so the sum of differences across all four `PlayerScore` rows is zero. `ScoreTable.DeriveVirtualEnd` ([ScoreTable.razor:520](../Tsump.Shared/Components/ScoreTable.razor)) runs this on every edit. Crucially, `ScoreImportService.ApplyAsync` ([ScoreImportService.cs](../Tsump/Services/ScoreImportService.cs)) also calls it on imported results, so manual entry and imported results produce identical Mr. X values.

### Uma

`ScoreTable.CalculateUma` ([ScoreTable.razor:561](../Tsump.Shared/Components/ScoreTable.razor)) only fires when all `EndPoints` are filled AND the differences sum to zero. Ranking is by `Difference` descending; **ties get averaged Uma** across the tied positions.

Weekly Uma is from `ClubSettings.WeeklyUma{3,4}Players`. Tournament Uma is from `Tournament.Uma{3,4}Players` if set, otherwise **falls back to weekly settings** — see [TournamentScoreContextResolver.cs](../Tsump/Services/TournamentScoreContextResolver.cs). Same fallback rule applies to `StartingPoints`.

### Score-status classification

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
