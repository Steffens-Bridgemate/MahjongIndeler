using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Tsump.Models;

namespace Tsump.Services;

/// <summary>One member the new round will keep, and the head start it writes to their record.</summary>
/// <param name="ThreeCount">3-player tables in the outgoing round, including the member's existing
/// extra count — the same tally the History page and the assignment algorithm use.</param>
/// <param name="Value">What <see cref="Member.ExtraThreePlayerTableCount"/> becomes. 0 for most.</param>
public record ThreePlayerCarryOver(
    Guid MemberId, string Name, double ThreeCount, int SessionsAttended, double Value)
{
    public double Ratio => SessionsAttended > 0 ? ThreeCount / SessionsAttended : 0;
}

/// <summary>What <see cref="ClubDataService.StartNewRoundAsync"/> is about to do to the roster.
/// Computed up front so the Settings page can show it before the user commits.</summary>
public record NewRoundPlan(
    IReadOnlyList<Member> RemovedMembers,
    IReadOnlyList<ThreePlayerCarryOver> ThreePlayerCarryOvers)
{
    /// <summary>Only the members the reset actually nudges — what the preview needs to show.
    /// Rounding to whole steps leaves most of the roster at 0, which is the point.</summary>
    public IReadOnlyList<ThreePlayerCarryOver> Nudged =>
        ThreePlayerCarryOvers.Where(c => c.Value != 0).ToList();
}

/// <summary>Whole-club operations that span several stores: the backup export and the
/// start-of-round reset that builds on it. Both live here rather than on a page because the
/// export is used from two places (Data management, and the new-round flow in Settings) and the
/// reset writes to four stores in an order that matters.</summary>
public class ClubDataService
{
    private readonly MemberService _members;
    private readonly SessionService _sessions;
    private readonly SettingsService _settings;
    private readonly TournamentService _tournaments;
    private readonly IJSRuntime _js;

    // Deliberately not translated: the backup filename should read the same whoever exported it.
    private const string LastKnownDataSuffix = "Last known data";

    // The carry-over is rounded to whole steps of this size and capped at this magnitude, matching
    // the member form's own step and range. Rounding hard is deliberate: it leaves most of the
    // roster at exactly 0 and keeps only the tails, so the field still reads as "nudged" rather
    // than as a ledger of noise.
    private const int CarryOverDecimals = 1;
    private const double CarryOverLimit = 0.5;

    public ClubDataService(MemberService members, SessionService sessions, SettingsService settings,
                           TournamentService tournaments, IJSRuntime js)
    {
        _members = members;
        _sessions = sessions;
        _settings = settings;
        _tournaments = tournaments;
        _js = js;
    }

    /// <summary>Serialises the whole club dataset and hands it to the browser as a download.
    /// Returns the filename used. <paramref name="filenamePrefix"/> replaces the default prefix
    /// (the competition period); the date-time suffix is always appended.</summary>
    public async Task<string> ExportAsync(string? filenamePrefix = null)
    {
        var members = await _members.GetAllAsync();
        var sessions = await _sessions.GetAllAsync();
        var settings = await _settings.GetAsync();
        var tournaments = await _tournaments.GetAllAsync();

        // The primary language is a local UI preference, not shared club data — strip it from the
        // export so importing a club file never changes the importer's language. (settings is a
        // throwaway copy from GetAsync, so mutating it here is safe.)
        var competitionPeriod = settings.CompetitionPeriod;
        settings.PrimaryLanguage = "";

        var exportData = new
        {
            ExportDate = DateTime.Now.ToString("o"),
            AppName = "Tsumo!",
            Members = members,
            Sessions = sessions,
            Settings = settings,
            Tournaments = tournaments
        };

        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var prefix = filenamePrefix
            ?? (string.IsNullOrWhiteSpace(competitionPeriod) ? "tsumo-export" : competitionPeriod.Trim());
        var filename = $"{prefix}-{DateTime.Now:yy-MMdd-HHmm}.json";
        await _js.InvokeVoidAsync("downloadFile", filename, base64);
        return filename;
    }

