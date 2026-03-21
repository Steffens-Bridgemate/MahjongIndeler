namespace Tsump.Models;

public class ClubSettings
{
    public string CompetitionPeriod { get; set; } = "";
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

    // Tournament scoring defaults
    public int TournamentStartingPoints { get; set; } = 30000;
    public List<int> TournamentUma4Players { get; set; } = new() { 15000, 5000, -5000, -15000 };
    public List<int> TournamentUma3Players { get; set; } = new() { 12500, 0, -12500 };
}

public class ScheduleEntry
{
    public DayOfWeek Day { get; set; }
    public List<TimeSpan> StartTimes { get; set; } = new();
}
