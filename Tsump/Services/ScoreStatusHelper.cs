using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Classifies the score-entry status of a hanchan (or a tournament session) for use
/// in colored tab headers. Operates on <see cref="TableAssignment"/> lists so it can
/// serve both <see cref="Hanchan"/> and <c>TournamentSession</c> consumers.
/// </summary>
public static class ScoreStatusHelper
{
    public enum Status
    {
        Normal,    // no scores entered
        Partial,   // some scores entered but not (all-)complete
        Complete,  // every table has all real EndPoints + sumDiff = 0
        Stale,     // empty itself, but a later sibling has scores → should have been completed first
    }

    /// <summary>Every real PlayerScore has EndPoints and each table's differences sum to 0.</summary>
    public static bool TablesAreComplete(List<TableAssignment> tables)
    {
        if (tables.Count == 0) return false;
        foreach (var table in tables)
        {
            if (table.Score == null) return false;
            var realPlayers = table.Score.PlayerScores.Where(p => !p.IsVirtual).ToList();
            if (realPlayers.Count == 0 || !realPlayers.All(p => p.EndPoints.HasValue)) return false;
            if (table.Score.PlayerScores.Sum(p => p.Difference) != 0) return false;
        }
        return true;
    }

    /// <summary>Any real PlayerScore has an EndPoints value set.</summary>
    public static bool TablesHaveAnyScore(List<TableAssignment> tables)
        => tables.Any(t => t.Score != null
            && t.Score.PlayerScores.Any(p => !p.IsVirtual && p.EndPoints.HasValue));

    /// <summary>
    /// Status for one entry given its later siblings (used to detect the "stale" case
    /// where a later one has scores but this earlier one is empty).
    /// </summary>
    public static Status Classify(
        List<TableAssignment> currentTables,
        IEnumerable<List<TableAssignment>> laterSiblingsTables)
    {
        if (TablesAreComplete(currentTables)) return Status.Complete;
        if (TablesHaveAnyScore(currentTables)) return Status.Partial;
        return laterSiblingsTables.Any(TablesHaveAnyScore) ? Status.Stale : Status.Normal;
    }

    /// <summary>Aggregates a set of per-entry statuses into one cross-entry status.</summary>
    public static Status Aggregate(IEnumerable<Status> perEntry)
    {
        var list = perEntry.ToList();
        if (list.Count == 0) return Status.Normal;
        if (list.All(s => s == Status.Complete)) return Status.Complete;
        if (list.Any(s => s == Status.Stale)) return Status.Stale;
        if (list.Any(s => s != Status.Normal)) return Status.Partial;
        return Status.Normal;
    }

    /// <summary>Bootstrap utility classes for a nav-link tab in the given status.</summary>
    public static string TabClass(Status s) => s switch
    {
        Status.Partial => "bg-warning text-dark",
        Status.Complete => "bg-success text-white",
        Status.Stale => "bg-danger text-white",
        _ => "",
    };
}
