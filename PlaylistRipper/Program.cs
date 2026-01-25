using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PlaylistRipper.Core;
using PlaylistRipper.Models;

class Program
{
    // yt-dlp args that made YouTube work for you
    private const string YtDlpBaseArgs = "--js-runtimes deno --remote-components ejs:github";

    static async Task Main()
    {
        string rootFolder = @"D:\YTPlaylistRipper";
        string sessionPath = Path.Combine(rootFolder, "session.json");
        string archivePath = Path.Combine(rootFolder, "archive.txt");

        Console.Write("Enter YouTube playlist URL: ");
        string playlistUrl = (Console.ReadLine() ?? "").Trim();

        // Defaults you requested
        var cfg = PromptConfig(
            defaultStaging: @"D:\YTPlaylistRipper\Staging",
            defaultOffload: @"D:\YTPlaylistRipper\Offload",
            defaultZipThresholdGb: 2.0,
            defaultMinFreeStagingGb: 10.0,
            defaultMinFreeOffloadGb: 1.0
        );


        Directory.CreateDirectory(cfg.StagingFolder);
        Directory.CreateDirectory(cfg.OffloadFolder);

        // Create a default session object (used if there is no previous session)
        var session = new SessionState
        {
            PlaylistUrl = playlistUrl,
            NextIndex = 1,
            StagingFolder = cfg.StagingFolder,
            OffloadFolder = cfg.OffloadFolder,
            ZipThresholdBytes = cfg.ZipThresholdBytes,

            // SessionState only has ONE min-free value right now.
            // We treat it as "min free on STAGING drive".
            MinFreeSpaceBytes = cfg.MinFreeStagingBytes,

            FormatMode = "Best"
        };


        // Try to load an existing session
        var existing = SessionStore.TryLoad(sessionPath);
        if (existing != null)
        {
            Console.WriteLine($"\nFound previous session:");
            Console.WriteLine($"  Playlist: {existing.PlaylistUrl}");
            Console.WriteLine($"  NextIndex: {existing.NextIndex}");
            Console.WriteLine($"  Staging: {existing.StagingFolder}");
            Console.WriteLine($"  Offload: {existing.OffloadFolder}");

            Console.Write("Resume it? (Y/N): ");
            var ans = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

            if (ans == "Y")
            {
                // Use the saved session
                session = existing;

                // Apply session settings back into playlistUrl + cfg
                playlistUrl = session.PlaylistUrl;

                cfg = new Config(
                    session.StagingFolder,
                    session.OffloadFolder,
                    session.ZipThresholdBytes,

                    // Persisted staging min-free from session
                    session.MinFreeSpaceBytes,

                    // Offload min-free isn't persisted yet; keep current cfg value
                    cfg.MinFreeOffloadBytes
                );

                Directory.CreateDirectory(cfg.StagingFolder);
                Directory.CreateDirectory(cfg.OffloadFolder);
            }
            else
            {
                SessionStore.Delete(sessionPath);
            }
        }

        // Pull playlist videos
        var videoUrls = await GetPlaylistVideos(playlistUrl, cfg);

        Console.WriteLine($"\nFound {videoUrls.Count} videos.");
        Console.WriteLine($"Resuming at index: {session.NextIndex}\n");

        int index = 1;

        foreach (var url in videoUrls)
        {
            // Skip already-processed items when resuming
            if (index < session.NextIndex)
            {
                index++;
                continue;
            }

            Console.WriteLine($"\n[{index}/{videoUrls.Count}] {url}");

            // 1) If staging + next video would exceed zip threshold, offload first
            long stagingBytes = GetFolderSize(cfg.StagingFolder);
            long offloadBytes = GetFolderSize(cfg.OffloadFolder);
            long totalBytes = stagingBytes + offloadBytes;

            long nextBytes = await TryEstimateVideoSizeBytes(url, cfg);

            Console.WriteLine($"   Staging: {FormatBytes(stagingBytes)}");
            Console.WriteLine($"   Offload: {FormatBytes(offloadBytes)}");
            Console.WriteLine($"   Total (staging+offload): {FormatBytes(totalBytes)}");
            Console.WriteLine($"   Next est: {(nextBytes > 0 ? FormatBytes(nextBytes) : "Unknown")}");


            if (WouldExceedZipThreshold(stagingBytes, nextBytes, cfg.ZipThresholdBytes))
            {
                if (stagingBytes > 0)
                {
                    // BEFORE we zip: ensure OFFLOAD drive can accept a zip roughly the size of staging
                    if (IsDriveSpaceThreatened(cfg.OffloadFolder, cfg.MinFreeOffloadBytes, stagingBytes, out var offloadFree))
                    {
                        Console.WriteLine($"\n⚠️  Warning: Low free space on OFFLOAD drive.");
                        Console.WriteLine($"   Offload free now: {FormatBytes(offloadFree)}");
                        Console.WriteLine($"   Need to add (zip est): {FormatBytes(stagingBytes)}");
                        Console.WriteLine($"   Minimum required free space: {FormatBytes(cfg.MinFreeOffloadBytes)}");
                        Console.WriteLine();
                        Console.Write("Type P to pause & reconfigure, or C to continue anyway: ");

                        var choice = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

                        if (choice == "P")
                        {
                            Console.WriteLine("\nPaused. Enter new paths/thresholds to continue.");
                            cfg = PromptConfig(
                                defaultStaging: cfg.StagingFolder,
                                defaultOffload: cfg.OffloadFolder,
                                defaultZipThresholdGb: cfg.ZipThresholdBytes / (1024d * 1024 * 1024),
                                defaultMinFreeStagingGb: cfg.MinFreeStagingBytes / (1024d * 1024 * 1024),
                                defaultMinFreeOffloadGb: cfg.MinFreeOffloadBytes / (1024d * 1024 * 1024)
                            );

                            Directory.CreateDirectory(cfg.StagingFolder);
                            Directory.CreateDirectory(cfg.OffloadFolder);

                            // Keep session aligned with cfg
                            session.StagingFolder = cfg.StagingFolder;
                            session.OffloadFolder = cfg.OffloadFolder;
                            session.ZipThresholdBytes = cfg.ZipThresholdBytes;
                            session.MinFreeSpaceBytes = cfg.MinFreeStagingBytes;
                            SessionStore.Save(sessionPath, session);
                        }
                        else if (choice == "C")
                        {
                            // Your spec: continuation requires new destination + parameters
                            Console.WriteLine("\nContinuing requires new target destination + parameters.");
                            cfg = PromptConfig(
                                defaultStaging: cfg.StagingFolder,
                                defaultOffload: cfg.OffloadFolder,
                                defaultZipThresholdGb: cfg.ZipThresholdBytes / (1024d * 1024 * 1024),
                                defaultMinFreeStagingGb: cfg.MinFreeStagingBytes / (1024d * 1024 * 1024),
                                defaultMinFreeOffloadGb: cfg.MinFreeOffloadBytes / (1024d * 1024 * 1024)
                            );


                            Directory.CreateDirectory(cfg.StagingFolder);
                            Directory.CreateDirectory(cfg.OffloadFolder);

                            session.StagingFolder = cfg.StagingFolder;
                            session.OffloadFolder = cfg.OffloadFolder;
                            session.ZipThresholdBytes = cfg.ZipThresholdBytes;
                            session.MinFreeSpaceBytes = cfg.MinFreeStagingBytes;
                            SessionStore.Save(sessionPath, session);
                        }
                        else
                        {
                            Console.WriteLine("Invalid choice. Pausing by default.");
                            cfg = PromptConfig(
                                defaultStaging: cfg.StagingFolder,
                                defaultOffload: cfg.OffloadFolder,
                                defaultZipThresholdGb: cfg.ZipThresholdBytes / (1024d * 1024 * 1024),
                                defaultMinFreeStagingGb: cfg.MinFreeStagingBytes / (1024d * 1024 * 1024),
                                defaultMinFreeOffloadGb: cfg.MinFreeOffloadBytes / (1024d * 1024 * 1024)
                            );


                            Directory.CreateDirectory(cfg.StagingFolder);
                            Directory.CreateDirectory(cfg.OffloadFolder);

                            session.StagingFolder = cfg.StagingFolder;
                            session.OffloadFolder = cfg.OffloadFolder;
                            session.ZipThresholdBytes = cfg.ZipThresholdBytes;
                            session.MinFreeSpaceBytes = cfg.MinFreeStagingBytes;
                            SessionStore.Save(sessionPath, session);
                        }
                    }

                    Console.WriteLine($"   ▶ Zip threshold would be exceeded. Offloading staging first...");
                    ZipAndOffload(cfg.StagingFolder, cfg.OffloadFolder);
                }
                else
                {
                    Console.WriteLine($"   ▶ Zip threshold would be exceeded, but staging is empty. Proceeding.");
                }
            }


            // 2) Ensure destination drive has enough free space per your min-free threshold
            if (IsDriveSpaceThreatened(cfg.StagingFolder, cfg.MinFreeStagingBytes, nextBytes, out var freeBytes))
            {
                Console.WriteLine($"\n⚠️  Warning: Low free space on staging drive.");
                Console.WriteLine($"   Free now: {FormatBytes(freeBytes)}");
                Console.WriteLine($"   Minimum required free space: {FormatBytes(cfg.MinFreeStagingBytes)}");
                Console.WriteLine($"   Next download estimate: {(nextBytes > 0 ? FormatBytes(nextBytes) : "Unknown")}");
                Console.WriteLine();
                Console.Write("Type P to pause & reconfigure, or C to continue anyway: ");
                var choice = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

                if (choice == "P")
                {
                    Console.WriteLine("\nPaused. Enter new paths/thresholds to continue.");
                    cfg = PromptConfig(
                        defaultStaging: cfg.StagingFolder,
                        defaultOffload: cfg.OffloadFolder,
                        defaultZipThresholdGb: cfg.ZipThresholdBytes / (1024d * 1024 * 1024),
                        defaultMinFreeStagingGb: cfg.MinFreeStagingBytes / (1024d * 1024 * 1024),
                        defaultMinFreeOffloadGb: cfg.MinFreeOffloadBytes / (1024d * 1024 * 1024)
                    );

                    Directory.CreateDirectory(cfg.StagingFolder);
                    Directory.CreateDirectory(cfg.OffloadFolder);
                }
                else if (choice == "C")
                {
                    // As per your spec: continuation requires new user-specified destination and parameters
                    Console.WriteLine("\nContinuing requires new target destination + parameters.");
                    cfg = PromptConfig(
                        defaultStaging: cfg.StagingFolder,
                        defaultOffload: cfg.OffloadFolder,
                        defaultZipThresholdGb: cfg.ZipThresholdBytes / (1024d * 1024 * 1024),
                        defaultMinFreeStagingGb: cfg.MinFreeStagingBytes / (1024d * 1024 * 1024),
                        defaultMinFreeOffloadGb: cfg.MinFreeOffloadBytes / (1024d * 1024 * 1024)
                    );

                    Directory.CreateDirectory(cfg.StagingFolder);
                    Directory.CreateDirectory(cfg.OffloadFolder);
                }
                else
                {
                    Console.WriteLine("Invalid choice. Pausing by default.");
                    cfg = PromptConfig(
                        defaultStaging: cfg.StagingFolder,
                        defaultOffload: cfg.OffloadFolder,
                        defaultZipThresholdGb: cfg.ZipThresholdBytes / (1024d * 1024 * 1024),
                        defaultMinFreeStagingGb: cfg.MinFreeStagingBytes / (1024d * 1024 * 1024),
                        defaultMinFreeOffloadGb: cfg.MinFreeOffloadBytes / (1024d * 1024 * 1024)
                    );

                    Directory.CreateDirectory(cfg.StagingFolder);
                    Directory.CreateDirectory(cfg.OffloadFolder);
                }

                // IMPORTANT: session should follow cfg after reconfigure
                session.StagingFolder = cfg.StagingFolder;
                session.OffloadFolder = cfg.OffloadFolder;
                session.ZipThresholdBytes = cfg.ZipThresholdBytes;
                session.MinFreeSpaceBytes = cfg.MinFreeStagingBytes;
                SessionStore.Save(sessionPath, session);
            }

            // Save session BEFORE download (crash-safe: you'll retry this index on resume)
            session.PlaylistUrl = playlistUrl;
            session.NextIndex = index;
            session.StagingFolder = cfg.StagingFolder;
            session.OffloadFolder = cfg.OffloadFolder;
            session.ZipThresholdBytes = cfg.ZipThresholdBytes;
            session.MinFreeSpaceBytes = cfg.MinFreeStagingBytes;
            SessionStore.Save(sessionPath, session);

            // 3) Download the video into staging
            string args =
                $"{YtDlpBaseArgs} " +
                $"--download-archive \"{archivePath}\" " +
                $"-P \"{cfg.StagingFolder}\" " +
                $"-f \"bv*+ba/b\" " +
                $"\"{url}\"";

            int exitCode = await RunProcess("yt-dlp", args);

            if (exitCode != 0)
            {
                Console.WriteLine($"   ❌ yt-dlp failed (exit {exitCode}). Skipping and continuing.");
            }

            // Save progress AFTER attempt: next run starts at the next item
            session.NextIndex = index + 1;
            SessionStore.Save(sessionPath, session);

            index++;
        }

        // Clean finish: remove session file
        SessionStore.Delete(sessionPath);

        Console.WriteLine("\n✅ Done. (If staging still contains files, you can offload one final time.)");

        // Optional final offload
        long remaining = GetFolderSize(cfg.StagingFolder);
        if (remaining > 0)
        {
            Console.Write($"Staging still has {FormatBytes(remaining)}. Offload it now? (Y/N): ");
            if (((Console.ReadLine() ?? "").Trim().ToUpperInvariant()) == "Y")
            {
                ZipAndOffload(cfg.StagingFolder, cfg.OffloadFolder);
                Console.WriteLine("Final offload complete.");
            }
        }
    }

