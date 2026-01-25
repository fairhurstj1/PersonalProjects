namespace PlaylistRipper.Models;

public sealed class SessionState
{
    public string PlaylistUrl { get; set; } = "";
    public int NextIndex { get; set; } = 1; // 1-based
    public string StagingFolder { get; set; } = "";
    public string OffloadFolder { get; set; } = "";
    public long ZipThresholdBytes { get; set; }
    public long MinFreeSpaceBytes { get; set; }
    public string FormatMode { get; set; } = "Best"; // "Best" | "Max1080p" | "AudioOnly"
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
}
