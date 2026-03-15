namespace Tsump.Models;

public class ClubSettings
{
    public List<ScheduleEntry> Schedule { get; set; } = new();
    public TimeSpan DefaultStartTime { get; set; } = new TimeSpan(13, 0, 0);
}

public class ScheduleEntry
{
    public DayOfWeek Day { get; set; }
    public List<TimeSpan> StartTimes { get; set; } = new();
}
