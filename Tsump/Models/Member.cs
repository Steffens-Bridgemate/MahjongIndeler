namespace Tsump.Models;

public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime JoinedDate { get; set; } = DateTime.Today;
    public bool IsActive { get; set; } = true;
    public string LeagueId { get; set; } = string.Empty;
    /// <summary>A head start in 3-player tables, in tables. 0 is neutral; positive means "treat
    /// this member as having already had that much 3-duty", negative the reverse. Fractional since
    /// the start-of-round reset writes the outgoing round's standing here (see ClubDataService) —
    /// a whole table is far too blunt for that. Hand-editable on the member form.</summary>
    public double ExtraThreePlayerTableCount { get; set; }
}
