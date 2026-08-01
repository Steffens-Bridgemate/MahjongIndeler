namespace Tsump.Models;

public class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional compact event name (≤12 chars) carried in scoring invites — keeps the QR
    /// small while still showing which event the scorer is in. The scoring app reconstructs the
    /// "Hanchan N — Table M" part from the payload's numbers; this is just the prefix.</summary>
    public string? ShortName { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int SessionCount { get; set; } = 4;
    public List<TournamentParticipant> Participants { get; set; } = new();
    public List<TournamentSession> Sessions { get; set; } = new();
    public bool IsGenerated { get; set; }

    // Scoring overrides (null = use club defaults)
    public int? StartingPoints { get; set; }
    public List<int>? Uma4Players { get; set; }
    public List<int>? Uma3Players { get; set; }
    public AbsentScoringMode? AbsentScoringMode { get; set; }
    public int? AbsentCorrection { get; set; }
}

public class TournamentParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
    public string LeagueId { get; set; } = string.Empty;
}

public class TournamentSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SessionNumber { get; set; }
    public TimeSpan? StartTime { get; set; }
    public List<TableAssignment> Tables { get; set; } = new();
}
