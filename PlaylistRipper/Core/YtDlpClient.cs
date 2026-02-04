using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlaylistRipper.Core;

public record PlaylistReadResult(int ExitCode, string Output, List<string> Videos);

public class YtDlpClient
{
    private readonly ProcessRunner _runner;

    public YtDlpClient(ProcessRunner runner) => _runner = runner;

    private static string JoinArgs(string ytBaseArgs, string cookiesFilePath, string cookiesFromBrowser, string authArgs)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(ytBaseArgs))
            parts.Add(ytBaseArgs.Trim());

        // Prefer cookies file (most reliable on Windows)
        if (!string.IsNullOrWhiteSpace(cookiesFilePath))
            parts.Add($"--cookies \"{cookiesFilePath}\"");

        // Optional fallback (can fail w/ DPAPI)
        if (string.IsNullOrWhiteSpace(cookiesFilePath) && !string.IsNullOrWhiteSpace(cookiesFromBrowser))
            parts.Add($"--cookies-from-browser {cookiesFromBrowser.Trim()}");

        if (!string.IsNullOrWhiteSpace(authArgs))
            parts.Add(authArgs.Trim());

        return string.Join(" ", parts);
    }

    public async Task<PlaylistReadResult> GetPlaylistVideosAsync(
        string playlistUrl,
        string ytBaseArgs,
        string cookiesFilePath,
        string cookiesFromBrowser,
        string authArgs)
    {
        string baseArgs = JoinArgs(ytBaseArgs, cookiesFilePath, cookiesFromBrowser, authArgs);

        // Flat playlist + print watch URLs from id
        string args = $"{baseArgs} --flat-playlist --print \"%(id)s\" \"{playlistUrl}\"";
        var (exit, output) = await _runner.RunWithOutputAsync("yt-dlp", args);

        var result = new List<string>();

        foreach (var raw in (output ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) continue;

            // If yt-dlp prints IDs, build URLs
            if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                result.Add(line);
            else if (!line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                result.Add($"https://www.youtube.com/watch?v={line}");
        }

        return new PlaylistReadResult(exit, output ?? "", result);
    }

    public async Task<long> TryEstimateVideoSizeBytesAsync(
        string videoUrl,
        string ytBaseArgs,
        string cookiesFilePath,
        string cookiesFromBrowser,
        string authArgs)
    {
        string baseArgs = JoinArgs(ytBaseArgs, cookiesFilePath, cookiesFromBrowser, authArgs);
        string args = $"{baseArgs} --no-download --print \"%(filesize_approx)s\" \"{videoUrl}\"";

        var (exit, output) = await _runner.RunWithOutputAsync("yt-dlp", args);
        if (exit != 0) return -1;

        var line = (output ?? "")
            .Split('\n')
            .Select(s => s.Trim())
            .FirstOrDefault(s => s.Length > 0);

        return long.TryParse(line, out var bytes) && bytes > 0 ? bytes : -1;
    }

    public Task<(int exitCode, string output)> DownloadVideoAsync(
        string url,
        string stagingFolder,
        string archivePath,
        string ytBaseArgs,
        string cookiesFilePath,
        string cookiesFromBrowser,
        string authArgs)
    {
        string baseArgs = JoinArgs(ytBaseArgs, cookiesFilePath, cookiesFromBrowser, authArgs);

        string args =
            $"{baseArgs} " +
            $"--download-archive \"{archivePath}\" " +
            $"-P \"{stagingFolder}\" " +
            $"-f \"bv*+ba/b\" " +
            $"\"{url}\"";

        return _runner.RunWithOutputAsync("yt-dlp", args);
    }
}
