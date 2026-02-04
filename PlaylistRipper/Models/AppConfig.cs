namespace PlaylistRipper.Models;

public record AppConfig(
    string RootFolder,
    string StagingFolder,
    string OffloadFolder,
    long ZipThresholdBytes,
    long MinFreeStagingBytes,
    long MinFreeOffloadBytes,
    string ArchivePath,
    string SessionPath,
    string LogPath,
    string YtDlpBaseArgs,

    // Auth
    string CookiesFromBrowser,   // optional
    string CookiesFilePath,      // preferred
    string AuthArgs,             // optional extra args

    // Retry / pacing
    int MaxAttempts,
    int BaseDelaySeconds,
    int MaxDelaySeconds,
    int PoliteDelaySeconds,

    // Reliability caps
    int MaxDownloadsPerRun,      // 0 = unlimited
    int BreakEveryNDownloads,    // 0 = none
    int BreakSeconds             // break duration
);
