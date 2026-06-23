using System;
using System.IO;
using PKHeX.Core;

// Carves ONLY Gen 4 saves (DP / Pt / HGSS) from a raw disk image.
// Gen 5 hits are deliberately ignored here.
class Program
{
    static void Main()
    {
        string imagePath = @"/mnt/HD-24TB/alex_backups/hd_externo.img";
        string outputDir = @"/home/alex/Desktop/carve_saves/recovered_gen4";
        Directory.CreateDirectory(outputDir);

        const int clusterSize = 512;      // step; keep at 512 to avoid missing sector-aligned saves
        const int saveSize    = 0x80000;  // 512 KB, SIZE_G4RAW (== SIZE_G5RAW)
        byte[] buffer = new byte[saveSize];

        int found = 0;
        using FileStream fs = new(imagePath, FileMode.Open, FileAccess.Read);
        long totalLength = fs.Length;
        long position = 0;

        Console.WriteLine("Sweeping for Gen 4 saves (DP / Pt / HGSS)...");

        while (position + saveSize <= totalLength)
        {
            fs.Seek(position, SeekOrigin.Begin);
            fs.ReadExactly(buffer, 0, saveSize); // guaranteed full read (no stale-tail false negatives)

            SaveFile? sav = SaveUtil.GetSaveFile(buffer);

            // Only keep genuine Gen 4 mainline saves. At 0x80000 the only other
            // possible match is Gen 5 (BW/B2W2), which we skip by checking Generation.
            if (sav is not null && sav.Generation == 4)
            {
                Console.WriteLine($"[!] GEN 4 ({sav.GetType().Name}) @ offset {position}");
                string outPath = Path.Combine(outputDir, $"Recovered_Gen4_{position}.sav");
                File.WriteAllBytes(outPath, buffer);
                found++;
            }

            position += clusterSize;

            if (position % (clusterSize * 100000) == 0)
                Console.WriteLine($"Scanned {position / 1024 / 1024} MB... ({found} Gen 4 found)");
        }

        Console.WriteLine($"Scan complete. {found} Gen 4 saves written to {outputDir}");
    }
}
