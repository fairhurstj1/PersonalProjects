namespace PlaylistRipper.Core;

public static class Bytes
{
    public const double GB = 1024d * 1024 * 1024;

    public static string Format(long bytes)
    {
        if (bytes < 0) return "Unknown";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
