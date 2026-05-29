using Tsump.Components;
using Tsump.Models;
using Tsump.Scoring;

namespace Tsump.Services;

/// <summary>
/// Applies a decoded <see cref="ScoringResult"/> to the matching table inside whichever
/// container (Hanchan or TournamentSession) the ContextId points to. Resolves through
/// any registered <see cref="IScoreContextResolver"/>; the first one that recognises the
/// ContextId wins.
/// </summary>
public class ScoreImportService
{
    private readonly IEnumerable<IScoreContextResolver> _resolvers;
    private readonly SettingsService _settings;

    public ScoreImportService(IEnumerable<IScoreContextResolver> resolvers, SettingsService settings)
    {
        _resolvers = resolvers;
        _settings = settings;
    }

    public enum FailureReason
    {
        None,
        NoMatchingContainer,   // ContextId not recognised by any resolver
        NoMatchingTable,       // Container found, table number absent (e.g. session regenerated)
        AlreadyScored,         // Table already has non-virtual EndPoints and overwrite wasn't confirmed
    }

    public record Lookup(ResolvedContext Context);

    public record ApplyOutcome(bool Success, FailureReason Reason, ResolvedContext? Context);

    /// <summary>A decoded result mapped onto its (resolved) table, ready to render read-only.
    /// <see cref="NameResolver"/> resolves the table's player ids to display names regardless of
    /// container kind. <see cref="TableMismatch"/> is true when the score-row count no longer
    /// matches the table (e.g. the session was regenerated) — the table is shown un-applied.</summary>
    public record DecodePreview(ResolvedContext Context, Func<Guid, string> NameResolver, bool TableMismatch);

    /// <summary>Header info for a logged capture: the friendly hanchan/table label if any
    /// resolver recognises the ContextId, and whether the specific table still exists.</summary>
    public record ScanDescription(string? Label, bool Recognized, bool TableFound);

    /// <summary>Finds the table referenced by the result, without applying anything. Returns
    /// null if no resolver recognised the ContextId or the table wasn't found.</summary>
    public async Task<Lookup?> FindAsync(ScoringResult result)
    {
        var (context, _) = await ResolveAsync(result);
        return context == null ? null : new Lookup(context);
    }

    /// <summary>
    /// Writes the result's scores into the matching table, derives Mr. X's score for 3-player
    /// tables, and persists the container. Returns details for UI feedback.
    /// </summary>
    /// <param name="confirmOverwrite">
    /// If false and the target table already has any non-null EndPoints, the operation is
    /// refused with <see cref="FailureReason.AlreadyScored"/>. Pass true to overwrite.
    /// </param>
    public async Task<ApplyOutcome> ApplyAsync(ScoringResult result, Func<string, string> langGet, bool confirmOverwrite = false)
    {
        var (context, resolver) = await ResolveAsync(result);
        if (context == null || resolver == null)
        {
            // Distinguish "no resolver recognised it" from "container found but no table".
            foreach (var r in _resolvers)
            {
                var outcome = await r.FindAsync(result.ContextId, result.TableNumber);
                if (outcome is ResolveOutcome.ContainerOnly)
                    return new ApplyOutcome(false, FailureReason.NoMatchingTable, null);
            }
            return new ApplyOutcome(false, FailureReason.NoMatchingContainer, null);
        }

        var table = context.Table;
        if (!confirmOverwrite
            && table.Score != null
            && table.Score.PlayerScores.Any(p => !p.IsVirtual && p.EndPoints.HasValue))
        {
            return new ApplyOutcome(false, FailureReason.AlreadyScored, context);
        }

        if (!WriteScoresIntoTable(context, result, langGet))
            return new ApplyOutcome(false, FailureReason.NoMatchingTable, context);

        await resolver.SaveAsync(context);
        return new ApplyOutcome(true, FailureReason.None, context);
    }

