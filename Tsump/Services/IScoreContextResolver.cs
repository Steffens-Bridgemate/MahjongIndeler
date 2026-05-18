using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Locates the table referenced by an inbound <see cref="Tsump.Scoring.ScoringResult"/>
/// inside a specific kind of container (a <see cref="Hanchan"/> for weekly sessions, or
/// a <see cref="TournamentSession"/> inside a <see cref="Tournament"/>). ScoreImportService
/// iterates all registered resolvers and uses the first one that recognises the ContextId.
/// </summary>
public interface IScoreContextResolver
{
    Task<ResolveOutcome> FindAsync(Guid contextId, int tableNumber);

    /// <summary>Persists the container holding the resolved table after scores are written.</summary>
    Task SaveAsync(ResolvedContext context);
}

/// <summary>
/// Three-state outcome of a resolver lookup. <c>NotMine</c> means the resolver doesn't know
/// this ContextId — caller tries the next resolver. <c>ContainerOnly</c> means the container
/// was found but the table number doesn't exist inside it (e.g. the session was regenerated
/// after the link was shared). <c>Found</c> means everything matched.
/// </summary>
public abstract record ResolveOutcome
{
    public sealed record NotMine : ResolveOutcome;
    public sealed record ContainerOnly(string DisplayLabel) : ResolveOutcome;
    public sealed record Found(ResolvedContext Context) : ResolveOutcome;
}

/// <summary>
/// Type-erased view of a resolved table plus its scoring settings, so ScoreImportService
/// can apply the result without knowing whether the container is a Hanchan or a Tournament.
/// Callers that need the typed container can pattern-match on <see cref="Container"/>.
/// </summary>
public record ResolvedContext(
    object Container,
    string DisplayLabel,
    TableAssignment Table,
    int StartingPoints,
    List<int> Uma3Players,
    List<int> Uma4Players);
