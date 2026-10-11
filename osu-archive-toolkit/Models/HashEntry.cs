namespace osuArchiveToolkit.Models;

public class HashEntry
{
    public string Link { get; set; } = "";
    public string Hash { get; set; } = "";
    public string HashCreatedAt { get; set; } = "";
    public string SubmittedBy { get; set; } = "";
    public string LastFilePath { get; set; } = "";
    public long ByteSize { get; set; } = 0;
    public string LastEntryPath { get; set; } = "";
    public string ContentType { get; set; } = ""; //for now, It can be Standalone (no image related),
                                                  //Offline (image from disk), Link (from internet) or/and Online (stored in the github repo). 
}