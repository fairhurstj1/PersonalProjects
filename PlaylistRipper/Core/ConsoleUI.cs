using System;
using System.Globalization;

namespace PlaylistRipper.Core;

public enum LowSpaceAction { Continue, Reconfigure }

public record PromptedConfig(
    string RootFolder,
    string StagingFolder,
    string OffloadFolder,
    long ZipThresholdBytes,
    long MinFreeStagingBytes,
    long MinFreeOffloadBytes,
    string CookiesFromBrowser,
    string AuthArgs,
    int MaxAttempts,
    int BaseDelaySeconds,
    int MaxDelaySeconds,
    int PoliteDelaySeconds
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
        string defaultAuthArgs = "")
    {
        WriteLine("\n--- Configuration ---");
        string root = PromptPath("Root folder", defaultRoot);
        string staging = PromptPath("Staging folder", defaultStaging);
        string offload = PromptPath("Offload folder", defaultOffload);

        double zipGb = PromptDouble("Zip threshold (GB)", defaultZipThresholdGb, min: 0.05);
        double minStageGb = PromptDouble("Minimum free space on STAGING drive (GB)", defaultMinFreeStagingGb, min: 0.1);
        double minOffGb = PromptDouble("Minimum free space on OFFLOAD drive (GB)", defaultMinFreeOffloadGb, min: 0.05);

        string cookies = PromptCookiesFromBrowser();


        Write("Optional yt-dlp auth args (cookies/login). Leave blank for none.\nExample: --cookies \"C:\\path\\cookies.txt\"");
        string authArgs = (ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(authArgs))
            authArgs = defaultAuthArgs;

        int maxAttempts = (int)PromptDouble("Max retry attempts per video", 4, min: 1);
        int baseDelay = (int)PromptDouble("Retry base delay (seconds)", 10, min: 1);
        int maxDelay  = (int)PromptDouble("Retry max delay (seconds)", 120, min: 1);

        // Optional: polite spacing between successful downloads (not evasion; just less hammering)
        int politeDelay = (int)PromptDouble("Polite delay after each successful download (seconds)", 2, min: 0);


        return new PromptedConfig(
            RootFolder: root,
            StagingFolder: staging,
            OffloadFolder: offload,
            ZipThresholdBytes: (long)(zipGb * Bytes.GB),
            MinFreeStagingBytes: (long)(minStageGb * Bytes.GB),
            MinFreeOffloadBytes: (long)(minOffGb * Bytes.GB),
            CookiesFromBrowser: cookies,
            AuthArgs: authArgs,
            MaxAttempts: maxAttempts,
            BaseDelaySeconds: baseDelay,
            MaxDelaySeconds: maxDelay,
            PoliteDelaySeconds: politeDelay
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

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
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
        Console.Write("Optional cookies.txt path for YouTube login (recommended). Leave blank for none.\nCookies path: ");
        var input = (Console.ReadLine() ?? "").Trim();

        // Return empty = no cookies
        return input;
    }


}
