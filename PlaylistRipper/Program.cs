using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        Console.Write("Enter YouTube playlist URL: ");
        string playlistUrl = Console.ReadLine();

        var videoUrls = await GetPlaylistVideos(playlistUrl);

        Console.WriteLine($"\nFound {videoUrls.Count} videos.\n");

        int index = 1;
        foreach (var url in videoUrls)
        {
            Console.WriteLine($"[{index}/{videoUrls.Count}] Downloading: {url}");
            await RunProcess("yt-dlp",
                $"--js-runtimes deno --remote-components ejs:github --download-archive \"archive.txt\" -f \"bv*+ba/b\" \"{url}\"");
            index++;
        }

        Console.WriteLine("\nDone!");
    }

    static async Task<List<string>> GetPlaylistVideos(string playlistUrl)
    {
        var result = new List<string>();

        var output = await RunProcessCapture("yt-dlp", $"--flat-playlist --print \"%(url)s\" \"{playlistUrl}\"");

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("http"))
                result.Add(trimmed);
        }

        return result;
    }

    static async Task RunProcess(string file, string args)
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
        await p.WaitForExitAsync();
        return output;
    }
}
