using System;
using System.Globalization;
using System.IO;

namespace PlaylistRipper.Core;

public enum LowSpaceAction { Continue, Reconfigure }

public record PromptedConfig(
    string RootFolder,
    string StagingFolder,
    string OffloadFolder,
    long ZipThresholdBytes,
    long MinFreeStagingBytes,
    long MinFreeOffloadBytes,

    // Auth
    string CookiesFromBrowser,
    string CookiesFilePath,
    string AuthArgs,

    // Retry / pacing
    int MaxAttempts,
    int BaseDelaySeconds,
    int MaxDelaySeconds,
    int PoliteDelaySeconds,

    // Reliability caps
    int MaxDownloadsPerRun,
    int BreakEveryNDownloads,
    int BreakSeconds
);

public class ConsoleUi
{
    public void Write(string s) => Console.Write(s);
    public void WriteLine(string s) => Console.WriteLine(s);
    public string? ReadLine() => Console.ReadLine();

    public bool Confirm(string prompt)
    {
        Write(prompt);
        return (ReadLine() ?? "").Trim().ToUpperInvariant() == "Y";
    }

    public int PromptStartIndex(int suggested, int max)
    {
        Write($"Start from which video index? [Enter = {suggested}, 1-{max}]: ");
        var input = (ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input)) return suggested;

        if (int.TryParse(input, out var val))
        {
            if (val < 1) return 1;
            if (val > max) return max;
            return val;
        }

        WriteLine("   Invalid number. Using suggested value.");
        return suggested;
    }

    public PromptedConfig PromptConfig(
        string defaultRoot,
        string defaultStaging,
        string defaultOffload,
        double defaultZipThresholdGb,
        double defaultMinFreeStagingGb,
        double defaultMinFreeOffloadGb,
        string defaultAuthArgs = "",
        string defaultCookiesFilePath = ""
    )
    {
        WriteLine("\n--- Configuration ---");
        string root = PromptPath("Root folder", defaultRoot);
        string staging = PromptPath("Staging folder", defaultStaging);
        string offload = PromptPath("Offload folder", defaultOffload);

        double zipGb = PromptDouble("Zip threshold (GB)", defaultZipThresholdGb, min: 0.05);
        double minStageGb = PromptDouble("Minimum free space on STAGING drive (GB)", defaultMinFreeStagingGb, min: 0.1);
        double minOffGb = PromptDouble("Minimum free space on OFFLOAD drive (GB)", defaultMinFreeOffloadGb, min: 0.05);

        WriteLine("\n--- Auth (recommended) ---");
        string cookiesFile = PromptCookiesFile(defaultCookiesFilePath);

        // Keep this for convenience, but it’s optional and can fail on Windows (DPAPI)
        string cookiesFromBrowser = PromptCookiesFromBrowser();

        Write("Optional yt-dlp extra auth args (leave blank for none)\nExample: --username ... --password ...\nAuth args: ");
        string authArgs = (ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(authArgs))
            authArgs = defaultAuthArgs;

        WriteLine("\n--- Reliability ---");
        int maxAttempts = (int)PromptDouble("Max retry attempts per video", 4, min: 1);
        int baseDelay = (int)PromptDouble("Retry base delay (seconds)", 10, min: 1);
        int maxDelay  = (int)PromptDouble("Retry max delay (seconds)", 120, min: 1);
        int politeDelay = (int)PromptDouble("Polite delay after each successful download (seconds)", 2, min: 0);

        int maxDownloadsPerRun = (int)PromptDouble("Max downloads per run (0 = unlimited)", 50, min: 0);
        int breakEvery = (int)PromptDouble("Take a break every N downloads (0 = none)", 15, min: 0);
        int breakSeconds = (int)PromptDouble("Break duration in seconds", 120, min: 0);

        return new PromptedConfig(
            RootFolder: root,
            StagingFolder: staging,
            OffloadFolder: offload,
            ZipThresholdBytes: (long)(zipGb * Bytes.GB),
            MinFreeStagingBytes: (long)(minStageGb * Bytes.GB),
            MinFreeOffloadBytes: (long)(minOffGb * Bytes.GB),

            CookiesFromBrowser: cookiesFromBrowser,
            CookiesFilePath: cookiesFile,
            AuthArgs: authArgs,

            MaxAttempts: maxAttempts,
            BaseDelaySeconds: baseDelay,
            MaxDelaySeconds: maxDelay,
            PoliteDelaySeconds: politeDelay,

            MaxDownloadsPerRun: maxDownloadsPerRun,
            BreakEveryNDownloads: breakEvery,
            BreakSeconds: breakSeconds
        );
    }

    public LowSpaceAction LowSpacePrompt(string driveLabel, long freeBytes, long minFreeBytes, long bytesToAdd)
    {
        WriteLine($"\n⚠️  Warning: Low free space on {driveLabel} drive.");
        WriteLine($"   Free now: {Bytes.Format(freeBytes)}");
        WriteLine($"   Minimum required free space: {Bytes.Format(minFreeBytes)}");
        WriteLine($"   Bytes to add (est): {(bytesToAdd > 0 ? Bytes.Format(bytesToAdd) : "Unknown")}");
        WriteLine("");

        Write("Type R to reconfigure, or C to continue anyway: ");
        var choice = (ReadLine() ?? "").Trim().ToUpperInvariant();
        return choice == "R" ? LowSpaceAction.Reconfigure : LowSpaceAction.Continue;
    }

    private static string PromptPath(string label, string defaultValue)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            var input = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input)) input = defaultValue;
            if (!string.IsNullOrWhiteSpace(input)) return input;
            Console.WriteLine("   Path is required.");
        }
    }

    private static double PromptDouble(string label, double defaultValue, double min)
    {
        while (true)
        {
            Console.Write($"{label} [{defaultValue.ToString(CultureInfo.InvariantCulture)}]: ");
            var input = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input)) return defaultValue;

            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) && val >= min)
                return val;

            Console.WriteLine($"   Enter a number >= {min} (use decimals like 0.5).");
        }
    }

    private static string PromptCookiesFromBrowser()
    {
        Console.Write("Optional: cookies-from-browser? (chrome/edge/firefox/none) [none]: ");
        var input = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(input) || input is "none" or "n")
            return "";

        if (input is "chrome" or "edge" or "firefox")
            return input;

        Console.WriteLine("   Unrecognized option. Using 'none'.");
        return "";
    }

    private static string PromptCookiesFile(string defaultPath)
    {
        Console.Write($"Cookies file path (recommended). [Enter = {defaultPath}]: ");
        var input = (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input)) input = defaultPath;

        if (string.IsNullOrWhiteSpace(input)) return "";
        if (!File.Exists(input))
        {
            Console.WriteLine("   File not found. Leaving cookies file blank.");
            return "";
        }
        return input;
    }
}
