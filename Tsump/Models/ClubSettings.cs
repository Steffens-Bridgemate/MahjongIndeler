namespace Tsump.Models;

/// <summary>How a tournament ranking treats the hanchans a dropped-out player did not play.
/// For the substitution modes (Zero/Average) a signed per-hanchan correction can be added on top.</summary>
public enum AbsentScoringMode
{
    /// <summary>A hanchan not played contributes zero.</summary>
    Zero = 0,
    /// <summary>A hanchan not played contributes the average of the player's *played* hanchans —
    /// recomputed as results come in, so the substituted value can change over time.</summary>
    Average = 1,
    /// <summary>The player is excluded from the ranking altogether and listed on a separate,
    /// unranked line below it: played hanchans show their actual scores, missed ones show "—".
    /// The correction does not apply in this mode.</summary>
    Excluded = 2,
}

public class ClubSettings
{
    public string CompetitionPeriod { get; set; } = "";
    public bool EnableScoreEntry { get; set; } = false;
    public List<ScheduleEntry> Schedule { get; set; } = new();
    public TimeSpan DefaultStartTime { get; set; } = new TimeSpan(13, 0, 0);
    public List<TimeSpan> TournamentStartTimes { get; set; } = new()
    {
        new TimeSpan(10, 0, 0),
        new TimeSpan(11, 30, 0),
        new TimeSpan(13, 0, 0),
        new TimeSpan(14, 30, 0),
        new TimeSpan(16, 0, 0)
    };

    // Weekly scoring defaults
    public int WeeklyStartingPoints { get; set; } = 30000;
    public List<int> WeeklyUma4Players { get; set; } = new() { 15000, 5000, -5000, -15000 };
    public List<int> WeeklyUma3Players { get; set; } = new() { 12500, 0, -12500 };

    // Members table pinned columns (default: email visible)
    public List<string> MembersPinnedColumns { get; set; } = new() { "email" };

    // Tournament scoring defaults
    public int TournamentStartingPoints { get; set; } = 30000;
    public List<int> TournamentUma4Players { get; set; } = new() { 15000, 5000, -5000, -15000 };
    public List<int> TournamentUma3Players { get; set; } = new() { 12500, 0, -12500 };
    // Ranking treatment of hanchans a dropped-out player did not play (see AbsentScoringMode);
    // the correction is ×1000 points, added per missed hanchan on top of the mode's base value.
    public AbsentScoringMode TournamentAbsentScoringMode { get; set; } = AbsentScoringMode.Zero;
    public int TournamentAbsentCorrection { get; set; } = 0;

    // When true, the "Share scoring link" feature is exposed per-table. The scoring app's
    // URL is hard-coded in Tsump.Scoring.ScoringAppConfig (not user-configurable, since
    // PWA-installed organizer instances hide the address bar).
    public bool EnableExternalScoring { get; set; } = false;

    // When true, every captured scan/import string is recorded by ScanLogService and a "Log"
    // nav item is exposed for inspecting and decoding them. Diagnostic aid for HID-scanner
    // round-trip issues; off by default.
    public bool EnableScanLogging { get; set; } = false;

    // Club branding (colours, brand text, website link, logo, banner). Defaults are the Tsumo!
    // club's, so the live deployment is unchanged out of the box; the Style tab edits these and
    // "Reset to neutral" swaps in ClubStyle.Neutral().
    public ClubStyle Style { get; set; } = new();

    // The club's primary display language (one of LanguageService.SupportedLanguages). The
    // secondary language is always English. Default "nl" keeps the Tsumo deployment unchanged.
    public string PrimaryLanguage { get; set; } = "nl";
}

public class ClubStyle
{
    public string PrimaryColor { get; set; } = "#004b8d";
    public string LinkColor { get; set; } = "#0088cc";

    // Upper-left brand. Shown as text when LogoImage is null.
    public string BrandText { get; set; } = "Tsumo!";

    // Upper-right link. Hidden entirely when WebsiteUrl is blank.
    public string WebsiteUrl { get; set; } = "https://www.mahjongclubtsumo.nl/";
    public string WebsiteLabel { get; set; } = "Mahjongclub Tsumo";

    // Either a remote URL or a downscaled data: URL produced by readImageAsDataUrl.
    public string? LogoImage { get; set; }
    public string? BannerImage { get; set; } = "https://www.mahjongclubtsumo.nl/images/headers/header-logo.jpg";

    // All properties are immutable strings, so a memberwise clone is a deep copy. Used by the
    // Style editor to keep an independent working copy vs. the last-saved snapshot.
    public ClubStyle Clone() => (ClubStyle)MemberwiseClone();

    // The "blank slate" palette for a club that isn't Tsumo. Deliberately desaturated so it
    // reads as "unbranded" rather than impersonating another club.
    public static ClubStyle Neutral() => new()
    {
        PrimaryColor = "#5a6b7b",
        LinkColor = "#3b6ea5",
        BrandText = "Mahjong",
        WebsiteUrl = "",
        WebsiteLabel = "",
        LogoImage = null,
        BannerImage = null,
    };
}

public class ScheduleEntry
{
    public DayOfWeek Day { get; set; }
    public List<TimeSpan> StartTimes { get; set; } = new();
}
