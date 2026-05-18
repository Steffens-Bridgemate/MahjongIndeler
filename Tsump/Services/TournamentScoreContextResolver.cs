using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Resolves <see cref="Tsump.Scoring.ScoringResult.ContextId"/> against tournament sessions
/// persisted by <see cref="TournamentService"/>. ContextId maps to <c>TournamentSession.Id</c>.
/// </summary>
public class TournamentScoreContextResolver : IScoreContextResolver
{
    private readonly TournamentService _tournaments;
    private readonly SettingsService _settings;
    private readonly LanguageService _lang;

    public TournamentScoreContextResolver(TournamentService tournaments, SettingsService settings, LanguageService lang)
    {
        _tournaments = tournaments;
        _settings = settings;
        _lang = lang;
    }

    public async Task<ResolveOutcome> FindAsync(Guid contextId, int tableNumber)
    {
        var all = await _tournaments.GetAllAsync();
        Tournament? matchingTournament = null;
        TournamentSession? matchingSession = null;
        foreach (var t in all)
        {
            var s = t.Sessions.FirstOrDefault(s => s.Id == contextId);
            if (s != null)
            {
                matchingTournament = t;
                matchingSession = s;
                break;
            }
        }
        if (matchingTournament == null || matchingSession == null)
            return new ResolveOutcome.NotMine();

        var label = $"{matchingTournament.Name} · {_lang.Get("Hanchan")} {matchingSession.SessionNumber}";

        var table = matchingSession.Tables.FirstOrDefault(t => t.TableNumber == tableNumber);
        if (table == null) return new ResolveOutcome.ContainerOnly(label);

        var settings = await _settings.GetAsync();
        var context = new ResolvedContext(
            matchingTournament,
            $"{label} · {_lang.Get("Table")} {tableNumber}",
            table,
            matchingTournament.StartingPoints ?? settings.WeeklyStartingPoints,
            matchingTournament.Uma3Players ?? settings.WeeklyUma3Players,
            matchingTournament.Uma4Players ?? settings.WeeklyUma4Players);
        return new ResolveOutcome.Found(context);
    }

    public async Task SaveAsync(ResolvedContext context)
    {
        if (context.Container is Tournament tournament)
            await _tournaments.SaveAsync(tournament);
    }
}
