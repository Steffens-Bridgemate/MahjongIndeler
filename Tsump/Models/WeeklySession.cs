namespace Tsump.Models;

public class WeeklySession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public List<Guid> PresentMemberIds { get; set; } = new();
    public List<Guid> AbsentMemberIds { get; set; } = new();
    public List<TableAssignment> Tables { get; set; } = new();
    public bool IsFinalized { get; set; }
}

public class TableAssignment
{
    public int TableNumber { get; set; }
    public List<Guid> PlayerIds { get; set; } = new();
    public int PlayerCount => PlayerIds.Count;
}
