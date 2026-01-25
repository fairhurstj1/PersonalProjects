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
    string YtDlpBaseArgs
);
