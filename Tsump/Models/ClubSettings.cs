namespace Tsump.Models;

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

    // When true, the "Share scoring link" feature is exposed per-table. The scoring app's
    // URL is hard-coded in Tsump.Scoring.ScoringAppConfig (not user-configurable, since
    // PWA-installed organizer instances hide the address bar).
    public bool EnableExternalScoring { get; set; } = false;

    // When true, every captured scan/import string is recorded by ScanLogService and a "Log"
    // nav item is exposed for inspecting and decoding them. Diagnostic aid for HID-scanner
    // round-trip issues; off by default.
    public bool EnableScanLogging { get; set; } = false;
}

public class ScheduleEntry
{
    public DayOfWeek Day { get; set; }
    public List<TimeSpan> StartTimes { get; set; } = new();
}
