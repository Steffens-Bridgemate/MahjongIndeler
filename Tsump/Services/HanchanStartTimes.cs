using Tsump.Models;

namespace Tsump.Services;

/// <summary>A tournament's hanchan start times live in two places: the tournament-level override
/// list (<see cref="Tournament.StartTimes"/>, null = club defaults) and a copy on each generated
/// <see cref="TournamentSession"/> — the copy is what printouts, guidesheets, scoring invites and
/// the rankings read. Every editor funnels through here so the two can't drift apart.</summary>
public static class HanchanStartTimes
{
    /// <summary>The times in effect for hanchans 1..N: the tournament's own list when it has one,
    /// else the time already stamped on that session, else the club defaults in time order. The
    /// session fallback is what keeps tournaments generated before <see cref="Tournament.StartTimes"/>
    /// existed showing their real times instead of today's club defaults; it can't swallow a
    /// regenerate, because freshly generated sessions carry no time yet. Always sized to the
    /// tournament's hanchan count (or the sessions actually present, if that is larger).</summary>
    public static List<TimeSpan?> Effective(Tournament tournament, IReadOnlyList<TimeSpan> clubDefaults)
    {
        var count = Math.Max(tournament.SessionCount, tournament.Sessions.Count);
        var overrides = tournament.StartTimes;
        var defaults = overrides == null ? clubDefaults.OrderBy(t => t).ToList() : new List<TimeSpan>();

        var result = new List<TimeSpan?>(count);
        for (int i = 0; i < count; i++)
        {
            if (overrides != null)
            {
                result.Add(i < overrides.Count ? overrides[i] : null);
                continue;
            }
            var stamped = tournament.Sessions.FirstOrDefault(s => s.SessionNumber == i + 1)?.StartTime;
            result.Add(stamped ?? (i < defaults.Count ? defaults[i] : null));
        }
        return result;
    }

    /// <summary>Copies the effective times onto the generated sessions. No-op before generation.</summary>
    public static void ApplyToSessions(Tournament tournament, IReadOnlyList<TimeSpan> clubDefaults)
    {
        var times = Effective(tournament, clubDefaults);
        foreach (var session in tournament.Sessions)
        {
            var i = session.SessionNumber - 1;
            session.StartTime = i >= 0 && i < times.Count ? times[i] : null;
        }
    }

    /// <summary>Sets one hanchan's start time. Materialises the override list from the currently
    /// effective times first, so the first edit freezes the club defaults in place rather than
    /// leaving the untouched hanchans tracking a setting that may change later.</summary>
    public static void Set(Tournament tournament, int sessionNumber, TimeSpan? time, IReadOnlyList<TimeSpan> clubDefaults)
    {
        var index = sessionNumber - 1;
        if (index < 0) return;

        var times = Effective(tournament, clubDefaults);
        while (times.Count <= index) times.Add(null);
        times[index] = time;

        tournament.StartTimes = times;
        ApplyToSessions(tournament, clubDefaults);
    }

    /// <summary>Drops the override so the tournament follows the club defaults again.</summary>
    public static void Reset(Tournament tournament, IReadOnlyList<TimeSpan> clubDefaults)
    {
        tournament.StartTimes = null;
        // Wipe the stamped copies first — Effective falls back to them, so leaving them in place
        // would just re-apply the very values being reset away.
        foreach (var session in tournament.Sessions) session.StartTime = null;
        ApplyToSessions(tournament, clubDefaults);
    }

    /// <summary>Replaces the hour of an existing time, keeping the minutes. A negative hour (the
    /// "--" option) clears the time entirely.</summary>
    public static TimeSpan? WithHour(TimeSpan? current, int hour) =>
        hour >= 0 ? new TimeSpan(hour, current?.Minutes ?? 0, 0) : null;

    /// <summary>Replaces the minutes of an existing time, keeping the hour. A negative value (the
    /// "--" option) clears the time entirely.</summary>
    public static TimeSpan? WithMinute(TimeSpan? current, int minute) =>
        minute >= 0 ? new TimeSpan(current?.Hours ?? 0, minute, 0) : null;
}
