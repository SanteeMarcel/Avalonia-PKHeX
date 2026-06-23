using System;
using System.IO;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite; // pk.Sprite(), SpriteUtil
using SkiaSharp;

// Usage: dotnet run --project PKHeX.SpriteTest -- /path/to/savefile
// Exercises the ported SkiaSharp sprite pipeline with NO Avalonia involved.
// Prints each step so the last line before a crash localizes the failing call.

if (args.Length == 0)
{
    Console.Error.WriteLine("Pass the path to a save file as the first argument.");
    return 1;
}

var path = args[0];
Console.WriteLine($"[1] Loading save: {path}");
var sav = SaveUtil.GetSaveFile(path);
if (sav is null)
{
    Console.Error.WriteLine("Unrecognized save file.");
    return 2;
}
Console.WriteLine($"[2] Loaded {sav.GetType().Name}  OT={sav.OT}  Version={sav.Version}  Boxes={sav.BoxCount}");

Console.WriteLine("[3] SpriteUtil.Initialize(sav)…");
SpriteUtil.Initialize(sav);
Console.WriteLine("[3] ok");

var outDir = Path.Combine(AppContext.BaseDirectory, "sprite_out");
Directory.CreateDirectory(outDir);
Console.WriteLine($"[4] Output dir: {outDir}");

var box = sav.GetBoxData(0);
Console.WriteLine($"[5] Box 0 has {box.Length} slots; rendering non-empty…");

int ok = 0, empty = 0;
for (int i = 0; i < box.Length; i++)
{
    var pk = box[i];
    if (pk.Species == 0) { empty++; continue; }

    Console.WriteLine($"  slot {i}: species {pk.Species} — pk.Sprite()…");
    SKBitmap skb = pk.Sprite();
    Console.WriteLine($"    sprite {skb.Width}x{skb.Height} colortype={skb.ColorType} — encoding…");

    using var image = SKImage.FromBitmap(skb);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    var file = Path.Combine(outDir, $"slot{i:D2}_sp{pk.Species}.png");
    using (var fs = File.Create(file))
        data.SaveTo(fs);
    Console.WriteLine($"    wrote {file}");
    ok++;
}

Console.WriteLine($"[6] Done. {ok} sprites written, {empty} empty slots.");
return 0;
