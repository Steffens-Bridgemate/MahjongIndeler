using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Resolves <see cref="Tsump.Scoring.ScoringResult.ContextId"/> against weekly-session
/// hanchans persisted by <see cref="SessionService"/>.
/// </summary>
public class HanchanScoreContextResolver : IScoreContextResolver
{
    private readonly SessionService _sessions;
    private readonly SettingsService _settings;
    private readonly LanguageService _lang;
    private readonly MemberService _members;

    public HanchanScoreContextResolver(SessionService sessions, SettingsService settings, LanguageService lang, MemberService members)
    {
        _sessions = sessions;
        _settings = settings;
        _lang = lang;
        _members = members;
    }

    public async Task<ResolveOutcome> FindAsync(Guid contextId, int tableNumber)
    {
        var all = await _sessions.GetAllAsync();
        var hanchan = all.FirstOrDefault(h => h.Id == contextId);
        if (hanchan == null) return new ResolveOutcome.NotMine();

        var time = hanchan.StartTime.HasValue ? " · " + hanchan.StartTime.Value.ToString(@"hh\:mm") : "";
        var label = $"{_lang.FormatDate(hanchan.Date)}{time}";

        var table = hanchan.Tables.FirstOrDefault(t => t.TableNumber == tableNumber);
        if (table == null) return new ResolveOutcome.ContainerOnly(label);

        var settings = await _settings.GetAsync();
        var members = await _members.GetAllAsync();
        var names = table.PlayerIds
            .Select(id => members.FirstOrDefault(m => m.Id == id)?.Name ?? _lang.Get("Unknown"))
            .ToList();
        var context = new ResolvedContext(
            hanchan,
            $"{label} · {_lang.Get("Table")} {tableNumber}",
            table,
            settings.WeeklyStartingPoints,
            settings.WeeklyUma3Players,
            settings.WeeklyUma4Players,
            names);
        return new ResolveOutcome.Found(context);
    }

    public async Task SaveAsync(ResolvedContext context)
    {
        if (context.Container is Hanchan hanchan)
            await _sessions.SaveAsync(hanchan);
    }
}
