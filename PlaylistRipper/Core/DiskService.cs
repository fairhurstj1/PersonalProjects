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

    public long GetEffectiveZipThresholdBytes(string offloadFolder, long configuredZipThresholdBytes, long minFreeOffloadBytes)
    {
        // How much room can we safely consume on OFFLOAD right now?
        long free = GetDriveFreeSpaceBytes(offloadFolder);
        if (free <= 0) return configuredZipThresholdBytes; // can't measure, fall back to configured

        long safeUsable = free - minFreeOffloadBytes;

        // If safeUsable <= 0, OFFLOAD is already below the minimum free buffer.
        if (safeUsable <= 0) return 0;

        // Effective threshold is whichever is smaller:
        // - the user configured max chunk size
        // - what OFFLOAD can safely accept right now
        long effective = Math.Min(configuredZipThresholdBytes, safeUsable);

        // Never allow negative
        return Math.Max(0, effective);
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
