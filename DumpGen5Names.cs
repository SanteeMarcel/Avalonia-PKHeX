using System;
using System.IO;
using System.Text;
using PKHeX.Core;

// Reads every carved Gen 5 candidate and writes a tab-separated list of
// trainer name / TID / SID / source file so you can search it by eye or with grep.
// Most rows will be garbage (16-bit-CRC false positives); a real save shows a readable name.
class Program
{
    static void Main()
    {
        string savesDir = @"/home/alex/Desktop/carve_saves/recovered";
        string outFile  = @"/home/alex/Desktop/carve_saves/gen5_trainer_names.tsv";

        using var writer = new StreamWriter(outFile, false, new UTF8Encoding(false));
        writer.WriteLine("OT\tTID\tSID\tFile");

        int total = 0, written = 0;
        foreach (var file in Directory.EnumerateFiles(savesDir, "Recovered_Gen5_*.sav"))
        {
            total++;
            try
            {
                byte[] data = File.ReadAllBytes(file);
                SaveFile? sav = SaveUtil.GetSaveFile(data);
                if (sav is null || sav.Generation != 5)
                    continue;

                writer.WriteLine($"{Clean(sav.OT)}\t{sav.TID16}\t{sav.SID16}\t{Path.GetFileName(file)}");
                written++;
            }
            catch
            {
                // unreadable block, skip
            }

            if (total % 1000 == 0)
                Console.WriteLine($"Processed {total} files...");
        }

        Console.WriteLine($"Done. Read {total} files, wrote {written} rows to {outFile}");
    }

    // Replace control / non-printing chars so the output stays grep-friendly on one line.
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "(empty)";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(char.IsControl(c) || c == '\t' ? '·' : c);
        return sb.ToString();
    }
}
