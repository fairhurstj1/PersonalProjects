using System.Diagnostics;
using System.Threading.Tasks;

namespace PlaylistRipper.Core;

public class ProcessRunner
{
    public async Task<int> RunAsync(string file, string args, bool echo = true)
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

        if (echo)
        {
            p.OutputDataReceived += (_, e) => { if (e.Data != null) System.Console.WriteLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) System.Console.WriteLine(e.Data); };
        }

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync();
        return p.ExitCode;
    }

    public async Task<(int exitCode, string output)> RunWithOutputAsync(string file, string args)
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

        string stdout = await p.StandardOutput.ReadToEndAsync();
        string stderr = await p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync();

        string combined = stdout + "\n" + stderr;
        return (p.ExitCode, combined);
    }


    public async Task<string> CaptureAsync(string file, string args)
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

        return string.IsNullOrWhiteSpace(output) ? err : output;
    }
}
