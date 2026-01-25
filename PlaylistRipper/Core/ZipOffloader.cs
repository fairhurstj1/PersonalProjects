using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PlaylistRipper.Core;

public class ZipOffloader
{
    public void ZipAndOffload(string stagingFolder, string offloadFolder)
    {
        Directory.CreateDirectory(stagingFolder);
        Directory.CreateDirectory(offloadFolder);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string zipName = $"Chunk_{timestamp}.zip";
        string zipPath = Path.Combine(offloadFolder, zipName);

        int bump = 1;
        while (File.Exists(zipPath))
        {
            zipName = $"Chunk_{timestamp}_{bump}.zip";
            zipPath = Path.Combine(offloadFolder, zipName);
            bump++;
        }

        Console.WriteLine($"   📦 Creating zip: {zipPath}");
        ZipFile.CreateFromDirectory(stagingFolder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        var zi = new FileInfo(zipPath);
        if (!zi.Exists || zi.Length == 0)
        {
            Console.WriteLine("   ❌ Zip failed or is empty. NOT deleting staging.");
            return;
        }

        Console.WriteLine($"   ✅ Zip complete: {Bytes.Format(zi.Length)}");
        Console.WriteLine("   🧹 Clearing staging folder...");

        foreach (var file in Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); } catch { }
        }
        foreach (var dir in Directory.EnumerateDirectories(stagingFolder, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