    /// <summary>What starting a new round would do to the roster: which members disappear, and what
    /// head start each survivor carries into the new round. Read-only — the Settings page shows this
    /// as the preview, and <see cref="StartNewRoundAsync"/> recomputes it to do the same for real.</summary>
    public async Task<NewRoundPlan> BuildNewRoundPlanAsync()
    {
        return BuildNewRoundPlan(await _members.GetAllAsync(), await _sessions.GetAllAsync());
    }

    private static NewRoundPlan BuildNewRoundPlan(List<Member> members, List<Hanchan> sessions)
    {
        var removed = members.Where(m => !m.IsActive).OrderBy(m => m.Name).ToList();
        var retained = members.Where(m => m.IsActive).ToList();

        // Same basis as the History page's % column and TableAssignmentService: only the sessions
        // the optimizer counts, and the existing extra count is part of the outgoing tally.
        var attendance = retained.ToDictionary(m => m.Id, _ => 0);
        var threeCounts = retained.ToDictionary(m => m.Id, m => m.ExtraThreePlayerTableCount);
        foreach (var session in sessions.Where(s => !s.ExcludeFromOptimization))
        {
            foreach (var id in session.PresentMemberIds)
                if (attendance.ContainsKey(id)) attendance[id]++;

            foreach (var table in session.Tables.Where(t => t.PlayerCount == 3))
                foreach (var id in table.PlayerIds)
                    if (threeCounts.ContainsKey(id)) threeCounts[id]++;
        }

        if (retained.Count == 0)
            return new NewRoundPlan(removed, Array.Empty<ThreePlayerCarryOver>());

        // Carry the exact quantity the assignment algorithm itself ranks on — its post-assignment
        // ratio (PostAssignmentThreeRatio) — so the wipe preserves the standing it had built up.
        // Its +1/+1 smoothing is why a member who never played comes out as the *least* attractive
        // 3-duty candidate rather than the most, which is deliberate and must survive the reset.
        var standing = retained.ToDictionary(
            m => m.Id, m => (threeCounts[m.Id] + 1) / (attendance[m.Id] + 1));

        // Centre on the median, not the mean: never-played members sit at 1.0 and drag a mean far
        // enough up to push most of the roster negative, which would make 0 stop meaning "neutral".
        var zeroPoint = Median(standing.Values);

        var carryOvers = retained
            .Select(m =>
            {
                var value = Math.Clamp(
                    Math.Round(standing[m.Id] - zeroPoint, CarryOverDecimals, MidpointRounding.AwayFromZero),
                    -CarryOverLimit, CarryOverLimit);
                return new ThreePlayerCarryOver(
                    m.Id, m.Name, threeCounts[m.Id], attendance[m.Id], value);
            })
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Name)
            .ToList();

        return new NewRoundPlan(removed, carryOvers);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Starts a new competition round: exports the current data as a backup, renames the
    /// competition period, drops the inactive members, rewrites every remaining member's extra
    /// 3-player count from <see cref="BuildNewRoundPlanAsync"/>, and clears every hanchan and
    /// tournament. Returns the backup's filename. The export runs first and everything after it is
    /// irreversible — that file is the only way back.</summary>
    public async Task<string> StartNewRoundAsync(string roundName)
    {
        var settings = await _settings.GetAsync();
        var members = await _members.GetAllAsync();
        var plan = BuildNewRoundPlan(members, await _sessions.GetAllAsync());

        // Named after the round that is *ending* — it holds that round's last known state.
        var outgoing = string.IsNullOrWhiteSpace(settings.CompetitionPeriod)
            ? LastKnownDataSuffix
            : $"{settings.CompetitionPeriod.Trim()} {LastKnownDataSuffix}";
        var backupFilename = await ExportAsync(outgoing);

        settings.CompetitionPeriod = roundName.Trim();
        await _settings.SaveAsync(settings);

        // Written for every survivor, not just the nudged ones — the outgoing round's own extra
        // count is spent, so anyone the plan puts at 0 must actually be reset to 0.
        var values = plan.ThreePlayerCarryOvers.ToDictionary(c => c.MemberId, c => c.Value);
        var retained = members.Where(m => m.IsActive).ToList();
        foreach (var member in retained)
            member.ExtraThreePlayerTableCount = values.GetValueOrDefault(member.Id, 0);
        await _members.ReplaceAllAsync(retained);

        await _sessions.DeleteAllAsync();
        await _tournaments.DeleteAllAsync();

        return backupFilename;
    }
}
