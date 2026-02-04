using PlaylistRipper.Models;
using System;
using System.IO;
using System.Text.Json;

namespace PlaylistRipper.Core;

public class SessionStoreFacade
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SessionState? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);

            // Try normal deserialize
            var s = JsonSerializer.Deserialize<SessionState>(json, Options);
            if (s != null && !string.IsNullOrWhiteSpace(s.PlaylistUrl))
                return s;

            // Back-compat: try reading legacy keys
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var legacy = new SessionState();

            if (root.TryGetProperty("playlistUrl", out var pUrl))
                legacy.PlaylistUrl = pUrl.GetString() ?? "";

            if (root.TryGetProperty("currentIndex", out var pIdx) && pIdx.TryGetInt32(out var idx))
                legacy.NextIndex = idx;

            if (root.TryGetProperty("stagingFolder", out var pSt))
                legacy.StagingFolder = pSt.GetString() ?? "";

            if (root.TryGetProperty("offloadFolder", out var pOf))
                legacy.OffloadFolder = pOf.GetString() ?? "";

            if (root.TryGetProperty("zipThresholdBytes", out var pZ) && pZ.TryGetInt64(out var z))
                legacy.ZipThresholdBytes = z;

            // Old file had single minFreeBytes; map it to staging
            if (root.TryGetProperty("minFreeBytes", out var pM) && pM.TryGetInt64(out var m))
                legacy.MinFreeStagingBytes = m;

            return string.IsNullOrWhiteSpace(legacy.PlaylistUrl) ? null : legacy;
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path, SessionState session)
    {
        try
        {
            var json = JsonSerializer.Serialize(session, Options);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