    // --------------------------
    // Configuration / prompts
    // --------------------------

    record Config(
    string StagingFolder,
    string OffloadFolder,
    long ZipThresholdBytes,
    long MinFreeStagingBytes,
    long MinFreeOffloadBytes
    );


    static Config PromptConfig(
        string defaultStaging,
        string defaultOffload,
        double defaultZipThresholdGb,
        double defaultMinFreeStagingGb,
        double defaultMinFreeOffloadGb,
        bool forceNewPaths = false)
    {
        Console.WriteLine("\n--- Configuration ---");

        string staging = PromptPath("Staging folder", defaultStaging, required: true, forceNew: forceNewPaths);
        string offload = PromptPath("Offload folder", defaultOffload, required: true, forceNew: forceNewPaths);

        double zipGb = PromptDouble("Zip threshold (GB)", defaultZipThresholdGb, min: 0.05);
        
        long zipBytes = (long)(zipGb * 1024 * 1024 * 1024);
        
        double minFreeStagingGb = PromptDouble("Minimum free space on STAGING drive (GB)", defaultMinFreeStagingGb, min: 0.5);
        double minFreeOffloadGb = PromptDouble("Minimum free space on OFFLOAD drive (GB)", defaultMinFreeOffloadGb, min: 0.1);

        long minFreeStagingBytes = (long)(minFreeStagingGb * 1024 * 1024 * 1024);
        long minFreeOffloadBytes = (long)(minFreeOffloadGb * 1024 * 1024 * 1024);

        return new Config(staging, offload, zipBytes, minFreeStagingBytes, minFreeOffloadBytes);
    }

