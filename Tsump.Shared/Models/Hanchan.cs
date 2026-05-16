namespace Tsump.Models;

public class Hanchan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public TimeSpan? StartTime { get; set; }
    public List<Guid> PresentMemberIds { get; set; } = new();
    public List<Guid> AbsentMemberIds { get; set; } = new();
    public List<TableAssignment> Tables { get; set; } = new();
    public bool IsFinalized { get; set; }
    public bool ExcludeFromOptimization { get; set; }
}

public class TableAssignment
{
    public int TableNumber { get; set; }
    public List<Guid> PlayerIds { get; set; } = new();
    public int PlayerCount => PlayerIds.Count;
    public TableScore? Score { get; set; }
}

public class TableScore
{
    public List<PlayerScore> PlayerScores { get; set; } = new();
}

public class PlayerScore
{
    public Guid PlayerId { get; set; }
    public bool IsVirtual { get; set; }
    public string VirtualName { get; set; } = string.Empty;
    public int StartingPoints { get; set; }
    public int? EndPoints { get; set; }
    public int Loan { get; set; }
    public int Difference => (EndPoints ?? StartingPoints) + Loan - StartingPoints;
    public int Uma { get; set; }
    public int Penalty { get; set; }
    public int Total => Difference + Uma + Penalty;
}
