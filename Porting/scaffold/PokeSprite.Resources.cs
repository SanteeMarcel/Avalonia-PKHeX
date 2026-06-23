// Phase 1 scaffold — hand-written replacement for the generated
// PKHeX.Drawing.PokeSprite/Properties/Resources.Designer.cs
//
// WHY: the generated class exposed 6,411 sprites as System.Drawing.Bitmap, deserialized via
// ResourceManager from ResXFileRef entries. The underlying data is just PNG files on disk
// (all 6,411 basenames are unique), so we instead embed each PNG by logical name and decode
// it to SKBitmap on demand. Same public surface the callers use:
//   * Resources.ResourceManager.GetObject("name")   (dynamic lookup, ~6,380 sprites)
//   * Resources.<name>                               (~40 sprites referenced directly in code)
//
// APPLY:
//   1) Delete Properties/Resources.Designer.cs AND Properties/Resources.resx (sprites only;
//      this project's resx holds nothing but the sprite file-refs + boilerplate).
//   2) Add this file as Properties/Resources.cs.
//   3) In the .csproj, embed the PNGs by logical name (see PKHeX.Drawing.PokeSprite.csproj
//      in this folder) and remove the old <EmbeddedResource Update="...resx"> block.
//
// NOTE: not compiled here. The named-property list below was generated from every
// `Resources.<name>` reference found in the project's source — verify none are missing
// after your first build (a missing one shows as CS0117 "Resources does not contain ...").
using System.Collections.Concurrent;
using System.Reflection;
using SkiaSharp;

namespace PKHeX.Drawing.PokeSprite.Properties;

/// <summary>
/// Sprite resource accessor backed by embedded PNGs, decoded to <see cref="SKBitmap"/>.
/// Drop-in replacement for the old StronglyTypedResourceBuilder output.
/// </summary>
public static class Resources
{
    private static readonly Assembly Asm = typeof(Resources).Assembly;
    private static readonly ConcurrentDictionary<string, SKBitmap?> Cache = new();

    /// <summary>Backwards-compatible shim so existing <c>Resources.ResourceManager.GetObject(name)</c> calls keep working.</summary>
    public static readonly ManagerShim ResourceManager = new();

    public sealed class ManagerShim
    {
        public SKBitmap? GetObject(string name) => Resources.GetObject(name);
    }

    /// <summary>Loads the embedded PNG with the given logical name (no extension) and decodes it. Cached.</summary>
    public static SKBitmap? GetObject(string name) => Cache.GetOrAdd(name, Load);

    private static SKBitmap? Load(string name)
    {
        // Logical name set in the .csproj is "<basename>.png".
        using var stream = Asm.GetManifestResourceStream(name + ".png");
        return stream is null ? null : SKBitmap.Decode(stream);
    }

    private static SKBitmap Get(string name) => GetObject(name) ?? throw new MissingManifestSpriteException(name);

    // ---- Named sprites referenced directly in code (from a full scan of Resources.<name>) ----
    public static SKBitmap _ball4          => Get("_ball4");
    public static SKBitmap a_490_e         => Get("a_490_e");
    public static SKBitmap a_egg           => Get("a_egg");
    public static SKBitmap aitem_tm        => Get("aitem_tm");
    public static SKBitmap alpha_alt       => Get("alpha_alt");
    public static SKBitmap b_0             => Get("b_0");
    public static SKBitmap b_249x          => Get("b_249x");
    public static SKBitmap b_490_e         => Get("b_490_e");
    public static SKBitmap b_egg           => Get("b_egg");
    public static SKBitmap b_unknown       => Get("b_unknown");
    public static SKBitmap bitem_tm        => Get("bitem_tm");
    public static SKBitmap bitem_tr        => Get("bitem_tr");
    public static SKBitmap bitem_unk       => Get("bitem_unk");
    public static SKBitmap dyna            => Get("dyna");
    public static SKBitmap hint            => Get("hint");
    public static SKBitmap locked          => Get("locked");
    public static SKBitmap party1          => Get("party1");
    public static SKBitmap party2          => Get("party2");
    public static SKBitmap party3          => Get("party3");
    public static SKBitmap party4          => Get("party4");
    public static SKBitmap party5          => Get("party5");
    public static SKBitmap party6          => Get("party6");
    public static SKBitmap rare_icon_alt   => Get("rare_icon_alt");
    public static SKBitmap rare_icon_alt_2 => Get("rare_icon_alt_2");
    public static SKBitmap sickburn        => Get("sickburn");
    public static SKBitmap sickfrostbite   => Get("sickfrostbite");
    public static SKBitmap sickparalyze    => Get("sickparalyze");
    public static SKBitmap sickpoison      => Get("sickpoison");
    public static SKBitmap sicksleep       => Get("sicksleep");
    public static SKBitmap sicktoxic       => Get("sicktoxic");
    public static SKBitmap slotDel68       => Get("slotDel68");
    public static SKBitmap slotDrag68      => Get("slotDrag68");
    public static SKBitmap slotHover68     => Get("slotHover68");
    public static SKBitmap slotSet68       => Get("slotSet68");
    public static SKBitmap slotTrans68     => Get("slotTrans68");
    public static SKBitmap slotView68      => Get("slotView68");
    public static SKBitmap starter         => Get("starter");
    public static SKBitmap team            => Get("team");
    public static SKBitmap valid           => Get("valid");
    public static SKBitmap warn            => Get("warn");
}

public sealed class MissingManifestSpriteException(string name)
    : System.Exception($"Embedded sprite not found: '{name}.png'. Check the EmbeddedResource LogicalName glob in the .csproj.");
