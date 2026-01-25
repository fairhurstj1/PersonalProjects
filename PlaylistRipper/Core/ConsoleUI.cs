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
    long MinFreeOffloadBytes
);

public class ConsoleUi
{
    public void Write(string s) => Console.Write(s);
    public void WriteLine(string s) => Console.WriteLine(s);
    public string? ReadLine() => Console.ReadLine();

    public bool Confirm(string prompt)
    {
        Write(prompt);
        return ((ReadLine() ?? "").Trim().ToUpperInvariant()) == "Y";
    }

    public PromptedConfig PromptConfig(
        string defaultRoot,
        string defaultStaging,
        string defaultOffload,
        double defaultZipThresholdGb,
        double defaultMinFreeStagingGb,
        double defaultMinFreeOffloadGb)
    {
        WriteLine("\n--- Configuration ---");
        string root = PromptPath("Root folder", defaultRoot);
        string staging = PromptPath("Staging folder", defaultStaging);
        string offload = PromptPath("Offload folder", defaultOffload);

        double zipGb = PromptDouble("Zip threshold (GB)", defaultZipThresholdGb, min: 0.05);
        double minStageGb = PromptDouble("Minimum free space on STAGING drive (GB)", defaultMinFreeStagingGb, min: 0.1);
        double minOffGb = PromptDouble("Minimum free space on OFFLOAD drive (GB)", defaultMinFreeOffloadGb, min: 0.05);

        return new PromptedConfig(
            RootFolder: root,
            StagingFolder: staging,
            OffloadFolder: offload,
            ZipThresholdBytes: (long)(zipGb * Bytes.GB),
            MinFreeStagingBytes: (long)(minStageGb * Bytes.GB),
            MinFreeOffloadBytes: (long)(minOffGb * Bytes.GB)
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
}
