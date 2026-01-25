using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlaylistRipper.Core;

public class YtDlpClient
{
    private readonly ProcessRunner _runner;

    public YtDlpClient(ProcessRunner runner) => _runner = runner;

    public async Task<List<string>> GetPlaylistVideosAsync(string playlistUrl, string ytBaseArgs)
    {
        var result = new List<string>();
        string args = $"{ytBaseArgs} --flat-playlist --print \"%(url)s\" \"{playlistUrl}\"";

        var output = await _runner.CaptureAsync("yt-dlp", args);

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                result.Add(trimmed);
        }

        return result;
    }

    public async Task<long> TryEstimateVideoSizeBytesAsync(string videoUrl, string ytBaseArgs)
    {
        string args = $"{ytBaseArgs} --no-download --print \"%(filesize_approx)s\" \"{videoUrl}\"";
        var output = await _runner.CaptureAsync("yt-dlp", args);

        var line = output.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
        if (line == null) return -1;

        return long.TryParse(line, out var bytes) && bytes > 0 ? bytes : -1;
    }

    public Task<int> DownloadVideoAsync(string url, string stagingFolder, string archivePath, string ytBaseArgs)
    {
        string args =
            $"{ytBaseArgs} " +
            $"--download-archive \"{archivePath}\" " +
            $"-P \"{stagingFolder}\" " +
            $"-f \"bv*+ba/b\" " +
            $"\"{url}\"";

        return _runner.RunAsync("yt-dlp", args, echo: true);
    }
}
