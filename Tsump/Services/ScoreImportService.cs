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

    public ScoreImportService(IEnumerable<IScoreContextResolver> resolvers)
    {
        _resolvers = resolvers;
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

        ScoreTable.InitializeScores(new List<TableAssignment> { table }, langGet, context.StartingPoints);

        var score = table.Score!;
        foreach (var entry in result.Scores)
        {
            var ps = score.PlayerScores.FirstOrDefault(p => p.PlayerId == entry.PlayerId);
            if (ps == null) continue;
            ps.EndPoints = entry.EndPoints;
            ps.Loan = entry.Loan;
            ps.Penalty = entry.Penalty;
        }

        // 3-player table: derive Mr. X's EndPoints so the sum of differences is zero.
        // Same helper that ScoreTable runs on edit, so manual entry and imported results
        // produce identical Mr. X values.
        ScoreTable.DeriveVirtualEnd(table);

        // Compute Uma using the same logic the in-app ScoreTable uses on edit.
        ScoreTable.CalculateUma(table, context.Uma3Players, context.Uma4Players);

        await resolver.SaveAsync(context);
        return new ApplyOutcome(true, FailureReason.None, context);
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
