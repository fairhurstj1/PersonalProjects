using System;
using System.IO;
using System.Linq;

namespace PlaylistRipper.Core;

public class DiskService
{
    public long GetFolderSize(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; } catch { }
        }
        return total;
    }

    public long GetDriveFreeSpaceBytes(string anyFolderOnDrive)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(anyFolderOnDrive));
            if (string.IsNullOrWhiteSpace(root)) return -1;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    public bool IsDriveSpaceThreatened(string folderOnDrive, long minFreeBytes, long bytesToAdd, out long freeBytes)
    {
        freeBytes = GetDriveFreeSpaceBytes(folderOnDrive);
        if (freeBytes < 0) return false;

        if (bytesToAdd > 0)
            return (freeBytes - bytesToAdd) < minFreeBytes;

        return freeBytes < minFreeBytes;
    }
}