    static string PromptPath(string label, string defaultValue, bool required, bool forceNew)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            string input = (Console.ReadLine() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(input))
                input = defaultValue;

            if (forceNew && string.Equals(input, defaultValue, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("   Please enter a NEW path (different from the previous one).");
                continue;
            }

            if (!required || !string.IsNullOrWhiteSpace(input))
                return input;

            Console.WriteLine("   Path is required.");
        }
    }

    static double PromptDouble(string label, double defaultValue, double min)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue.ToString(CultureInfo.InvariantCulture)}]: ");
            string input = (Console.ReadLine() ?? "").Trim();

            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;

            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) && val >= min)
                return val;

            Console.WriteLine($"   Enter a number >= {min} (use decimal like 2.5).");
        }
    }

    // --------------------------
    // Threshold logic
    // --------------------------

    static bool WouldExceedZipThreshold(long stagingBytes, long nextBytes, long zipThresholdBytes)
    {
        if (zipThresholdBytes <= 0) return false;
        if (nextBytes <= 0)
        {
            // Unknown size → we only zip if staging already exceeds threshold
            return stagingBytes >= zipThresholdBytes;
        }
        return (stagingBytes + nextBytes) > zipThresholdBytes;
    }

    static long GetDriveFreeSpaceBytes(string anyFolderOnDrive)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(anyFolderOnDrive));
            if (string.IsNullOrWhiteSpace(root)) return -1;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    static bool IsDriveSpaceThreatened(string folderOnDrive, long minFreeBytes, long bytesToAdd, out long freeBytes)
    {
        freeBytes = GetDriveFreeSpaceBytes(folderOnDrive);
        if (freeBytes < 0) return false; // couldn't determine -> don't block

        // If we know how many bytes we’re about to add, check "free-after-add"
        if (bytesToAdd > 0)
            return (freeBytes - bytesToAdd) < minFreeBytes;

        // Otherwise just check the raw free space
        return freeBytes < minFreeBytes;
    }



    // --------------------------
    // Zip/offload
    // --------------------------

    static void ZipAndOffload(string stagingFolder, string offloadFolder)
    {
        Directory.CreateDirectory(stagingFolder);
        Directory.CreateDirectory(offloadFolder);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string zipName = $"Chunk_{timestamp}.zip";
        string zipPath = Path.Combine(offloadFolder, zipName);

        Console.WriteLine($"   📦 Creating zip: {zipPath}");

        int bump = 1;
        while (File.Exists(zipPath))
        {
            zipName = $"Chunk_{timestamp}_{bump}.zip";
            zipPath = Path.Combine(offloadFolder, zipName);
            bump++;
        }

        ZipFile.CreateFromDirectory(stagingFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        // Safety check: zip exists and is non-empty
        var zi = new FileInfo(zipPath);
        if (!zi.Exists || zi.Length == 0)
        {
            Console.WriteLine("   ❌ Zip failed or is empty. NOT deleting staging.");
            return;
        }

        Console.WriteLine($"   ✅ Zip complete: {FormatBytes(zi.Length)}");
        Console.WriteLine("   🧹 Clearing staging folder...");

        foreach (var file in Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); } catch { }
        }
        foreach (var dir in Directory.EnumerateDirectories(stagingFolder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // --------------------------
    // Size estimation (best effort)
    // --------------------------

    static async Task<long> TryEstimateVideoSizeBytes(string videoUrl, Config cfg)
    {
        string args =
            $"{YtDlpBaseArgs} " +
            $"--no-download " +
            $"--print \"%(filesize_approx)s\" " +
            $"\"{videoUrl}\"";

        string output = await RunProcessCapture("yt-dlp", args);
        if (string.IsNullOrWhiteSpace(output)) return -1;

        var line = output.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
        if (line == null) return -1;

        if (long.TryParse(line, out long bytes) && bytes > 0) return bytes;
        return -1;
    }

    // --------------------------
    // Playlist parsing
    // --------------------------

    static async Task<List<string>> GetPlaylistVideos(string playlistUrl, Config cfg)
    {
        var result = new List<string>();

        var output = await RunProcessCapture("yt-dlp",
            $"{YtDlpBaseArgs} --flat-playlist --print \"%(url)s\" \"{playlistUrl}\"");

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                result.Add(trimmed);
        }

        return result;
    }

    // --------------------------
    // Process helpers
    // --------------------------

    static async Task<int> RunProcess(string file, string args)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        p.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        p.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    static async Task<string> RunProcessCapture(string file, string args)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        p.Start();
        string output = await p.StandardOutput.ReadToEndAsync();
        string err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(err))
            return err;

        return output;
    }

    // --------------------------
    // Utilities
    // --------------------------

    static long GetFolderSize(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; }
            catch { }
        }
        return total;
    }

    static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "Unknown";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
