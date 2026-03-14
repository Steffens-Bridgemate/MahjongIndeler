namespace Tsump.Models;

public class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public int SessionCount { get; set; } = 4;
    public List<TournamentParticipant> Participants { get; set; } = new();
    public List<TournamentSession> Sessions { get; set; } = new();
    public bool IsGenerated { get; set; }
}

public class TournamentParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
}

public class TournamentSession
{
    public int SessionNumber { get; set; }
    public List<TableAssignment> Tables { get; set; } = new();
}
