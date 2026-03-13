namespace Tsump.Models;

public class ClubSettings
{
    public List<ScheduleEntry> Schedule { get; set; } = new();
}

public class ScheduleEntry
{
    public DayOfWeek Day { get; set; }
    public List<TimeSpan> StartTimes { get; set; } = new();
}
