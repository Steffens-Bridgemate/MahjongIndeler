namespace Tsump.Services;

/// <summary>
/// Lets the tournament sub-pages tell the sidebar which tournament is open and whether it has been
/// generated (so Guidesheets/Rankings can be gated), without the sidebar having to re-load that
/// state. Showing/hiding the contextual nav itself is still driven by the URL in NavMenu; this only
/// carries the "is generated" flag, which can flip live (e.g. right after Generate) and must reflect
/// in the sidebar without a navigation.
/// </summary>
public class TournamentNavContext
{
    public Guid? Id { get; private set; }
    public bool IsGenerated { get; private set; }

    public event Action? OnChanged;

    public void Set(Guid id, bool isGenerated)
    {
        if (Id == id && IsGenerated == isGenerated) return;
        Id = id;
        IsGenerated = isGenerated;
        OnChanged?.Invoke();
    }
}
