namespace osuArchiveToolkit.Models;

public class ReconciliationLogEntry
{
    public string Hash { get; set; } = "";
    public string LastKnownPath { get; set; } = "";
    public string EventType { get; set; } = "";
    public string OccurredAt { get; set; } = "";
}