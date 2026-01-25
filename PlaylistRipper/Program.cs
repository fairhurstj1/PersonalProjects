using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    // yt-dlp args that made YouTube work for you
    private const string YtDlpBaseArgs = "--js-runtimes deno --remote-components ejs:github";

    static async Task Main()
    {
        Console.Write("Enter YouTube playlist URL: ");
        string playlistUrl = (Console.ReadLine() ?? "").Trim();

        // Defaults you requested
        var cfg = PromptConfig(
            defaultStaging: @"D:\YTPlaylistRipper\Staging",
            defaultOffload: @"D:\YTPlaylistRipper\Offload",
            defaultZipThresholdGb: 2.0,
            defaultMinFreeGb: 10.0
        );

        Directory.CreateDirectory(cfg.StagingFolder);
        Directory.CreateDirectory(cfg.OffloadFolder);

        var videoUrls = await GetPlaylistVideos(playlistUrl, cfg);

        Console.WriteLine($"\nFound {videoUrls.Count} videos.\n");

        int index = 1;
        foreach (var url in videoUrls)
        {
            Console.WriteLine($"\n[{index}/{videoUrls.Count}] {url}");

            // 1) If staging + next video would exceed zip threshold, offload first
            long stagingBytes = GetFolderSize(cfg.StagingFolder);
            long nextBytes = await TryEstimateVideoSizeBytes(url, cfg);

            Console.WriteLine($"   Staging: {FormatBytes(stagingBytes)}");
            Console.WriteLine($"   Next est: {(nextBytes > 0 ? FormatBytes(nextBytes) : "Unknown")}");

            if (WouldExceedZipThreshold(stagingBytes, nextBytes, cfg.ZipThresholdBytes))
            {
                if (stagingBytes > 0)
                {
                    Console.WriteLine($"   ▶ Zip threshold would be exceeded. Offloading staging first...");
                    ZipAndOffload(cfg.StagingFolder, cfg.OffloadFolder);
                }
                else
                {
                    Console.WriteLine($"   ▶ Zip threshold would be exceeded, but staging is empty. Proceeding.");
                }
            }

            // 2) Ensure destination drive has enough free space per your min-free threshold
            if (IsFreeSpaceThreatened(cfg.StagingFolder, cfg.MinFreeSpaceBytes, out var freeBytes))
            {
                Console.WriteLine($"\n⚠️  Warning: Low free space on staging drive.");
                Console.WriteLine($"   Free now: {FormatBytes(freeBytes)}");
                Console.WriteLine($"   Minimum required free space: {FormatBytes(cfg.MinFreeSpaceBytes)}");
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
                        defaultMinFreeGb: cfg.MinFreeSpaceBytes / (1024d * 1024 * 1024)
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
                        defaultMinFreeGb: cfg.MinFreeSpaceBytes / (1024d * 1024 * 1024),
                        forceNewPaths: true
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
                        defaultMinFreeGb: cfg.MinFreeSpaceBytes / (1024d * 1024 * 1024)
                    );
                    Directory.CreateDirectory(cfg.StagingFolder);
                    Directory.CreateDirectory(cfg.OffloadFolder);
                }
            }

            // 3) Download the video into staging
            var archivePath = Path.Combine(cfg.StagingFolder, "archive.txt");

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

            index++;
        }

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

    record Config(string StagingFolder, string OffloadFolder, long ZipThresholdBytes, long MinFreeSpaceBytes);

    static Config PromptConfig(
        string defaultStaging,
        string defaultOffload,
        double defaultZipThresholdGb,
        double defaultMinFreeGb,
        bool forceNewPaths = false)
    {
        Console.WriteLine("\n--- Configuration ---");

        string staging = PromptPath("Staging folder", defaultStaging, required: true, forceNew: forceNewPaths);
        string offload = PromptPath("Offload folder", defaultOffload, required: true, forceNew: forceNewPaths);

        double zipGb = PromptDouble("Zip threshold (GB)", defaultZipThresholdGb, min: 0.05);
        double minFreeGb = PromptDouble("Minimum free space on staging drive (GB)", defaultMinFreeGb, min: 0.5);

        long zipBytes = (long)(zipGb * 1024 * 1024 * 1024);
        long minFreeBytes = (long)(minFreeGb * 1024 * 1024 * 1024);

        return new Config(staging, offload, zipBytes, minFreeBytes);
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

    static bool IsFreeSpaceThreatened(string stagingFolder, long minFreeBytes, out long freeBytes)
    {
        freeBytes = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(stagingFolder));
            if (string.IsNullOrWhiteSpace(root)) return false;

            var drive = new DriveInfo(root);
            freeBytes = drive.AvailableFreeSpace;

            return freeBytes < minFreeBytes;
        }
        catch
        {
            return false;
        }
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

        // If the zip exists somehow, make a unique name
        int bump = 1;
        while (File.Exists(zipPath))
        {
            zipName = $"Chunk_{timestamp}_{bump}.zip";
            zipPath = Path.Combine(offloadFolder, zipName);
            bump++;
        }

        ZipFile.CreateFromDirectory(stagingFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        Console.WriteLine("   🧹 Clearing staging folder...");

        // Keep archive.txt if you want resume across chunks (optional).
        // For now, we keep it in staging so each chunk has its own archive.
        // If you want one archive for the whole run, move archive.txt outside staging.
        Directory.Delete(stagingFolder, true);
        Directory.CreateDirectory(stagingFolder);
    }

    // --------------------------
    // Size estimation (best effort)
    // --------------------------

    static async Task<long> TryEstimateVideoSizeBytes(string videoUrl, Config cfg)
    {
        // Ask yt-dlp for approximate size. Not always available.
        // --no-download ensures we only query metadata.
        // We print filesize_approx if present.
        string args =
            $"{YtDlpBaseArgs} " +
            $"--no-download " +
            $"--print \"%(filesize_approx)s\" " +
            $"\"{videoUrl}\"";

        string output = await RunProcessCapture("yt-dlp", args);
        if (string.IsNullOrWhiteSpace(output)) return -1;

        var line = output.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
        if (line == null) return -1;

        // filesize_approx comes as bytes if available
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

        // Sometimes useful metadata prints to stderr; include it if stdout is empty
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
            catch { /* ignore */ }
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
