namespace Tsump.Models;

public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime JoinedDate { get; set; } = DateTime.Today;
    public bool IsActive { get; set; } = true;
}