    /// <summary>Renders a decoded result as a read-only ScoreTable preview (no persistence).
    /// When a resolver recognises the ContextId, the real table's player names and scoring
    /// settings are used. Otherwise — a valid result whose context isn't local (another
    /// organizer, or a regenerated/deleted session) — a synthetic table with positional player
    /// names ("Player 1", …) and club-default settings is built, so any ScoreTable-worthy result
    /// can still be inspected. Returns null only when <paramref name="result"/> is null.</summary>
    public async Task<DecodePreview?> BuildPreviewAsync(ScoringResult result, Func<string, string> langGet)
    {
        if (result == null) return null;

        var (context, _) = await ResolveAsync(result);
        if (context != null)
        {
            // Mutates the freshly-deserialised (transient) table only; never persisted.
            var ok = WriteScoresIntoTable(context, result, langGet);
            return new DecodePreview(context, ByPosition(context.Table, context.PlayerNames), TableMismatch: !ok);
        }

        // Unrecognised context: fabricate a table from the result so the scores are still
        // viewable. One synthetic player id per score row (PlayerCount drives Mr. X derivation
        // for 3-player tables exactly as a real table would).
        var settings = await _settings.GetAsync();
        var playerIds = result.Scores.Select(_ => Guid.NewGuid()).ToList();
        // Parenthesised to signal these are fabricated, not real player names.
        var names = playerIds.Select((_, i) => $"({langGet("Player")} {i + 1})").ToList();
        var syntheticTable = new TableAssignment { TableNumber = result.TableNumber, PlayerIds = playerIds };
        var syntheticContext = new ResolvedContext(
            result,                                   // placeholder container; SaveAsync is never called on a preview
            $"{langGet("Table")} {result.TableNumber}",
            syntheticTable,
            settings.WeeklyStartingPoints,
            settings.WeeklyUma3Players,
            settings.WeeklyUma4Players,
            names);
        var okSyn = WriteScoresIntoTable(syntheticContext, result, langGet);
        return new DecodePreview(syntheticContext, ByPosition(syntheticTable, names), TableMismatch: !okSyn);
    }

    // Resolves a player id to a display name by its position in the table, falling back to the
    // raw id when out of range.
    private static Func<Guid, string> ByPosition(TableAssignment table, List<string> names)
        => id =>
        {
            var idx = table.PlayerIds.IndexOf(id);
            return idx >= 0 && idx < names.Count ? names[idx] : id.ToString();
        };

    /// <summary>Describes a (contextId, tableNumber) for the scan-log header without applying
    /// anything: friendly label when recognised, plus whether the table still exists.</summary>
    public async Task<ScanDescription> DescribeAsync(Guid contextId, int tableNumber)
    {
        foreach (var r in _resolvers)
        {
            var outcome = await r.FindAsync(contextId, tableNumber);
            switch (outcome)
            {
                case ResolveOutcome.Found f:
                    return new ScanDescription(f.Context.DisplayLabel, Recognized: true, TableFound: true);
                case ResolveOutcome.ContainerOnly c:
                    return new ScanDescription(c.DisplayLabel, Recognized: true, TableFound: false);
            }
        }
        return new ScanDescription(null, Recognized: false, TableFound: false);
    }

    /// <summary>Initialises Score rows on the table and writes the result's scores by position,
    /// deriving Mr. X and Uma exactly as the in-app ScoreTable does on edit. Returns false on a
    /// player-count mismatch (table regenerated after the link was issued), leaving Score
    /// initialised but un-applied so a preview can still render the empty table.</summary>
    private static bool WriteScoresIntoTable(ResolvedContext context, ScoringResult result, Func<string, string> langGet)
    {
        var table = context.Table;
        ScoreTable.InitializeScores(new List<TableAssignment> { table }, langGet, context.StartingPoints);

        var score = table.Score!;
        // Position-based pairing — result.Scores[i] applies to the i-th non-virtual PlayerScore.
        // Order is the same on both sides (the invite carried PlayerNames in table.PlayerIds
        // order; the scoring app renders + returns in that order). A count mismatch means the
        // table was regenerated after the link was issued.
        var realPlayers = score.PlayerScores.Where(p => !p.IsVirtual).ToList();
        if (result.Scores.Count != realPlayers.Count)
            return false;

        for (int i = 0; i < result.Scores.Count; i++)
        {
            var entry = result.Scores[i];
            realPlayers[i].EndPoints = entry[ScoringPayloadCodec.ScoreEndPoints];
            realPlayers[i].Loan      = entry[ScoringPayloadCodec.ScoreLoan];
            realPlayers[i].Penalty   = entry[ScoringPayloadCodec.ScorePenalty];
        }

        // 3-player table: derive Mr. X's EndPoints so the sum of differences is zero. Same helper
        // that ScoreTable runs on edit, so manual entry and imported results match.
        ScoreTable.DeriveVirtualEnd(table);
        ScoreTable.CalculateUma(table, context.Uma3Players, context.Uma4Players);
        return true;
    }

    private async Task<(ResolvedContext? Context, IScoreContextResolver? Resolver)> ResolveAsync(ScoringResult result)
    {
        foreach (var r in _resolvers)
        {
            var outcome = await r.FindAsync(result.ContextId, result.TableNumber);
            if (outcome is ResolveOutcome.Found f)
                return (f.Context, r);
        }
        return (null, null);
    }
}
