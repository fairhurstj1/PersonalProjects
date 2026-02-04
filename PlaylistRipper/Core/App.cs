using System;
using System.Collections.Generic;
using System.IO;
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
        string rootFolder = @"D:\YTPlaylistRipper";
        string logPath = Path.Combine(rootFolder, "run.log");

        const string ytBaseArgs = "--js-runtimes deno --remote-components ejs:github";

        _ui.Write("Enter YouTube playlist URL: ");
        string playlistUrl = (_ui.ReadLine() ?? "").Trim();

        var retry = new RetryPolicy(_ui);

        var cfg = _ui.PromptConfig(
            defaultRoot: rootFolder,
            defaultStaging: Path.Combine(rootFolder, "Staging"),
            defaultOffload: Path.Combine(rootFolder, "Offload"),
            defaultZipThresholdGb: 2.0,
            defaultMinFreeStagingGb: 10.0,
            defaultMinFreeOffloadGb: 1.0,
            defaultAuthArgs: "",
            defaultCookiesFilePath: Path.Combine(rootFolder, "cookies.txt")
        );

        string sessionPath = Path.Combine(cfg.RootFolder, "session.json");
        string archivePath = Path.Combine(cfg.RootFolder, "archive.txt");


        var appCfg = new AppConfig(
            RootFolder: cfg.RootFolder,
            StagingFolder: cfg.StagingFolder,
            OffloadFolder: cfg.OffloadFolder,
            ZipThresholdBytes: cfg.ZipThresholdBytes,
            MinFreeStagingBytes: cfg.MinFreeStagingBytes,
            MinFreeOffloadBytes: cfg.MinFreeOffloadBytes,
            ArchivePath: archivePath,
            SessionPath: sessionPath,
            LogPath: logPath,
            YtDlpBaseArgs: ytBaseArgs,
            CookiesFromBrowser: cfg.CookiesFromBrowser,
            CookiesFilePath: cfg.CookiesFilePath,
            AuthArgs: cfg.AuthArgs,
            MaxAttempts: cfg.MaxAttempts,
            BaseDelaySeconds: cfg.BaseDelaySeconds,
            MaxDelaySeconds: cfg.MaxDelaySeconds,
            PoliteDelaySeconds: cfg.PoliteDelaySeconds,
            MaxDownloadsPerRun: cfg.MaxDownloadsPerRun,
            BreakEveryNDownloads: cfg.BreakEveryNDownloads,
            BreakSeconds: cfg.BreakSeconds
        );

        Directory.CreateDirectory(appCfg.RootFolder);
        Directory.CreateDirectory(appCfg.StagingFolder);
        Directory.CreateDirectory(appCfg.OffloadFolder);

        var log = new LogService(appCfg.LogPath);
        log.Info($"Start. Playlist={playlistUrl}");

        var current = new SessionState
        {
            PlaylistUrl = playlistUrl,
            NextIndex = 1,
            StagingFolder = appCfg.StagingFolder,
            OffloadFolder = appCfg.OffloadFolder,
            ZipThresholdBytes = appCfg.ZipThresholdBytes,
            MinFreeStagingBytes = appCfg.MinFreeStagingBytes,
            MinFreeOffloadBytes = appCfg.MinFreeOffloadBytes,
            CookiesFromBrowser = appCfg.CookiesFromBrowser,
            CookiesFilePath = appCfg.CookiesFilePath,
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
                log.Info($"Resuming session at index {current.NextIndex}");
            }
            else
            {
                _session.Delete(appCfg.SessionPath);
                log.Info("Session deleted by user choice.");
            }
        }

        // Read playlist with full diagnostics
        var pr = await _yt.GetPlaylistVideosAsync(
            playlistUrl,
            appCfg.YtDlpBaseArgs,
            current.CookiesFilePath,
            current.CookiesFromBrowser,
            current.AuthArgs
        );

        if (pr.Videos.Count == 0)
        {
            _ui.WriteLine("\n❌ Could not read playlist (0 videos). yt-dlp said:");
            _ui.WriteLine(pr.Output);
            log.Error($"Playlist read failed. Exit={pr.ExitCode}. Output={pr.Output}");
            return;
        }

        _ui.WriteLine($"\nFound {pr.Videos.Count} videos.");
        int chosenStart = _ui.PromptStartIndex(current.NextIndex, pr.Videos.Count);
        current.NextIndex = chosenStart;
        _session.Save(appCfg.SessionPath, current);
        log.Info($"Starting at index {current.NextIndex}");

        int downloadedThisRun = 0;

        for (int index = 1; index <= pr.Videos.Count; index++)
        {
            if (index < current.NextIndex) continue;

            if (appCfg.MaxDownloadsPerRun > 0 && downloadedThisRun >= appCfg.MaxDownloadsPerRun)
            {
                _ui.WriteLine($"\n🛑 Reached MaxDownloadsPerRun={appCfg.MaxDownloadsPerRun}. Stopping safely.");
                log.Info("MaxDownloadsPerRun reached. Stopping.");
                _session.Save(appCfg.SessionPath, current);
                return;
            }

            string url = pr.Videos[index - 1];
            _ui.WriteLine($"\n[{index}/{pr.Videos.Count}] {url}");

            long stagingBytes = _disk.GetFolderSize(appCfg.StagingFolder);
            long offloadBytes = _disk.GetFolderSize(appCfg.OffloadFolder);

            long nextBytes = await _yt.TryEstimateVideoSizeBytesAsync(
                url, appCfg.YtDlpBaseArgs,
                current.CookiesFilePath,
                current.CookiesFromBrowser,
                current.AuthArgs
            );

            _ui.WriteLine($"   Staging: {Bytes.Format(stagingBytes)}");
            _ui.WriteLine($"   Offload: {Bytes.Format(offloadBytes)}");
            _ui.WriteLine($"   Next est: {(nextBytes > 0 ? Bytes.Format(nextBytes) : "Unknown")}");

            // Offload if needed
            // Compute an effective zip threshold that adapts to OFFLOAD free space.
            // This prevents false alarms and prevents trying to create a zip that won't fit.
            long effectiveZipThreshold = _disk.GetEffectiveZipThresholdBytes(
                offloadFolder: appCfg.OffloadFolder,
                configuredZipThresholdBytes: appCfg.ZipThresholdBytes,
                minFreeOffloadBytes: appCfg.MinFreeOffloadBytes
            );

            // Debug visibility (helps you trust it)
            long offloadFreeNow = _disk.GetDriveFreeSpaceBytes(appCfg.OffloadFolder);
            _ui.WriteLine($"   OFFLOAD free: {Bytes.Format(offloadFreeNow)}");
            _ui.WriteLine($"   Zip threshold (configured): {Bytes.Format(appCfg.ZipThresholdBytes)}");
            _ui.WriteLine($"   Zip threshold (effective): {Bytes.Format(effectiveZipThreshold)}");

            // If effective threshold is 0, OFFLOAD drive is already too full (below min free buffer).
            if (effectiveZipThreshold == 0)
            {
                var action = _ui.LowSpacePrompt(
                    driveLabel: "OFFLOAD",
                    freeBytes: offloadFreeNow,
                    minFreeBytes: appCfg.MinFreeOffloadBytes,
                    bytesToAdd: stagingBytes // what we'd like to move
                );

                if (action == LowSpaceAction.Reconfigure)
                {
                    var newCfg = _ui.PromptConfig(
                        defaultRoot: appCfg.RootFolder,
                        defaultStaging: appCfg.StagingFolder,
                        defaultOffload: appCfg.OffloadFolder,
                        defaultZipThresholdGb: appCfg.ZipThresholdBytes / Bytes.GB,
                        defaultMinFreeStagingGb: appCfg.MinFreeStagingBytes / Bytes.GB,
                        defaultMinFreeOffloadGb: appCfg.MinFreeOffloadBytes / Bytes.GB,
                        defaultAuthArgs: appCfg.AuthArgs,
                        defaultCookiesFilePath: appCfg.CookiesFilePath
                    );

                    appCfg = appCfg with
                    {
                        RootFolder = newCfg.RootFolder,
                        StagingFolder = newCfg.StagingFolder,
                        OffloadFolder = newCfg.OffloadFolder,
                        ZipThresholdBytes = newCfg.ZipThresholdBytes,
                        MinFreeStagingBytes = newCfg.MinFreeStagingBytes,
                        MinFreeOffloadBytes = newCfg.MinFreeOffloadBytes,
                        CookiesFromBrowser = newCfg.CookiesFromBrowser,
                        CookiesFilePath = newCfg.CookiesFilePath,
                        AuthArgs = newCfg.AuthArgs,
                        MaxAttempts = newCfg.MaxAttempts,
                        BaseDelaySeconds = newCfg.BaseDelaySeconds,
                        MaxDelaySeconds = newCfg.MaxDelaySeconds,
                        PoliteDelaySeconds = newCfg.PoliteDelaySeconds,
                        MaxDownloadsPerRun = newCfg.MaxDownloadsPerRun,
                        BreakEveryNDownloads = newCfg.BreakEveryNDownloads,
                        BreakSeconds = newCfg.BreakSeconds
                    };

                    Directory.CreateDirectory(appCfg.StagingFolder);
                    Directory.CreateDirectory(appCfg.OffloadFolder);

                    // Update session too
                    current.StagingFolder = appCfg.StagingFolder;
                    current.OffloadFolder = appCfg.OffloadFolder;
                    current.ZipThresholdBytes = appCfg.ZipThresholdBytes;
                    current.MinFreeStagingBytes = appCfg.MinFreeStagingBytes;
                    current.MinFreeOffloadBytes = appCfg.MinFreeOffloadBytes;
                    current.AuthArgs = appCfg.AuthArgs;
                    current.CookiesFromBrowser = appCfg.CookiesFromBrowser;
                    current.CookiesFilePath = appCfg.CookiesFilePath;
                    _session.Save(appCfg.SessionPath, current);

                    // Recompute effective threshold after reconfigure
                    effectiveZipThreshold = _disk.GetEffectiveZipThresholdBytes(appCfg.OffloadFolder, appCfg.ZipThresholdBytes, appCfg.MinFreeOffloadBytes);
                }
                else
                {
                    // They chose continue; but there's no safe space. Stop cleanly.
                    _ui.WriteLine("   🛑 OFFLOAD is below minimum free space. Stopping safely.");
                    _session.Save(appCfg.SessionPath, current);
                    return;
                }
            }

            // Now decide if we should offload staging BEFORE downloading next.
            // Use the effective threshold (not just configured).
            if (Thresholds.WouldExceedZipThreshold(stagingBytes, nextBytes, effectiveZipThreshold) && stagingBytes > 0)
            {
                // Secondary check: will OFFLOAD accept the zip of roughly stagingBytes?
                // (This should rarely trigger now, but it's still a good final guard.)
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
                        // same reconfigure block as above (keep your existing one)
                        // (If you want, I can give you a helper method to avoid duplicating this.)
                    }
                    else
                    {
                        _ui.WriteLine("   🛑 Not enough OFFLOAD space to safely offload staging. Stopping safely.");
                        _session.Save(appCfg.SessionPath, current);
                        return;
                    }
                }

                _ui.WriteLine("   ▶ Zip threshold would be exceeded. Offloading staging first...");
                _zip.ZipAndOffload(appCfg.StagingFolder, appCfg.OffloadFolder);
            }


            // Save BEFORE download
            current.PlaylistUrl = playlistUrl;
            current.NextIndex = index;
            _session.Save(appCfg.SessionPath, current);

            // Download (with retries)
            var (exit, output) = await retry.RunWithRetriesAsync(
                action: () => _yt.DownloadVideoAsync(
                    url: url,
                    stagingFolder: appCfg.StagingFolder,
                    archivePath: appCfg.ArchivePath,
                    ytBaseArgs: appCfg.YtDlpBaseArgs,
                    cookiesFilePath: current.CookiesFilePath,
                    cookiesFromBrowser: current.CookiesFromBrowser,
                    authArgs: current.AuthArgs
                ),
                maxAttempts: appCfg.MaxAttempts,
                baseDelaySeconds: appCfg.BaseDelaySeconds,
                maxDelaySeconds: appCfg.MaxDelaySeconds,
                classify: (code, text) => YtFailure.Classify(code, text)
            );

            var kind = YtFailure.Classify(exit, output);
            log.Info($"Download result index={index} exit={exit} kind={kind}");

            if (exit != 0)
            {
                _ui.WriteLine($"   ❌ yt-dlp failed ({kind}).");
                _ui.WriteLine("   See run.log for details.");
                log.Error(output);

                if (kind == FailureKind.AuthRequired)
                {
                    _ui.WriteLine("   🔒 Auth / age restriction detected. Update cookies.txt then rerun.");
                    _session.Save(appCfg.SessionPath, current);
                    return;
                }

                if (kind == FailureKind.RateLimited)
                {
                    _ui.WriteLine("   🛑 Rate limit detected. Wait and rerun later.");
                    _session.Save(appCfg.SessionPath, current);
                    return;
                }

                // Skippable failure: advance index
                current.NextIndex = index + 1;
                _session.Save(appCfg.SessionPath, current);
                continue;
            }

            if (exit != 0)
            {
                _ui.WriteLine("---- yt-dlp output (tail) ----");
                var lines = output.Split('\n').Select(x => x.TrimEnd()).Where(x => x.Length > 0).ToList();
                foreach (var l in lines.Skip(Math.Max(0, lines.Count - 20)))
                    _ui.WriteLine(l);
                _ui.WriteLine("-----------------------------");
            }


            // Success
            downloadedThisRun++;

            // Polite delay
            if (appCfg.PoliteDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(appCfg.PoliteDelaySeconds));

            // Fixed break every N downloads
            if (appCfg.BreakEveryNDownloads > 0 &&
                appCfg.BreakSeconds > 0 &&
                downloadedThisRun % appCfg.BreakEveryNDownloads == 0)
            {
                _ui.WriteLine($"   ⏸ Break: sleeping {appCfg.BreakSeconds}s...");
                log.Info($"Break for {appCfg.BreakSeconds}s after {downloadedThisRun} downloads.");
                await Task.Delay(TimeSpan.FromSeconds(appCfg.BreakSeconds));
            }

            // Advance session
            current.NextIndex = index + 1;
            _session.Save(appCfg.SessionPath, current);
        }

        _session.Delete(appCfg.SessionPath);
        _ui.WriteLine("\n✅ Done.");
        log.Info("Finished successfully.");
    }
}
