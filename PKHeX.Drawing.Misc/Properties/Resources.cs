// SkiaSharp port — replacement for the generated PKHeX.Drawing.Misc/Properties/Resources.Designer.cs
// Same approach as PKHeX.Drawing.PokeSprite: 850 PNGs (unique basenames) embedded by logical
// name and decoded to SKBitmap on demand. Preserves the call surface:
//   Resources.ResourceManager.GetObject("name")  and  Resources.<name>
using System.Collections.Concurrent;
using System.Reflection;
using SkiaSharp;

namespace PKHeX.Drawing.Misc.Properties;

public static class Resources
{
    private static readonly Assembly Asm = typeof(Resources).Assembly;
    private static readonly ConcurrentDictionary<string, SKBitmap?> Cache = new();

    public static readonly ManagerShim ResourceManager = new();

    public sealed class ManagerShim
    {
        public SKBitmap? GetObject(string name) => Resources.GetObject(name);
    }

    public static SKBitmap? GetObject(string name) => Cache.GetOrAdd(name, Load);

    private static SKBitmap? Load(string name)
    {
        using var stream = Asm.GetManifestResourceStream(name + ".png");
        if (stream is null)
            return null;
        var bmp = SKBitmap.Decode(stream);
        // Force Bgra8888 so ImageUtil's BGRA pixel math is correct (decoders may return Rgba8888).
        if (bmp is null || bmp.ColorType == SKColorType.Bgra8888)
            return bmp;
        var converted = bmp.Copy(SKColorType.Bgra8888);
        bmp.Dispose();
        return converted;
    }

    private static SKBitmap Get(string name) => GetObject(name) ?? throw new MissingManifestSpriteException(name);

    // Named sprites referenced directly in code:
    public static SKBitmap Bag_Key      => Get("Bag_Key");
    public static SKBitmap b_unknown     => Get("b_unknown");
    public static SKBitmap box_wp01bdsp  => Get("box_wp01bdsp");
    public static SKBitmap box_wp02bdsp  => Get("box_wp02bdsp");
    public static SKBitmap box_wp16xy    => Get("box_wp16xy");
    public static SKBitmap donut_uni382  => Get("donut_uni382");
    public static SKBitmap donut_uni383  => Get("donut_uni383");
    public static SKBitmap donut_uni384  => Get("donut_uni384");
    public static SKBitmap donut_uni491  => Get("donut_uni491");
    public static SKBitmap donut_uni807  => Get("donut_uni807");
    public static SKBitmap tr_00         => Get("tr_00");
}

public sealed class MissingManifestSpriteException(string name)
    : System.Exception($"Embedded sprite not found: '{name}.png'. Check the EmbeddedResource LogicalName glob in the .csproj.");
