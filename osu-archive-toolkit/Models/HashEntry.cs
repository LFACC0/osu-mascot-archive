namespace osuArchiveToolkit.Models;

public class HashEntry
{
    public string Hash { get; set; } = "";
    public string HashCreatedAt { get; set; } = "";
    public string SubmittedBy { get; set; } = "";
    public string LastFilePath { get; set; } = "";
    public long ByteSize { get; set; } = 0;
    public string LastEntryPath { get; set; } = "";
}