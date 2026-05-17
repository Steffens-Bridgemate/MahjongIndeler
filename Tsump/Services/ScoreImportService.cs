using Tsump.Components;
using Tsump.Models;
using Tsump.Scoring;

namespace Tsump.Services;

/// <summary>
/// Applies a decoded <see cref="ScoringResult"/> to the matching Hanchan + table in the
/// organizer's stored sessions. Shared by the route-based ImportScorePage and the in-app
/// paste flow on WeeklySessionPage.
/// </summary>
public class ScoreImportService
{
    private readonly SessionService _sessions;
    private readonly SettingsService _settings;

    public ScoreImportService(SessionService sessions, SettingsService settings)
    {
        _sessions = sessions;
        _settings = settings;
    }

    public enum FailureReason
    {
        None,
        NoMatchingHanchan,
        NoMatchingTable,
        AlreadyScored,
    }

    public record Lookup(Hanchan Hanchan, TableAssignment Table);

    public record ApplyOutcome(bool Success, FailureReason Reason, Hanchan? Hanchan, TableAssignment? Table);

    /// <summary>Finds the Hanchan + table referenced by the result, without applying anything.</summary>
    public async Task<Lookup?> FindAsync(ScoringResult result)
    {
        var all = await _sessions.GetAllAsync();
        var hanchan = all.FirstOrDefault(h => h.Id == result.HanchanId);
        var table = hanchan?.Tables.FirstOrDefault(t => t.TableNumber == result.TableNumber);
        return hanchan == null || table == null ? null : new Lookup(hanchan, table);
    }

    /// <summary>
    /// Writes the result's scores into the matching table, derives Mr. X's score for 3-player
    /// tables, and saves the hanchan. Returns details for UI feedback.
    /// </summary>
    /// <param name="confirmOverwrite">
    /// If false and the target table already has any non-null EndPoints, the operation is
    /// refused with <see cref="FailureReason.AlreadyScored"/>. Pass true to overwrite.
    /// </param>
    public async Task<ApplyOutcome> ApplyAsync(ScoringResult result, Func<string, string> langGet, bool confirmOverwrite = false)
    {
        var lookup = await FindAsync(result);
        if (lookup == null)
        {
            var all = await _sessions.GetAllAsync();
            var hanchan = all.FirstOrDefault(h => h.Id == result.HanchanId);
            return new ApplyOutcome(false,
                hanchan == null ? FailureReason.NoMatchingHanchan : FailureReason.NoMatchingTable,
                hanchan, null);
        }

        var (matchingHanchan, matchingTable) = (lookup.Hanchan, lookup.Table);

        if (!confirmOverwrite
            && matchingTable.Score != null
            && matchingTable.Score.PlayerScores.Any(p => !p.IsVirtual && p.EndPoints.HasValue))
        {
            return new ApplyOutcome(false, FailureReason.AlreadyScored, matchingHanchan, matchingTable);
        }

        var settings = await _settings.GetAsync();
        ScoreTable.InitializeScores(new List<TableAssignment> { matchingTable }, langGet, settings.WeeklyStartingPoints);

        var score = matchingTable.Score!;
        foreach (var entry in result.Scores)
        {
            var ps = score.PlayerScores.FirstOrDefault(p => p.PlayerId == entry.PlayerId);
            if (ps == null) continue;
            ps.EndPoints = entry.EndPoints;
            ps.Loan = entry.Loan;
            ps.Penalty = entry.Penalty;
        }

        // 3-player table: derive Mr. X's EndPoints from the three real players so the sum of
        // differences is zero (the scoring app already validated this).
        //   Difference = (EndPoints - StartingPoints) + Loan
        //   Mr. X has Loan = 0, so MrX.EndPoints = MrX.StartingPoints - sum(realDifferences)
        var mrX = score.PlayerScores.FirstOrDefault(p => p.IsVirtual);
        if (mrX != null)
        {
            var realDiff = score.PlayerScores
                .Where(p => !p.IsVirtual && p.EndPoints.HasValue)
                .Sum(p => (p.EndPoints!.Value - p.StartingPoints) + p.Loan);
            mrX.EndPoints = mrX.StartingPoints - realDiff;
        }

        // Compute Uma using the same logic the in-app ScoreTable uses on edit.
        ScoreTable.CalculateUma(matchingTable, settings.WeeklyUma3Players, settings.WeeklyUma4Players);

        await _sessions.SaveAsync(matchingHanchan);
        return new ApplyOutcome(true, FailureReason.None, matchingHanchan, matchingTable);
    }
}
