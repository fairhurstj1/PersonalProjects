using System;
using System.IO;

namespace PlaylistRipper.Core;

public class LogService
{
    private readonly string _path;

    public LogService(string path) => _path = path;

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);

    private void Write(string level, string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}\n");
        }
        catch { /* don't crash logging */ }
    }
}
