namespace PlaylistRipper.Models;

public class SessionState
{
    public string PlaylistUrl { get; set; } = "";
    public int NextIndex { get; set; } = 1;

    public string StagingFolder { get; set; } = "";
    public string OffloadFolder { get; set; } = "";

    public long ZipThresholdBytes { get; set; }
    public long MinFreeStagingBytes { get; set; }
    public long MinFreeOffloadBytes { get; set; }

    public string FormatMode { get; set; } = "Best";

    public string AuthArgs { get; set; } = "";
    public string CookiesFromBrowser { get; set; } = "";

}
