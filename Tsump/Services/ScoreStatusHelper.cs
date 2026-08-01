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

    /// <summary>One table has every real PlayerScore's EndPoints set and its differences sum to 0.</summary>
    public static bool TableIsComplete(TableAssignment table)
    {
        if (table.Score == null) return false;
        var realPlayers = table.Score.PlayerScores.Where(p => !p.IsVirtual).ToList();
        if (realPlayers.Count == 0 || !realPlayers.All(p => p.EndPoints.HasValue)) return false;
        return table.Score.PlayerScores.Sum(p => p.Difference) == 0;
    }

    /// <summary>Every real PlayerScore has EndPoints and each table's differences sum to 0.</summary>
    public static bool TablesAreComplete(List<TableAssignment> tables)
        => tables.Count > 0 && tables.All(TableIsComplete);

    /// <summary>Any real PlayerScore on this table has an EndPoints value set. Also the
    /// "don't reseat this table" test: scores are bound to the seat, not the player, so swapping
    /// players at a table that already has scores would silently re-label them.</summary>
    public static bool HasAnyScore(TableAssignment table)
        => table.Score != null
            && table.Score.PlayerScores.Any(p => !p.IsVirtual && p.EndPoints.HasValue);

    /// <summary>Any real PlayerScore has an EndPoints value set.</summary>
    public static bool TablesHaveAnyScore(List<TableAssignment> tables)
        => tables.Any(HasAnyScore);

    /// <summary>
    /// Status for one entry given its later siblings.
    /// Stale (red) wins over Partial (yellow): once a later sibling has scores, any earlier
    /// not-yet-complete entry is "stale" — it should have been completed first, regardless
    /// of whether it already has some scores or is still empty.
    /// </summary>
    public static Status Classify(
        List<TableAssignment> currentTables,
        IEnumerable<List<TableAssignment>> laterSiblingsTables)
    {
        if (TablesAreComplete(currentTables)) return Status.Complete;
        if (laterSiblingsTables.Any(TablesHaveAnyScore)) return Status.Stale;
        if (TablesHaveAnyScore(currentTables)) return Status.Partial;
        return Status.Normal;
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

    /// <summary>Bootstrap utility classes for a nav-link tab in the given status.
    /// <paramref name="isActive"/> distinguishes the selected (full-opacity) tab from unselected
    /// ones, which are dimmed via <c>--bs-bg-opacity</c> (see <see cref="TabStyle(bool)"/>):
    /// on the dimmed (faded) red, white text washes out, so an unselected danger tab uses dark
    /// text. The selected red keeps white text.</summary>
    public static string TabClass(Status s, bool isActive) => s switch
    {
        Status.Partial => "bg-warning text-dark",
        Status.Complete => "bg-success text-white",
        Status.Stale => isActive ? "bg-danger text-white" : "bg-danger text-dark",
        _ => "",
    };

    /// <summary>Background opacity applied to unselected colored tab headers so they
    /// are visually distinct from the (full-opacity) selected tab.</summary>
    public const double UnselectedTabBgOpacity = 0.4;

    /// <summary>Inline style that dims the bg-* utility on a Bootstrap nav-link when
    /// the tab is not the active one. Bootstrap 5.2+ honors <c>--bs-bg-opacity</c>
    /// on its bg-* utilities without affecting the foreground color.</summary>
    public static readonly string UnselectedTabBgStyle =
        $"--bs-bg-opacity: {UnselectedTabBgOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)};";

    /// <summary>Returns the bg-opacity inline style for a nav-link tab, or an empty
    /// string when the tab is active (full opacity).</summary>
    public static string TabStyle(bool isActive) => isActive ? "" : UnselectedTabBgStyle;

    /// <summary>As <see cref="TabStyle(bool)"/> but with a caller-chosen unselected background
    /// opacity (e.g. a panel that wants its inactive tabs dimmed more strongly).</summary>
    public static string TabStyle(bool isActive, double unselectedOpacity) =>
        isActive ? "" : $"--bs-bg-opacity: {unselectedOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)};";
}
