using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlaylistRipper.Models;

namespace PlaylistRipper.Core;

public class App
{
    private readonly ConsoleUi _ui;
    private readonly YtDlpClient _yt;
    private readonly DiskService _disk;
    private readonly ZipOffloader _zip;
    private readonly SessionStoreFacade _session;

    public App(ConsoleUi ui, YtDlpClient yt, DiskService disk, ZipOffloader zip, SessionStoreFacade session)
    {
        _ui = ui;
        _yt = yt;
        _disk = disk;
        _zip = zip;
        _session = session;
    }

    public async Task RunAsync()
    {
        // Constants / defaults
        string rootFolder = @"D:\YTPlaylistRipper";
        string sessionPath = Path.Combine(rootFolder, "session.json");
        string archivePath = Path.Combine(rootFolder, "archive.txt");

        const string ytBaseArgs = "--js-runtimes deno --remote-components ejs:github";

        _ui.Write("Enter YouTube playlist URL: ");
        string playlistUrl = (_ui.ReadLine() ?? "").Trim();

        var retry = new RetryPolicy(_ui);

        // Prompt config
        var cfg = _ui.PromptConfig(
            defaultRoot: rootFolder,
            defaultStaging: Path.Combine(rootFolder, "Staging"),
            defaultOffload: Path.Combine(rootFolder, "Offload"),
            defaultZipThresholdGb: 2.0,
            defaultMinFreeStagingGb: 10.0,
            defaultMinFreeOffloadGb: 1.0
        );

        var appCfg = new AppConfig(
            RootFolder: cfg.RootFolder,
            StagingFolder: cfg.StagingFolder,
            OffloadFolder: cfg.OffloadFolder,
            ZipThresholdBytes: cfg.ZipThresholdBytes,
            MinFreeStagingBytes: cfg.MinFreeStagingBytes,
            MinFreeOffloadBytes: cfg.MinFreeOffloadBytes,
            ArchivePath: archivePath,
            SessionPath: sessionPath,
            YtDlpBaseArgs: ytBaseArgs,
            CookiesFromBrowser: cfg.CookiesFromBrowser,
            AuthArgs: cfg.AuthArgs,
            MaxAttempts: cfg.MaxAttempts,
            BaseDelaySeconds: cfg.BaseDelaySeconds,
            MaxDelaySeconds: cfg.MaxDelaySeconds,
            PoliteDelaySeconds: cfg.PoliteDelaySeconds
        );


        Directory.CreateDirectory(appCfg.RootFolder);
        Directory.CreateDirectory(appCfg.StagingFolder);
        Directory.CreateDirectory(appCfg.OffloadFolder);

        // Session bootstrap
        var current = new SessionState
        {
            PlaylistUrl = playlistUrl,
            NextIndex = 1,
            StagingFolder = appCfg.StagingFolder,
            OffloadFolder = appCfg.OffloadFolder,
            ZipThresholdBytes = appCfg.ZipThresholdBytes,
            MinFreeStagingBytes = appCfg.MinFreeStagingBytes,
            MinFreeOffloadBytes = appCfg.MinFreeOffloadBytes,
            FormatMode = "Best",
            CookiesFromBrowser = appCfg.CookiesFromBrowser,
            AuthArgs = appCfg.AuthArgs
        };

        var existing = _session.TryLoad(appCfg.SessionPath);
        if (existing != null)
        {
            _ui.WriteLine("\nFound previous session:");
            _ui.WriteLine($"  Playlist: {existing.PlaylistUrl}");
            _ui.WriteLine($"  NextIndex: {existing.NextIndex}");
            _ui.WriteLine($"  Staging: {existing.StagingFolder}");
            _ui.WriteLine($"  Offload: {existing.OffloadFolder}");
            _ui.Write("Resume it? (Y/N): ");

            var ans = (_ui.ReadLine() ?? "").Trim().ToUpperInvariant();
            if (ans == "Y")
            {
                current = existing;
                playlistUrl = current.PlaylistUrl;

                // overwrite appCfg folders/thresholds with session
                appCfg = appCfg with
                {
                    StagingFolder = current.StagingFolder,
                    OffloadFolder = current.OffloadFolder,
                    ZipThresholdBytes = current.ZipThresholdBytes,
                    MinFreeStagingBytes = current.MinFreeStagingBytes,
                    MinFreeOffloadBytes = current.MinFreeOffloadBytes,
                    CookiesFromBrowser = current.CookiesFromBrowser,
                    AuthArgs = current.AuthArgs
                };

                Directory.CreateDirectory(appCfg.StagingFolder);
                Directory.CreateDirectory(appCfg.OffloadFolder);
            }
            else
            {
                _session.Delete(appCfg.SessionPath);
            }
        }

        // Build yt-dlp args (base args + optional cookies file)
        string ytArgs = appCfg.YtDlpBaseArgs;

        if (!string.IsNullOrWhiteSpace(appCfg.CookiesFromBrowser))
        {
            ytArgs += $" --cookies \"{appCfg.CookiesFromBrowser}\"";
        }


        // Pull playlist URLs
        List<string> videoUrls;
        try
        {
            videoUrls = await _yt.GetPlaylistVideosAsync(playlistUrl, ytArgs, appCfg.AuthArgs);
        }
        catch (Exception ex)
        {
            _ui.WriteLine("\n❌ Could not read playlist. yt-dlp said:");
            _ui.WriteLine(ex.Message);
            return;
        }

        if (videoUrls.Count == 0)
        {
            _ui.WriteLine("\n❌ yt-dlp returned no items (but no explicit ERROR).");
            return;
        }

        _ui.WriteLine($"\nFound {videoUrls.Count} videos.");
        int chosenStart = _ui.PromptStartIndex(current.NextIndex, videoUrls.Count);
        current.NextIndex = chosenStart;
        _session.Save(appCfg.SessionPath, current);

        _ui.WriteLine($"Starting at index: {current.NextIndex}\n");

        // Main loop
        for (int index = 1; index <= videoUrls.Count; index++)
        {
            string url = videoUrls[index - 1];

            if (index < current.NextIndex)
                continue;

            _ui.WriteLine($"\n[{index}/{videoUrls.Count}] {url}");

            long stagingBytes = _disk.GetFolderSize(appCfg.StagingFolder);
            long offloadBytes = _disk.GetFolderSize(appCfg.OffloadFolder);
            long nextBytes = await _yt.TryEstimateVideoSizeBytesAsync(url, ytArgs, appCfg.AuthArgs);


            _ui.WriteLine($"   Staging: {Bytes.Format(stagingBytes)}");
            _ui.WriteLine($"   Offload: {Bytes.Format(offloadBytes)}");
            _ui.WriteLine($"   Next est: {(nextBytes > 0 ? Bytes.Format(nextBytes) : "Unknown")}");

            // If adding next would exceed threshold, offload staging first (if not empty)
            if (Thresholds.WouldExceedZipThreshold(stagingBytes, nextBytes, appCfg.ZipThresholdBytes) && stagingBytes > 0)
            {
                // ensure offload drive can accept roughly "stagingBytes" more
                if (_disk.IsDriveSpaceThreatened(appCfg.OffloadFolder, appCfg.MinFreeOffloadBytes, stagingBytes, out var offloadFree))
                {
                    var action = _ui.LowSpacePrompt(
                        driveLabel: "OFFLOAD",
                        freeBytes: offloadFree,
                        minFreeBytes: appCfg.MinFreeOffloadBytes,
                        bytesToAdd: stagingBytes
                    );

                    if (action == LowSpaceAction.Reconfigure)
                    {
                        var newCfg = _ui.PromptConfig(
                            defaultRoot: appCfg.RootFolder,
                            defaultStaging: appCfg.StagingFolder,
                            defaultOffload: appCfg.OffloadFolder,
                            defaultZipThresholdGb: appCfg.ZipThresholdBytes / Bytes.GB,
                            defaultMinFreeStagingGb: appCfg.MinFreeStagingBytes / Bytes.GB,
                            defaultMinFreeOffloadGb: appCfg.MinFreeOffloadBytes / Bytes.GB
                        );

                        appCfg = appCfg with
                        {
                            RootFolder = newCfg.RootFolder,
                            StagingFolder = newCfg.StagingFolder,
                            OffloadFolder = newCfg.OffloadFolder,
                            ZipThresholdBytes = newCfg.ZipThresholdBytes,
                            MinFreeStagingBytes = newCfg.MinFreeStagingBytes,
                            MinFreeOffloadBytes = newCfg.MinFreeOffloadBytes
                        };

                        Directory.CreateDirectory(appCfg.StagingFolder);
                        Directory.CreateDirectory(appCfg.OffloadFolder);
                    }
                }

                _ui.WriteLine("   ▶ Zip threshold would be exceeded. Offloading staging first...");
                _zip.ZipAndOffload(appCfg.StagingFolder, appCfg.OffloadFolder);
            }

            // Ensure staging drive has room for next download
            if (_disk.IsDriveSpaceThreatened(appCfg.StagingFolder, appCfg.MinFreeStagingBytes, nextBytes, out var stagingFree))
            {
                var action = _ui.LowSpacePrompt(
                    driveLabel: "STAGING",
                    freeBytes: stagingFree,
                    minFreeBytes: appCfg.MinFreeStagingBytes,
                    bytesToAdd: nextBytes
                );

                if (action == LowSpaceAction.Reconfigure)
                {
                    var newCfg = _ui.PromptConfig(
                        defaultRoot: appCfg.RootFolder,
                        defaultStaging: appCfg.StagingFolder,
                        defaultOffload: appCfg.OffloadFolder,
                        defaultZipThresholdGb: appCfg.ZipThresholdBytes / Bytes.GB,
                        defaultMinFreeStagingGb: appCfg.MinFreeStagingBytes / Bytes.GB,
                        defaultMinFreeOffloadGb: appCfg.MinFreeOffloadBytes / Bytes.GB
                    );

                    appCfg = appCfg with
                    {
                        RootFolder = newCfg.RootFolder,
                        StagingFolder = newCfg.StagingFolder,
                        OffloadFolder = newCfg.OffloadFolder,
                        ZipThresholdBytes = newCfg.ZipThresholdBytes,
                        MinFreeStagingBytes = newCfg.MinFreeStagingBytes,
                        MinFreeOffloadBytes = newCfg.MinFreeOffloadBytes
                    };

                    Directory.CreateDirectory(appCfg.StagingFolder);
                    Directory.CreateDirectory(appCfg.OffloadFolder);
                }
            }

            // Save session BEFORE downloading (crash-safe)
            current.PlaylistUrl = playlistUrl;
            current.NextIndex = index;
            current.StagingFolder = appCfg.StagingFolder;
            current.OffloadFolder = appCfg.OffloadFolder;
            current.ZipThresholdBytes = appCfg.ZipThresholdBytes;
            current.MinFreeStagingBytes = appCfg.MinFreeStagingBytes;
            current.MinFreeOffloadBytes = appCfg.MinFreeOffloadBytes;
            current.AuthArgs = appCfg.AuthArgs;
            _session.Save(appCfg.SessionPath, current);

            // Download
            var (exit, output) = await retry.RunWithRetriesAsync(
                action: () => _yt.DownloadVideoAsync(
                    url: url,
                    stagingFolder: appCfg.StagingFolder,
                    archivePath: appCfg.ArchivePath,
                    ytBaseArgs: ytArgs,
                    authArgs: appCfg.AuthArgs
                ),
                maxAttempts: appCfg.MaxAttempts,
                baseDelaySeconds: appCfg.BaseDelaySeconds,
                maxDelaySeconds: appCfg.MaxDelaySeconds,
                classify: (code, text) => YtFailure.Classify(code, text)
            );

            var kind = YtFailure.Classify(exit, output);

            if (exit != 0)
            {
                _ui.WriteLine($"   ❌ yt-dlp failed ({kind}).");

                if (kind == FailureKind.AuthRequired)
                {
                    _ui.WriteLine("   🔒 Login / age restriction detected. Fix auth settings then rerun.");
                    _session.Save(appCfg.SessionPath, current);
                    return; // stop safely
                }

                if (kind == FailureKind.RateLimited)
                {
                    _ui.WriteLine("   🛑 Rate limit detected. Pausing so you can wait and restart later.");
                    _session.Save(appCfg.SessionPath, current);
                    return; // stop safely
                }

                _ui.WriteLine("   ↪ Skipping this video and continuing.");
            }
            else
            {
                // Optional polite delay after success
                if (appCfg.PoliteDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(appCfg.PoliteDelaySeconds));
            }

            // Advance only if success OR skippable failure
            bool shouldAdvanceIndex =
                exit == 0 ||
                (kind != FailureKind.AuthRequired && kind != FailureKind.RateLimited);

            if (shouldAdvanceIndex)
            {
                current.NextIndex = index + 1;
                current.AuthArgs = appCfg.AuthArgs;
                _session.Save(appCfg.SessionPath, current);
            }

        }

        _session.Delete(appCfg.SessionPath);
        _ui.WriteLine("\n✅ Done."); 

        // Optional final offload
        long remaining = _disk.GetFolderSize(appCfg.StagingFolder);
        if (remaining > 0 && _ui.Confirm($"Staging still has {Bytes.Format(remaining)}. Offload it now? (Y/N): "))
        {
            _zip.ZipAndOffload(appCfg.StagingFolder, appCfg.OffloadFolder);
            _ui.WriteLine("Final offload complete.");
        }
    }
}
