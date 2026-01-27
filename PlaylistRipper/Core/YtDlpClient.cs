using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlaylistRipper.Core;

public class YtDlpClient
{
    private readonly ProcessRunner _runner;

    public YtDlpClient(ProcessRunner runner) => _runner = runner;

    private static string JoinArgs(string ytBaseArgs, string authArgs)
    {
        authArgs = (authArgs ?? "").Trim();
        return string.IsNullOrWhiteSpace(authArgs) ? ytBaseArgs : $"{ytBaseArgs} {authArgs}";
    }

    public async Task<List<string>> GetPlaylistVideosAsync(string playlistUrl, string ytBaseArgs, string authArgs)
    {
        var result = new List<string>();
        string baseArgs = JoinArgs(ytBaseArgs, authArgs);

        // Force full watch URLs
        string args = $"{baseArgs} --flat-playlist --print \"https://www.youtube.com/watch?v=%(id)s\" \"{playlistUrl}\"";

        var output = await _runner.CaptureAsync("yt-dlp", args);

        // IMPORTANT: if yt-dlp errored, show it instead of returning 0
        if (output.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0)
            throw new Exception("yt-dlp failed while reading playlist:\n" + output.Trim());

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                result.Add(line);
        }

        return result;
    }



    public async Task<long> TryEstimateVideoSizeBytesAsync(string videoUrl, string ytBaseArgs, string authArgs)
    {
        string baseArgs = JoinArgs(ytBaseArgs, authArgs);

        string args = $"{baseArgs} --no-download --print \"%(filesize_approx)s\" \"{videoUrl}\"";
        var output = await _runner.CaptureAsync("yt-dlp", args);

        var line = output.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
        if (line == null) return -1;

        return long.TryParse(line, out var bytes) && bytes > 0 ? bytes : -1;
    }

    public async Task<(int exitCode, string output)> DownloadVideoAsync(
    string url,
    string stagingFolder,
    string archivePath,
    string ytBaseArgs,
    string authArgs)
    {
        string baseArgs = JoinArgs(ytBaseArgs, authArgs);

        string args =
            $"{baseArgs} " +
            $"--download-archive \"{archivePath}\" " +
            $"-P \"{stagingFolder}\" " +
            $"-f \"bv*+ba/b\" " +
            $"\"{url}\"";

        return await _runner.RunWithOutputAsync("yt-dlp", args);
    }

}
