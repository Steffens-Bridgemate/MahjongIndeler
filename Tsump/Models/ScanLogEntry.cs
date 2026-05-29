namespace Tsump.Models;

/// <summary>
/// One recorded import capture (scanned / pasted / clipboard / camera / file). Stores the raw
/// captured string verbatim plus a timestamp; everything else (decoded result, hanchan/table
/// label, score preview) is derived on demand from <see cref="Raw"/> so the log stays consistent
/// with current data even if sessions are edited after the capture.
/// </summary>
public class ScanLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Raw { get; set; } = string.Empty;
}
