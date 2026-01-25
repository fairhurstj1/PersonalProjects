using System.Text.Json;
using PlaylistRipper.Models;

namespace PlaylistRipper.Core;

public static class SessionStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true
    };

    public static SessionState? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SessionState>(json, Opts);
        }
        catch { return null; }
    }

    public static void Save(string path, SessionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(state, Opts);
        File.WriteAllText(path, json);
    }

    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
