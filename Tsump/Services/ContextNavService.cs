namespace Tsump.Services;

/// <summary>
/// Generic contextual sidebar navigation. A page publishes a flat list of entries that
/// <c>NavMenu</c> renders in place of the normal menu, so the page's own state and handlers drive
/// the sidebar (used by the weekly session page, whose "tabs" are in-page state rather than routes).
/// The owning page clears it on dispose, so leaving the page restores the normal menu.
/// </summary>
public class ContextNavService
{
    public IReadOnlyList<ContextNavEntry> Entries { get; private set; } = Array.Empty<ContextNavEntry>();

    public event Action? OnChanged;

    public void Set(IReadOnlyList<ContextNavEntry> entries)
    {
        Entries = entries;
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        if (Entries.Count == 0) return;
        Entries = Array.Empty<ContextNavEntry>();
        OnChanged?.Invoke();
    }
}

public sealed class ContextNavEntry
{
    public string Label { get; init; } = "";
    /// <summary>A "bi-…-nav-menu" glyph class (background-image icon), optional.</summary>
    public string? IconClass { get; init; }
    /// <summary>A Bootstrap bg-* class for a leading status dot, optional (used instead of an icon).</summary>
    public string? StatusClass { get; init; }
    public bool IsActive { get; init; }
    public bool Disabled { get; init; }
    /// <summary>Nesting depth: 0 = top-level, 1 = an indented sub-item (e.g. the tournament's
    /// per-hanchan / per-table jump list under its active view selector).</summary>
    public int Indent { get; init; }
    /// <summary>When set, the entry is a plain link; otherwise <see cref="OnSelect"/> drives it.</summary>
    public string? Href { get; init; }
    public Func<Task>? OnSelect { get; init; }
    /// <summary>Optional trailing "?" explainer action.</summary>
    public Func<Task>? OnExplain { get; init; }
}
