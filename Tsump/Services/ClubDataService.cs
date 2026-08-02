using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Tsump.Models;

namespace Tsump.Services;

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

    /// <summary>Starts a new competition round: exports the current data as a backup, renames the
    /// competition period, drops the inactive members, and clears every hanchan and tournament.
    /// Returns the backup's filename. The export runs first and everything after it is
    /// irreversible — that file is the only way back.</summary>
    public async Task<string> StartNewRoundAsync(string roundName)
    {
        var settings = await _settings.GetAsync();

        // Named after the round that is *ending* — it holds that round's last known state.
        var outgoing = string.IsNullOrWhiteSpace(settings.CompetitionPeriod)
            ? LastKnownDataSuffix
            : $"{settings.CompetitionPeriod.Trim()} {LastKnownDataSuffix}";
        var backupFilename = await ExportAsync(outgoing);

        settings.CompetitionPeriod = roundName.Trim();
        await _settings.SaveAsync(settings);

        var members = await _members.GetAllAsync();
        await _members.ReplaceAllAsync(members.Where(m => m.IsActive).ToList());

        await _sessions.DeleteAllAsync();
        await _tournaments.DeleteAllAsync();

        return backupFilename;
    }
}
