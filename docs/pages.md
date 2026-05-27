# Pages — non-obvious bits

Sections appear only for pages with cross-file behaviour worth recording. Pages with self-evident behaviour (read the .razor) are intentionally absent.

## Members.razor

[Tsump/Pages/Members.razor](../Tsump/Pages/Members.razor)

- **Soft-delete via `IsActive`**: deleting a member who appears in any historical session sets `IsActive = false` instead of removing the record — historical data keeps a valid `PlayerId` reference. Only members with zero history are hard-deleted. See `DeleteClicked` (~line 354).
- Active flag drives default attendance pre-selection in `WeeklySessionPage` and the player list in `MeetingMatrix`.
- `MembersPinnedColumns` lives on `ClubSettings`, not in component state — column visibility is club-wide, not per-user.

## Settings.razor

[Tsump/Pages/Settings.razor](../Tsump/Pages/Settings.razor)

- **Two gate flags** on `ClubSettings`:
  - `EnableScoreEntry` — master gate. Reveals all scoring config (weekly + tournament uma/starting points + the external-scoring flag).
  - `EnableExternalScoring` — child gate. Reveals the per-table "Share scoring link" + QR + Import buttons across `WeeklySessionPage` and `TournamentDetail`. **Forced off** when `EnableScoreEntry` is toggled off (see line ~129).
- **Dual scoring config**: weekly defaults (`WeeklyStartingPoints` / `WeeklyUma{3,4}Players`) and tournament defaults (`TournamentStartingPoints` / `TournamentUma{3,4}Players`) are independent. A tournament can additionally have its own per-tournament overrides — those fall back to the tournament-defaults from settings only via `TournamentScoreContextResolver`'s null-coalesce. The Settings page only edits the club-wide defaults, not per-tournament overrides.
- **Scoring app URL is compile-time**: `Tsump.Scoring.ScoringAppConfig.DeployedUrl` ([Tsump.Shared/Scoring/ScoringAppConfig.cs](../Tsump.Shared/Scoring/ScoringAppConfig.cs)) is a `const string`. Not user-configurable — PWA-installed instances hide the address bar so users can't be expected to type a URL. Pointing at a different scoring app deployment requires editing the constant and redeploying.
- **Schedule** drives `SettingsService.GetNextScheduledSlotAsync` — used by the workflow page to pre-fill the next club-night's date/time.

## MeetingMatrix.razor

[Tsump/Pages/MeetingMatrix.razor](../Tsump/Pages/MeetingMatrix.razor)

- Pure statistical view over `SessionService.GetAllAsync()` — counts pair-meetings across all stored hanchans. Active members only.
- Drives no logic (read-only). The matrix is informational; the **actual** optimisation metric used when generating assignments lives in `TableAssignmentService`. Both consume the same underlying meeting counts but the matrix page doesn't share code with the assignment generator.
- Distribution table (right of the matrix) is clickable: clicking a meeting count dims all matrix cells except those with that count — for spotting which pairs are over/under-met.

## TournamentDetail.razor / WeeklySessionPage.razor

Cross-cutting concerns covered in [domain.md](domain.md) (status classification, Mr. X, Uma) and [score-import-ui.md](score-import-ui.md) (import panel, share actions, QR overlays). The pages themselves are mostly straightforward wiring around those.

Two non-obvious things specific to WeeklySessionPage worth flagging:

- `scoreTableVersion` is bumped on `SaveScores`, `SaveHanchan`, and `ApplyImportPreview`. It's part of every `ScoreTable`'s `@key` tuple so Blazor disposes/recreates each `ScoreTable` after a save — needed because the underlying `TableAssignment` references are replaced when `hanchansOnDate` is reloaded.
- `CurrentHanchanNumber` derives the 1-based "Hanchan N of the day" by ordering `hanchansOnDate` by `StartTime`. There's no stored hanchan number (see [domain.md](domain.md)).

## Workflow.razor

[Tsump/Pages/Workflow.razor](../Tsump/Pages/Workflow.razor)

- Step-by-step "guided session" UI for less-experienced organizers. Calls into the same `SessionService` / `TableAssignmentService` as `WeeklySessionPage`, but presents one step at a time with explicit Back/Next.
- Uses `SettingsService.GetNextScheduledSlotAsync` to pre-fill date/time from the club schedule.
- Passes `workflowStepKey` to `WeeklySessionPage` via query string so the back-to-workflow button on that page knows where to return.
