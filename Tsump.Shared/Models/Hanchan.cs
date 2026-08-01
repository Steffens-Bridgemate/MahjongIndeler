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

    /// <summary>Players seated here who dropped out mid-event. They stay in <see cref="PlayerIds"/>
    /// so the seating record survives, but stop counting as players: the table then scores as a
    /// 3-player table with Mr. X in the fourth seat. Set only from the organizer app.</summary>
    public List<Guid> AbsentPlayerIds { get; set; } = new();

    /// <summary>Seated players still actually playing, in <see cref="PlayerIds"/> order — the
    /// order every positional pairing (invite QR, score rows) depends on. A method rather than a
    /// property so System.Text.Json doesn't write it into every stored table.</summary>
    public List<Guid> ActivePlayerIds()
        => PlayerIds.Where(id => !AbsentPlayerIds.Contains(id)).ToList();

    public int PlayerCount => PlayerIds.Count(id => !AbsentPlayerIds.Contains(id));
    public TableScore? Score { get; set; }

    /// <summary>Organizer-side bookkeeping: has this table's invite QR been handed out / scanned.
    /// Set from the QR modal (Enter) and the table card checkbox; persisted with the container.
    /// Not part of the scoring wire payload.</summary>
    public bool Scanned { get; set; }
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
